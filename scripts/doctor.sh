#!/usr/bin/env bash
#
# Reports whether this machine can build, test and play SRD_Combat the way CI does.
#
# This is the "write the validator that asserts the shape of what should have been found"
# lesson from the extraction pipeline, pointed at the development environment instead of
# the SRD. Every environment problem this project has hit was silent: a machine with no
# .NET 8 SDK still built green locally, because `global.json` used to roll forward to
# whatever SDK was newest installed — .NET 10 on this machine, and (until #428's second
# half) on CI's runner too, even though CI pointed `setup-dotnet` at this exact pin.
# `global.json` now sets `rollForward: disable`, so that silent substitution can no
# longer happen anywhere — but prose still cannot catch a machine drifting from the pin
# on its own. This script answers for whichever machine you are actually sitting at.
#
# Exit code is 0 when nothing is broken, 1 when something will bite you.
# Warnings do not fail: they mark things that only matter for specific tasks.

set -uo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Colour only when writing to a terminal, so piping to a file or CI log stays readable.
if [[ -t 1 ]]; then
    readonly RED=$'\033[0;31m' GREEN=$'\033[0;32m' YELLOW=$'\033[0;33m'
    readonly BOLD=$'\033[1m' DIM=$'\033[2m' RESET=$'\033[0m'
else
    readonly RED='' GREEN='' YELLOW='' BOLD='' DIM='' RESET=''
fi

failures=0
warnings=0

pass() { printf '  %s✓%s %s\n' "$GREEN" "$RESET" "$1"; }
warn() { printf '  %s!%s %s\n' "$YELLOW" "$RESET" "$1"; warnings=$((warnings + 1)); }
fail() { printf '  %s✗%s %s\n' "$RED" "$RESET" "$1"; failures=$((failures + 1)); }
note() { printf '    %s%s%s\n' "$DIM" "$1" "$RESET"; }
section() { printf '\n%s%s%s\n' "$BOLD" "$1" "$RESET"; }

printf '%sSRD_Combat — environment check%s\n' "$BOLD" "$RESET"
note "$REPO_ROOT"

# ---------------------------------------------------------------------------
section 'Operating system'

if [[ -r /etc/os-release ]]; then
    # shellcheck disable=SC1091
    os_name="$(. /etc/os-release && printf '%s' "${PRETTY_NAME:-unknown}")"
    pass "$os_name"
else
    warn 'Cannot read /etc/os-release.'
fi

# ---------------------------------------------------------------------------
section '.NET SDK'

# The version CI installs, and therefore the only compiler whose verdict actually gates a
# merge. CI's setup-dotnet step reads `global-json-file: global.json`, and since #428's
# closing fix set `rollForward: disable`, global.json's exact patch is the *only* SDK
# either CI or a correct local build will resolve — no substitute major, minor, or even
# patch satisfies it. Read it here rather than hardcoding, so the two cannot drift apart.
ci_version="$(grep -oP '"version"\s*:\s*"\K[^"]+' "$REPO_ROOT/global.json" 2>/dev/null)"

if [[ -z "$ci_version" ]]; then
    warn 'Could not read the SDK version from global.json — skipping the CI comparison.'
else
    note "CI and a correct local build both resolve exactly $ci_version (rollForward: disable accepts no substitute)"
fi

if ! command -v dotnet >/dev/null 2>&1; then
    fail 'dotnet is not on PATH.'
    note 'Install it with `mise install` from the repository root.'
else
    pass "dotnet found at $(command -v dotnet)"

    # More than one dotnet on PATH has been a live problem here: an apt binary holding no
    # SDKs shadowing a snap that has them, or the reverse.
    mapfile -t dotnet_paths < <(command -v -a dotnet 2>/dev/null | sort -u)
    if (( ${#dotnet_paths[@]} > 1 )); then
        warn "More than one dotnet on PATH: ${dotnet_paths[*]}"
        note 'The first one wins. Confirm it is the one you mean.'
    fi

    mapfile -t sdks < <(dotnet --list-sdks 2>/dev/null | awk '{print $1}')

    if (( ${#sdks[@]} == 0 )); then
        fail 'dotnet is installed but carries no SDKs (runtime-only host).'
    else
        note "Installed SDKs: ${sdks[*]}"

        if [[ -n "$ci_version" ]]; then
            has_exact=0
            for sdk in "${sdks[@]}"; do
                [[ "$sdk" == "$ci_version" ]] && has_exact=1
            done

            if (( has_exact )); then
                pass "The exact pinned SDK is installed: $ci_version"
            else
                fail "SDK $ci_version is not installed, and rollForward: disable accepts no substitute — not even a newer patch of 8.0."
                note 'This is not just a different compiler and analyzers: with rollForward disabled,'
                note '`dotnet` will refuse to resolve an SDK for this repository at all.'
                note 'Fix: `mise install` in the repository root.'
            fi
        fi
    fi

    # What the SDK actually selects here. With rollForward: disable there is no roll-forward
    # left to silently apply — this is either exactly the pin or a hard resolution failure.
    if selected="$(cd "$REPO_ROOT" && dotnet --version 2>/dev/null)"; then
        if [[ -n "$ci_version" && "$selected" != "$ci_version" ]]; then
            fail "This repository resolves to SDK $selected, not the pinned $ci_version."
        else
            pass "This repository resolves to SDK $selected"
        fi
    else
        fail 'dotnet could not resolve an SDK for this repository.'
        note 'global.json pins an exact version with rollForward: disable, and no installed SDK matches it.'
    fi
fi

# ---------------------------------------------------------------------------
section 'Version pinning'

if [[ -f "$REPO_ROOT/global.json" ]]; then
    pinned="$(grep -oP '"version"\s*:\s*"\K[^"]+' "$REPO_ROOT/global.json" 2>/dev/null)"
    roll="$(grep -oP '"rollForward"\s*:\s*"\K[^"]+' "$REPO_ROOT/global.json" 2>/dev/null)"
    pass "global.json pins $pinned (rollForward: ${roll:-default})"

    if [[ "$roll" != 'disable' ]]; then
        warn "rollForward is ${roll:-default}, not disable: a machine missing the pinned SDK can silently build on a substitute instead of failing."
        note 'This project sets rollForward: disable deliberately (#428) — that is exactly how it'
        note 'came to build on .NET 10 while CI, reading the same pin, built on 10.0.400 too.'
    fi
else
    warn 'No global.json — the SDK version is whatever happens to be installed.'
fi

if command -v mise >/dev/null 2>&1; then
    pass "mise found ($(mise --version 2>/dev/null | head -1))"

    if [[ -f "$REPO_ROOT/.mise.toml" ]]; then
        if (cd "$REPO_ROOT" && mise current dotnet >/dev/null 2>&1); then
            pass "mise is managing dotnet $(cd "$REPO_ROOT" && mise current dotnet 2>/dev/null)"
        else
            warn 'mise is installed but is not managing dotnet for this repository.'
            note 'Run `mise install` here, then activate mise in your shell:'
            note '  eval "$(mise activate bash)"   # and append it to ~/.bashrc'
        fi
    fi
else
    warn 'mise is not installed, so the toolchain is not pinned on this machine.'
    note 'Install: https://mise.jdx.dev/getting-started.html'
    note 'Then, in the repository root:'
    note '  mise install'
    note '  eval "$(mise activate bash)"   # and append it to ~/.bashrc'
    note 'The activation line is not optional: without it `mise install` succeeds and'
    note 'dotnet still resolves to whatever is first on PATH.'
fi

# ---------------------------------------------------------------------------
section 'Build and test prerequisites'

if [[ -f "$REPO_ROOT/SRDCombat.sln" ]]; then
    pass 'SRDCombat.sln present (classic format, readable by .NET 8)'
else
    if compgen -G "$REPO_ROOT/*.slnx" >/dev/null; then
        fail 'Found a .slnx solution. .NET 8 cannot read it and CI will not find a project.'
        note 'Recreate with `dotnet new sln --format sln`.'
    else
        fail 'No SRDCombat.sln at the repository root.'
    fi
fi

if [[ -d "$REPO_ROOT/data/srd" ]]; then
    content_files="$(find "$REPO_ROOT/data/srd" -name '*.json' 2>/dev/null | wc -l)"
    pass "Extracted content present (data/srd, $content_files json files)"
else
    fail 'data/srd is missing — the game and its content tests cannot run.'
fi

# ---------------------------------------------------------------------------
section 'Optional tooling'

note 'Nothing below is needed to build, test or play.'

# Only tools/SrdExtract needs the PDF, and it is deliberately never committed.
srd_pdf="${SRD_PDF:-$HOME/Downloads/SRD_CC_v5.2.1.pdf}"

if [[ -f "$srd_pdf" ]]; then
    pass "SRD PDF found at $srd_pdf"
else
    warn "No SRD PDF at $srd_pdf"
    note 'Needed only to re-run tools/SrdExtract. Ignore unless you are re-extracting.'
fi

if command -v pdftotext >/dev/null 2>&1; then
    pass 'pdftotext present (for eyeballing SRD pages)'
else
    warn 'pdftotext not installed — `sudo apt install poppler-utils` if you need it.'
fi

# The variant matters, not just the presence: the standard build cannot run a C# project,
# and installing it (mise's godot package does exactly that) leaves `client/` failing with
# no obvious cause. `--version` prints e.g. `4.7.stable.mono.official.<hash>`.
godot_bin="$(command -v godot 2>/dev/null || true)"
[[ -z "$godot_bin" && -x "$HOME/.local/bin/godot" ]] && godot_bin="$HOME/.local/bin/godot"

if [[ -n "$godot_bin" ]]; then
    godot_version="$("$godot_bin" --version 2>/dev/null | head -n 1)"
    if [[ "$godot_version" == *mono* ]]; then
        pass "Godot found ($godot_version) — the .NET build, which client/ needs"
    else
        warn "Godot at $godot_bin is not the .NET build ($godot_version) — it cannot run client/."
        note 'Install the ".NET" variant from godotengine.org; `godot --version` should say `mono`.'
    fi
else
    warn 'Godot not installed. Needed only to run client/, the Phase 7 viewer.'
fi

if command -v gh >/dev/null 2>&1; then
    pass 'gh present (the work queue and the merge workflow both use it)'
else
    warn 'gh not installed — `gh issue list` is this project'"'"'s work queue.'
fi

# The asset pipeline (tools/asset_pipeline/master_to_sprite.py, #294) is the
# only thing in the repo that needs Pillow, and needs nothing else — no
# numpy, no scikit-learn, deliberately, so this is the whole dependency.
if command -v python3 >/dev/null 2>&1; then
    if python3 -c 'import PIL' >/dev/null 2>&1; then
        pass "python3 with Pillow present (needed for tools/asset_pipeline)"
    else
        warn 'python3 found but Pillow is not installed.'
        note 'Needed only for tools/asset_pipeline/master_to_sprite.py: `pip install Pillow`.'
    fi
else
    warn 'python3 not installed. Needed only for tools/asset_pipeline (the sprite pipeline).'
fi

# ---------------------------------------------------------------------------
printf '\n'

if (( failures > 0 )); then
    printf '%s%d problem(s)%s' "$RED" "$failures" "$RESET"
    (( warnings > 0 )) && printf ', %d warning(s)' "$warnings"
    printf '. Fix the problems before trusting a local build.\n'
    exit 1
fi

if (( warnings > 0 )); then
    printf '%sReady%s, with %d warning(s) — all optional.\n' "$GREEN" "$RESET" "$warnings"
else
    printf '%sReady.%s Everything checks out.\n' "$GREEN" "$RESET"
fi
