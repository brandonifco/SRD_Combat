---
name: regenerate-content
description: Re-run the SRD extractor into data/srd and review what moved: exact counts, the fifteen expected warnings, the residue census, pool floors. Use after any change to tools/SrdExtract, ConditionRules.Executable or a Core definition field, or when a content test fails on an entry nobody edited.
---

# regenerate-content

`data/srd` is committed and generated. Two things are therefore true at once: the game
runs without the PDF, and **the content can disagree with the code** whenever the code
that produces it changes and nobody re-runs the producer. The symptom of the second is
always the same and always confusing — a content test failing on an entry you did not
touch — because the rider claims call `ConditionRules.CanBeImposed` at extraction time,
so the allowlist decides what lands in `UnmodelledClauses` as residue. Change the code,
regenerate, or the content lies about what the engine does.

## When a regeneration is mandatory

- Any change under `tools/SrdExtract/` — parsers, grammars, claims, `KnownCorrections`.
- Any change to `ConditionRules.Executable` (CLAUDE.md says this in bold; it is the
  commonest miss).
- A new or renamed field on a `Core` definition that the extractor fills. There is no DTO
  mirror and no schema, deliberately: adding the field plus regenerating *is* the change.
- A content test failing on an entry outside your diff. Regenerate and read the diff
  before assuming the test is wrong.

The PDF is at `~/Downloads/SRD_CC_v5.2.1.pdf` and is never committed; only this step
needs it.

## The procedure

```bash
bash .claude/skills/regenerate-content/scripts/regenerate.sh --out <scratchpad>/regen
```

It snapshots the residue census, runs the extractor, checks the book's exact totals and
the warning count, diffs the residue, and writes the extractor's own `--census`. Then
read, in this order:

1. **The counts and the warnings.** 330 monsters, 339 spells, 38 weapons, 13 armor,
   258 magic items, 0 errors, **exactly 15 warnings**. The fifteen are the Archmage's XP
   (a real SRD inconsistency, kept), twelve column-break-truncated spell component
   lines, and two "Rarity Varies" items. A sixteenth is something new the validator
   found; a fourteenth means a known inconsistency stopped being reported, which is
   worse. Trust the run over any prose count.
2. **`residue.diff`.** Each line is one monster, one entry, one clause. A clause that
   **vanished** is now claimed — the extractor asserts the model *expresses* it. That is
   only true if code executes it: a claim follows the code and never leads it. If you
   did not add that code, the claim is a misattribution and the regeneration is wrong.
   A clause that **appeared** is honesty: something that was silently credited is now
   counted, and it may demote a creature out of the pool.
3. **`git diff --stat data/srd`.** Only the files your change reaches should move.
   A monster-only grammar change that moves `spells.json` has coupling you did not
   intend (PR #408 checked exactly this and found none; PR #389 predicted species
   residue would move and reported honestly that it did not).
4. **The tests that pin the content**, focused first:

   ```bash
   dotnet test tests/SrdExtract.Tests -c Debug --filter "FullyQualifiedName~CorpusRoundTrip"
   dotnet test tests/SRDCombat.Content.Tests -c Debug
   ```

   `CorpusRoundTripTests` fails on exactly the entries whose stored JSON disagrees with
   the parser — before regeneration that is your change's footprint, after it the test
   must be green. `EntryMechanicsTests` carries exact counts (multiattacks with residue,
   tier-one counts) that a regeneration legitimately moves: update them with the reason
   in the same commit, never by loosening to a floor. `MonsterPoolTests` holds pool
   floors that only ratchet **up** as creatures are restored; a floor that would have
   to go down is a stop-and-ask, because a demotion is a real rule the engine never
   executed (PR #389's Ettin) and the steward decides whether to accept it.

## What goes in the PR

The regeneration has its own section, on the model of PRs #395 and #408:

- the command, the clean-run line, and the warning count;
- which files under `data/srd` changed and why only those;
- the residue movement, counted (`N vanished, M appeared`) with each vanished clause
  tied to the code that now executes it;
- any grade or pool movement, per creature, with the blocking clause quoted;
- what you verified against the PDF (`srd-lookup` skill) — at least every entry whose
  claim changed, quoting the printed wording.

The `data/srd` diff itself is reviewable: ask `qc` to read the residue diff line by
line. Committing regenerated content without that account is the goblin bug wearing a
regeneration.

## Do not

- Pass `--force`. It overrides validation errors; an error is the validator doing its
  job, and the fix is upstream of the run.
- Hand-edit anything under `data/srd`. It is overwritten by the next run, and a hand
  edit that survives a regeneration is a `KnownCorrections` entry with a self-invalidating
  check, not a JSON edit.
- Regenerate to make a content test green without reading the residue diff.
- Treat a lower pool floor as a number to adjust. It is a creature the game will stop
  fielding; say so and route it.
