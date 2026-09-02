#!/usr/bin/env bash
#
# Runs the Godot probe once into a directory and compares it with a baseline directory,
# naming every capture that differs and where on the frame it differs.
#
#   probe-compare.sh <baseline-dir> <candidate-dir> [--no-run]
#
#   <baseline-dir>   captures from a tree you trust — normally two identical runs of
#                    unmodified main (scripts/probe-diff.sh produces that pair)
#   <candidate-dir>  where this run's captures go; must not already hold captures (a
#                    stale PNG from an earlier run would compare identical to the
#                    baseline and hide a step the probe no longer reaches)
#
# Needs a reachable X display (detected with find-display.sh when DISPLAY is unset or
# dead — it has been :0 one day and :1 another) and Godot's x11 driver; a capture is a rendered frame and there
# is nothing to render headless. The client is rebuilt on every run, unconditionally:
# Godot does not compile the C# assembly on launch, so a skipped build would run the
# *baseline's* DLL against the changed source and report "0 differ" about a change that
# never executed.
#
# For each differing PNG the report gives the bounding box of the changed pixels. That
# box is the diagnosis: a ~45x45 square at the active token's ring is a wall-clock read
# (#518); a full-frame difference is a layout or camera change; a box over the log is a
# narration change. A capture missing on one side, or a .skipped.txt that appears or
# vanishes, is listed too — a shrunk capture set is exactly what #499 exists to catch.
#
# Exit 0 identical, 1 differences listed, 2 a probe run failed.
set -uo pipefail

base="${1:-}"; cand="${2:-}"; run=1
[[ -n "$base" && -n "$cand" ]] || { sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//' >&2; exit 2; }
[[ "${3:-}" == "--no-run" ]] && run=0
root="$(git rev-parse --show-toplevel)"
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if (( run )); then
    DISPLAY="$(bash "$here/find-display.sh")" || exit 2
    export DISPLAY
    echo "probe-compare.sh: using display $DISPLAY"
    if [[ -d "$cand" ]] && compgen -G "$cand/*.png" >/dev/null; then
        echo "probe-compare.sh: '$cand' already holds captures — use a fresh directory so a stale PNG cannot stand in for a step the probe no longer reaches" >&2; exit 2
    fi
    mkdir -p "$cand"
    echo "probe-compare.sh: building the client (Godot does not compile C# on launch)"
    dotnet build "$root/client/SRDCombat.Viewer.csproj" -c Debug >"$cand/build.log" 2>&1 || { echo "probe-compare.sh: client build failed — see $cand/build.log" >&2; exit 2; }
    echo "probe-compare.sh: main probe run (seed 1) -> $cand"
    timeout 1500 godot --path "$root/client" --display-driver x11 -- --seed=1 "--probe=$cand" >"$cand/probe-main.log" 2>&1 || { echo "probe-compare.sh: main run did not exit cleanly — see $cand/probe-main.log" >&2; exit 2; }
    echo "probe-compare.sh: one-fight run (slot menu) -> $cand"
    timeout 300 godot --path "$root/client" --display-driver x11 -- --one-fight --seed=1 "--probe=$cand" >"$cand/probe-one-fight.log" 2>&1 || { echo "probe-compare.sh: one-fight run did not exit cleanly — see $cand/probe-one-fight.log" >&2; exit 2; }
fi

[[ -d "$base" ]] && compgen -G "$base/*.png" >/dev/null || { echo "probe-compare.sh: baseline '$base' holds no captures — establish one first with scripts/probe-diff.sh" >&2; exit 2; }
python3 - "$base" "$cand" <<'PY'
import os, sys
base, cand = sys.argv[1:3]
names = sorted(set(f for d in (base, cand) for f in os.listdir(d) if f.endswith((".png", ".skipped.txt"))))
try:
    from PIL import Image, ImageChops
except ImportError:
    Image = None
    print("  (Pillow not installed: differing files are named but no pixel box is computed — pip install Pillow)")
diffs = 0
for n in names:
    a, b = os.path.join(base, n), os.path.join(cand, n)
    if not os.path.exists(a):
        print(f"  NEW      {n} (candidate only)"); diffs += 1; continue
    if not os.path.exists(b):
        print(f"  MISSING  {n} (baseline only)"); diffs += 1; continue
    if open(a, "rb").read() == open(b, "rb").read():
        continue
    diffs += 1
    if n.endswith(".png") and Image:
        ia, ib = Image.open(a).convert("RGB"), Image.open(b).convert("RGB")
        if ia.size != ib.size:
            print(f"  DIFF     {n}: size {ia.size} -> {ib.size}"); continue
        box = ImageChops.difference(ia, ib).getbbox()
        w, h = box[2] - box[0], box[3] - box[1]
        print(f"  DIFF     {n}: changed pixels within x={box[0]}..{box[2]} y={box[1]}..{box[3]} ({w}x{h} of {ia.size[0]}x{ia.size[1]})")
    else:
        print(f"  DIFF     {n}")
print(f"{len(names)} captures compared, {diffs} differ")
sys.exit(1 if diffs else 0)
PY
