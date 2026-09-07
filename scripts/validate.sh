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

# #599: tools/asset_pipeline/test_master_to_sprite.py is a real unittest suite
# that nothing ran automatically — a green CI said nothing about the Python
# pipeline tests. It has no Debug/Release dimension, so this runs once, not
# once per configuration (see the `ci` case below).
#
# Guarded rather than required: a machine without python3/Pillow still gates
# the dotnet suite, per #599's acceptance criteria — `./scripts/doctor.sh`
# reports the same gap as an optional-tooling warning. CI always has both (the
# workflow installs Pillow before calling this), so there the guard never
# trips and a real test failure always fails the gate.
python_tests() {
  local test_file=tools/asset_pipeline/test_master_to_sprite.py

  echo; echo "== tools/ Python tests ($test_file) =="

  # The test file is tracked and always expected. A missing one is a repo
  # regression (renamed/deleted), not an environment gap, so it fails the gate
  # even where python3/Pillow are absent — otherwise the step could pass
  # vacuously, gating nothing (#599).
  if [[ ! -f "$test_file" ]]; then
    echo "ERROR: $test_file is missing (renamed or deleted?) — the gate cannot verify the pipeline (#599)." >&2
    return 1
  fi

  # python3/Pillow absent IS an environment gap: a machine without them still
  # gates the dotnet suite (./scripts/doctor.sh reports the gap). CI installs
  # Pillow, so there this never trips and a real test failure fails the gate.
  if ! command -v python3 >/dev/null 2>&1; then
    echo "skipped: python3 not found (see ./scripts/doctor.sh)"
    return 0
  fi
  if ! python3 -c 'import PIL' >/dev/null 2>&1; then
    echo "skipped: Pillow not installed (see ./scripts/doctor.sh)"
    return 0
  fi

  python3 "$test_file"
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
    python_tests
    git diff --check; docs_grep ;;
  ci)
    case "${2:-}" in
      Debug|Release)
        sdk_pin; dotnet restore "$SLN"
        build "$2"; test_suite "$2"
        # Run once across the two-leg matrix, not once per leg — see
        # python_tests' comment. Debug is the leg that carries it. Use a full
        # `if` rather than `[[ … ]] && python_tests`: as the branch's trailing
        # statement, the `&&` form returns the failed `[[ ]]`'s exit 1 on the
        # Release leg (condition false, short-circuited), failing an otherwise-
        # green `ci Release` — which `validate.sh full` never exercises.
        if [[ "$2" == "Debug" ]]; then
          python_tests
        fi
        ;;
      *) usage ;;
    esac ;;
  *) usage ;;
esac
