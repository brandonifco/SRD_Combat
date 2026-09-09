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
# WHAT THIS CAN AND CANNOT SAY. It matches a curated phrase list against a normalised
# issue body. That detects the wordings this project actually uses; it cannot establish
# that no issue *mandates* the retired rule, because a paraphrase is still a paraphrase
# (Codex review of PR #724, finding 2). So a clean run says "no known wording found",
# never "no issue mandates it", and the phrase list is regression-tested by
# scripts/test-queue-drift.sh rather than trusted.
#
#   ./scripts/queue-drift.sh              # the gate: anything filed since the sweep
#   ./scripts/queue-drift.sh --since D    # widen the window (knockout: an early date
#                                         #   re-arms the grandfathered issues)
#   ./scripts/queue-drift.sh --all        # every hit, grandfathered ones included
#   ./scripts/queue-drift.sh --fixture F  # read a saved JSON list instead of querying,
#                                         #   so the matching is testable offline
#
# Not part of scripts/validate.sh in its live mode, deliberately: that needs the network
# and a GitHub token, and validate.sh is the merge gate that must pass on a plane. The
# fixture-driven regression test IS in validate.sh, because it needs neither.
#
# Exit 0 no known wording found, 1 drift found, 2 could not answer.
set -uo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

SINCE="2026-09-09"
ALL=0
FIXTURE=""
while (( $# )); do
    case "$1" in
        --since)   SINCE="${2:?--since needs a date}"; shift 2 ;;
        --fixture) FIXTURE="${2:?--fixture needs a path}"; shift 2 ;;
        --all)     ALL=1; shift ;;
        -h|--help) sed -n '2,36p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "queue-drift.sh: unknown argument $1" >&2; exit 2 ;;
    esac
done

# A malformed --since silently misclassifies rather than failing: under lexicographic
# comparison "2026-09-09" < "2026-9-09", so `--since 2026-9-09` grandfathers an issue
# created that very day and reports clean (Codex review of PR #724, finding 4). The
# comparison is only valid on canonical ISO dates, so canonical ISO is enforced.
[[ "$SINCE" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] \
    || { echo "queue-drift.sh: --since must be YYYY-MM-DD (got '$SINCE')" >&2; exit 2; }
date -d "$SINCE" >/dev/null 2>&1 \
    || { echo "queue-drift.sh: --since is not a real date ('$SINCE')" >&2; exit 2; }

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

command -v jq >/dev/null || { echo "queue-drift.sh: jq not on PATH" >&2; exit 2; }

if [[ -n "$FIXTURE" ]]; then
    [[ -r "$FIXTURE" ]] || { echo "queue-drift.sh: cannot read fixture $FIXTURE" >&2; exit 2; }
    json="$(cat -- "$FIXTURE")"
else
    command -v gh >/dev/null || { echo "queue-drift.sh: gh not on PATH" >&2; exit 2; }
    json="$(gh api --paginate 'repos/{owner}/{repo}/issues?state=open&per_page=100' 2>/dev/null)" \
        || { echo "queue-drift.sh: could not query the issue queue (network or auth)" >&2; exit 2; }
fi

# An empty or non-array response must not read as "nothing matched". `gh` exiting 0 with
# empty stdout, and jq accepting an empty input stream, together produced a confident
# CLEAN over no data at all — a verifier reporting green because it never looked, which
# is #528's shape inside the script written to close #528's shape. So: the response has
# to parse, and it has to *be* a list of issues, before zero matches means anything.
if ! count="$(jq -s -r 'if (length == 0) then "nodata"
                        elif any(.[]; type != "array") then "notarray"
                        else ([.[][]] | length | tostring) end' <<<"$json" 2>/dev/null)"; then
    echo "queue-drift.sh: the issue list did not parse — cannot say whether the queue is clean" >&2
    exit 2
fi
case "$count" in
    nodata)   echo "queue-drift.sh: the issue list was empty — cannot say whether the queue is clean" >&2; exit 2 ;;
    notarray) echo "queue-drift.sh: the issue list was not an array of issues — cannot say whether the queue is clean" >&2; exit 2 ;;
esac

# Normalise before matching, rather than growing the patterns to cover every rendering.
# Codex's finding 2 evaded a raw phrase list four ways: a CRLF body split "both
# canonical\r\nranges"; markdown emphasis split "both **canonical** ranges"; the equals
# form "--seeds=1-20"; and naming the ranges outright ("--seeds 1-120 and 200-320")
# without ever using the phrase. Stripping \r, markdown emphasis and repeated whitespace
# and lowercasing kills the first three classes at the source; the fourth is a pattern of
# its own below.
NORMALISE='(.body // "") | ascii_downcase | gsub("[\r\n]"; " ") | gsub("[*_`]"; "") | gsub(" +"; " ")'

WAIVER='seeds ?=? ?1-20([^0-9]|$)'
GATE='both canonical ranges|both canonical seed ranges|both ranges|both seed ranges|pacingmeasure on both|pacingmeasure both|two-canonical-range|canonical seed ranges against a same-build'
# Naming both canonical ranges is the gate written out longhand, and carries no phrase
# to match. Either order; the pair is the signal, since one range alone is an ordinary
# reference (#707 quotes 1-120 while fixing the instrument, and is exempt by name).
PAIR_A='1-120'
PAIR_B='200-320'

if ! matched="$(jq -r -s \
        --arg waiver "$WAIVER" --arg gate "$GATE" --arg pa "$PAIR_A" --arg pb "$PAIR_B" \
        --arg normalise "$NORMALISE" '
    [.[][]] | .[]
    | select(.pull_request == null)
    | . as $i
    | ('"$NORMALISE"') as $b
    | [ (if ($b | test($waiver))                          then "spot-check waiver (--seeds 1-20)" else empty end),
        (if ($b | test($gate))                            then "per-PR both-ranges gate"          else empty end),
        (if ($b | test($pa)) and ($b | test($pb))         then "both canonical ranges, named"     else empty end) ]
      as $hits
    | select($hits | length > 0)
    | "\($i.number)\t\($i.created_at[0:10])\t\($hits | unique | join(" + "))\t\($i.title[0:72])"
' <<<"$json")"; then
    echo "queue-drift.sh: the issue list did not parse — cannot say whether the queue is clean" >&2
    exit 2
fi

# Not `mapfile < <(sort <<<"$matched")` unguarded: a here-string of the empty string is
# still one empty line, which would put a phantom row through the loop. And `sort` runs
# on its own so its status is visible rather than swallowed by a process substitution.
rows=()
if [[ -n "$matched" ]]; then
    sorted="$(sort -n <<<"$matched")" \
        || { echo "queue-drift.sh: could not sort the matches" >&2; exit 2; }
    mapfile -t rows <<<"$sorted"
fi

new=(); old=(); skipped=()
for row in "${rows[@]}"; do
    n="${row%%$'\t'*}"; rest="${row#*$'\t'}"; created="${rest%%$'\t'*}"
    if [[ -n "${EXEMPT[$n]:-}" ]]; then skipped+=("  #$n — ${EXEMPT[$n]}"); continue; fi
    if [[ ! "$created" < "$SINCE" ]]; then new+=("$row"); else old+=("$row"); fi
done

printf 'queue-drift.sh: %d open issues (of %d) carry wording for a measurement rule retired 2026-08-28 (#551)\n' \
    $(( ${#new[@]} + ${#old[@]} )) "$count"

if (( ${#skipped[@]} )); then
    echo; echo "Exempt, by curated reason:"; printf '%s\n' "${skipped[@]}"
fi

if (( ${#old[@]} )); then
    echo; printf 'Filed before %s, so not failed here: %d. #712 tracks their correction.\n' "$SINCE" "${#old[@]}"
    if (( ALL )); then printf '  #%s\n' "${old[@]//$'\t'/  }"; fi
fi

if (( ${#new[@]} == 0 )); then
    echo; echo "CLEAN: no issue filed since $SINCE carries a known retired-gate wording."
    exit 0
fi

echo
echo "DRIFT: filed since $SINCE, and mandating a rule that no longer exists —"
printf '  #%s\n' "${new[@]//$'\t'/  }"
cat <<'MSG'

**Edit the body.** These were filed after the rule was retired, so there is no history
to preserve — the "correct by dated comment" convention is for the issues that predate
the sweep, and a comment would leave a body a fresh engineer still executes. (Without
this, the guard prescribed a correction that could never clear its own finding — Codex
review of PR #724, finding 3.)

What a PR owes now: direct evidence its own change is correct, deterministic pins
wherever behaviour is seeded, and — where the change carries an obvious severe risk (a
stall, an unwinnable encounter, broken progression) — that specific risk named and shown
not to occur. A difficulty reading belongs to the re-baselining checkpoint (#542).

Do NOT delete a criterion that survived the retirement: a named severe risk, a hard
zero-`Stalled` assertion, or a dice-consumption trip-wire is exactly what the current
rule asks for. #429, #437 and #438 are the worked mixed cases.
MSG
exit 1
