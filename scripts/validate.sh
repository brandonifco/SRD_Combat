#!/usr/bin/env bash
#
# The canonical gate. Humans, agents, and CI all call this script, so the build and
# test invocation exists in exactly one place instead of being restated in
# CLAUDE.md, CONTRIBUTING.md, and .github/workflows/dotnet.yml — three copies that
# could drift apart without anything failing.
#
#   ./scripts/validate.sh fast    # builds Debug + Release at 0 warnings, no tests
#   ./scripts/validate.sh full    # the merge gate: fast + the whole suite
#   ./scripts/validate.sh sdk-pin # the #428 drift check alone
#
# fast and full end with the docs-grep gate (#417): prose still citing what the diff
# deleted is printed, loudly, and does not fail the run — a hit may be a justified
# historical mention, and the rule is "fix or justify in the PR", which a script cannot
# judge. It runs here so nobody has to remember it; CI skips it (no origin/main).
#
# CI calls `ci Debug` / `ci Release` so each matrix leg does its half.
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

SLN=SRDCombat.sln

usage() { echo "usage: $0 {fast|full|sdk-pin|ci Debug|ci Release}" >&2; exit 2; }

# A green local build used to be able to compile on a different major than CI gated
# (#428). global.json pins with rollForward: disable, and this asserts the pin held.
sdk_pin() {
  local pinned resolved
  pinned="$(grep -oP '"version"\s*:\s*"\K[^"]+' global.json)"
  resolved="$(dotnet --version)"
  echo "global.json pins: $pinned"
  echo "dotnet resolved:  $resolved"
  if [[ "$resolved" != "$pinned" ]]; then
    echo "::error::dotnet resolved SDK $resolved, but global.json pins $pinned. Not building on the pinned SDK — the #428 drift class is open again."
    exit 1
  fi
}

build() { dotnet build "$SLN" --configuration "$1" --no-restore; }

docs_grep() {
  local script=.claude/skills/docs-sync/scripts/docs-grep.sh
  [[ -f "$script" ]] || return 0
  echo; echo "== docs-grep (#417): prose that cites what this diff deleted =="
  bash "$script" || echo "== docs-grep: fix each hit, or justify it in the PR body =="
}

# Minimal console verbosity keeps a CI failure readable in a few lines rather than
# thousands — the log is read by agents, and a wall of passing test names is noise.
# The trx is written only so a failed run can be inspected in detail.
test_suite() {
  local args=(--logger "console;verbosity=minimal")
  if [[ "${GITHUB_ACTIONS:-}" == "true" ]]; then
    mkdir -p TestResults
    args+=(--logger "trx;LogFileName=test-results.trx" --results-directory ./TestResults)
  fi
  dotnet test "$SLN" --configuration "$1" --no-build "${args[@]}"
}

case "${1:-}" in
  sdk-pin) sdk_pin ;;
  fast)
    sdk_pin; dotnet restore "$SLN"
    build Debug; build Release; git diff --check; docs_grep ;;
  full)
    sdk_pin; dotnet restore "$SLN"
    build Debug; test_suite Debug
    build Release; test_suite Release
    git diff --check; docs_grep ;;
  ci)
    case "${2:-}" in
      Debug|Release)
        sdk_pin; dotnet restore "$SLN"
        build "$2"; test_suite "$2" ;;
      *) usage ;;
    esac ;;
  *) usage ;;
esac
