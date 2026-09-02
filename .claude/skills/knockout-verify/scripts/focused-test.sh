#!/usr/bin/env bash
#
# Runs one test project (optionally filtered) and reports RED / GREEN / VACUOUS from the
# trx counters, not from the console text.
#
#   focused-test.sh <test.csproj | tests/Project.Dir> [--filter <expr>] [-c Debug|Release]
#
# Why not read the console: the first knockout harness this project wrote reported
# all-26 green because xUnit's [FAIL] lines go to stderr and its summary line does not
# name tests (PR #507). The trx file carries exact counters and failed test names.
#
# Verdicts and exit codes:
#   GREEN     0  every test passed
#   RED       1  at least one test failed (names listed)
#   VACUOUS   2  zero tests ran — the filter matched nothing, so "green" meant nothing
#   BUILD     3  the project did not compile — a stub that does not build teaches nothing
set -uo pipefail

proj="${1:-}"; [[ -n "$proj" ]] || { echo "usage: focused-test.sh <csproj|dir> [--filter expr] [-c Debug]" >&2; exit 4; }
shift
filter=""; config="Debug"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --filter) filter="$2"; shift 2 ;;
        -c) config="$2"; shift 2 ;;
        *) echo "focused-test.sh: unknown argument $1" >&2; exit 4 ;;
    esac
done

results="$(mktemp -d)"
trap 'rm -rf "$results"' EXIT

args=(test "$proj" -c "$config" --logger trx --results-directory "$results" --logger "console;verbosity=quiet")
[[ -n "$filter" ]] && args+=(--filter "$filter")

out="$(dotnet "${args[@]}" 2>&1)"; rc=$?
trx="$(find "$results" -name '*.trx' | head -1)"

if [[ -z "$trx" ]]; then
    echo "BUILD: no test results were produced (dotnet test exit $rc)"
    printf '%s\n' "$out" | grep -E 'error [A-Z]+[0-9]+' | head -10
    exit 3
fi

python3 - "$trx" <<'PY'
import sys, xml.etree.ElementTree as ET
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
root = ET.parse(sys.argv[1]).getroot()
c = root.find(".//t:ResultSummary/t:Counters", NS)
total, passed, failed = int(c.get("total")), int(c.get("passed")), int(c.get("failed"))
skipped = total - passed - failed
if total == 0:
    print("VACUOUS: 0 tests ran — the filter matched nothing; a green here is not evidence")
    sys.exit(2)
if failed:
    print(f"RED: {failed} failed, {passed} passed, {skipped} skipped")
    for r in root.findall(".//t:UnitTestResult", NS):
        if r.get("outcome") == "Failed":
            print(f"  failed  {r.get('testName')}")
    sys.exit(1)
print(f"GREEN: {passed} passed, {skipped} skipped")
sys.exit(0)
PY
