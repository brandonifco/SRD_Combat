#!/usr/bin/env bash
#
# Fails when an open issue's acceptance criteria mandate a measurement rule this
# project retired.
#
# #551 retired the per-PR pacing gate on 2026-08-28: pacing is measured at
# re-baselining checkpoints, an ordinary gameplay PR does not run the canonical seed
# ranges, and the `--seeds 1-20` spot-check waiver went with the universal gate it was
# an exception to. The charters absorbed that; nothing else did. On 2026-09-09 a
# queue-truth review found the retired wording still standing as *acceptance criteria*
# in 22 open issues (#712) and in a Codex charter mirror (#702) — and #475's criterion
# 4 would have written it into a shipped source header, where the docs-grep gate (#417)
# could never reach it, because no diff would have deleted anything.
#
# That is the shape #417 structurally cannot catch: a convention retired in one commit
# leaves copies in every artifact that quotes it, and a grep for what a diff *deleted*
# finds none of them. An issue is the worst place for such a copy, because a charter is
# what a reviewer consults while an issue is what an engineer *executes*.
#
#   ./scripts/queue-drift.sh              # the gate: anything filed since the sweep
#   ./scripts/queue-drift.sh --since D    # widen the window (knockout: an early date
#                                         #   re-arms the 22 grandfathered issues)
#   ./scripts/queue-drift.sh --all        # every hit, grandfathered ones included
#
# Not part of scripts/validate.sh, deliberately: this needs the network and a GitHub
# token, and validate.sh is the merge gate that must pass on a plane. Run it when
# filing or grooming issues — the file-issue skill says so.
#
# Exit 0 clean, 1 drift found, 2 cannot query.
set -uo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# The sweep's date. An issue filed before it carries the retired wording in its body
# with a dated correction comment (#712) and is reported but not failed; an issue filed
# on or after it has no such excuse.
SINCE="2026-09-09"
ALL=0
while (( $# )); do
    case "$1" in
        --since) SINCE="${2:?--since needs a date}"; shift 2 ;;
        --all)   ALL=1; shift ;;
        -h|--help) sed -n '2,30p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "queue-drift.sh: unknown argument $1" >&2; exit 2 ;;
    esac
done

# Curated, never inferred — the same discipline `Narrative` follows in the extractor,
# and for the same reason: a heuristic for "is this mention legitimate?" would have
# false negatives, and a false negative here re-admits the drift. Each entry states why.
declare -A EXEMPT=(
    [316]="the both-ranges run IS this issue's deliverable (re-run the distinct-creature measurement), not a gate on an unrelated change"
    [551]="the retirement itself"
    [702]="reports this drift in the Codex charter mirrors"
    [707]="corrects the instrument; quotes the retired wording as the thing being fixed"
    [712]="the sweep that produced this script"
)

# Two shapes, both retired together on 2026-08-28.
WAIVER='seeds 1-20'
GATE='both canonical ranges|both canonical seed ranges|both ranges, same-build|PacingMeasure on both|PacingMeasure both ranges|both seed ranges|two-canonical-range'

command -v gh >/dev/null || { echo "queue-drift.sh: gh not on PATH" >&2; exit 2; }
json="$(gh api --paginate 'repos/{owner}/{repo}/issues?state=open&per_page=100' 2>/dev/null)" \
    || { echo "queue-drift.sh: could not query the issue queue (network or auth)" >&2; exit 2; }

# Both patterns match case-insensitively. They did not at first: the waiver's `test`
# carried no "i" while the gate's did, so a criterion opening a sentence with "Seeds
# 1-20" walked straight past a guard that caught "seeds 1-20" — a false negative, and a
# false negative here re-admits the drift the script exists to catch.
#
# jq's exit status is checked rather than assumed, and this is the defect that mattered
# most: `mapfile -t rows < <(jq …)` hides jq's status entirely, so a malformed response
# or a jq error produced an empty `rows`, and an empty `rows` reported "CLEAN" and
# exited 0. A verifier that reports green when it could not answer is #528's shape
# exactly, in the script written to close #528's shape. `set -uo pipefail` does not save
# this — there is no `-e`, and a process substitution is not a pipeline.
select='
    .[] | select(.pull_request == null)
    | . as $i
    | ($i.body // "" | gsub("\n"; " ")) as $b
    | [ (if ($b | test($waiver; "i")) then "spot-check waiver (--seeds 1-20)" else empty end),
        (if ($b | test($gate; "i"))   then "per-PR both-ranges gate"          else empty end) ] as $hits
    | select($hits | length > 0)
    | "\($i.number)\t\($i.created_at[0:10])\t\($hits | join(" + "))\t\($i.title[0:72])"
'
if ! matched="$(jq -r --arg waiver "$WAIVER" --arg gate "$GATE" "$select" <<<"$json")"; then
    echo "queue-drift.sh: the issue list did not parse — cannot say whether the queue is clean" >&2
    exit 2
fi
# Not `mapfile < <(sort <<<"$matched")` unguarded: a here-string of the empty string is
# still one empty line, which would make `rows` a one-element array of "" and put a
# phantom row through the loop below.
rows=()
[[ -n "$matched" ]] && mapfile -t rows < <(sort -n <<<"$matched")

new=(); old=(); skipped=()
for row in "${rows[@]}"; do
    n="${row%%$'\t'*}"; rest="${row#*$'\t'}"; created="${rest%%$'\t'*}"
    if [[ -n "${EXEMPT[$n]:-}" ]]; then skipped+=("  #$n — ${EXEMPT[$n]}"); continue; fi
    if [[ "$created" > "$SINCE" || "$created" == "$SINCE" ]]; then new+=("$row"); else old+=("$row"); fi
done

printf 'queue-drift.sh: %d open issues cite a measurement rule retired 2026-08-28 (#551)\n' \
    $(( ${#new[@]} + ${#old[@]} ))

if (( ${#skipped[@]} )); then
    echo; echo "Exempt, by curated reason:"; printf '%s\n' "${skipped[@]}"
fi

if (( ${#old[@]} )); then
    echo; printf 'Grandfathered — filed before %s, corrected by dated comment (#712): %d\n' "$SINCE" "${#old[@]}"
    if (( ALL )); then printf '  #%s\n' "${old[@]//$'\t'/  }"; fi
fi

if (( ${#new[@]} == 0 )); then
    echo; echo "CLEAN: no issue filed since $SINCE mandates a retired measurement gate."
    exit 0
fi

echo
echo "DRIFT: filed since $SINCE, and mandating a rule that no longer exists —"
printf '  #%s\n' "${new[@]//$'\t'/  }"
cat <<'MSG'

Each of these must be corrected by comment (not a silent body rewrite — history is
archived, never deleted), stating which half was retired and which was kept. What a PR
owes now: direct evidence its own change is correct, deterministic pins wherever
behaviour is seeded, and — where the change carries an obvious severe risk (a stall, an
unwinnable encounter, broken progression) — that specific risk named and shown not to
occur. A difficulty reading belongs to the re-baselining checkpoint (#542), not the PR.

Do NOT delete a criterion that survived the retirement: a named severe risk, a hard
zero-`Stalled` assertion, or a dice-consumption trip-wire is exactly what the current
rule asks for. #429, #437, #438 and #478 are the worked mixed cases.
MSG
exit 1
