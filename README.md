# SRD_Combat

A turn-based tactical combat game built on the **System Reference Document 5.2.1**
(the 2024 D&D rules, CC-BY-4.0).

Take a party of four through an escalating ladder of thirty fights. Earn XP, level up,
and collect weapons, armour, spells and magic items along the way. It is a combat game:
no exploration, no dialogue, no overland travel. Everything between fights exists to
serve the next fight.

**Status: playable end to end** (as of 2026-08-15). The whole gauntlet runs in the
console client and under the mouse in the Godot client, with wounds, spent resources
and the fallen carried between fights, rests that restore what the printed rules say,
levels earned by experience, and loot. Character creation is in both clients — build
your own party of four, every option shown with its printed SRD text — or take the
pre-made one. Automated runs clear the ladder routinely (median 24 fights of 30, and
54 of 120 seeded runs clear all thirty); no human has yet played a run to its end.

The open front is **party-side depth**: 17 spells of the book's 339 have an effect the
engine executes, six of the twelve classes are offered, and most class features past
the first few levels are reported as unimplemented rather than silently approximated.
The pacing numbers say that is what decides how far a run gets. They also say the
difficulty is not yet a curve — runs mostly either end in the first four fights or
clear all thirty, with little in between.

## What it is

- **A full tactical grid** — squares, movement, reach, opportunity attacks, conditions,
  concentration, area-of-effect spells, and cover: the printed degrees judged along the
  line of fire, raising AC and Dexterity saves, with Total Cover refusing the shot
  outright rather than letting it miss.
- **Levels 1–5**, the tier the SRD supports best for pure combat.
- **A persistent party of four** advancing across a run — autosaved after every cleared
  fight, resumed with `--continue`, and defeat means reload rather than reset.
- **Faithful to SRD 5.2.1**, including 2024-era Weapon Mastery and the published
  XP-budget encounter-building tables. A rule the engine cannot execute is refused
  with a named code, never silently approximated.

## Layout

```
src/SRDCombat.Core        pure rules — no I/O
src/SRDCombat.Content     content loading and validation, straight from the Core types
src/SRDCombat.Game        the gauntlet: party persistence, encounter ladder, rewards
src/SRDCombat.Console     text client
client/                   Godot client — the same gauntlet, played with the mouse
tools/SrdExtract          SRD PDF -> JSON. Build-time only, never shipped.
data/                     generated + hand-corrected SRD content
docs/                     design documents
scripts/                  doctor.sh — checks this machine against what CI expects
```

## Build, test and play

Everything needed is committed — there is no content to generate and no asset to
fetch. On a new machine, pin the SDK first (`CLAUDE.md` has the full setup and why
each line earns its place):

```bash
mise install
./scripts/doctor.sh
```

Then:

```bash
dotnet build SRDCombat.sln -c Debug
```

```bash
dotnet test SRDCombat.sln -c Debug
```

```bash
dotnet run --project src/SRDCombat.Console
```

Both builds must produce **0 warnings** — `TreatWarningsAsErrors` is on. `--seed <n>`
makes a run reproducible, which is a complete bug report; `--create` builds your own
party of four at the keyboard, every option with its printed text; `--continue` resumes the
autosave. The Godot client has its own [README](client/README.md), including the one
build step a first launch needs.

## Documentation

[The design and development plan](docs/2026-08-11-design-and-development-plan.md) is
the governing document. Read it before starting work.

## Licensing

Game content is derived from SRD 5.2.1 under CC-BY-4.0 — see [NOTICE.md](NOTICE.md)
for the required attribution. The source code carries no licence file, so all rights
are reserved.
