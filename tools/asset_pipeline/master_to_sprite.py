#!/usr/bin/env python3
"""master_to_sprite.py — the committed master-to-sprite pipeline (issue #294).

Turns one of Brandon's full-resolution paintings in ``client/assets/masters/``
into a sprite frame the client can ship, by four MECHANICAL steps applied in
order:

    0. facing      — a master painted facing left is mirrored so the emitted
                     sheet faces right, like every other hand-drawn sheet
                     (issue #457). Declared per master in ``MASTER_FACING``
                     below, never inferred from pixels and never done to the
                     master file itself — see that table's own comment.
    1. crop        — trim to the opaque bounding box (transparent margin
                     gone, feet land on the new canvas's bottom edge, per the
                     client's own "feet on the bottom edge" convention).
    2. downscale   — a two-stage box filter with an unsharp pass at the
                     intermediate size, the recipe recorded on
                     ``SpriteLibrary.ByClassName`` for the Barbarian repaint.
    3. hard alpha  — every pixel snapped to fully opaque or fully
                     transparent, so non-integer camera scales never draw a
                     semi-transparent halo around the silhouette.

**The pipeline is mechanical-only — it never reinterprets colour. That is a
policy, not an omission.** Earlier versions of this script carried two more
steps: a palette pass (per-image median-cut clustering, then snapping the
clusters to the fixed master palette at ``client/assets/palette/
SRD_Combat.gpl``) and a de-grain pass (folding isolated pixels into their
neighbourhood majority). Both were removed at Brandon's direction on
2026-08-26, after every art-pipeline failure this project has had traced to
a colour-reinterpreting step and none to a mechanical one:

    - PR #238's de-graining/consolidation pass was merged, then reverted
      whole at Brandon's direction ("revert back to the original art. i'll
      adjust on my own").
    - PR #461's facing fix was first built as pipeline regeneration of the
      shipped sheets; Brandon rejected it, and it shipped as lossless
      in-place mirrors instead.
    - PR #446 (Bugbear Warrior) was withheld at his before/after with
      "looks like it's made of metal" — the palette pass mapping his warm
      tones onto the project palette. Issue #458 asked whether conformance
      desaturates warm tones at sprite scale; this policy answers it by
      removing the question.

So: colour is Brandon's alone. What comes out of this script is his own
paint, moved and resized, with a hardened edge — nothing else. Palette
coherence across the roster comes from his hand and his palette file, not
from a script snapping his pixels to it after the fact. The retired colour
machinery (``quantize_to_palette``, ``degrain``, the conformance commands,
and the long #238 post-mortem that governed them) lives in this file's git
history — see the #427/#446-era revisions — if a *proposal* ever wants it
back; nothing may run it against art by default.

The unsharp pass in step 2 stays on the mechanical side of that line
deliberately: it is part of the resampling recipe (recovering linework a
2500px-to-64px reduction would otherwise blur away), applied uniformly,
approved with the Barbarian repaint, and it maps nothing to a foreign
palette. If a before/after ever shows it misbehaving, it is Brandon's call
like everything else here.

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

    # A master | currently-shipped | pipeline-output comparison sheet, for
    # Brandon's before/after review — never committed by this script:
    python3 tools/asset_pipeline/master_to_sprite.py compare ogre \\
        --out /tmp/preview --zoom 8

    # What masters exist, and whether they already have a shipped folder:
    python3 tools/asset_pipeline/master_to_sprite.py list

Requires only Pillow (``pip install Pillow`` — already on this machine, and
the one dependency ``scripts/doctor.sh`` checks for under "Optional
tooling").

Everything is deterministic: no random seeds anywhere, because nothing here
samples or initialises randomly — a box filter, an unsharp mask, and an
alpha threshold are fixed procedures. Re-running this script against the
same master with the same flags reproduces the same output byte-for-byte.

Geometry contract (#467) — read this before regenerating a shipped sheet
--------------------------------------------------------------------------

**The sheets shipped today predate this pipeline's current settings.** Most
of the roster was hand-processed, hand-tuned per image, or produced by an
earlier revision of this script before TARGET_HEIGHT and the two-stage
downscale were settled. Running `process`/`batch` against a master today
does **not** reproduce its shipped sheet's canvas size — PR #461 found this
the hard way, regenerating 17 sheets and silently changing every one of
their dimensions (the Ogre alone went from a shipped 169x169 down to a
pipeline-default 119x64), with nothing in CI catching it before Brandon saw
it break in a live fight.

So: a batch's output is *never* the same size as what it replaces, and that
is expected, not a bug in this script — the size difference belongs to
`compare`'s before/after review, not to a diff you should be surprised by.
What must not happen silently is the sheet actually shipping with new
geometry unreviewed: `tests/SRDCombat.Viewer.Tests/SpriteGeometryTests.cs`
pins every shipped sheet's canvas dimensions and frame count against a
committed manifest (`Fixtures/sprite-geometry-manifest.tsv`), and fails the
suite the moment `client/assets/sprites/` disagrees with it — regardless of
whether the change came through this script, a hand edit, or anything else.
Regenerating that manifest (`SpriteGeometryManifestWriter`, same
un-skip/run/re-skip discipline as the frozen transcript) is part of landing
any batch that changes a shipped sheet's size, in the same commit as the
art.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import PIL
from PIL import Image, ImageFilter

REPO_ROOT = Path(__file__).resolve().parents[2]
MASTERS_DIR = REPO_ROOT / "client" / "assets" / "masters"
SPRITES_DIR = REPO_ROOT / "client" / "assets" / "sprites"

# Determinism is verified against this Pillow version specifically (see the
# module docstring). Resampling internals and PNG encoding are Pillow's own
# to change between releases; "re-runnable by anyone" means recording what
# this was actually run against, not just declaring the dependency — quote
# this line in a batch's PR alongside the before/after.
VERIFIED_PILLOW_VERSION = "10.2.0"
MIN_PILLOW_VERSION = (10, 0)

_pillow_version_parts = tuple(int(p) for p in PIL.__version__.split(".")[:2])
if _pillow_version_parts < MIN_PILLOW_VERSION:
    raise RuntimeError(
        f"Pillow {PIL.__version__} is older than {'.'.join(map(str, MIN_PILLOW_VERSION))}, "
        f"the minimum this pipeline has been verified against (see "
        f"VERIFIED_PILLOW_VERSION). `pip install --upgrade Pillow` first."
    )
elif PIL.__version__ != VERIFIED_PILLOW_VERSION:
    # Below the floor is a hard error (above); anything else that isn't the
    # exact verified version is a warning, not a raise — a newer Pillow is
    # not known-broken, only unverified. This lives at module scope (not
    # inside `main`) so it fires for library callers too, not just the CLI.
    print(
        f"note: running Pillow {PIL.__version__}, verified against "
        f"{VERIFIED_PILLOW_VERSION} — quote this line in a batch's PR if "
        f"output ever looks different on a different machine",
        file=sys.stderr,
    )

# ---------------------------------------------------------------------------
# Per-sprite parameters. "Parameters live in the script, not in anyone's
# head" (the issue's own words) — this table is where a sprite's downscale
# target and crop margin are recorded once a batch is tuned, rather than
# passed by hand on a command line and forgotten. Per-creature *stature*
# policy (should an Ogre stand taller than a Goblin) is #296's decision, not
# this script's — TARGET_HEIGHT below is a pipeline default (Godot client's
# NominalStature, see client/SpriteLibrary.cs) used until a sprite has a
# considered override, not a claim about final in-game size.
TARGET_HEIGHT = 64

# stem (in client/assets/masters/, without extension) -> overrides.
# "target_height" overrides TARGET_HEIGHT; "crop_margin" overrides
# crop_to_opaque's default zero-margin trim (see crop_to_opaque's own
# `margin` parameter). These two are the only keys _resolve_overrides
# recognises — an unknown key raises rather than being silently accepted
# and ignored, the same partly-structured-table shape this project's rule
# doc warns about elsewhere. (The colour-step keys the retired palette and
# de-grain passes used are gone with them; an old entry naming one now
# fails loudly here instead of silently doing nothing.)
_SPRITE_OVERRIDE_KEYS = {
    "target_height",
    "crop_margin",
}

SPRITE_TARGETS: dict[str, dict[str, int]] = {
    # No overrides yet — every master processes at the pipeline default
    # until a specific batch's before/after says otherwise. Add entries
    # here, never as a one-off flag on someone's command line, per the
    # issue's "parameters in the script not in anyone's head" criterion.
}

# A master's native facing, as Brandon actually painted it — filled in only
# by looking at the painting (issue #457's own acceptance criterion: no
# heuristic ever guesses this from pixels). The client assumes every sheet
# it loads faces right — the Craftpix convention — and mirrors a monster's
# sheet at rest so it faces the party (`RestingFacesLeft` in
# client/FightScreen.cs; party art is never mirrored, so it must be
# right-facing to begin with). A master painted facing left ships backwards
# unless this pipeline mirrors it once, here, at generation.
#
# Absent from this table means "painted facing right" — the common case —
# and is never written down for that reason; only "left" is ever a real
# entry, validated below so a typo lands as a loud error rather than a
# silently-ignored key.
#
# #457's audit (visual, one master at a time, recorded in that issue's PR
# body) found two populations among the side-on paintings — front-facing
# poses (most humanoids, the swarms, the sessile plants and oozes) have no
# meaningful facing and are never listed — and confirmed the bug is
# systemic rather than a one-off: Brandon draws a side-on creature facing
# left by habit, and only two of them (Dire Wolf, Giant Hyena) were ever
# caught and hand-corrected before this table existed (client/README.md's
# "two of the first batch went in backwards" note). Giant Hyena, Axe Beak,
# Owlbear, Winter Wolf and Giant Bat are painted left but already ship
# facing right — someone flipped the shipped sheet by hand once, before
# this pipeline could do it at generation — so they are listed here for
# every *future* regeneration to keep doing what the hand fix already did,
# without re-touching their currently-correct shipped sheets.
MASTER_FACING: dict[str, str] = {
    "ogre": "left",
    "goblin_warrior": "left",
    "ankheg": "left",
    "giant_fire_beetle": "left",
    "giant_lizard": "left",
    "giant_rat": "left",
    "giant_vulture": "left",
    "giant_centipede": "left",
    "centaur": "left",
    "hippogriff": "left",
    "manticore": "left",
    "worg": "left",
    "black_dragon_wyrmling": "left",
    "blue_dragon_wyrmling": "left",
    "green_dragon_wyrmling": "left",
    "red_dragon_wyrmling": "left",
    "white_dragon_wyrmling": "left",
    # Already hand-corrected in the shipped sheet (see the note above) —
    # declared so a future regeneration reproduces the same, already-right
    # facing rather than reverting to the master's raw left-facing paint.
    "giant_hyena": "left",
    "axe_beak": "left",
    "owlbear": "left",
    "winter_wolf": "left",
    "giant_bat": "left",
}

_MASTER_FACING_VALUES = {"left", "right"}


def _validate_facing_table() -> None:
    bad = {stem: facing for stem, facing in MASTER_FACING.items() if facing not in _MASTER_FACING_VALUES}
    if bad:
        raise ValueError(
            f"MASTER_FACING has unrecognised facing value(s): {bad} — only "
            f"{sorted(_MASTER_FACING_VALUES)} are valid. A right-facing "
            f"master is never listed at all (see the table's own comment); "
            f"this is not a place for a third state."
        )


_validate_facing_table()


def apply_facing(image: Image.Image, stem: str) -> Image.Image:
    """Mirrors a master painted facing left so the emitted sheet faces
    right — see ``MASTER_FACING``'s own comment for the convention and the
    audit behind it. A stem absent from the table is assumed already
    right-facing (the common case) and is returned unchanged."""

    return (
        image.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        if MASTER_FACING.get(stem, "right") == "left"
        else image
    )


def _validate_override_keys(key: str, overrides: dict, known: set[str], table_name: str) -> None:
    unknown = set(overrides) - known
    if unknown:
        raise ValueError(
            f"{table_name}[{key!r}] has unrecognised key(s) {sorted(unknown)} "
            f"— known keys are {sorted(known)}. An unknown key is silently "
            f"ignored otherwise, which is a promise this table doesn't keep."
        )


def _resolve_overrides(
    overrides: dict,
    known: set[str],
    table_name: str,
    key: str,
    **explicit: int | None,
) -> dict[str, int]:
    """Merges an explicit (CLI) value over a per-item override table over
    the module defaults, with `is not None` throughout — never `or` — so
    that an explicit `0` (or any other falsy-but-valid value) is honoured
    rather than silently replaced by the default."""

    _validate_override_keys(key, overrides, known, table_name)

    defaults = {
        "target_height": TARGET_HEIGHT,
        "crop_margin": 0,
    }

    resolved: dict[str, int] = {}
    for name in known:
        explicit_value = explicit.get(name)
        if explicit_value is not None:
            resolved[name] = explicit_value
        else:
            resolved[name] = overrides.get(name, defaults[name])

    return resolved


ALPHA_THRESHOLD = 128  # hard-alpha cut: >= this is opaque, else transparent


# ---------------------------------------------------------------------------
# Pipeline stages


def crop_to_opaque(image: Image.Image, margin: int = 0) -> Image.Image:
    """Crops to the tight bounding box of non-transparent content. A master
    with no alpha channel (a flat photograph, not yet cut out) is returned
    unchanged — cropping only ever removes transparent margin, never guesses
    at a background colour to key out.

    The no-alpha branch below is dead from `process_master`'s own call site
    today (it always converts to "RGBA" first), but this function is public
    and reused directly — by `compare`, and by anyone poking at the
    pipeline's stages one at a time — so the guard stays for a caller that
    hands in a flat image on purpose.
    """

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

    Both box downscales resize through premultiplied alpha ("RGBa" —
    lowercase a is Pillow's premultiplied mode, distinct from "RGBA"). A
    cutout master's fully-transparent pixels keep whatever RGB the source
    photograph happened to have there (background, desk, whatever was
    behind the painting); resizing straight RGBA averages that leftover
    colour into every opaque pixel near an edge, at every box-filter step.
    Premultiplying first forces a fully-transparent pixel's RGB to exactly
    (0, 0, 0) before the average runs, so it can only ever contribute zero
    weight — un-premultiplying afterward recovers real colour for anything
    that ended up with real coverage. qc's review of PR #427 found and
    measured this "invisible pixels are voting" shape; the fix was verified
    by flattening the transparent region's RGB to a constant — a
    no-visible-change edit that used to move output pixels and no longer
    does.
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
        resized = _resize_premultiplied(image, intermediate_size)
        rgb = resized.convert("RGB").filter(
            ImageFilter.UnsharpMask(
                radius=unsharp_radius,
                percent=unsharp_percent,
                threshold=unsharp_threshold,
            )
        )
        stage1 = Image.merge("RGBA", (*rgb.split(), resized.split()[3]))

    if stage1.size == final_size:
        return stage1

    return _resize_premultiplied(stage1, final_size)


def _resize_premultiplied(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Box-resizes an RGBA image without letting transparent-region RGB
    bleed into opaque neighbours — see staged_downscale's docstring for
    why. ``convert("RGBa")`` is Pillow's premultiplied-alpha mode."""

    return image.convert("RGBa").resize(size, Image.Resampling.BOX).convert("RGBA")


def harden_alpha(image: Image.Image, threshold: int = ALPHA_THRESHOLD) -> Image.Image:
    """Snaps every pixel to fully opaque or fully transparent. Downscaling
    leaves a fringe of partial alpha at every edge; a soft edge photographs
    fine but reads as a grey-brown halo once nearest-neighbour resampling
    draws it at the board's non-integer camera scales — the same failure
    mode #238 diagnosed for colour, just on the alpha channel instead."""

    r, g, b, a = image.convert("RGBA").split()
    hard_a = a.point(lambda v: 255 if v >= threshold else 0)
    return Image.merge("RGBA", (r, g, b, hard_a))


def _validate_pipeline_params(height: int) -> None:
    if height <= 0:
        raise ValueError(f"target_height must be positive, got {height}")


def process_master(
    stem: str,
    target_height: int | None = None,
    crop_margin: int | None = None,
    masters_dir: Path = MASTERS_DIR,
) -> Image.Image:
    """Runs the full pipeline on one master file, returning the finished
    RGBA frame — Brandon's own colours, untouched. Deterministic and
    side-effect-free — writing the result to disk is the caller's job."""

    resolved = _resolve_overrides(
        SPRITE_TARGETS.get(stem, {}),
        _SPRITE_OVERRIDE_KEYS,
        "SPRITE_TARGETS",
        stem,
        target_height=target_height,
        crop_margin=crop_margin,
    )
    _validate_pipeline_params(resolved["target_height"])

    source_path = masters_dir / f"{stem}.png"
    if not source_path.exists():
        raise FileNotFoundError(
            f"No master at {source_path} (masters are .png cutouts; "
            f".jpeg siblings are raw camera captures, not pipeline input)"
        )

    image = Image.open(source_path).convert("RGBA")
    image = apply_facing(image, stem)
    image = crop_to_opaque(image, margin=resolved["crop_margin"])
    image = staged_downscale(image, resolved["target_height"])
    return harden_alpha(image)


# ---------------------------------------------------------------------------
# Neutral image facts — informational only, printed alongside a batch so a
# before/after has numbers to point at. There is no pass/fail here: the
# conformance gate this file used to carry went with the palette pass (see
# the module docstring), and nothing may reintroduce a script deciding
# whether Brandon's colours are "legal".


def _image_stats(image: Image.Image) -> tuple[int, int, int]:
    """(opaque pixels, semi-transparent pixels, distinct opaque colours)."""

    colors = image.convert("RGBA").getcolors(maxcolors=1_000_000) or []
    opaque = [(count, rgba) for count, rgba in colors if rgba[3] == 255]
    semi = sum(count for count, rgba in colors if 0 < rgba[3] < 255)
    distinct = {rgba[:3] for _, rgba in opaque}
    return sum(count for count, _ in opaque), semi, len(distinct)


def _print_stats(image: Image.Image, indent: int = 0) -> None:
    opaque, semi, distinct = _image_stats(image)
    pad = " " * indent
    print(
        f"{pad}{opaque} opaque px, {semi} semi-transparent px, "
        f"{distinct} distinct colours (original paint — no palette step)"
    )


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
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    result = process_master(
        args.stem,
        target_height=args.target_height,
        crop_margin=args.crop_margin,
    )

    out_path = out_dir / f"{args.stem}.png"
    result.save(out_path)

    print(f"{args.stem}: {result.size[0]}x{result.size[1]} -> {out_path}")
    _print_stats(result)
    return 0


def _cmd_batch(args: argparse.Namespace) -> int:
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    exit_code = 0
    for stem in args.stems:
        try:
            result = process_master(stem)
        except FileNotFoundError as exc:
            print(f"{stem}: SKIPPED — {exc}", file=sys.stderr)
            exit_code = 1
            continue

        out_path = out_dir / f"{stem}.png"
        result.save(out_path)
        print(f"{stem}: {result.size[0]}x{result.size[1]} -> {out_path}")
        _print_stats(result, indent=2)

    return exit_code


def _cmd_compare(args: argparse.Namespace) -> int:
    """Builds a side-by-side PNG: master thumbnail | currently shipped
    sprite (if any) | pipeline output — each panel at a shared
    nearest-neighbour zoom so detail is visible. Written to the given
    output directory; never committed by this script."""

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

    pipeline_out = process_master(args.stem)
    pipeline_zoomed = pipeline_out.resize(
        (pipeline_out.width * zoom, pipeline_out.height * zoom), Image.Resampling.NEAREST
    )

    shipped_folder = SPRITES_DIR / _guess_folder(args.stem)
    shipped_path = shipped_folder / "Idle.png"
    shipped_zoomed = None

    if shipped_path.exists():
        shipped_img = Image.open(shipped_path).convert("RGBA")
        shipped_zoomed = shipped_img.resize(
            (shipped_img.width * zoom, shipped_img.height * zoom), Image.Resampling.NEAREST
        )

    panels = [("master (thumbnail)", master_thumb)]
    if shipped_zoomed is not None:
        panels.append((f"currently shipped ({zoom}x nearest)", shipped_zoomed))
    panels.append((f"pipeline output ({zoom}x nearest)", pipeline_zoomed))

    sheet = _compose_sheet(args.stem, panels)
    out_path = out_dir / f"{args.stem}_compare.png"
    sheet.save(out_path)
    print(f"{args.stem}: comparison sheet -> {out_path}")
    _print_stats(pipeline_out, indent=2)
    return 0


def _compose_sheet(title: str, panels: list[tuple[str, Image.Image]]) -> Image.Image:
    from PIL import ImageDraw

    label_height = 36
    padding = 16
    max_panel_height = max(im.height for _, im in panels)
    total_width = sum(im.width for _, im in panels) + padding * (len(panels) + 1)
    total_height = max_panel_height + label_height * 2 + padding * 2

    sheet = Image.new("RGB", (total_width, total_height), (30, 30, 34))
    draw = ImageDraw.Draw(sheet)
    draw.text((padding, 6), title, fill=(230, 230, 225))

    x = padding
    for label, panel in panels:
        y = label_height + padding + (max_panel_height - panel.height)
        if panel.mode == "RGBA":
            sheet.paste(panel, (x, y), panel)
        else:
            sheet.paste(panel, (x, y))

        draw.text((x, y + panel.height + 4), label, fill=(200, 200, 195))
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
    p_process.add_argument("--crop-margin", type=int, default=None)
    p_process.set_defaults(func=_cmd_process)

    p_batch = sub.add_parser("batch", help="run the pipeline on several masters")
    p_batch.add_argument("stems", nargs="+")
    p_batch.add_argument("--out", required=True, help="output directory")
    p_batch.set_defaults(func=_cmd_batch)

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

    # Pillow version check (raise below the floor, warn off the verified
    # version) runs at module import time — see VERIFIED_PILLOW_VERSION
    # above — so it covers library callers as well as this CLI entry point.

    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
