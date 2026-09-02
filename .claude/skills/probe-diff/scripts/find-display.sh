#!/usr/bin/env bash
#
# Prints a reachable X display (":0", ":1", …) or exits 1. The display this machine
# offers has moved: :1 was unreachable and :0 worked on 2026-08-27; on 2026-09-02 it was
# the other way round. Nothing should hardcode one — detect it.
#
#   export DISPLAY="$(bash .claude/skills/probe-diff/scripts/find-display.sh)"
set -uo pipefail
command -v xdpyinfo >/dev/null || { echo "find-display.sh: xdpyinfo is not installed (sudo apt install x11-utils), so no display can be probed" >&2; exit 1; }
if [[ -n "${DISPLAY:-}" ]] && xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then echo "$DISPLAY"; exit 0; fi
for sock in /tmp/.X11-unix/X*; do
    [[ -e "$sock" ]] || continue
    d=":${sock##*/X}"
    if xdpyinfo -display "$d" >/dev/null 2>&1; then echo "$d"; exit 0; fi
done
echo "find-display.sh: no reachable X display (tried DISPLAY and /tmp/.X11-unix/*)" >&2; exit 1
