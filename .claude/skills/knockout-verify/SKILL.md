---
name: knockout-verify
description: Prove a test, guard, refusal, validator or gate goes red when its behaviour is stubbed out, and record the table for the PR. Use whenever a PR adds or changes a test or guard, claims 'pinned by' or 'covered by', or a knockout table comes back all green.
---

# knockout-verify

A test that cannot fail is not evidence. It is a green light wired to nothing.

This project learned that the expensive way. On 2026-08-26/27 five verification
instruments were found lying in one session (#528): the probe "covered the end-to-end
path" while four behaviours could be deleted with 4,496 tests staying green (#490); a
probe helper compared bare captions against `"Space · End Turn"` and so never clicked
anything (#499); a "refusal" capture was a byte-copy of the previous capture; nine of
sixteen probe captures differed on every run from one unguarded clock (#518); a sprite
gate enumerated the filesystem and was green in CI, red on Brandon's machine (#522).
Every one looked handled; every one was found only when someone established a
baseline before trusting it. And the correlation in that session was stark: everything
that shipped *with* a knockout table was honest, everything without one was not.

So: **an instrument that has not been knocked out is not evidence, and a PR that
asserts one says so out loud rather than letting a green suite imply it.**

## The procedure

1. **List the claims.** Each "pins", "guards", "refuses", "catches" in your PR body is a
   claim. Write them down as one line each — behaviour, and the test or check that
   supposedly detects its absence.
2. **Design one stub per claim** that removes *exactly that behaviour* and nothing else,
   and still compiles. The clean shapes:
   - invert or short-circuit the guard: `if (!present)` → `if (!present || true)`;
   - drop the branch: delete the `return refusal;` line, or the condition around it;
   - revert the fix: put the old predicate back;
   - for a validator or gate: feed it the exact historical bad input (a sheet resized
     to 119×64, PR #461's shape; an untracked extra PNG; a deleted tracked file).
   A stub that changes two things tells you nothing about which one the test saw.
3. **Snapshot, stub, run, and let it restore.** Snapshot first — the restore comes
   from the snapshot, not from HEAD, so your own uncommitted fix in the same file is
   safe:

   ```bash
   bash .claude/skills/knockout-verify/scripts/knockout.sh snapshot -- src/SRDCombat.Game/ScenarioArguments.cs
   # apply the stub with the Edit tool
   bash .claude/skills/knockout-verify/scripts/knockout.sh run \
     --project tests/SRDCombat.Game.Tests --filter "FullyQualifiedName~ScenarioArgumentsTests" \
     --label "TryParseLevel: always take the absent branch" \
     --log <scratchpad>/knockout-table.md -- src/SRDCombat.Game/ScenarioArguments.cs
   ```

   `run` refuses without a snapshot, refuses if no named file differs from its snapshot
   (no stub applied), runs the focused test, appends a row to the table, copies the
   snapshot back, and refuses to continue unless every file is byte-identical to it.
   Each stub is applied alone; never stack them. Snapshot again after any real edit,
   or the restore would undo it.
4. **Read the verdicts.** They are not all good news, and the honest ones are the
   point:
   - **RED** — the claim holds. Record which tests went red; if a hundred did, your stub
     was too wide, try a narrower one.
   - **GREEN** — nothing pins this. Either write the test that goes red, or state the
     gap in the PR as a gap. PR #523 is the model: "Probe guard removed → GREEN. That
     last row is the honest one and it is the point" — the guard was reachable only by
     the probe capture comparison, and the PR said so instead of implying coverage.
   - **VACUOUS** — the filter matched no tests. A knockout that ran nothing is green by
     vacuity; fix the filter.
   - **BUILD FAILED** — the stub did not compile. That is not a knockout; the question
     is whether the test detects a *behaviour* change, and a build error is not one.
5. **Put the table in the PR** under Tests and evidence, with the file:line of each
   stub. The log file is the table; do not retype it from memory.

## Reading the table as a whole

- **All green is a broken harness, not a good result.** PR #507's first run reported
  26 of 26 green — the harness misparsed xUnit's output. Validate at least one row by
  hand (apply the stub, run the test, look at the failure with your own eyes) before
  trusting a table, and always when every row agrees.
- **A single RED that named the wrong test** is a finding too: PR #507's second run
  found a test that passed with its ordering reversed because two monsters happened to
  share a colour. The stub went red, but not where expected. Read the failed names.
- **Instruments outside xUnit** knock out the same way with a different runner: for
  the probe, remove the guard and run `scripts/probe-diff.sh` expecting a difference;
  for a validator, feed the historical bad input and expect the named error; for a
  script gate, run it against the tree with the bad state present. The table has the
  same two columns.

## Where this stops

It cannot reach what the suite cannot reach. The Godot client's live node — `PlayMode`
under `OnReady`, the argv wiring behind `OS.GetCmdlineUserArgs()` — is pinned by
nothing in xUnit (#490, #190), and a knockout there comes back GREEN by construction.
Say that in the PR, cite the issue, and name what *does* pin it (the probe capture
comparison, or nothing). "Not reachable" written down is honest; "covered by the probe"
without a red row is the shape #528 was filed about.

`scripts/focused-test.sh` is usable on its own whenever you want a RED/GREEN verdict
from a project or filter that does not lie about counts.
