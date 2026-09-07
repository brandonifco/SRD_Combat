#!/usr/bin/env bash
#
# Two-phase knockout step, so a stub can never take your own uncommitted work with it.
#
#   knockout.sh snapshot -- <file> [<file>...]
#       Copies the named files (exactly as they are now — committed or not) into a
#       snapshot under the worktree's git-dir. Run this BEFORE applying the stub.
#
#   knockout.sh run --project <csproj|dir> [--filter <expr>] --label "<what the stub removes>" \
#                   [--log <table.md>] -- <file> [<file>...]
#       With the stub applied: runs the focused test, records a table row, restores the
#       files FROM THE SNAPSHOT (not from HEAD — an earlier version restored with
#       `git checkout --`, which would have discarded an uncommitted fix in the same
#       file), and asserts each file is byte-identical to its snapshot afterwards.
#       Restore stamps the files with a fresh mtime (NOT `cp -p`): the restored source
#       must be newer than the assemblies built while the stub was in place, or a later
#       incremental build serves the stale stubbed binary (#612). A guard fails the run
#       if any restored file ends up older than the moment the stubbed build started.
#
# Refuses: to run in the primary checkout; to `run` without a snapshot of every named
# file; to `run` when no named file differs from its snapshot (no stub applied — the
# commonest way a knockout table lies).
#
# Appends a Markdown row to --log (default ./knockout-table.md) so the PR's table is
# assembled from recorded verdicts rather than from memory.
set -uo pipefail

mode="${1:-}"; shift || true
proj=""; filter=""; label=""; log="./knockout-table.md"; files=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --project) proj="$2"; shift 2 ;;
        --filter) filter="$2"; shift 2 ;;
        --label) label="$2"; shift 2 ;;
        --log) log="$2"; shift 2 ;;
        --) shift; files=("$@"); break ;;
        *) echo "knockout.sh: unknown argument $1" >&2; exit 4 ;;
    esac
done
[[ "$mode" == "snapshot" || "$mode" == "run" ]] && [[ ${#files[@]} -gt 0 ]] || { sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//' >&2; exit 4; }

git_dir="$(git rev-parse --path-format=absolute --git-dir)"
if [[ "$git_dir" == "$(git rev-parse --path-format=absolute --git-common-dir)" ]]; then
    echo "knockout.sh: this is the primary checkout; knockouts modify and restore files, so run them in your worktree" >&2; exit 1
fi
snap="$git_dir/knockout-snapshot"

if [[ "$mode" == "snapshot" ]]; then
    for f in "${files[@]}"; do
        [[ -f "$f" ]] || { echo "knockout.sh: no such file '$f'" >&2; exit 1; }
        mkdir -p "$snap/$(dirname "$f")"; cp -p "$f" "$snap/$f"
    done
    echo "snapshot: ${files[*]} → $snap"
    echo "now apply the stub, then: knockout.sh run --project … --label … -- ${files[*]}"
    exit 0
fi

[[ -n "$proj" && -n "$label" ]] || { echo "knockout.sh run: --project and --label are required" >&2; exit 4; }
for f in "${files[@]}"; do
    [[ -f "$snap/$f" ]] || { echo "knockout.sh: no snapshot of '$f' — run 'knockout.sh snapshot -- $f' before applying the stub" >&2; exit 1; }
done
changed=0
for f in "${files[@]}"; do cmp -s "$f" "$snap/$f" || changed=1; done
(( changed )) || { echo "knockout.sh: no named file differs from its snapshot — no stub is applied, so there is nothing to knock out" >&2; exit 1; }

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
args=("$proj"); [[ -n "$filter" ]] && args+=(--filter "$filter")
# Fix the restore's mtime floor BEFORE the stubbed build runs: the test build below
# writes assemblies stamped "now", so a restored source must end up NEWER than this
# instant or an incremental build will serve the stale stubbed binary (#612).
run_start=$(date +%s)
verdict="$(bash "$here/focused-test.sh" "${args[@]}")"; rc=$?

# Restore content from the snapshot, but do NOT preserve its (older) mtime: `cp -p`
# stamped the restored file with the original mtime, which can predate the assemblies
# just built with the stub in place, so the next incremental build sees the source as
# "not newer than the output" and serves the stale, stubbed binary (#612).
# `sleep 1` before `touch` crosses a whole-second boundary, so the restored mtime is
# *strictly* later than any assembly written during the run even on a filesystem with
# 1-second timestamp resolution — MSBuild treats an equal mtime as up-to-date, so a
# same-second restore would otherwise still serve the stale artifact.
for f in "${files[@]}"; do cp "$snap/$f" "$f"; done
sleep 1
for f in "${files[@]}"; do touch "$f"; done
for f in "${files[@]}"; do
    cmp -s "$f" "$snap/$f" || { echo "knockout.sh: '$f' did not restore to its snapshot — inspect before doing anything else" >&2; exit 1; }
    # Regression guard: if a future edit reintroduces mtime-preservation on restore,
    # the restored file will carry its pre-run (snapshot) mtime, older than run_start,
    # and this fails loudly rather than letting a stale artifact be served silently.
    mt=$(stat -c %Y "$f")
    (( mt >= run_start )) || { echo "knockout.sh: '$f' restored with a stale mtime ($mt < run_start $run_start) — an incremental build could serve the stubbed binary; do not preserve mtime on restore (#612)" >&2; exit 1; }
done

first="$(printf '%s\n' "$verdict" | head -1)"
case $rc in
    1) cell="**RED** — ${first#RED: }" ;;
    0) cell="**GREEN** — ${first#GREEN: } (nothing pins this)" ;;
    2) cell="**VACUOUS** — filter matched no tests" ;;
    3) cell="**BUILD FAILED** — not a knockout; choose a stub that compiles" ;;
    *) cell="unknown ($rc)" ;;
esac
[[ -s "$log" ]] || printf '| Stub | Result |\n| --- | --- |\n' > "$log"
printf '| %s | %s |\n' "$label" "$cell" >> "$log"

printf '%s\n' "$verdict"
echo "recorded: | $label | $cell |  →  $log"
echo "restored from snapshot: ${files[*]}"
exit 0
