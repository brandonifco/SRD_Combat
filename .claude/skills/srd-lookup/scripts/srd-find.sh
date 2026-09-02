#!/usr/bin/env bash
#
# Finds where a phrase is printed in the SRD, by page, so the page can be read with
# srd-page.sh.
#
#   srd-find.sh <regex> [--context N]
#
# Searches reference/SRD_raw.txt (gitignored; regenerated here with pdftotext if it is
# missing — the file keeps pdftotext's form-feed page breaks, which is how a line maps
# back to a page). Matching is case-insensitive (folded with tolower, so it works on
# mawk as well as gawk), and curly quotes are folded to straight ones on both sides, so
# a phrase copied from data/srd ("the target's") matches the printed "the target’s".
# Output is `page:line: text`, with N lines of context when asked.
set -euo pipefail

pattern="${1:-}"; ctx=0
[[ -n "$pattern" ]] || { echo "usage: srd-find.sh <regex> [--context N]" >&2; exit 2; }
[[ "${2:-}" == "--context" ]] && ctx="${3:-2}"
root="$(git rev-parse --show-toplevel)"
raw="$root/reference/SRD_raw.txt"
pdf="${SRD_PDF:-$HOME/Downloads/SRD_CC_v5.2.1.pdf}"

if [[ ! -s "$raw" ]]; then
    [[ -f "$pdf" ]] || { echo "srd-find.sh: no $raw and no PDF at $pdf to make it from" >&2; exit 1; }
    mkdir -p "$root/reference"
    echo "srd-find.sh: extracting $raw from the PDF (once)" >&2
    pdftotext "$pdf" "$raw"
fi

# pdftotext puts the form feed at the START of the next page's first line, so a line
# carrying one already belongs to the new page: count first, then assign.
awk -v ctx="$ctx" -v pat="$pattern" '
    BEGIN {
        page = 1
        gsub(/’/, "\047", pat); gsub(/[“”]/, "\"", pat); pat = tolower(pat)
    }
    {
        line = $0
        n = gsub(/\f/, "", line)
        page += n
        buf[NR] = line; pg[NR] = page
        probe = line; gsub(/’/, "\047", probe); gsub(/[“”]/, "\"", probe)
        if (tolower(probe) ~ pat) hit[NR] = 1
    }
    END {
        for (i = 1; i <= NR; i++) {
            if (!(i in hit)) continue
            for (j = i - ctx; j <= i + ctx; j++) {
                if (j < 1 || j > NR) continue
                mark = (j == i) ? ">" : " "
                printf "%s%d:%d: %s\n", mark, pg[j], j, buf[j]
            }
            if (ctx > 0) print "--"
        }
    }' "$raw"
