#!/usr/bin/env bash
#
# Prints one page of the SRD PDF as text, one column at a time, so a rule can be read
# the way it is printed rather than the way memory has it.
#
#   srd-page.sh <page> [left|right|both|full]     (default: both, left column then right)
#
# Printed page numbers equal PDF indices for this document (364 pages). Body text is two
# 297pt columns on a 594pt-wide sheet; asking for a whole page in -layout mode
# interleaves the columns line by line and produces sentences that exist nowhere in the
# book, which is how a rule gets misread. Cropping per column is the fix for prose.
# It is the wrong tool for a FULL-WIDTH TABLE — the class feature tables (pages 28-82)
# and the origin tables span both columns, and a column crop cuts their rows in half
# (page 28's Barbarian table comes back as "Rage, Unarmored Defense, Wea" and orphaned
# numbers). Use `full` for those: whole-page -layout keeps a table's rows together.
#
# The PDF lives at ~/Downloads/SRD_CC_v5.2.1.pdf (override with SRD_PDF) and is never
# committed; this needs pdftotext (poppler-utils).
set -euo pipefail

page="${1:-}"; which="${2:-both}"
[[ "$page" =~ ^[0-9]+$ ]] || { echo "usage: srd-page.sh <page> [left|right|both]" >&2; exit 2; }
pdf="${SRD_PDF:-$HOME/Downloads/SRD_CC_v5.2.1.pdf}"
[[ -f "$pdf" ]] || { echo "srd-page.sh: no PDF at $pdf" >&2; exit 1; }
command -v pdftotext >/dev/null || { echo "srd-page.sh: pdftotext not installed (sudo apt install poppler-utils)" >&2; exit 1; }

column() { # x-origin
    pdftotext -f "$page" -l "$page" -x "$1" -y 0 -W 297 -H 783 -layout "$pdf" - | sed -E 's/[[:space:]]+$//' | { grep -v '^$' || true; }
}
full() {
    pdftotext -f "$page" -l "$page" -layout "$pdf" - | sed -E 's/[[:space:]]+$//' | { grep -v '^$' || true; }
}
case "$which" in
    left)  column 0 ;;
    right) column 297 ;;
    both)  echo "── page $page, left column ──"; column 0; echo; echo "── page $page, right column ──"; column 297 ;;
    full)  echo "── page $page, whole page (for full-width tables; prose columns interleave) ──"; full ;;
    *) echo "usage: srd-page.sh <page> [left|right|both|full]" >&2; exit 2 ;;
esac
