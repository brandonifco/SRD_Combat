#!/usr/bin/env bash
#
# Merges a ready PR and CONFIRMS it merged, because the confirmation is the part that
# has gone wrong: GitHub has answered `gh pr merge` with a 504 on a merge that went
# through, and a stale local main has then dropped a slice branched from it.
#
#   merge.sh <pr-number>
#
# Sequence: pr-ready.sh must pass → `gh pr merge --merge` (merge commits are the only
# method enabled on this repository) → poll `gh pr view` until state MERGED → fetch
# origin/main and assert the merge commit is in it. The merge command's own exit code
# is reported but never trusted on its own.
#
# Exit 0 when MERGED and present in origin/main; 1 otherwise.
set -uo pipefail

pr="${1:-}"; [[ -n "$pr" ]] || { echo "usage: merge.sh <pr-number>" >&2; exit 2; }
here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

bash "$here/pr-ready.sh" "$pr" || { echo "merge.sh: not merging #$pr — see above" >&2; exit 1; }

echo "merge.sh: gh pr merge $pr --merge"
if gh pr merge "$pr" --merge; then
    echo "merge.sh: merge command returned success (confirming anyway)"
else
    echo "merge.sh: merge command returned failure — a 504 here has meant 'merged' before, so confirming before believing it"
fi

state="UNKNOWN"; merged_at="-"; oid="-"
for attempt in 1 2 3 4 5 6; do
    read -r state merged_at oid <<<"$(gh pr view "$pr" --json state,mergedAt,mergeCommit --jq '"\(.state) \(.mergedAt // "-") \(.mergeCommit.oid // "-")"' 2>/dev/null || echo "UNREACHABLE - -")"
    [[ "$state" == "MERGED" && "$oid" != "-" ]] && break
    echo "merge.sh: attempt $attempt — state $state; retrying in 10 s"
    sleep 10
done

if [[ "$state" != "MERGED" || "$oid" == "-" ]]; then
    echo "merge.sh: #$pr is $state after retries — it did NOT merge. Read 'gh pr view $pr' before doing anything else." >&2; exit 1
fi

git fetch -q origin main
if git merge-base --is-ancestor "$oid" origin/main; then
    echo "MERGED: #$pr at $merged_at as ${oid:0:7}, present in origin/main"
    # The primary checkout is where every session loads its hooks, skills and CLAUDE.md
    # from, so what just merged only takes effect once the primary is on it. Fast-forward
    # it now, if it is on main and clean; the guard hook allows exactly this move.
    primary="$(dirname "$(git rev-parse --path-format=absolute --git-common-dir)")"
    if [[ "$(git -C "$primary" symbolic-ref --short HEAD 2>/dev/null)" == "main" ]]; then
        if [[ -z "$(git -C "$primary" status --porcelain --untracked-files=no)" ]]; then
            git -C "$primary" merge --ff-only origin/main >/dev/null && echo "merge.sh: primary checkout $primary fast-forwarded to $(git -C "$primary" rev-parse --short HEAD)"
        else
            echo "merge.sh: primary checkout $primary is on main but has uncommitted changes — not fast-forwarded; sessions there keep the old hooks and skills until it is clean"
        fi
    else
        echo "merge.sh: primary checkout $primary is not on main ($(git -C "$primary" symbolic-ref --short HEAD 2>/dev/null)) — nothing merged reaches new sessions until it is: git -C $primary checkout main && git -C $primary merge --ff-only origin/main"
    fi
    echo "merge.sh: next slice — worktree.sh <scratchpad> <branch> --after-pr $pr; remove your finished worktree with 'git worktree remove <path>' (never --force)"
    exit 0
fi
echo "merge.sh: #$pr says MERGED as $oid but origin/main does not contain it yet. Fetch again in a minute; do not branch until it does." >&2
exit 1
