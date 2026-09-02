#!/usr/bin/env bash
#
# The docs-grep gate (#417): given a diff, find the prose that still cites what the diff
# deleted or renamed.
#
#   docs-grep.sh [<base-ref>] [--range <a>..<b>]
#
# Default base is origin/main (merge-base with HEAD, plus the working tree). --range
# reads a historical range instead, for re-running on a merged PR.
#
# What it extracts from the diff's deleted lines:
#   - deleted or renamed files (by basename, and the stem without extension)
#   - identifiers that appear on '-' lines and on no '+' line — PascalCase types,
#     methods and tests, four or more characters, that also no longer occur anywhere in
#     the tree's code (so a moved declaration does not count as deleted)
# and then greps for each in: docs/**/*.md, CLAUDE.md, README.md, CONTRIBUTING.md,
# NOTICE.md, client/README.md, .claude/**/*.md, and XML doc comments (`///` lines) in
# every .cs file. Every hit is printed with file:line. The author fixes or justifies
# each one in the PR; qc re-runs this on review.
#
# It is deliberately dumb: grep-by-identifier catches the class of drift that has cost
# qc rounds (a deleted test file still named in a design doc, a method a doc says
# "never refuses" after the diff made it refuse) and cannot catch semantic drift with
# no shared identifier — that stays a reader's job. Known blind spots, by design:
#   - a name with one capital and under eight characters (Dodge, Loot) is taken as prose
#   - a name that survives in NON-comment code anywhere is "moved, not deleted"; a `//`
#     or `///` comment does not keep it alive (it would otherwise hide the very
#     citations this looks for)
#   - only .cs .py .sh .yml .json .csproj .tscn diffs are read; a rename in any other
#     file type is not seen
#   - candidates can include capitalised words from deleted comments (noise, not hits)
#
# Exit 0 with no hits, 1 with hits, 2 usage.
set -uo pipefail

root="$(git rev-parse --show-toplevel)"; cd "$root"
base="origin/main"; range=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --range) range="$2"; shift 2 ;;
        -*) echo "usage: docs-grep.sh [<base-ref>] [--range a..b]" >&2; exit 2 ;;
        *) base="$1"; shift ;;
    esac
done

if [[ -z "$range" ]] && ! git rev-parse --verify -q "$base" >/dev/null; then
    echo "docs-grep: $base does not resolve here (a shallow clone?) — skipped, not passed"; exit 0
fi
if [[ -n "$range" ]]; then
    diff_cmd=(git diff -U0 "$range")
    # A match inside a comment does not count: a deleted symbol that lives on only in a
    # `// used to` note, or in the very `///` citation this gate exists to find, is gone.
    # Captured, not piped into `grep -q`: under pipefail an early exit turns "found" into
    # "not found" and every live symbol becomes a candidate.
    tree_grep() { local out; out="$(git grep -h -I -- "$1" "${range#*..}" -- '*.cs' '*.py' '*.sh' '*.yml' 2>/dev/null | grep -vE '^[[:space:]]*(//|#)')"; [[ -n "$out" ]]; }
    names_cmd=(git diff --name-status -M "$range")
else
    mb="$(git merge-base "$base" HEAD)"
    diff_cmd=(git diff -U0 "$mb")
    tree_grep() { local out; out="$(grep -rhI --include='*.cs' --include='*.py' --include='*.sh' --include='*.yml' --exclude-dir=bin --exclude-dir=obj --exclude-dir=.git -- "$1" . 2>/dev/null | grep -vE '^[[:space:]]*(//|#)')"; [[ -n "$out" ]]; }
    names_cmd=(git diff --name-status -M "$mb")
fi

declare -A candidates=()
# Deleted and renamed files: cite-able by basename and by stem.
while IFS=$'\t' read -r status a b; do
    case "$status" in
        D) candidates["$(basename "$a")"]=file; candidates["${a##*/}"]=file; s="$(basename "$a")"; candidates["${s%.*}"]=file ;;
        R*) s="$(basename "$a")"; candidates["$s"]=file; candidates["${s%.*}"]=file ;;
    esac
done < <("${names_cmd[@]}")

# Identifiers on '-' lines and on no '+' line.
globs=('*.cs' '*.py' '*.sh' '*.yml' '*.json' '*.csproj' '*.tscn')
minus="$("${diff_cmd[@]}" -- "${globs[@]}" | grep -E '^-[^-]' | grep -oE '\b[A-Z][A-Za-z0-9_]{3,}\b' | sort -u)"
plus="$("${diff_cmd[@]}" -- "${globs[@]}" | grep -E '^\+[^+]' | grep -oE '\b[A-Z][A-Za-z0-9_]{3,}\b' | sort -u)"
for id in $(comm -23 <(printf '%s\n' "$minus") <(printf '%s\n' "$plus")); do
    # A single capitalised word ("Fixed", "Hit") is prose, not an identifier: require a
    # second capital (CamelCase) or a name long enough to be a type on its own.
    caps="${id//[^A-Z]/}"
    (( ${#caps} >= 2 || ${#id} >= 8 )) || [[ "$id" == *_* ]] || continue
    [[ "$id" == "$caps" ]] && continue   # ALLCAPS is a comment shout, not a symbol
    # Still declared or used somewhere in the tree's code → moved, not gone.
    tree_grep "\b$id\b" && continue
    candidates["$id"]=identifier
done

if (( ${#candidates[@]} == 0 )); then
    echo "docs-grep: nothing deleted or renamed that prose could cite"; exit 0
fi

echo "docs-grep: $(printf '%s\n' "${!candidates[@]}" | sort | tr '\n' ' ')"
hits=0
prose=(CLAUDE.md README.md CONTRIBUTING.md NOTICE.md client/README.md)
for id in $(printf '%s\n' "${!candidates[@]}" | sort); do
    [[ ${#id} -ge 4 ]] || continue
    if [[ -n "$range" ]]; then
        # Historical range: read the prose as it stood at the range's end, not today's.
        ref="${range#*..}"
        prose_hits() { { git grep -nI -w -- "$1" "$ref" -- 'docs/*.md' '.claude/*.md' "${prose[@]}" 2>/dev/null;
                         git grep -nI -- "///.*\b$1\b" "$ref" -- '*.cs' 2>/dev/null; } | sed "s|^$ref:||"; }
    else
        prose_hits() { { grep -rnI --include='*.md' -w -- "$1" docs .claude "${prose[@]}" 2>/dev/null;
                         grep -rnI --include='*.cs' --exclude-dir=bin --exclude-dir=obj -- "///.*\b$1\b" . 2>/dev/null; } | sed 's|^\./||'; }
    fi
    while IFS= read -r line; do
        [[ -n "$line" ]] || continue
        (( hits == 0 )) && echo "hits (each needs fixing or a stated justification in the PR):"
        hits=$((hits + 1)); printf '  %-28s %s\n' "$id" "$line"
    done < <(prose_hits "$id")
done
if (( hits )); then echo "docs-grep: $hits hit(s)"; exit 1; fi
echo "docs-grep: no prose cites what this diff removed"; exit 0
