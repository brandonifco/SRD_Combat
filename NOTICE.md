# Third-party content notices

## System Reference Document 5.2.1

Game content in this repository — including monster stat blocks, spells, weapons,
armour, equipment, magic items, species, classes, subclasses, backgrounds, feats,
conditions, and the rules they encode — is derived from the System Reference
Document 5.2.1, used under the Creative Commons Attribution 4.0 International
License.

> This work includes material from the System Reference Document 5.2.1
> ("SRD 5.2.1") by Wizards of the Coast LLC, available at
> https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative
> Commons Attribution 4.0 International License, available at
> https://creativecommons.org/licenses/by/4.0/legalcode.

Per the SRD's own terms, this project includes no other attribution to Wizards of
the Coast or its parent or affiliates beyond the statement above.

### Notes on how the content is derived

- Content under `data/` is generated from the SRD by `tools/SrdExtract`. It is a
  structured transformation of the source, not a reproduction of the document.
- Where this project's engine implements a rule differently from the SRD — a
  simplification, an omission, or a deliberate divergence — that difference is
  recorded alongside the content or in `docs/`, so shipped text never describes
  behaviour the game does not actually have.
- **Printed values are preserved even where the SRD disagrees with itself.** The
  Archmage's stat block prints `CR 12 (XP 8,000)` while the SRD's own Challenge Rating
  table gives 8,400 for CR 12. The printed 8,000 is what ships; the discrepancy is
  reported by the content validator rather than silently overridden.
- **Corrections are limited to extraction artifacts and are listed in one place.**
  Where the printed page is right but the PDF's text layer is wrong, the value is
  repaired in `tools/SrdExtract/Parsing/KnownCorrections.cs`, which records why each
  correct value is certain from the rules. At present there is exactly one: the Young
  White Dragon's Intelligence save, whose minus sign does not survive text extraction.

### The source PDF is not distributed here

`reference/` holds local text extractions of the SRD PDF used during development.
It is untracked and not distributed with this project. The SRD itself is freely
available from Wizards of the Coast at the URL in the attribution above.
