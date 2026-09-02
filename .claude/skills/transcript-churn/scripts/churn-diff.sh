#!/usr/bin/env bash
#
# Shows the WHOLE frozen-transcript churn as a unified diff, by regenerating the fixture
# into place and diffing it against the committed one — then putting it back unless
# told to keep it.
#
#   churn-diff.sh [--keep] [--out <diff-file>]
#
# Why a script: the failing assertion prints a truncated Assert.Equal excerpt, and
# reading a whole fight's churn from that is how a shifted dice stream gets mistaken for
# a wording change. The writer test (TranscriptWriter.WriteSkirmishTranscript) is
# skipped by design; this un-skips it in the working tree, runs it, and restores the
# test file byte-for-byte, so the un-skip can never be committed by accident.
#
#   (no flag)  regenerate, diff, restore the fixture — read-only in effect
#   --keep     regenerate and leave the new fixture in the tree, for committing once
#              every hunk has been accounted for in the PR body
#
# Refuses in the primary checkout, and refuses if the fixture or the test file already
# has uncommitted changes (someone regenerated already — read `git diff` first).
#
# Exit 0 no churn, 1 churn (diff printed and, with --out, saved), 3 build/test failure.
set -uo pipefail

keep=0; out=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --keep) keep=1; shift ;;
        --out) out="$2"; shift 2 ;;
        *) echo "usage: churn-diff.sh [--keep] [--out file]" >&2; exit 4 ;;
    esac
done

root="$(git rev-parse --show-toplevel)"
cd "$root"
if [[ "$(git rev-parse --path-format=absolute --git-dir)" == "$(git rev-parse --path-format=absolute --git-common-dir)" ]]; then
    echo "churn-diff.sh: this is the primary checkout; regenerate in your worktree" >&2; exit 1
fi

fixture="tests/SRDCombat.Core.Tests/Fixtures/skirmish-transcript.txt"
test_file="tests/SRDCombat.Core.Tests/Combat/FrozenTranscriptTests.cs"
skip_line='[Fact(Skip = "Writes the committed fixture. Un-skip, run, re-skip, and review the diff.")]'

git diff --quiet -- "$fixture" "$test_file" || { echo "churn-diff.sh: $fixture or $test_file already has uncommitted changes — read 'git diff' before regenerating again" >&2; exit 1; }
grep -qF "$skip_line" "$test_file" || { echo "churn-diff.sh: the writer's Skip attribute is not where this script expects it; update skip_line in this script" >&2; exit 1; }

restore_test() { git checkout -- "$test_file"; }
restore_both() { git checkout -- "$test_file" "$fixture"; }
# Until the writer has run cleanly, a failure may have half-written the fixture; put
# both files back so the next invocation does not refuse on a dirty fixture.
trap restore_both EXIT

sed -i "s|$(printf '%s' "$skip_line" | sed 's/[][\.*^$/|]/\\&/g')|[Fact]|" "$test_file"
echo "churn-diff.sh: regenerating $fixture"
if ! dotnet test tests/SRDCombat.Core.Tests -c Debug --filter "FullyQualifiedName~TranscriptWriter" --logger "console;verbosity=quiet" >/tmp/churn-diff.$$ 2>&1; then
    echo "churn-diff.sh: the writer did not run cleanly:" >&2; tail -30 /tmp/churn-diff.$$ >&2; rm -f /tmp/churn-diff.$$; exit 3
fi
rm -f /tmp/churn-diff.$$
restore_test; trap - EXIT

if git diff --quiet -- "$fixture"; then
    echo "NO CHURN: the regenerated transcript is byte-identical to the committed fixture"
    exit 0
fi

added="$(git diff --numstat -- "$fixture" | awk '{print $1}')"
removed="$(git diff --numstat -- "$fixture" | awk '{print $2}')"
first="$(git diff -U0 -- "$fixture" | grep -m1 -E '^@@' | sed -E 's/^@@ -([0-9]+).*/\1/')"
echo "CHURN: +$added -$removed lines; first divergence at fixture line $first"
if [[ -n "$out" ]]; then git diff -- "$fixture" > "$out"; echo "diff saved to $out"; fi
git --no-pager diff --stat -- "$fixture"

if (( keep )); then
    echo "kept: the new fixture is in the tree. Commit it only with the account of every hunk in the PR body."
else
    git checkout -- "$fixture"
    echo "restored: the committed fixture is back. Re-run with --keep once the churn is intended and accounted for."
fi
exit 1
