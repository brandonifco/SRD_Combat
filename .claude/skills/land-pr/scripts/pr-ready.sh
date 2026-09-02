#!/usr/bin/env bash
#
# Says whether a PR is actually ready to merge, from evidence rather than from the
# colour of a badge.
#
#   pr-ready.sh <pr-number> [--wait [timeout-seconds]]
#
# Checks, each with the failure it exists to catch:
#   - state OPEN, not draft, base main
#   - at least one check run exists — #513: a PR with ZERO check runs reports
#     mergeStateStatus CLEAN, and after #485 restricted the push trigger nothing else
#     covers a branch whose PR opened before CI woke up
#   - both build-and-test legs (Debug, Release) present and SUCCESS, and no run
#     FAILURE / CANCELLED / TIMED_OUT
#   - mergeStateStatus: CLEAN is ready; BEHIND is reported as a warning (main moved —
#     the merge commit will still be tested only by main's own run); DIRTY / BLOCKED
#     / CONFLICTING is not ready
#
# --wait polls every 60 s until no run is in progress (default timeout 3600 s; a full
# PR run takes roughly 25 minutes end to end). Run it in the background and act when it
# returns — do not poll it by hand.
#
# Exit 0 ready, 1 not ready, 2 usage.
set -uo pipefail

pr="${1:-}"; [[ -n "$pr" ]] || { echo "usage: pr-ready.sh <pr-number> [--wait [seconds]]" >&2; exit 2; }
wait=0; timeout=3600
if [[ "${2:-}" == "--wait" ]]; then wait=1; timeout="${3:-3600}"; fi

required=("build-and-test (Debug)" "build-and-test (Release)")

fetch() { gh pr view "$pr" --json state,isDraft,baseRefName,mergeStateStatus,statusCheckRollup,headRefOid,url; }

deadline=$(( $(date +%s) + timeout ))
while :; do
    json="$(fetch)" || { echo "pr-ready.sh: gh pr view failed for #$pr" >&2; exit 1; }
    in_progress="$(jq -r '[.statusCheckRollup[]? | select(.__typename == "CheckRun" and .status != "COMPLETED")] | length' <<<"$json")"
    total="$(jq -r '.statusCheckRollup | length' <<<"$json")"
    computing="$(jq -r '.mergeStateStatus' <<<"$json")"
    if (( wait )) && { (( in_progress > 0 )) || (( total == 0 )) || [[ "$computing" == "UNKNOWN" ]]; } && (( $(date +%s) < deadline )); then
        echo "pr-ready.sh: #$pr — $in_progress of $total runs still in progress; waiting"
        sleep 60; continue
    fi
    break
done

ok=1
say()  { printf '  ok    %s\n' "$1"; }
warn() { printf '  warn  %s\n' "$1"; }
bad()  { printf '  FAIL  %s\n' "$1"; ok=0; }

echo "PR #$pr $(jq -r .url <<<"$json") head $(jq -r '.headRefOid[0:7]' <<<"$json")"
[[ "$(jq -r .state <<<"$json")" == "OPEN" ]] && say "state OPEN" || bad "state is $(jq -r .state <<<"$json")"
[[ "$(jq -r .isDraft <<<"$json")" == "false" ]] && say "not a draft" || bad "still a draft"
[[ "$(jq -r .baseRefName <<<"$json")" == "main" ]] && say "base main" || bad "base is $(jq -r .baseRefName <<<"$json")"

if (( total == 0 )); then
    bad "ZERO check runs reported (#513) — CI never ran on this head; push again or re-run the workflow, and do not trust mergeStateStatus"
else
    for name in "${required[@]}"; do
        c="$(jq -r --arg n "$name" '[.statusCheckRollup[] | select(.name == $n)] | last | "\(.status // "-") \(.conclusion // "-")"' <<<"$json")"
        case "$c" in
            "COMPLETED SUCCESS") say "$name SUCCESS" ;;
            "null null"|"- -") bad "$name is missing from the check runs" ;;
            *) bad "$name: $c" ;;
        esac
    done
    # mapfile, not a pipe into while: a pipeline's while runs in a subshell and its
    # ok=0 never reaches the verdict — that shape said READY over a printed FAIL.
    mapfile -t failed < <(jq -r '.statusCheckRollup[] | select(.conclusion == "FAILURE" or .conclusion == "CANCELLED" or .conclusion == "TIMED_OUT") | "\(.name) \(.conclusion) \(.detailsUrl)"' <<<"$json")
    for line in "${failed[@]}"; do bad "$line"; done
    mapfile -t pending < <(jq -r '.statusCheckRollup[] | select(.__typename == "CheckRun" and .status != "COMPLETED") | "\(.name) still \(.status)"' <<<"$json")
    for line in "${pending[@]}"; do bad "$line"; done
fi

case "$(jq -r .mergeStateStatus <<<"$json")" in
    CLEAN) say "mergeStateStatus CLEAN" ;;
    BEHIND) warn "mergeStateStatus BEHIND — main has moved since this branch was cut; the merge itself is untested until main's run. Rebase onto origin/main if the slice touches what moved" ;;
    UNSTABLE) bad "mergeStateStatus UNSTABLE — a check failed (vulnerable-packages is not a required context, so GitHub would let this merge; this script does not)" ;;
    *) bad "mergeStateStatus $(jq -r .mergeStateStatus <<<"$json")" ;;
esac

(( ok )) && { echo "READY: #$pr"; exit 0; } || { echo "NOT READY: #$pr — read a failed run with: gh run view <id> --log-failed"; exit 1; }
