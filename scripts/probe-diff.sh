#!/usr/bin/env bash
#
# Runs the play screen's probe twice, into two fresh directories, and cmp's every file
# the two runs produced. "The probe still passes and its captures are unchanged" (#327's
# own acceptance criterion for every slice after this one) is otherwise a promise nobody
# checks — this makes it a command with an exit code.
#
# Two Godot invocations make up "the probe": the main gauntlet run (--seed=1), which
# reaches six of the eight focuses #327 moves plus the run-lifecycle states, and a
# --one-fight run, which starts at level 3 (FightScreen.ResolveFight fixes it there) to
# reach the Slot menu — the one focus that needs a caster with spell slots at more than
# one level, which no level 1 character has. Both write into the same directory; their
# capture names do not collide (#499).
#
# A step the probe could not reach writes "<name>.skipped.txt" next to where the PNG
# would have gone, so a shrunk capture set is a file this diff sees rather than a PNG
# nobody thought to look for.
#
# Usage: scripts/probe-diff.sh <dirA> <dirB>
#
# Needs a reachable X display and Godot's x11 driver — a headless run cannot produce a
# frame to capture. On this machine, 2026-08-26, DISPLAY=:0 works; CLAUDE.md's
# Environment section still names :1, which was not reachable here (ask Brandon before
# changing that claim on one machine's evidence). Exit 0 when both runs' output
# directories are identical, non-zero and the differing files named otherwise.

set -uo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ $# -ne 2 ]]; then
    echo "usage: $(basename "$0") <dirA> <dirB>" >&2
    exit 2
fi

readonly DIR_A="$1"
readonly DIR_B="$2"

# The main run's fight-out loop alone measured 15-15.5 minutes (docs/2026-08-26-
# playmode-refactor-design.md, S5.1); --one-fight quits as soon as the Slot menu is
# reached or its turn budget runs out, in practice well under a minute. 25 minutes each
# leaves headroom without letting a genuinely hung probe block a CI-shaped script forever.
readonly MAIN_TIMEOUT=1500
readonly ONE_FIGHT_TIMEOUT=300

run_probe() {
    local dir="$1"

    mkdir -p "$dir"

    echo "probe-diff: main run -> $dir"
    if ! timeout "$MAIN_TIMEOUT" godot --path "$REPO_ROOT/client" --display-driver x11 \
        -- --seed=1 "--probe=$dir"; then
        echo "probe-diff: the main run into $dir did not exit cleanly" >&2
        return 1
    fi

    echo "probe-diff: one-fight (slot menu) run -> $dir"
    if ! timeout "$ONE_FIGHT_TIMEOUT" godot --path "$REPO_ROOT/client" --display-driver x11 \
        -- --one-fight --seed=1 "--probe=$dir"; then
        echo "probe-diff: the one-fight run into $dir did not exit cleanly" >&2
        return 1
    fi
}

run_probe "$DIR_A" || exit 1
run_probe "$DIR_B" || exit 1

status=0

# A plain recursive diff rather than a fixed filename list: a capture that starts (or
# stops) being written, or a skip marker that appears in one run and not the other, is
# exactly the kind of shrinkage #499 exists to catch, and a fixed list would miss it by
# construction.
if diff -rq "$DIR_A" "$DIR_B"; then
    echo "probe-diff: identical — $(find "$DIR_A" -type f | wc -l) files compared"
else
    status=1
fi

exit "$status"
