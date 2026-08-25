#!/usr/bin/env python3
"""master_to_sprite.py — the committed master-to-sprite pipeline (issue #294).

Turns one of Brandon's full-resolution paintings in ``client/assets/masters/``
into a sprite frame the client can ship, by four mechanical steps applied in
order:

    1. crop      — trim to the opaque bounding box (transparent margin gone,
                   feet land on the new canvas's bottom edge, per the
                   client's own "feet on the bottom edge" convention).
    2. downscale — a two-stage box filter with an unsharp pass at the
                   intermediate size, the recipe recorded on
                   ``SpriteLibrary.ByClassName`` for the Barbarian repaint.
    3. palette   — colour reduced in two passes: first a per-image median-cut
                   clustering down to a small working set (16 colours by
                   default), then each of *those* colours — never each raw
                   pixel — is snapped to its nearest match in the fixed
                   master palette at ``client/assets/palette/
                   SRD_Combat.gpl``. Alpha is hardened to fully opaque or
                   fully transparent at the same stage.
    4. de-grain  — an isolated pixel (one that shares its colour with none of
                   its neighbours, where the neighbourhood has a clear
                   majority) is folded into that majority colour.

This script never invents a palette and never clusters across frames. That
second point is the whole lesson of PR #238 (merged, then reverted at
Brandon's direction — see ``git show 44411c7`` and the two revert commits
that follow it, ``c3b36dc`` and ``5c9dc7e``). #238 diagnosed the grain
correctly — a per-pixel quantize produces confetti that non-integer
nearest-neighbour resampling regenerates at every camera zoom — but its fix
ran a *shared* k-means across a character's four poses at once. The
Barbarian's Hurt frame is the smallest canvas of the four (51x66), so in
that shared vote its pixels were outvoted by Idle, Attack and Dead, and its
skin tones landed in clusters chosen for other poses — the result read as
pink blotches, worse than the grain it replaced.

Every step in this script operates on ONE image at a time. Nothing here
ever looks at a sibling frame, a sibling pose, or a sibling character. Step
3's clustering IS a k-means-family algorithm (median-cut), and that is
exactly the tool #238 reached for — the fix here is not "never cluster", it
is "never let one frame's clustering be decided by a different frame's
pixels". Each master is clustered alone, against nothing but its own
downscaled content, so a small canvas can never be outvoted by a larger
sibling — there is no sibling in scope when it happens. Mapping the small
per-image cluster palette onto the fixed master palette afterward is
ordinary nearest-colour lookup, which is deterministic and has nothing left
to vote on. De-grain is a further, separate local majority filter over one
frame's own 3x3 neighbourhoods, run after the palette mapping — never
across frames, never across poses, never across characters.

An earlier version of this script skipped the clustering step and mapped
every raw pixel straight to its nearest of the 52 master colours. It
technically passed the conformance check (every output pixel is a real
palette colour, so of course it does) but looked worse than the un-quantized
downscale: ~40% of the Ogre's opaque pixels had no matching neighbour at
all, because a photographic gradient snapped adjacent, barely-different
shades to different ramps scattered across the 52-colour menu. Clustering
first groups a gradient into one region before anything is snapped to the
master palette, which is what actually removes the grain rather than just
making it briefly true that every speckle is a "legal" colour. Measured on
the same master: ~40% isolated pixels direct-mapped, ~19-23% after
clustering to 16 colours plus one de-grain pass. The remaining grain is a
tuning question (cluster count, de-grain passes) each batch's before/after
will surface, not a defect in the shape of the pipeline.

Nothing here is run automatically against a shipped sprite. Every batch's
before/after goes to Brandon for approval before anything lands — see
CLAUDE.md's team protocol and the standing memory note "Art is Brandon's
domain". This script transforms mechanically; it does not draw, restyle, or
decide when a frame "looks right" enough to ship.

Usage
-----

    # One master, one frame, written to a scratch directory:
    python3 tools/asset_pipeline/master_to_sprite.py process ogre \\
        --out /tmp/preview

    # Several masters in one call, same output directory:
    python3 tools/asset_pipeline/master_to_sprite.py batch ogre skeleton \\
        --out /tmp/preview

    # A terrain tile — already final-resolution, so only the palette and
    # de-grain steps run (no separate high-res master exists for terrain):
    python3 tools/asset_pipeline/master_to_sprite.py terrain \\
        client/assets/sprites/Terrain/Wall_Woodland.png --out /tmp/preview

    # Palette conformance of any PNG(s) — the review's own colour-count
    # check, runnable as one command:
    python3 tools/asset_pipeline/master_to_sprite.py conformance \\
        client/assets/sprites/Barbarian_Drawn/Idle.png

    # Conformance across every shipped sprite and terrain tile at once:
    python3 tools/asset_pipeline/master_to_sprite.py conformance --shipped

    # What masters exist, and whether they already have a shipped folder:
    python3 tools/asset_pipeline/master_to_sprite.py list

Requires only Pillow (``pip install Pillow`` — already on this machine, and
the one dependency ``scripts/doctor.sh`` checks for under "Optional
tooling"). Deliberately no numpy, no scikit-learn: Pillow's own median-cut
quantizer is the one clustering step this needs, an image this small
processes fast enough in pure Python + Pillow otherwise, and the fewer
dependencies the more "runnable by anyone" this stays.

Everything is deterministic: no random seeds anywhere, because nothing here
samples or initialises randomly — median-cut is a fixed splitting procedure,
not a randomly-seeded one (verified: reprocessing the same master produces
byte-identical output, checked in this PR's own testing). Palette lookup
after clustering is exact nearest-colour by Euclidean distance in RGB with
ties broken by palette order, and the de-grain filter's majority rule
breaks ties by first-seen scan order for the same reason. Re-running this
script against the same master with the same flags reproduces the same
output byte-for-byte.
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageFilter

REPO_ROOT = Path(__file__).resolve().parents[2]
MASTERS_DIR = REPO_ROOT / "client" / "assets" / "masters"
SPRITES_DIR = REPO_ROOT / "client" / "assets" / "sprites"
PALETTE_PATH = REPO_ROOT / "client" / "assets" / "palette" / "SRD_Combat.gpl"

# The board-background-ref entry in the .gpl is explicitly a *reference*
# swatch (what the board itself paints under everything), never a colour a
# sprite should wear — including it as a legal sprite colour would let a
# figure blend into the floor. Every other line is the menu of usable ramps.
_PALETTE_EXCLUDE_NAMES = {"board-background-ref"}

# ---------------------------------------------------------------------------
# Per-sprite parameters. "Parameters live in the script, not in anyone's
# head" (the issue's own words) — this table is where a sprite's downscale
# target and crop margin are recorded once a batch is tuned, rather than
# passed by hand on a command line and forgotten. Nothing here is final:
# it is a starting point for the batches this issue's PR proposes, each one
# subject to Brandon's before/after approval, and per-creature *stature*
# policy (should an Ogre stand taller than a Goblin) is #296's decision, not
# this script's — TARGET_HEIGHT below is a pipeline default (Godot client's
# NominalStature, see client/SpriteLibrary.cs) used until a sprite has a
# considered override, not a claim about final in-game size.
TARGET_HEIGHT = 64

# stem (in client/assets/masters/, without extension) -> overrides.
# "target_height" overrides TARGET_HEIGHT; "reduce_colors" overrides
# REDUCE_COLORS_K; "degrain_passes" overrides the default de-grain pass
# count; "degrain_agreement" overrides the majority threshold (out of 8
# neighbours) needed before an isolated pixel is folded.
SPRITE_TARGETS: dict[str, dict[str, int]] = {
    # No overrides yet — every master processes at the pipeline default
    # until a specific batch's before/after says otherwise. Add entries
    # here, never as a one-off flag on someone's command line, per the
    # issue's "parameters in the script not in anyone's head" criterion.
}

# The per-image median-cut working set, before its colours are snapped to
# the master palette (step 3's first pass — see the module docstring for
# why this beats mapping every raw pixel straight to the 52-colour menu).
# 16 sits in the middle of the .gpl header's own "12-20 colors" per-sprite
# guidance; a sprite that still looks noisy at 16 is a candidate for a
# lower override, not for more de-grain passes papering over it.
REDUCE_COLORS_K = 16

DEFAULT_DEGRAIN_PASSES = 1
DEFAULT_DEGRAIN_AGREEMENT = 5  # of 8 neighbours, to fold an isolated pixel
ALPHA_THRESHOLD = 128  # hard-alpha cut: >= this is opaque, else transparent


# ---------------------------------------------------------------------------
# Palette


@dataclass(frozen=True)
class Palette:
    colors: tuple[tuple[int, int, int], ...]
    names: tuple[str, ...]

    def membership(self) -> set[tuple[int, int, int]]:
        return set(self.colors)


def load_palette(path: Path = PALETTE_PATH) -> Palette:
    """Reads the GIMP .gpl palette, in file order (stable, so palette index
    0 is always the same colour across runs — that stability is what makes
    Image.quantize's tie-breaking deterministic)."""

    colors: list[tuple[int, int, int]] = []
    names: list[str] = []

    for raw_line in path.read_text().splitlines():
        line = raw_line.strip()

        if not line or line.startswith("#"):
            continue
        if line.startswith(("GIMP Palette", "Name:", "Columns:")):
            continue

        parts = line.split(None, 3)

        if len(parts) < 3:
            continue

        try:
            r, g, b = int(parts[0]), int(parts[1]), int(parts[2])
        except ValueError:
            continue

        name = parts[3].strip() if len(parts) > 3 else ""

        if name in _PALETTE_EXCLUDE_NAMES:
            continue

        colors.append((r, g, b))
        names.append(name)

    if not colors:
        raise ValueError(f"No colours parsed from palette at {path}")

    return Palette(tuple(colors), tuple(names))


# ---------------------------------------------------------------------------
# Pipeline stages


def crop_to_opaque(image: Image.Image, margin: int = 0) -> Image.Image:
    """Crops to the tight bounding box of non-transparent content. A master
    with no alpha channel (a flat photograph, not yet cut out) is returned
    unchanged — cropping only ever removes transparent margin, never guesses
    at a background colour to key out."""

    if image.mode != "RGBA":
        return image

    bbox = image.split()[3].getbbox()

    if bbox is None:
        return image

    if margin:
        left, top, right, bottom = bbox
        bbox = (
            max(0, left - margin),
            max(0, top - margin),
            min(image.width, right + margin),
            min(image.height, bottom + margin),
        )

    return image.crop(bbox)


def staged_downscale(
    image: Image.Image,
    target_height: int,
    unsharp_radius: float = 2.0,
    unsharp_percent: int = 150,
    unsharp_threshold: int = 2,
) -> Image.Image:
    """Box-downscale to twice the target size, unsharp, then box-downscale
    to the target — the recipe SpriteLibrary.cs records for the Barbarian
    repaint. The single-step downscale this replaces has to blur hard
    enough to avoid aliasing a 2500px painting straight down to 64px, which
    loses the linework the drawing is legible from; landing at an
    intermediate size first and re-sharpening there recovers most of it
    before the final reduction.

    Alpha is resized alongside colour but never sharpened — an unsharp pass
    on the alpha channel haloes the silhouette's edge with semi-transparent
    fringing, which is exactly what the hard-alpha step after this one has
    to clean up blindly. Colour and alpha are treated separately here so
    that step gets a clean edge to threshold instead.
    """

    if target_height <= 0:
        raise ValueError("target_height must be positive")

    if image.height <= 0:
        raise ValueError("image has zero height")

    scale = target_height / image.height
    final_size = (max(1, round(image.width * scale)), target_height)

    intermediate_height = max(target_height * 2, target_height + 1)
    intermediate_scale = intermediate_height / image.height
    intermediate_size = (
        max(1, round(image.width * intermediate_scale)),
        intermediate_height,
    )

    # Never upscale past the source: a master already smaller than the
    # intermediate size skips straight to the final box downscale.
    if intermediate_size[0] >= image.width or intermediate_size[1] >= image.height:
        stage1 = image
    else:
        rgb = image.convert("RGB").resize(intermediate_size, Image.Resampling.BOX)
        rgb = rgb.filter(
            ImageFilter.UnsharpMask(
                radius=unsharp_radius,
                percent=unsharp_percent,
                threshold=unsharp_threshold,
            )
        )
        alpha = image.split()[3].resize(intermediate_size, Image.Resampling.BOX)
        stage1 = Image.merge("RGBA", (*rgb.split(), alpha))

    if stage1.size == final_size:
        return stage1

    return stage1.resize(final_size, Image.Resampling.BOX)


def harden_alpha(image: Image.Image, threshold: int = ALPHA_THRESHOLD) -> Image.Image:
    """Snaps every pixel to fully opaque or fully transparent. Downscaling
    leaves a fringe of partial alpha at every edge; a soft edge photographs
    fine but reads as a grey-brown halo once nearest-neighbour resampling
    draws it at the board's non-integer camera scales — the same failure
    mode #238 diagnosed for colour, just on the alpha channel instead."""

    r, g, b, a = image.convert("RGBA").split()
    hard_a = a.point(lambda v: 255 if v >= threshold else 0)
    return Image.merge("RGBA", (r, g, b, hard_a))


def _nearest_palette_color(
    color: tuple[int, int, int], candidates: tuple[tuple[int, int, int], ...]
) -> tuple[int, int, int]:
    return min(
        candidates,
        key=lambda c: (c[0] - color[0]) ** 2 + (c[1] - color[1]) ** 2 + (c[2] - color[2]) ** 2,
    )


def quantize_to_palette(
    image: Image.Image, palette: Palette, reduce_colors: int = REDUCE_COLORS_K
) -> Image.Image:
    """Maps every opaque pixel to a colour in the fixed master palette, in
    two passes rather than one direct nearest-colour lookup per pixel.

    Pass one clusters this image's own pixels down to `reduce_colors`
    representative colours with Pillow's median-cut quantizer — a
    deterministic splitting procedure, not a randomly-seeded k-means, and
    run against nothing but this one image's own downscaled content. Pass
    two snaps each of those *representative* colours (never each raw pixel)
    to its nearest match in the master palette, and every pixel belonging
    to that cluster follows it.

    Mapping every pixel straight to its nearest of the master palette's 52
    colours (the direct approach this replaced) technically conforms —
    every output pixel is a real palette colour — but leaves the grain
    #238 diagnosed intact: a photographic gradient's barely-different
    adjacent shades land on different, sometimes unrelated ramps, because
    each pixel is judged in isolation against the whole 52-colour menu.
    Clustering first groups a gradient into one region, so the pixels in it
    agree on a single palette colour instead of splitting between two.

    This clustering is per-image, independent, and re-fit from scratch
    every call — it never reads another frame's pixels and never persists
    anything between calls. That is what keeps it out of PR #238's failure
    mode: there is no shared vote for a small canvas to lose, because
    nothing is ever shared across canvases in the first place.
    """

    rgb = image.convert("RGB")
    clustered = rgb.quantize(
        colors=reduce_colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE
    )

    cluster_count = len(clustered.getpalette()) // 3
    raw_palette = clustered.getpalette()[: cluster_count * 3]
    cluster_colors = [
        (raw_palette[i], raw_palette[i + 1], raw_palette[i + 2])
        for i in range(0, len(raw_palette), 3)
    ]

    mapped_flat: list[int] = []
    for color in cluster_colors:
        mapped_flat.extend(_nearest_palette_color(color, palette.colors))

    pad_color = palette.colors[-1]
    while len(mapped_flat) < 256 * 3:
        mapped_flat.extend(pad_color)

    remapped = clustered.copy()
    remapped.putpalette(mapped_flat)
    return remapped.convert("RGB")


def _neighbourhoods(width: int, height: int, x: int, y: int) -> list[tuple[int, int]]:
    coords = []
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0:
                continue
            nx, ny = x + dx, y + dy
            if 0 <= nx < width and 0 <= ny < height:
                coords.append((nx, ny))
    return coords


def degrain(
    image: Image.Image,
    passes: int = DEFAULT_DEGRAIN_PASSES,
    agreement: int = DEFAULT_DEGRAIN_AGREEMENT,
) -> Image.Image:
    """Folds an isolated pixel into its neighbourhood's majority colour.

    A pixel is "isolated" when none of its (up to 8) same-alpha-state
    neighbours share its colour. It is only folded when one neighbour
    colour holds at least `agreement` of those neighbours (default 5 of 8)
    — a real edge or a deliberate single-pixel highlight sits among mixed
    neighbours and is left alone; only a clear, near-unanimous local
    majority overrides a pixel that agrees with none of it.

    This is the same idea PR #238's consolidation pass had — flatten the
    grain a per-pixel quantize leaves behind — run the way that avoids its
    bug: strictly within one frame's own 3x3 neighbourhoods. It never reads
    another frame, another pose, or another character's pixels, so a small
    canvas can never be outvoted by a larger sibling — there are no
    siblings in scope at all.

    Opaque and transparent pixels are never merged into each other: a
    neighbour only counts if it shares the centre pixel's opacity. That
    keeps this pass from eroding or growing the silhouette — it cleans
    colour noise inside a region and leaves the hard alpha edge exactly
    where the previous stage put it.
    """

    rgba = image.convert("RGBA")
    width, height = rgba.size
    pixels = rgba.load()

    for _ in range(passes):
        source = [[pixels[x, y] for x in range(width)] for y in range(height)]
        changes: list[tuple[int, int, tuple[int, int, int, int]]] = []

        for y in range(height):
            for x in range(width):
                center = source[y][x]
                center_opaque = center[3] >= ALPHA_THRESHOLD

                neighbours = [
                    source[ny][nx]
                    for nx, ny in _neighbourhoods(width, height, x, y)
                    if (source[ny][nx][3] >= ALPHA_THRESHOLD) == center_opaque
                ]

                if not neighbours:
                    continue

                if any(n[:3] == center[:3] for n in neighbours):
                    continue  # not isolated — at least one neighbour agrees

                if not center_opaque:
                    continue  # never invent colour inside transparent regions

                tally: dict[tuple[int, int, int], int] = {}
                order: list[tuple[int, int, int]] = []
                for n in neighbours:
                    key = n[:3]
                    if key not in tally:
                        order.append(key)
                    tally[key] = tally.get(key, 0) + 1

                # Deterministic tie-break: first-seen in scan order (which
                # is itself fixed — top-to-bottom, left-to-right) rather
                # than dict iteration order or a random choice.
                best_color, best_count = max(
                    ((c, tally[c]) for c in order), key=lambda item: item[1]
                )

                if best_count >= agreement:
                    changes.append((x, y, (*best_color, center[3])))

        for x, y, color in changes:
            pixels[x, y] = color

    return rgba


def process_master(
    stem: str,
    palette: Palette,
    target_height: int | None = None,
    reduce_colors: int | None = None,
    degrain_passes: int | None = None,
    degrain_agreement: int | None = None,
    masters_dir: Path = MASTERS_DIR,
) -> Image.Image:
    """Runs the full pipeline on one master file, returning the finished
    RGBA frame. Deterministic and side-effect-free — writing the result to
    disk is the caller's job."""

    overrides = SPRITE_TARGETS.get(stem, {})
    height = target_height or overrides.get("target_height", TARGET_HEIGHT)
    colors = reduce_colors or overrides.get("reduce_colors", REDUCE_COLORS_K)
    passes = (
        degrain_passes
        if degrain_passes is not None
        else overrides.get("degrain_passes", DEFAULT_DEGRAIN_PASSES)
    )
    agreement = (
        degrain_agreement
        if degrain_agreement is not None
        else overrides.get("degrain_agreement", DEFAULT_DEGRAIN_AGREEMENT)
    )

    source_path = masters_dir / f"{stem}.png"
    if not source_path.exists():
        raise FileNotFoundError(
            f"No master at {source_path} (masters are .png cutouts; "
            f".jpeg siblings are raw camera captures, not pipeline input)"
        )

    image = Image.open(source_path).convert("RGBA")
    image = crop_to_opaque(image)
    image = staged_downscale(image, height)
    image = harden_alpha(image)
    rgb_quantized = quantize_to_palette(image, palette, reduce_colors=colors)
    reassembled = Image.merge("RGBA", (*rgb_quantized.split(), image.split()[3]))
    return degrain(reassembled, passes=passes, agreement=agreement)


def process_terrain(
    path: Path,
    palette: Palette,
    reduce_colors: int | None = None,
    degrain_passes: int | None = None,
    degrain_agreement: int | None = None,
) -> Image.Image:
    """Runs the palette + de-grain half of the pipeline on an already-final
    terrain tile.

    Terrain has no separate high-resolution master in this repo — Ground,
    Wall, Low and Difficult tiles are Brandon's own art, committed directly
    at the size the board draws (see client/README.md's "battlefield has
    its own art too"). So there is nothing to crop or downscale: this is
    steps 3 and 4 of the pipeline only, which is exactly why the three
    terrain themes can currently be three different colour media — nobody
    ever ran even a palette-mapping pass over them. Same functions, same
    determinism, same conformance guarantee as a character master.
    """

    colors = reduce_colors if reduce_colors is not None else REDUCE_COLORS_K
    passes = degrain_passes if degrain_passes is not None else DEFAULT_DEGRAIN_PASSES
    agreement = degrain_agreement if degrain_agreement is not None else DEFAULT_DEGRAIN_AGREEMENT

    image = Image.open(path).convert("RGBA")
    image = harden_alpha(image)
    rgb_quantized = quantize_to_palette(image, palette, reduce_colors=colors)
    reassembled = Image.merge("RGBA", (*rgb_quantized.split(), image.split()[3]))
    return degrain(reassembled, passes=passes, agreement=agreement)


# ---------------------------------------------------------------------------
# Conformance measurement — the review's own colour-count check, as one
# runnable command instead of an eyeballed claim.


@dataclass(frozen=True)
class ConformanceReport:
    path: Path
    opaque_pixels: int
    distinct_colors: int
    colors_in_palette: int

    @property
    def fully_conformant(self) -> bool:
        return self.distinct_colors == self.colors_in_palette

    @property
    def pixel_conformance_pct(self) -> float:
        return 100.0 if self.opaque_pixels == 0 else self._pixel_hits / self.opaque_pixels * 100.0

    _pixel_hits: int = 0


def measure_conformance(path: Path, palette: Palette) -> ConformanceReport:
    image = Image.open(path).convert("RGBA")
    members = palette.membership()

    colors = image.getcolors(maxcolors=1_000_000) or []
    opaque_colors = [(count, rgba) for count, rgba in colors if rgba[3] >= ALPHA_THRESHOLD]

    distinct = {rgba[:3] for _, rgba in opaque_colors}
    in_palette = distinct & members
    opaque_pixels = sum(count for count, _ in opaque_colors)
    pixel_hits = sum(count for count, rgba in opaque_colors if rgba[:3] in members)

    return ConformanceReport(
        path=path,
        opaque_pixels=opaque_pixels,
        distinct_colors=len(distinct),
        colors_in_palette=len(in_palette),
        _pixel_hits=pixel_hits,
    )


def _iter_shipped_images() -> list[Path]:
    if not SPRITES_DIR.exists():
        return []
    return sorted(SPRITES_DIR.rglob("*.png"))


# ---------------------------------------------------------------------------
# CLI


def _cmd_list(_args: argparse.Namespace) -> int:
    if not MASTERS_DIR.exists():
        print(f"No masters directory at {MASTERS_DIR}")
        return 1

    stems = sorted({p.stem for p in MASTERS_DIR.glob("*.png")})
    print(f"{len(stems)} masters (.png cutouts) in {MASTERS_DIR}:\n")

    for stem in stems:
        shipped = "shipped" if (SPRITES_DIR / _guess_folder(stem)).exists() else "unshipped"
        print(f"  {stem:<28} {shipped}")

    return 0


def _guess_folder(stem: str) -> str:
    """A best-effort PascalCase guess at the matching sprite folder, for the
    `list` command's shipped/unshipped hint only — not used by the actual
    pipeline, and not a claim about which pool name a master belongs to.
    That mapping is #295's job."""

    return "_".join(part.capitalize() for part in stem.split("_"))


def _cmd_process(args: argparse.Namespace) -> int:
    palette = load_palette()
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    result = process_master(
        args.stem,
        palette,
        target_height=args.target_height,
        reduce_colors=args.reduce_colors,
        degrain_passes=args.degrain_passes,
        degrain_agreement=args.degrain_agreement,
    )

    out_path = out_dir / f"{args.stem}.png"
    result.save(out_path)

    report = measure_conformance(out_path, palette)
    print(f"{args.stem}: {result.size[0]}x{result.size[1]} -> {out_path}")
    _print_report(report)
    return 0


def _cmd_batch(args: argparse.Namespace) -> int:
    palette = load_palette()
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    exit_code = 0
    for stem in args.stems:
        try:
            result = process_master(stem, palette)
        except FileNotFoundError as exc:
            print(f"{stem}: SKIPPED — {exc}", file=sys.stderr)
            exit_code = 1
            continue

        out_path = out_dir / f"{stem}.png"
        result.save(out_path)
        report = measure_conformance(out_path, palette)
        print(f"{stem}: {result.size[0]}x{result.size[1]} -> {out_path}")
        _print_report(report, indent=2)

    return exit_code


def _cmd_terrain(args: argparse.Namespace) -> int:
    palette = load_palette()
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    exit_code = 0
    for raw_path in args.paths:
        path = Path(raw_path)

        if not path.exists():
            print(f"{path}: SKIPPED — not found", file=sys.stderr)
            exit_code = 1
            continue

        result = process_terrain(
            path,
            palette,
            reduce_colors=args.reduce_colors,
            degrain_passes=args.degrain_passes,
            degrain_agreement=args.degrain_agreement,
        )
        out_path = out_dir / path.name
        result.save(out_path)
        report = measure_conformance(out_path, palette)
        print(f"{path.name}: {result.size[0]}x{result.size[1]} -> {out_path}")
        _print_report(report, indent=2)

    return exit_code


def _print_report(report: ConformanceReport, indent: int = 0) -> None:
    pad = " " * indent
    status = "CONFORMANT" if report.fully_conformant else "not conformant"
    print(
        f"{pad}{status}: {report.colors_in_palette}/{report.distinct_colors} "
        f"colours in palette, {report.opaque_pixels} opaque px, "
        f"{report.pixel_conformance_pct:.1f}% of pixels in palette"
    )


def _cmd_conformance(args: argparse.Namespace) -> int:
    palette = load_palette()

    if args.shipped:
        paths = _iter_shipped_images()
        if not paths:
            print(f"No PNGs found under {SPRITES_DIR}")
            return 1
    else:
        paths = [Path(p) for p in args.paths]

    if not paths:
        print("Nothing to check — pass paths or --shipped.")
        return 1

    conformant = 0
    for path in paths:
        report = measure_conformance(path, palette)
        rel = path.relative_to(REPO_ROOT) if path.is_relative_to(REPO_ROOT) else path
        marker = "OK  " if report.fully_conformant else "FAIL"
        conformant += report.fully_conformant
        print(
            f"{marker} {rel}: {report.colors_in_palette}/{report.distinct_colors} "
            f"colours in palette ({report.pixel_conformance_pct:.1f}% of pixels)"
        )

    print(f"\n{conformant}/{len(paths)} fully conformant to {PALETTE_PATH.name}")
    return 0 if conformant == len(paths) else 1


def _cmd_compare(args: argparse.Namespace) -> int:
    """Builds a side-by-side PNG: master thumbnail | currently shipped
    sprite (if any) | pipeline output — each panel at a shared
    nearest-neighbour zoom so grain is visible where it exists. Written to
    the given output directory; never committed by this script."""

    palette = load_palette()
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    zoom = args.zoom
    master_path = MASTERS_DIR / f"{args.stem}.png"

    if not master_path.exists():
        print(f"No master at {master_path}", file=sys.stderr)
        return 1

    master_img = Image.open(master_path).convert("RGBA")
    master_thumb = master_img.copy()
    master_thumb.thumbnail((256, 256), Image.Resampling.BOX)

    pipeline_out = process_master(args.stem, palette)
    pipeline_report = measure_conformance_image(pipeline_out, palette)
    pipeline_zoomed = pipeline_out.resize(
        (pipeline_out.width * zoom, pipeline_out.height * zoom), Image.Resampling.NEAREST
    )

    shipped_folder = SPRITES_DIR / _guess_folder(args.stem)
    shipped_path = shipped_folder / "Idle.png"
    shipped_zoomed = None
    shipped_report = None

    if shipped_path.exists():
        shipped_img = Image.open(shipped_path).convert("RGBA")
        shipped_report = measure_conformance_image(shipped_img, palette)
        shipped_zoomed = shipped_img.resize(
            (shipped_img.width * zoom, shipped_img.height * zoom), Image.Resampling.NEAREST
        )

    panels = [("master (thumbnail)", master_thumb, None)]
    if shipped_zoomed is not None:
        panels.append((f"currently shipped ({zoom}x nearest)", shipped_zoomed, shipped_report))
    panels.append((f"pipeline output ({zoom}x nearest)", pipeline_zoomed, pipeline_report))

    sheet = _compose_sheet(args.stem, panels)
    out_path = out_dir / f"{args.stem}_compare.png"
    sheet.save(out_path)
    print(f"{args.stem}: comparison sheet -> {out_path}")
    return 0


def measure_conformance_image(image: Image.Image, palette: Palette) -> ConformanceReport:
    """Like measure_conformance, but from an in-memory image rather than a
    path (used by `compare`, which never writes the pipeline output to a
    real sprite path)."""

    members = palette.membership()
    colors = image.convert("RGBA").getcolors(maxcolors=1_000_000) or []
    opaque_colors = [(count, rgba) for count, rgba in colors if rgba[3] >= ALPHA_THRESHOLD]
    distinct = {rgba[:3] for _, rgba in opaque_colors}
    in_palette = distinct & members
    opaque_pixels = sum(count for count, _ in opaque_colors)
    pixel_hits = sum(count for count, rgba in opaque_colors if rgba[:3] in members)

    return ConformanceReport(
        path=Path("<in-memory>"),
        opaque_pixels=opaque_pixels,
        distinct_colors=len(distinct),
        colors_in_palette=len(in_palette),
        _pixel_hits=pixel_hits,
    )


def _compose_sheet(
    title: str, panels: list[tuple[str, Image.Image, ConformanceReport | None]]
) -> Image.Image:
    from PIL import ImageDraw

    label_height = 36
    padding = 16
    max_panel_height = max(im.height for _, im, _ in panels)
    total_width = sum(im.width for _, im, _ in panels) + padding * (len(panels) + 1)
    total_height = max_panel_height + label_height * 2 + padding * 2

    sheet = Image.new("RGB", (total_width, total_height), (30, 30, 34))
    draw = ImageDraw.Draw(sheet)
    draw.text((padding, 6), title, fill=(230, 230, 225))

    x = padding
    for label, panel, report in panels:
        y = label_height + padding + (max_panel_height - panel.height)
        if panel.mode == "RGBA":
            sheet.paste(panel, (x, y), panel)
        else:
            sheet.paste(panel, (x, y))

        caption = label
        if report is not None:
            status = "conformant" if report.fully_conformant else f"{report.colors_in_palette}/{report.distinct_colors} in palette"
            caption = f"{label} - {status}"

        draw.text((x, y + panel.height + 4), caption, fill=(200, 200, 195))
        x += panel.width + padding

    return sheet


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)

    p_list = sub.add_parser("list", help="list masters and whether each is shipped")
    p_list.set_defaults(func=_cmd_list)

    p_process = sub.add_parser("process", help="run the pipeline on one master")
    p_process.add_argument("stem", help="master filename without extension, e.g. 'ogre'")
    p_process.add_argument("--out", required=True, help="output directory")
    p_process.add_argument("--target-height", type=int, default=None)
    p_process.add_argument("--reduce-colors", type=int, default=None)
    p_process.add_argument("--degrain-passes", type=int, default=None)
    p_process.add_argument("--degrain-agreement", type=int, default=None)
    p_process.set_defaults(func=_cmd_process)

    p_batch = sub.add_parser("batch", help="run the pipeline on several masters")
    p_batch.add_argument("stems", nargs="+")
    p_batch.add_argument("--out", required=True, help="output directory")
    p_batch.set_defaults(func=_cmd_batch)

    p_terrain = sub.add_parser(
        "terrain", help="run the palette + de-grain steps on already-final terrain tiles"
    )
    p_terrain.add_argument("paths", nargs="+", help="terrain PNG paths, e.g. client/assets/sprites/Terrain/Wall_Woodland.png")
    p_terrain.add_argument("--out", required=True, help="output directory")
    p_terrain.add_argument("--reduce-colors", type=int, default=None)
    p_terrain.add_argument("--degrain-passes", type=int, default=None)
    p_terrain.add_argument("--degrain-agreement", type=int, default=None)
    p_terrain.set_defaults(func=_cmd_terrain)

    p_conf = sub.add_parser("conformance", help="palette conformance check")
    p_conf.add_argument("paths", nargs="*", help="PNG files to check")
    p_conf.add_argument(
        "--shipped", action="store_true", help="check every PNG under client/assets/sprites"
    )
    p_conf.set_defaults(func=_cmd_conformance)

    p_compare = sub.add_parser(
        "compare", help="build a master | shipped | pipeline-output comparison sheet"
    )
    p_compare.add_argument("stem")
    p_compare.add_argument("--out", required=True, help="output directory")
    p_compare.add_argument("--zoom", type=int, default=6, help="nearest-neighbour zoom factor")
    p_compare.set_defaults(func=_cmd_compare)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
