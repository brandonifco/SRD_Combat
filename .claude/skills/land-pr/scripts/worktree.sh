#!/usr/bin/env bash
#
# Starts a slice the way SRD_Combat's conventions require: a fresh branch from a
# freshly fetched origin/main, checked out into a worktree OUTSIDE the repository, with
# the previous slice's merge confirmed first if you name it.
#
#   worktree.sh <parent-dir> <branch> [--after-pr N] [--existing]
#
#   <parent-dir>   where the worktree goes. Use your session scratchpad. It must not be
#                  inside the repository — .claude/worktrees/ once held 3.7 GB and made
#                  every search return nine copies of the tree.
#   <branch>       e.g. fix/510-quit-confirm. Prefixes in use: fix/ feature/ refactor/
#                  docs/ test/ art/ chore/. Name the issue number when there is one.
#   --after-pr N   refuse to start unless PR N is really MERGED and its merge commit is
#                  an ancestor of origin/main. `gh pr merge` has returned 504 on merges
#                  that succeeded and "merged" on merges that did not; a slice branched
#                  from a stale base silently drops the previous one.
#   --existing     the branch already exists (resuming): check it out instead of
#                  creating it.
#
# Prints the worktree path on the last line of stdout. Exit 0 on success.
set -euo pipefail

usage() { sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//' >&2; exit 2; }

parent="${1:-}"; branch="${2:-}"; shift 2 2>/dev/null || usage
after_pr=""; existing=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --after-pr) after_pr="$2"; shift 2 ;;
        --existing) existing=1; shift ;;
        *) usage ;;
    esac
done
[[ -n "$parent" && -n "$branch" ]] || usage

primary_git="$(git rev-parse --path-format=absolute --git-common-dir)"
primary_root="$(dirname "$primary_git")"
[[ -f "$primary_root/SRDCombat.sln" ]] || { echo "worktree.sh: run this from inside an SRD_Combat checkout" >&2; exit 1; }

parent="$(realpath -m "$parent")"
this_root="$(git rev-parse --show-toplevel)"
for root in "$primary_root" "$this_root"; do
    case "$parent/" in
        "$root"/*) echo "worktree.sh: '$parent' is inside a checkout ($root). Use the session scratchpad (CLAUDE.md, Standing conventions)." >&2; exit 1 ;;
    esac
done

mkdir -p "$parent"

# Registrations whose directories are gone (finished sessions) block reuse of a path
# and clutter `git worktree list`; pruning them is safe — it never touches a live tree.
git worktree prune

echo "worktree.sh: fetching origin/main"
git fetch -q origin main

if [[ -n "$after_pr" ]]; then
    state="$(gh pr view "$after_pr" --json state,mergedAt,mergeCommit --jq '"\(.state) \(.mergedAt // "-") \(.mergeCommit.oid // "-")"')"
    read -r pr_state merged_at merge_oid <<<"$state"
    if [[ "$pr_state" != "MERGED" || "$merge_oid" == "-" ]]; then
        echo "worktree.sh: PR #$after_pr is $pr_state, not MERGED — do not branch on top of it yet." >&2; exit 1
    fi
    if ! git merge-base --is-ancestor "$merge_oid" origin/main; then
        echo "worktree.sh: PR #$after_pr reports merged at $merged_at, but its merge commit $merge_oid is not in origin/main. Re-fetch and look before trusting either." >&2; exit 1
    fi
    echo "worktree.sh: PR #$after_pr merged at $merged_at as ${merge_oid:0:7}, present in origin/main"
fi

slug="$(printf '%s' "$branch" | tr '/' '-')"
path="$parent/wt-$slug"
[[ -e "$path" ]] && { echo "worktree.sh: '$path' already exists. Reuse it, or remove it with 'git worktree remove' if it is yours and finished." >&2; exit 1; }

if git show-ref --verify --quiet "refs/heads/$branch"; then
    if (( existing )); then
        git worktree add -q "$path" "$branch"
    else
        echo "worktree.sh: branch '$branch' already exists locally. Pass --existing to resume it, or choose another name." >&2; exit 1
    fi
elif git show-ref --verify --quiet "refs/remotes/origin/$branch"; then
    # Exists on origin, not locally: a resume after the local branch was deleted. Creating
    # a fresh branch from origin/main here would make the later push non-fast-forward
    # and invite a force push; track the remote branch instead.
    git worktree add -q --track -b "$branch" "$path" "origin/$branch"
    echo "worktree.sh: resumed $branch from origin/$branch"
else
    (( existing )) && { echo "worktree.sh: --existing given but no local branch '$branch'" >&2; exit 1; }
    git worktree add -q -b "$branch" "$path" origin/main
fi

# Sanity: a linked worktree's git-dir differs from the common dir. This is the property
# the primary-checkout guard hook keys on, so assert it here rather than assume it.
[[ "$(git -C "$path" rev-parse --path-format=absolute --git-dir)" != "$primary_git" ]] || { echo "worktree.sh: '$path' is not a linked worktree — refusing" >&2; exit 1; }

echo "worktree.sh: $branch at $(git -C "$path" rev-parse --short HEAD) (origin/main $(git rev-parse --short origin/main))"
echo "worktree.sh: next — cd there, work, then ./scripts/validate.sh full before pushing"
echo "$path"
