---
name: srd-lookup
description: Read a rule from the printed SRD 5.2.1 PDF instead of memory: find the page, crop the column, quote the wording with its page. Use before implementing or reviewing any rule, condition, spell, stat-block entry or class feature, and whenever a PR claims 'verified against the PDF'.
---

# srd-lookup

Memory of the 2014 rules is wrong about the 2024 ones, and this project has been bitten
each time it trusted it: Grappled is Disadvantage only against targets *other than* the
grappler; there is **no generic Escape action** (escape is Athletics or Acrobatics against
a flat DC); a Long Rest restores *all* Hit Dice; ability increases come from the
background, not the species. Every one was caught by reading the page — the glossary
for the first three, the origins chapter for the last — not by reasoning. So the rule here is print first, and a reading that departs from print is a
written, cited decision (`AreaTargeting` is the model), never a silent one.

## Find the page

```bash
bash .claude/skills/srd-lookup/scripts/srd-find.sh "Grappled condition" --context 2
```

Prints `page:line: text`. The first run extracts `reference/SRD_raw.txt` from the PDF
(gitignored, under ten seconds). Printed page numbers equal PDF indices for this
document. Matching is case-insensitive, and curly quotes fold to straight ones on both
sides, so a phrase copied from `data/srd` matches the printed text.

If you already know the chapter, these ranges are the map (page = PDF index):

| Chapter | Pages |
| --- | --- |
| Classes | 28–82 |
| Character origins (species, backgrounds) | 83–86 |
| Feats | 87–88 |
| Equipment | 89–103 |
| Spells | 104–175 |
| Rules glossary (conditions, actions, cover, rests) | 176–191 |
| Gameplay toolbox (XP budgets **on 202**) | 192–203 |
| Magic items | 204–253 |
| Monsters | 254–343 |
| Animals | 344+ |

## Read the column, not the page

```bash
bash .claude/skills/srd-lookup/scripts/srd-page.sh 182 right
```

Body text is two columns. A whole-page text dump interleaves them line by line and
manufactures sentences that appear nowhere in the book — which is precisely how a rule
gets misread with total confidence. The script crops one 297-point column at a time.
**Except for full-width tables**: the class feature tables (pages 28–82), the origin
tables, and any table that spans the page are cut in half by a column crop — page 28's
Barbarian table comes back as "Rage, Unarmored Defense, Wea" with its numbers orphaned in
the other column. For a table, ask for `full`, which keeps rows together and accepts
that the prose around it interleaves:

```bash
bash .claude/skills/srd-lookup/scripts/srd-page.sh 28 full
```
Read the left column, then the right; a paragraph that ends mid-sentence continues at
the top of the next column or page. `srd-find.sh` tells you the page, not the column —
if the entry is not in the column you asked for, ask for the other one.

Three things to know about the text you get back: print uses curly quotes and
apostrophes (`“the target’s”`), and it is the *extractor* that straightens them into
`data/srd` (`"the target's"`) — `srd-find.sh` folds both so either spelling finds the
other, but a quote you paste from print carries the curly form; hyphenation at a line
break is a typesetting artefact, not a compound word; and a stat block's font carries meaning the
text loses — bold entry names, italic usage notes — so for a parser question, the
extractor's font-aware pass (`tools/SrdExtract`) is the authority and this is the
eyeball check.

## Quote and cite

What you write down is the printed sentence, verbatim, and where it is:

> "Attacks Affected. You have Disadvantage on attack rolls against any target other than
> the grappler." — SRD 5.2.1 p. 182, Grappled [Condition]

> "Ending a Grapple. A Grappled creature can use its action to make a Strength
> (Athletics) or Dexterity (Acrobatics) check against the grapple’s escape DC, ending
> the condition on itself on a success." — SRD 5.2.1 p. 182, Grappling

(Both found with the two commands above; the first draft of this skill cited page 179
from memory, and the script said 182. That is the whole point of the script.)

That line goes in the doc comment when the reading is a judgement call, in the PR body
under provenance when a parser claim rests on it, and in the issue when a divergence is
proposed. A claim of "verified against the PDF" without a page is a claim of having
looked, not of what was seen.

## When print is silent or ambiguous

Say so, in those words. Then write the reading down as an interpretation with its
reasoning, in the code's doc comment, following `AreaTargeting`. A deliberate departure
from a printed sentence needs the `designer`'s sign-off, and there is exactly one so far
(ending a move on a fallen ally). "The rules probably intend…" is a design decision
wearing a rules reading; route it.

## Do not

- Quote from memory, from a wiki, from the 2014 SRD, or from a summary. The 2024 text
  is the source and it changed things you would not expect.
- Read a stat block from a whole-page dump. Use the column crop, or the extracted entry
  in `data/srd/monsters.json`, which was parsed with fonts and coordinates.
- Add attribution to Wizards of the Coast anywhere beyond `NOTICE.md`; the SRD's own
  CC-BY terms forbid it.
