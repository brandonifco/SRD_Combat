#!/usr/bin/env bash
#
# Regression fixtures for scripts/queue-drift.sh.
#
# The guard matches a curated phrase list, and a phrase list is exactly the mechanism
# CLAUDE.md's bug 2 warns about: it always has false negatives, and a false negative
# here re-admits the drift the guard exists to catch. Codex's review of PR #724 found
# four wordings that walked straight past the first version — a CRLF-split phrase,
# markdown emphasis inside the phrase, `--seeds=1-20`, and naming the two canonical
# ranges outright without ever using the phrase. Each is a case below, so the next
# widening of the list cannot silently drop one.
#
# Fixture-driven and offline, so this runs in validate.sh (and therefore CI) while the
# live query — which needs the network and a token — does not.
#
# Exit 0 all pass, 1 any fail.
set -uo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
GUARD=./scripts/queue-drift.sh
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
pass=0; fail=0

# $1 name, $2 expected exit, $3 body, $4 created (default after the cutoff), $5 number
check() {
    local name="$1" want="$2" body="$3" created="${4:-2026-09-20}" number="${5:-9001}"
    jq -n --arg b "$body" --arg c "${created}T00:00:00Z" --argjson n "$number" \
        '[{number:$n, created_at:$c, title:"fixture", body:$b, pull_request:null}]' > "$tmp/f.json"
    "$GUARD" --fixture "$tmp/f.json" >/dev/null 2>&1
    local got=$?
    if [[ "$got" == "$want" ]]; then
        pass=$((pass+1)); printf '  ok    %s\n' "$name"
    else
        fail=$((fail+1)); printf '  FAIL  %s — wanted exit %s, got %s\n' "$name" "$want" "$got"
    fi
}

echo "== queue-drift.sh fixtures =="

# The four evasions Codex found. Each returned CLEAN before this suite existed.
check "emphasis inside the phrase"   1 'Run **both** canonical ranges against a same-build baseline.'
check "CRLF splitting the phrase"    1 $'Run both canonical\r\nranges against a same-build baseline.'
check "the ranges named outright"    1 'Attach PacingMeasure results for --seeds 1-120 and --seeds 200-320 against a same-build baseline to the PR.'
check "the equals form of the waiver" 1 'Run --seeds=1-20 with the structural proof in the PR body.'

# The plain forms, which the first version did catch — kept so a rewrite cannot lose them.
check "plain waiver"                 1 'Gate: --seeds 1-20 with the structural proof in the PR body.'
check "plain both-ranges gate"       1 'PacingMeasure on both canonical ranges against a same-build baseline.'
check "capitalised waiver"           1 'Seeds 1-20 with the structural proof.'
check "two-canonical-range wording"  1 'It never substitutes for the two-canonical-range gameplay gate.'

# Must NOT fire. A guard that cries wolf gets switched off.
check "no measurement wording"       0 'Refuse an out-of-range index by name, never clamped.'
check "one range alone is ordinary"  0 'Seeds 1-120 were used to produce the figure this issue corrects.'
check "1-200 is not 1-20"            0 'The roster cap bounds a count at 1-200 per entry.'
check "filed before the cutoff"      0 'Gate: --seeds 1-20 with the structural proof.' 2026-08-26
check "exempt issue"                 0 'PacingMeasure on both canonical ranges.' 2026-09-20 712

# Cannot-answer paths must never read as clean (#528's shape).
run_raw() { "$GUARD" --fixture "$1" >/dev/null 2>&1; echo $?; }
printf '' > "$tmp/empty.json"
printf '{"not":"an array"}' > "$tmp/object.json"
printf '[]' > "$tmp/emptyarray.json"
printf 'not json at all' > "$tmp/garbage.json"
for case in "empty response:2:$tmp/empty.json" \
            "object instead of a list:2:$tmp/object.json" \
            "an empty but valid list:0:$tmp/emptyarray.json" \
            "unparseable:2:$tmp/garbage.json"; do
    name="${case%%:*}"; rest="${case#*:}"; want="${rest%%:*}"; path="${rest#*:}"
    got="$(run_raw "$path")"
    if [[ "$got" == "$want" ]]; then pass=$((pass+1)); printf '  ok    %s → exit %s\n' "$name" "$got"
    else fail=$((fail+1)); printf '  FAIL  %s — wanted exit %s, got %s\n' "$name" "$want" "$got"; fi
done

# A malformed --since misclassifies silently under lexicographic comparison.
jq -n '[{number:9001, created_at:"2026-09-09T00:00:00Z", title:"fixture",
         body:"Gate: --seeds 1-20 with the structural proof.", pull_request:null}]' > "$tmp/f.json"
"$GUARD" --fixture "$tmp/f.json" --since 2026-9-09 >/dev/null 2>&1
if [[ $? == 2 ]]; then pass=$((pass+1)); printf '  ok    malformed --since refused\n'
else fail=$((fail+1)); printf '  FAIL  malformed --since was accepted\n'; fi

echo "== $pass passed, $fail failed =="
(( fail == 0 ))
