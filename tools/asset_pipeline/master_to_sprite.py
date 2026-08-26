#!/usr/bin/env python3
"""master_to_sprite.py — the committed master-to-sprite pipeline (issue #294).

Turns one of Brandon's full-resolution paintings in ``client/assets/masters/``
into a sprite frame the client can ship, by five mechanical steps applied in
order:

    0. facing    — a master painted facing left is mirrored so the emitted
                   sheet faces right, like every Craftpix pack and every
                   other hand-drawn sheet (issue #457). Declared per master
                   in ``MASTER_FACING`` below, never inferred from pixels and
                   never done to the master file itself — see that table's
                   own comment for the reasoning and the audit it came from.
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

Facing commutes with every step after it — a horizontal mirror does not
change a bounding box's area, a box filter's or unsharp pass's result, a
median-cut fit (pixels, not positions, feed it), or the 3x3-neighbourhood
de-grain majority (the neighbourhood is symmetric under a horizontal flip).
It is applied first anyway, immediately after the master is opened, so it
reads as what it is — a correction to the *source* — rather than something
entangled with cropping or colour.

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

A second version of this script — the one first opened as PR #427 — still
had #238's outvoting shape, just relocated inside a single frame rather
than across a character's poses. `quantize_to_palette`'s median-cut fit ran
over every pixel of the cropped, downscaled image, transparent ones
included, and a cutout master's transparent region keeps whatever RGB the
source photograph happened to have there — desk, background, whatever was
behind the painting. qc's review of that PR measured the effect directly:
flattening the invisible RGB to a constant (zero visible-content change)
moved 26% of the Ogre's *opaque* output pixels to a different final
colour, and the same straight-alpha issue in `staged_downscale`'s box
resizes let that junk bleed into opaque edge pixels before hardening even
ran. Both are fixed the same way — `_opaque_sample` fits the clustering on
opaque pixels only, and `_resize_premultiplied` resizes through
premultiplied alpha so a fully-transparent pixel can only ever contribute
zero — and both fixes were verified the same way qc found the bugs: by
flattening the invisible region to a constant and confirming zero pixels
change. The lesson generalises: "never let one frame's clustering be
decided by pixels that aren't this frame's *visible* content" is the
correct statement of what avoids #238, not just "never share the fit
across frames".

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

    # A master | currently-shipped | pipeline-output comparison sheet, for
    # Brandon's before/after review — never committed by this script:
    python3 tools/asset_pipeline/master_to_sprite.py compare ogre \\
        --out /tmp/preview --zoom 8

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
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import PIL
from PIL import Image, ImageFilter

REPO_ROOT = Path(__file__).resolve().parents[2]
MASTERS_DIR = REPO_ROOT / "client" / "assets" / "masters"
SPRITES_DIR = REPO_ROOT / "client" / "assets" / "sprites"
PALETTE_PATH = REPO_ROOT / "client" / "assets" / "palette" / "SRD_Combat.gpl"

# Determinism is verified against this Pillow version specifically (see the
# module docstring). Median-cut's internals and PNG encoding are Pillow's
# own to change between releases; "re-runnable by anyone" means recording
# what this was actually run against, not just declaring the dependency —
# quote this line in a batch's PR alongside the conformance numbers.
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
    # exact verified version is a warning, not a raise — median-cut internals
    # and PNG encoding are Pillow's own to change between releases, but a
    # newer Pillow is not known-broken, only unverified. This lives at module
    # scope (not inside `main`) so it fires for library callers too, not just
    # the CLI.
    print(
        f"note: running Pillow {PIL.__version__}, verified against "
        f"{VERIFIED_PILLOW_VERSION} — quote this line in a batch's PR if "
        f"output ever looks different on a different machine",
        file=sys.stderr,
    )

# Whether a sprite may legally wear board-background-ref, (22,22,29) — the
# colour the board itself paints under everything — is an OPEN QUESTION for
# Brandon, not something this script decides. An earlier version of this
# file excluded it unilaterally ("a sprite shouldn't blend into the floor"),
# which sounded right until qc's review of PR #427 found it wrong on the
# evidence: Brandon's own already-approved Barbarian frame wears that exact
# colour today, and the exclusion was silently re-scoring his approved art
# as non-conformant while inventing a divergence from the review's own
# 52-entry check (docs/2026-08-21-project-review.md) that the PR then
# mis-described as reproducing that check exactly. So this set is empty —
# every one of the 52 lines counts as legal, matching the review — until
# Brandon's batch review says otherwise. If it does, add the excluded
# name(s) here and EXPECTED_PALETTE_COLORS below drops by the same count.
_PALETTE_EXCLUDE_NAMES: set[str] = set()

# The .gpl's colour-line count, fixed by the source file. Per this
# project's own extraction lesson ("exact counts for totals fixed by the
# source, floors only for what should grow"), this is an assertion, not a
# floor: load_palette raises rather than silently shipping a
# different-sized palette because one line failed to parse or a name got
# added to _PALETTE_EXCLUDE_NAMES without updating this constant.
EXPECTED_PALETTE_COLORS = 52 - len(_PALETTE_EXCLUDE_NAMES)

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
# neighbours) needed before an isolated pixel is folded; "crop_margin"
# overrides crop_to_opaque's default zero-margin trim (see
# crop_to_opaque's own `margin` parameter). These five are the only keys
# _resolve_overrides recognises — an unknown key raises rather than being
# silently accepted and ignored, the same partly-structured-table shape
# this project's rule doc warns about elsewhere.
_SPRITE_OVERRIDE_KEYS = {
    "target_height",
    "reduce_colors",
    "degrain_passes",
    "degrain_agreement",
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
# without this PR re-touching their currently-correct shipped sheets (no
# visible change to justify a before/after for those five).
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


# Terrain's analog of SPRITE_TARGETS, keyed by filename (terrain tiles are
# addressed by path, not by a master stem — see process_terrain). No crop
# or downscale step applies to terrain (it has no separate high-resolution
# master), so "target_height" and "crop_margin" are not recognised here.
_TERRAIN_OVERRIDE_KEYS = {"reduce_colors", "degrain_passes", "degrain_agreement"}

TERRAIN_TARGETS: dict[str, dict[str, int]] = {
    # e.g. "Wall_Woodland.png": {"reduce_colors": 8} once a terrain batch
    # is actually tuned — nothing here yet, same reasoning as SPRITE_TARGETS.
}


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
        "reduce_colors": REDUCE_COLORS_K,
        "degrain_passes": DEFAULT_DEGRAIN_PASSES,
        "degrain_agreement": DEFAULT_DEGRAIN_AGREEMENT,
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
    Image.quantize's tie-breaking deterministic).

    Fails loudly on anything that isn't a blank line, a `#` comment, a
    recognised header, or a valid `R G B [name]` colour line — a line this
    project's own artist wrote wrong should stop a run, not silently shrink
    the palette by one entry (the "wrapped class list dropped 39 of 339
    spells while a floor test stayed green" lesson, applied here as an
    exact-count assertion instead of a floor).
    """

    colors: list[tuple[int, int, int]] = []
    names: list[str] = []

    for lineno, raw_line in enumerate(path.read_text().splitlines(), start=1):
        line = raw_line.strip()

        if not line or line.startswith("#"):
            continue
        if line.startswith(("GIMP Palette", "Name:", "Columns:")):
            continue

        parts = line.split(None, 3)

        if len(parts) < 3:
            raise ValueError(
                f"{path}:{lineno}: expected 'R G B [name]', got {raw_line!r}"
            )

        try:
            r, g, b = int(parts[0]), int(parts[1]), int(parts[2])
        except ValueError as exc:
            raise ValueError(
                f"{path}:{lineno}: non-integer colour component in {raw_line!r}"
            ) from exc

        name = parts[3].strip() if len(parts) > 3 else ""

        if name in _PALETTE_EXCLUDE_NAMES:
            continue

        colors.append((r, g, b))
        names.append(name)

    if len(colors) != EXPECTED_PALETTE_COLORS:
        raise ValueError(
            f"{path}: expected exactly {EXPECTED_PALETTE_COLORS} colours "
            f"(52 lines in the source file, minus {len(_PALETTE_EXCLUDE_NAMES)} "
            f"excluded by name), parsed {len(colors)}. Either the .gpl file "
            f"changed shape or _PALETTE_EXCLUDE_NAMES/EXPECTED_PALETTE_COLORS "
            f"are out of sync — update them together, deliberately, never one "
            f"without the other."
        )

    return Palette(tuple(colors), tuple(names))


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
    pipeline's stages one at a time, as this PR's own testing did — so the
    guard stays for a caller that hands in a flat image on purpose.
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
    that ended up with real coverage. This is the same "invisible pixels
    are voting" shape qc's review of PR #427 found in the clustering step
    below, one stage earlier: verified fixed by re-running with the
    transparent region's RGB flattened to a constant before this function
    ran — a no-visible-change edit that used to move output pixels and no
    longer does.
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


def _nearest_palette_color(
    color: tuple[int, int, int], candidates: tuple[tuple[int, int, int], ...]
) -> tuple[int, int, int]:
    return min(
        candidates,
        key=lambda c: (c[0] - color[0]) ** 2 + (c[1] - color[1]) ** 2 + (c[2] - color[2]) ** 2,
    )


def _opaque_sample(image: Image.Image, threshold: int = ALPHA_THRESHOLD) -> Image.Image:
    """A compact 1-tall image holding only this frame's opaque pixels, in
    scan order, position discarded.

    Used to *fit* the median-cut clustering below on visible content only.
    A cutout master's transparent region keeps whatever RGB the source
    photograph had there — background, desk, whatever — and that RGB is
    never displayed, but it is real data sitting in the same `.convert
    ("RGB")` call a naive fit would hand to `quantize()`. qc's review of PR
    #427 measured its effect directly: on the Ogre master, flattening the
    transparent region to a constant colour (zero visible-content change)
    moved 26% of the *opaque* output pixels to a different final colour.
    That is #238's outvoting shape recurring inside a single frame — an
    invisible majority deciding a visible minority's palette assignment —
    so the fit must never see a pixel nobody will ever see.
    """

    rgba = image.convert("RGBA")
    opaque_rgb = [pixel[:3] for pixel in rgba.getdata() if pixel[3] >= threshold]

    if not opaque_rgb:
        # Fully transparent input: nothing visible to fit on. One arbitrary
        # colour keeps quantize() well-defined; every pixel using it is
        # discarded by the caller's own alpha mask regardless (a
        # fully-transparent frame survives every stage without crashing).
        opaque_rgb = [(0, 0, 0)]

    sample = Image.new("RGB", (len(opaque_rgb), 1))
    sample.putdata(opaque_rgb)
    return sample


def quantize_to_palette(
    image: Image.Image, palette: Palette, reduce_colors: int = REDUCE_COLORS_K
) -> Image.Image:
    """Maps every opaque pixel to a colour in the fixed master palette, in
    two passes rather than one direct nearest-colour lookup per pixel.

    Pass one clusters this image's *opaque* pixels down to `reduce_colors`
    representative colours with Pillow's median-cut quantizer — a
    deterministic splitting procedure, not a randomly-seeded k-means, fit
    on nothing but this one image's own visible content (see
    `_opaque_sample`). Pass two snaps each of those *representative*
    colours (never each raw pixel) to its nearest match in the master
    palette, and every pixel belonging to that cluster follows it. The
    resulting small palette is then applied — nearest-cluster assignment,
    not a re-fit — to every pixel in the full image, transparent ones
    included; that assignment is thrown away by the caller's alpha mask for
    any pixel that isn't opaque, so it only has to be well-defined, not
    meaningful.

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
    nothing is ever shared across canvases in the first place. Restricting
    the fit to opaque pixels is the same principle applied one level
    deeper: no *invisible* population gets a vote either.
    """

    rgba = image.convert("RGBA")
    rgb = rgba.convert("RGB")

    fit_source = _opaque_sample(rgba)
    fitted = fit_source.quantize(
        colors=reduce_colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE
    )

    cluster_count = len(fitted.getpalette()) // 3
    raw_palette = fitted.getpalette()[: cluster_count * 3]
    cluster_colors = [
        (raw_palette[i], raw_palette[i + 1], raw_palette[i + 2])
        for i in range(0, len(raw_palette), 3)
    ]

    # Apply the fitted clusters to every pixel of the *actual* image —
    # nearest-cluster assignment against a fixed palette, not a re-fit — so
    # transparent-region pixels get some assignment (irrelevant, discarded
    # by the caller) without ever influencing what the clusters were.
    fit_reference = Image.new("P", (1, 1))
    fit_reference.putpalette(_padded_flat_palette(cluster_colors))
    assigned = rgb.quantize(palette=fit_reference, dither=Image.Dither.NONE)

    mapped_colors = [_nearest_palette_color(color, palette.colors) for color in cluster_colors]

    remapped = assigned.copy()
    remapped.putpalette(_padded_flat_palette(mapped_colors))
    return remapped.convert("RGB")


def _padded_flat_palette(colors: list[tuple[int, int, int]]) -> list[int]:
    """Flattens a list of RGB tuples into the 256*3-length list
    `Image.putpalette` wants, padding with the last real colour so an
    unused index can never be picked by a nearest-colour distance against
    real content."""

    flat: list[int] = []
    for r, g, b in colors:
        flat.extend((r, g, b))

    pad_color = colors[-1]
    while len(flat) < 256 * 3:
        flat.extend(pad_color)

    return flat


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
    colour holds a clear majority of those neighbours — a real edge or a
    deliberate single-pixel highlight sits among mixed neighbours and is
    left alone; only a near-unanimous local majority overrides a pixel that
    agrees with none of it. `agreement` (default 5) is that threshold
    *scaled to an 8-neighbour interior pixel* — a border pixel with fewer
    neighbours needs `round(agreement * available / 8)` of them, not
    `agreement` outright, which would be unreachable at a corner (3
    neighbours can never reach 5) and needlessly strict at an edge (5 of 5
    unanimity instead of the interior's 5 of 8). Without this scaling,
    verified: an isolated corner speck survives while an identical interior
    speck folds, so a sprite's silhouette edge and a terrain tile's border
    would keep more grain than its interior for no reason but geometry.

    `available` is the pixel's *geometric* neighbour count — how many cells
    of its 3x3 block the canvas actually has — never the same-alpha-state
    count above. Scaling by the same-alpha count instead was tried and is
    wrong: every silhouette-adjacent pixel (not just canvas borders) then
    has a reduced `available`, since the opposite-alpha side of the block is
    excluded from it too, so the fold threshold drops exactly where a
    deliberate single-pixel highlight — a weapon tip, a thin outline — most
    needs the interior's full unanimity bar to stay protected. Scaling by
    geometry alone leaves those pixels at the interior's 5-of-8 (or whatever
    `agreement` is out of 8) while still relaxing the bar at an actual
    canvas edge or corner, which is the only place fewer cells genuinely
    exist to agree.

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

    **Not wrap-aware.** A terrain tile is meant to repeat — the board tiles
    it edge-to-edge — but this pass judges a border pixel's neighbourhood
    from the tile's own edge inward only, never from the *opposite* edge
    the tiled board would actually place next to it. A tile's border can
    therefore end up processed differently than its interior would suggest,
    and a residue that is grid-aligned rather than random. Left as a known
    limitation rather than fixed here: making this wrap-aware means the
    caller must say whether an image tiles at all (a character sprite does
    not; a Ground/Wall/Difficult terrain strip does along one axis), which
    is a real design decision for whoever tunes the first terrain batch,
    not something to guess at in this pass.
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

                # Scaled to the pixel's *geometric* neighbour count — how
                # many cells of its 3x3 block the canvas has — not
                # `len(neighbours)` (the same-alpha-state count filtered
                # above). Scaling by the filtered count was tried and wrong:
                # it shrinks `available` on every silhouette-adjacent pixel,
                # not just canvas borders, folding exactly the deliberate
                # single-pixel highlights (a weapon tip, a thin outline) the
                # docstring promises to leave alone. See the docstring's
                # border-geometry note. A true interior pixel (8 geometric
                # neighbours) sees no change from the old flat comparison; a
                # true corner (3) or edge (5) of the *canvas* gets a
                # correspondingly lower bar instead of an unreachable or
                # needlessly strict one.
                required = max(
                    1, round(agreement * len(_neighbourhoods(width, height, x, y)) / 8)
                )

                if best_count >= required:
                    changes.append((x, y, (*best_color, center[3])))

        for x, y, color in changes:
            pixels[x, y] = color

    return rgba


def _validate_pipeline_params(
    colors: int, passes: int, agreement: int, height: int | None = None
) -> None:
    """`height` is None for terrain, which has no downscale step at all —
    only a character master's target_height is checked."""

    if height is not None and height <= 0:
        raise ValueError(f"target_height must be positive, got {height}")
    if colors < 1:
        raise ValueError(f"reduce_colors must be at least 1, got {colors}")
    if passes < 0:
        raise ValueError(f"degrain_passes must be non-negative, got {passes}")
    if not (0 <= agreement <= 8):
        raise ValueError(f"degrain_agreement must be between 0 and 8, got {agreement}")


def process_master(
    stem: str,
    palette: Palette,
    target_height: int | None = None,
    reduce_colors: int | None = None,
    degrain_passes: int | None = None,
    degrain_agreement: int | None = None,
    crop_margin: int | None = None,
    masters_dir: Path = MASTERS_DIR,
) -> Image.Image:
    """Runs the full pipeline on one master file, returning the finished
    RGBA frame. Deterministic and side-effect-free — writing the result to
    disk is the caller's job."""

    resolved = _resolve_overrides(
        SPRITE_TARGETS.get(stem, {}),
        _SPRITE_OVERRIDE_KEYS,
        "SPRITE_TARGETS",
        stem,
        target_height=target_height,
        reduce_colors=reduce_colors,
        degrain_passes=degrain_passes,
        degrain_agreement=degrain_agreement,
        crop_margin=crop_margin,
    )
    _validate_pipeline_params(
        resolved["reduce_colors"],
        resolved["degrain_passes"],
        resolved["degrain_agreement"],
        height=resolved["target_height"],
    )

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
    image = harden_alpha(image)
    rgb_quantized = quantize_to_palette(image, palette, reduce_colors=resolved["reduce_colors"])
    reassembled = Image.merge("RGBA", (*rgb_quantized.split(), image.split()[3]))
    return degrain(
        reassembled, passes=resolved["degrain_passes"], agreement=resolved["degrain_agreement"]
    )


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

    resolved = _resolve_overrides(
        TERRAIN_TARGETS.get(path.name, {}),
        _TERRAIN_OVERRIDE_KEYS,
        "TERRAIN_TARGETS",
        path.name,
        reduce_colors=reduce_colors,
        degrain_passes=degrain_passes,
        degrain_agreement=degrain_agreement,
    )
    _validate_pipeline_params(
        resolved["reduce_colors"],
        resolved["degrain_passes"],
        resolved["degrain_agreement"],
    )

    image = Image.open(path).convert("RGBA")
    image = harden_alpha(image)
    rgb_quantized = quantize_to_palette(image, palette, reduce_colors=resolved["reduce_colors"])
    reassembled = Image.merge("RGBA", (*rgb_quantized.split(), image.split()[3]))
    return degrain(
        reassembled, passes=resolved["degrain_passes"], agreement=resolved["degrain_agreement"]
    )


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
    """Every PNG actually *shipped* with the repo under
    client/assets/sprites — git-tracked files only.

    A plain directory scan (`SPRITES_DIR.rglob`, this function's first
    version) counts whatever happens to be sitting in the working tree,
    tracked or not — verified wrong on this exact repo: an untracked WIP
    drop (`Projectiles/Spell.png`, unrelated to this PR) inflated the
    conformance denominator from the true 126 to a claimed 127. "Shipped"
    means committed, so this asks git rather than the filesystem.
    """

    if not SPRITES_DIR.exists():
        return []

    try:
        listed = subprocess.run(
            ["git", "-C", str(REPO_ROOT), "ls-files", "--", "client/assets/sprites"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout
    except (subprocess.CalledProcessError, FileNotFoundError) as exc:
        print(
            f"warning: `git ls-files` unavailable ({exc}); falling back to a "
            f"directory scan, which may include untracked files",
            file=sys.stderr,
        )
        return sorted(SPRITES_DIR.rglob("*.png"))

    return sorted(
        REPO_ROOT / line for line in listed.splitlines() if line.endswith(".png")
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
        crop_margin=args.crop_margin,
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

    if args.shipped and args.paths:
        print(
            "error: --shipped and explicit paths are mutually exclusive "
            "(pass one or the other, not both — an earlier version of this "
            "command silently ignored the paths and checked --shipped "
            "instead, which is exactly the kind of thing 'nothing lies' "
            "rules out)",
            file=sys.stderr,
        )
        return 2

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
    p_process.add_argument("--crop-margin", type=int, default=None)
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

    # Pillow version check (raise below the floor, warn off the verified
    # version) runs at module import time now — see VERIFIED_PILLOW_VERSION
    # above — so it covers library callers as well as this CLI entry point.

    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
