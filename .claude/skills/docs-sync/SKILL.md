---
name: docs-sync
description: Keep prose true to the diff: grep docs, charters and /// comments for what a change deleted or renamed, regenerate docs/status.md when counts moved, fix invalidated CLAUDE.md claims in the same commit. Use before any commit that deletes, renames or changes cited behaviour.
---

# docs-sync

"Docs are part of the diff" is one of the four invariants CLAUDE.md keeps inline
because it can be broken from anywhere. It was broken four times in the F1 close-out
alone, after a dedicated drift sweep (#379) the same phase — #417's tally: a design doc left asserting
"never refuses on distance" about a method the same PR made refuse; a test file deleted
while two docs still named it; a doc comment saying a census "is not wired to any output
yet" the moment the wiring merged. The common cause is not carelessness. It is that the
docs citing a symbol are exactly the files you are not editing when the symbol moves,
and a periodic sweep catches old drift once and new drift never.

So the check is mechanical and runs on the diff.

## 1. Grep the prose for what you removed

```bash
bash .claude/skills/docs-sync/scripts/docs-grep.sh
```

It reads the diff against `origin/main` (plus your working tree), collects deleted or
renamed files and every identifier that is on a `-` line, on no `+` line, and nowhere
else in the code, then greps `docs/`, `CLAUDE.md`, the READMEs, `.claude/` and every
`///` doc comment for them. Each hit is `identifier  file:line`. For each one, do one of
two things: fix the prose in this commit, or write in the PR why the citation is still
true. There is no third option, and qc re-runs the script on review.

It is dumb on purpose. It cannot see that a sentence became false when no identifier
changed — "the pool holds two boss-band creatures" after a third is added. That is the
next step.

## 2. Read the claims your change touches

Grep is for symbols; you are for sentences. Before committing, search the docs for the
*subject* of your change, not just its identifiers:

```bash
rg -n -i "concentration|incapacitated" CLAUDE.md docs/guides docs/*.md
```

The places that go stale most, in order: the doc comment on the type you changed (it
says what the type does; does it still?); `docs/guides/engine.md` or
`docs/guides/extraction.md` for the subsystem; the design doc under `docs/` that
specified the slice, whose "acceptance" and "not yet" sentences date fastest;
`CLAUDE.md`'s Current state table and Standing conventions; `client/README.md` for
anything the player can see. When a design decision proves wrong, the doc is corrected
in the same commit and the old text moves to `docs/history/`, never to `/dev/null`.

## 3. Regenerate the measured facts

```bash
./scripts/status.sh            # content counts, line counts, git position
./scripts/status.sh --tests    # also runs the suite, for the test counts
```

`docs/status.md` is generated and never hand-edited: every hand-maintained copy of a
test count drifted, and CLAUDE.md's table claimed 4,718 against a measured 4,814 the
day it was replaced. Regenerate it when your change adds or removes tests, regenerates
content, or moves the line counts enough to matter, and commit the result with the
change. Do not type a number into prose that the script can produce; link to the file.

What the script deliberately cannot write is the *reading* of a number — that a pacing
median saturates, why a coverage percentage was retired. Those live in CLAUDE.md, and if
your change alters one, you write the new reading.

## 4. Numbers you typed yourself

A count asserted from one reading and repeated until it looked established is a named
bug shape here (PR #564: "nine worktrees" — there were eight). Any number in your PR body
or doc edit that is not from `status.sh` is a number you measured in this session with a
command you can paste. Re-count before you write it a second time.

## What this does not replace

`qc`'s read of semantic drift, and the steward's guard on CLAUDE.md's plan rows. This
skill catches the greppable class so those two spend their time on the other one.
