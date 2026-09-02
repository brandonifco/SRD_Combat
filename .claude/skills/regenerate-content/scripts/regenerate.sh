#!/usr/bin/env bash
#
# Re-runs the SRD extractor and reports what moved — counts, warnings, the residue
# census — so a regeneration is reviewed by its diff rather than by its exit code.
#
#   regenerate.sh [--out <report-dir>]
#
# Steps, and the reason each exists:
#   1. Refuses if data/srd already has uncommitted changes (someone regenerated already;
#      read that diff first) or if this is the primary checkout.
#   2. Snapshots every monster entry's residue (unmodelledClauses) to a sorted list.
#   3. Runs `dotnet run --project tools/SrdExtract -- --out data/srd`. The extractor
#      refuses to write on validation errors; --force is deliberately not passed here.
#   4. Checks the summary against the book's fixed totals — 330 monsters, 339 spells,
#      38 weapons, 13 armor, 258 magic items — and 0 errors. Exact, not floors: a count
#      the source fixes is wrong the moment it differs (a wrapped class list once dropped
#      39 spells under a `>= 300` test that stayed green).
#   5. Expects exactly 15 warnings. Every one is known (the Archmage's XP, twelve
#      column-truncated spell component lines, two "Rarity Varies" items); a 16th is a
#      finding and a 14th means a known inconsistency silently stopped being reported.
#   6. Diffs the residue list: clauses that appeared, clauses that vanished, per
#      monster and entry. A vanished clause is a claim the code now makes — it must be
#      backed by code that expresses it. An appeared clause is honesty, and may demote
#      a creature out of the pool (MonsterPoolTests' floors).
#   7. Writes the extractor's own census (--census) for the report.
#
# Exit 0 when the run is clean and the report is written; 1 on any refusal or on a
# count/warning mismatch (the data is still written — read the report, then decide).
set -uo pipefail

root="$(git rev-parse --show-toplevel)"; cd "$root"
report="${2:-}"; [[ "${1:-}" == "--out" && -n "$report" ]] || report="$root/../regenerate-report"
mkdir -p "$report"

if [[ "$(git rev-parse --path-format=absolute --git-dir)" == "$(git rev-parse --path-format=absolute --git-common-dir)" ]]; then
    echo "regenerate.sh: this is the primary checkout; regenerate in your worktree" >&2; exit 1
fi
git diff --quiet -- data/srd || { echo "regenerate.sh: data/srd already has uncommitted changes — read 'git diff --stat data/srd' before regenerating again" >&2; exit 1; }
[[ -f "${SRD_PDF:-$HOME/Downloads/SRD_CC_v5.2.1.pdf}" ]] || { echo "regenerate.sh: no SRD PDF at ${SRD_PDF:-$HOME/Downloads/SRD_CC_v5.2.1.pdf}" >&2; exit 1; }

residue() {
    # One line per (monster, entry, clause), sorted, so `diff` reads as a census.
    jq -r '.items[] as $m | $m.entries[] as $e
           | ($e.unmodelledClauses // [])[] | "\($m.name) | \($e.section) | \($e.name) | \(.)"' data/srd/monsters.json | sort
}
residue > "$report/residue-before.txt"

echo "regenerate.sh: running the extractor"
dotnet run --project tools/SrdExtract -- --out data/srd > "$report/extract.log" 2>&1; rc=$?
tail -5 "$report/extract.log"
(( rc == 0 )) || { echo "regenerate.sh: extractor exited $rc — read $report/extract.log" >&2; exit 1; }

ok=1
expect() { # label, wanted, got
    if [[ "$2" == "$3" ]]; then printf '  ok    %s %s\n' "$1" "$3"; else printf '  FAIL  %s: expected %s, got %s\n' "$1" "$2" "$3"; ok=0; fi
}
count() { jq '.items|length' "data/srd/$1.json"; }
expect monsters 330 "$(count monsters)"
expect spells 339 "$(count spells)"
expect weapons 38 "$(count weapons)"
expect armor 13 "$(count armor)"
expect magic-items 258 "$(count magic-items)"
# The extractor prints its own totals ("Validation warnings: N") and lists at most 25
# lines under each, so the totals are read, not the lines.
warnings="$(grep -oE '^Validation warnings: [0-9]+' "$report/extract.log" | grep -oE '[0-9]+$' || echo 0)"
expect "warnings (all expected)" 15 "$warnings"
errors="$(grep -oE '^Validation errors: [0-9]+' "$report/extract.log" | grep -oE '[0-9]+$' || echo 0)"
expect errors 0 "$errors"

residue > "$report/residue-after.txt"
diff "$report/residue-before.txt" "$report/residue-after.txt" > "$report/residue.diff"
vanished="$(grep -c '^<' "$report/residue.diff")"; appeared="$(grep -c '^>' "$report/residue.diff")"
echo "  residue: $(wc -l < "$report/residue-after.txt") lines after; $vanished vanished (now claimed — each needs the code that expresses it), $appeared appeared (now honest — may demote a creature)"
git diff --stat -- data/srd | tail -3
dotnet run --project tools/SrdExtract -- --census "$report/census.txt" --out data/srd > /dev/null 2>&1 && grep -m1 '^Census' "$report/census.txt"

echo "report: $report (extract.log, residue-before/after.txt, residue.diff, census.txt)"
(( ok )) && exit 0 || { echo "regenerate.sh: a count or warning total moved — that is a finding, not noise" >&2; exit 1; }
