---
name: transcript-churn
description: Read the frozen skirmish transcript's whole diff and classify every hunk before regenerating it. Use when FrozenTranscriptTests fails or a change touches Encounter, the turn loop, dice, movement, conditions, death or narration strings.
---

# transcript-churn

`tests/SRDCombat.Core.Tests/Fixtures/skirmish-transcript.txt` pins one whole fight's
narration byte-for-byte: two hand-authored sides, one seed, initiative through
movement, opportunity attacks, damage, downing, death saves and death. It is the most
valuable test in the suite because it covers the *interaction* of all of those at once,
which no unit test does — and it has churned five times, each time on a real gameplay
change, twice catching a shipped bug nothing else saw.

Which means the failing test is not the problem. It is the report. The temptation when
it goes red is to regenerate the fixture so the tree is green again, and that is
precisely the move that would have shipped both bugs. **Read the diff before touching
the fixture. Regenerate only once every changed line is understood and intended.**

## 1. Get the whole diff

The assertion prints a truncated excerpt. Do not read the churn from it.

```bash
bash .claude/skills/transcript-churn/scripts/churn-diff.sh --out <scratchpad>/transcript.diff
```

The script un-skips the writer in the working tree, regenerates the fixture into place,
diffs it against the committed one, restores both files, and tells you the line count
and the first divergence. Read the saved diff whole.

## 2. Find the first divergence and classify from there

Everything after the first divergent line may be a consequence of it, so start there
and work forward. Each hunk is one of three things:

- **An intended narration change.** Your PR reworded a line, added a step kind, changed
  what is printed. The engine did the same thing; only the words differ. Expected, and
  cheap to account for — but check the count: three reworded lines should produce three
  hunks, not thirty.
- **A shifted dice stream.** One extra or one fewer roll somewhere moves every later
  roll by one, and the fight diverges completely from that point — different hits,
  different deaths, a different winner. The signature is a small, explicable first hunk
  (an Advantage roll now consumed where none was; a roll skipped on a refused action)
  followed by wholesale difference. Decide whether the *first* roll change is intended.
  If it is, the rest follows and the fight is simply a different fight now; say so, and
  confirm `TheFightExercisesTheHardParts` still passes, because a duller fight that
  happens to match is exactly what that test guards against. If it is not, you have
  found a bug in your change: fix the code.
- **An unintended behaviour change.** A creature moves differently, an attack that
  should have been refused lands, a condition ends a round early, damage differs with
  the same rolls. This is the case the fixture exists for. Fix the code, not the
  fixture. The two shipped bugs it caught were this shape.

Write the classification down as you go: hunk range, which of the three, one line of
why. That list becomes the PR's account.

## 3. Decide, then regenerate

Only when every hunk is classified and none is the third kind:

```bash
bash .claude/skills/transcript-churn/scripts/churn-diff.sh --keep
```

That leaves the new fixture in the tree. Commit it *with* the code change, never in a
separate commit, and put the hunk-by-hunk account in the PR body under Tests and
evidence. Then hand the PR to `qc`, whose charter says a regeneration without a
written account of why each changed line changed is a rejection.

If the churn was the third kind, the fixture stays as it is; the fix goes in, and you
run step 1 again until the diff is empty or fully intended.

## The neighbouring facts

- The transcript uses hand-authored combatants on purpose, so it moves when the
  *engine* moves and not when content does. `RealMonsterCombatTests` covers the
  content direction; if your change is to `data/srd` and the transcript still churned,
  something reached the engine that should not have.
- `TheSameSeed_AlwaysProducesTheSameFight` failing alongside the transcript means
  ambient randomness got in — `Random.Shared`, a clock, enumeration order. That is a
  determinism bug, not a churn, and no regeneration will fix it.
- `ScriptedRandomSource` throwing on a surplus roll in some other test is the same
  dice-stream signal seen from a different angle: a test's premise changed. Treat it as
  a finding, not a nuisance.
- The probe captures (`scripts/probe-diff.sh`) are the client-side sibling of this
  fixture, with the same rule: a capture that differs is read, not regenerated.
