---
name: probe-diff
description: Verify a Godot client change with the probe's captures: baseline twice on unmodified main, run once on the change, read every differing capture by its pixel box. Use for any change under client/ that draws, lays out, animates or handles input, and whenever a PR says 'captures unchanged'.
---

# probe-diff

The Godot client has no live-node tests: nothing in xUnit constructs `PlayMode` or calls
`OnReady` (#490, #190). What stands in for them is the **probe** — the play screen
driving itself through real synthesized clicks and saving a PNG after each step — and
the gate is that two runs of the same code produce byte-identical captures, and a change
produces exactly the differences it intended.

That gate lied for a while. Nine of sixteen captures differed on *every* run after one
unguarded `Time.GetTicksMsec()` read drew the active ring at a different phase of its
blink (#518), and it was found only because someone ran unmodified `main` twice before
trusting a comparison. That is the whole discipline: **baseline first, then compare, then
read the boxes.**

## 1. Baseline: two runs of the trusted tree

```bash
export DISPLAY="$(bash .claude/skills/probe-diff/scripts/find-display.sh)"
scripts/probe-diff.sh <scratchpad>/probe-A <scratchpad>/probe-B
```

Run this on unmodified `main` (a second worktree, or before you apply the change). It
runs both probe invocations twice and `diff -rq`s the directories. If it is *not*
identical, stop: the instrument is broken before your change is in the picture, and the
differing capture names tell you where (a ring, a clock, an unseeded roll). Fix or file
that first; a comparison against a noisy baseline proves nothing.

Practicalities, all measured: the reachable display **moves** — `:0` on 2026-08-27
with `:1` dead, `:1` on 2026-09-02 with `:0` dead — so `find-display.sh` probes the
sockets under `/tmp/.X11-unix` rather than assuming; `--display-driver x11` is
required; `dotnet build client/SRDCombat.Viewer.csproj` must precede the first launch
or Godot fails on `Cannot instantiate C# script`. The pair takes well under a minute
once `ClickButton` actually clicks (it did not, for a while — #499).

## 2. Candidate: one run of the change, compared

```bash
bash .claude/skills/probe-diff/scripts/probe-compare.sh <scratchpad>/probe-A <scratchpad>/probe-C
```

It runs the probe once on the current tree into `probe-C` and compares with the
baseline. For every differing PNG it prints the bounding box of the changed pixels;
for every capture present on one side only, or every `.skipped.txt` that appeared or
vanished, it says so.

## 3. Read every box

Each differing capture is one of:

- **The change you made**, where you made it. A floating number over the struck token;
  a new button in the row; a rewritten log line. The box should sit exactly there and
  the count of differing captures should match the number of steps that show it.
- **A clock or an unseeded roll.** A small box at the active token (the ring), or a
  box that moves between two runs of the *same* code. Guard it behind the probe/capture
  flag the way `_animateSprites` and `FloatingNumberMotion` already are: a verification
  image must not depend on when the frame was taken.
- **Collateral.** A camera that now frames differently, a panel that moved because a
  label grew, the log squeezed by a longer initiative list. Not wrong by definition, but
  not what the PR claimed either; say it.
- **A capture that vanished or a `.skipped.txt` that appeared.** The probe could not
  reach that step any more. That is a regression in the probe's reach until shown
  otherwise, and `play-2-refused` being a byte-copy of `play-1-turn-ready` (#499) is the
  reminder that a capture existing is not the same as a capture showing what its name
  says.

Open the PNGs. A bounding box tells you where; only the picture tells you what.

## 4. What goes in the PR

- The baseline pair: identical, and the tree it was taken on.
- The comparison: `N captures compared, M differ`, and for each of the M, the box and
  the one-line reading from step 3.
- Before/after PNGs attached for anything visual Brandon should see — he owns taste,
  and a played-run complaint outranks the number.

A PR that says "captures unchanged" carries the `0 differ` line; one that says "the
number floats above the token" carries the box over the token and nothing else.

## The capture set, for orientation

The main run (`--seed=1 --probe=<dir>`): `run-0-interlude`, `play-1-turn-ready`,
`play-1b-quit-confirm`, `play-2-refused`, `play-2b-hint`, `play-2c-tab-armed`,
`play-3-moved`, `play-4-attacked`, `play-5-feature`, `play-6-turn-ended`,
`play-7-spell-menu`, `play-8-cast`, `play-9-attack-menu`, `run-9-outcome-card`,
`run-9-after-fight`, `run-10-shop`. The one-fight run adds `play-9-spell-menu` and
`play-9-slot-menu` (the Slot menu needs a level-3 caster, which `--one-fight` provides).
Several depend on the fight — whether a caster's turn comes up, whether fight 1 clears —
and write `<name>.skipped.txt` naming why when they cannot be produced. Full detail:
`client/README.md`, "The probe".
