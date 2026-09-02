# Extraction guide — the SRD pipeline and its traps

Moved out of `CLAUDE.md` on 2026-08-29. Needed only for work on `tools/SrdExtract` or
`data/srd`, which is why it no longer rides in every agent's context — but **read it
whole before parsing another SRD chapter.** Every trap below failed *silently*, caught
only by a validator or by checking against the book.

Read this together with CLAUDE.md's **"The rule this project runs on"**, which stays
inline there and is the doctrine this guide implements. The three founding bugs live in
that section; do not touch a parser without them.


The source PDF is `~/Downloads/SRD_CC_v5.2.1.pdf` (364 pages), not in the repo.
`reference/` holds gitignored text extractions
(`pdftotext ~/Downloads/SRD_CC_v5.2.1.pdf reference/SRD_raw.txt`; pages are
two-column — crop per column with `-x/-W` when eyeballing, the page is 594pt wide).

For eyeballing a page by column, the `srd-lookup` skill's `srd-page.sh` does the crop;
for re-running the pipeline and reviewing the residue diff, the `regenerate-content`
skill is the procedure.

The real pipeline is PdfPig with per-word coordinates and fonts:

```bash
dotnet run --project tools/SrdExtract -- --out data/srd
```

It refuses to write on validation errors (`--force` overrides). A clean run reports
330 monsters, 339 spells, 38 weapons, 13 armor, 258 magic items, 0 errors, and
**15 warnings, all expected** (the Archmage's XP — a real SRD inconsistency kept
deliberately; twelve column-break-truncated spell component lines; two "Rarity
Varies" items). Trust the run over any prose count.

**Fonts matter more than text** (`StatBlockFonts`): the same font at different sizes
is different signals, and `GillSans` / `GillSans-SemiBold` / `GillSans-SemiBold-SC700`
are three signals a substring test conflates — match exactly. Source variances
already handled (check before assuming a parser bug): `5 ft.` and `5 feet`; four
blocks with CR fields flipped; flat damage with no dice; `Melee or Ranged Attack
Roll` must be first in the regex alternation; the ability table's positional
MOD/SAVE triples. `KnownCorrections` holds the one hand repair and self-invalidates
when stale.

Page ranges (printed numbers = PDF indices): classes 28–82, origins 83–86, feats
87–88, equipment 89–103, spells 104–175, glossary 176–191, toolbox 192–203 (**XP
budgets on 202**), magic items 204–253, monsters 254–343, animals 344+.


Every one of these failed **silently**, caught only by a validator or by checking
against the book:

- **Typeface differs by chapter** (Cambria player-facing, Optima bestiary); match
  the style suffix, not the whole font name.
- **Weight differs within a table**; match the family (`GillSans`), not the face.
- **A class page mixes two layouts** — two-column body plus full-width table;
  `ClassParser` reads each page twice.
- **Don't split key from value on a gap**; match the closed set of known keys.
- **Table header columns are 12pt+ apart; words within a column 2–5pt.**
- **Not every caster uses the same table** (the Warlock's slot columns).
- **The Sorcerer's feature column wraps** — join on the raw cell, re-split, and only
  the line directly under a parsed row may join. Validator:
  `class.feature.no_heading`.
- **The two-column pass can slice the full-width table into feature prose** (#116) —
  prose is Cambria, tables are GillSans, so features append only Cambria lines.
  Validator: `class.feature.table_noise`.
- **The origins chapter has the same trap, and a wide column makes it worse** (#374):
  Draconic Ancestors, Elven Lineages and Fiendish Legacies are full-width tables
  inside the two-column species pages, sliced and interleaved into whichever trait
  was open — `OriginParser` now appends only Cambria lines to a trait, same as
  `ClassParser`. The Elven Lineages table's first column is wide enough to cross the
  column boundary outright, landing its fragments in the *next* species entirely:
  Gnome's Gnomish Lineage carried Elf's table, and Human's Versatile carried
  Tiefling's Fiendish Legacies. Validator: `species.trait.table_noise`.
- **A wrapped class list dropped 39 of 339 spells for months** while a `>= 300`
  floor test stayed green. Two lessons: *a number the pipeline prints about itself
  is not a check*, and *a floor is the wrong shape for a count fixed by the source*
  — exact counts for the book's totals, floors only for what should grow.

**The general lesson: write the validator that asserts the shape of what should have
been found.**
