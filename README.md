# SRD_Combat

A turn-based tactical combat game built on the **System Reference Document 5.2.1**
(the 2024 D&D rules, CC-BY-4.0).

Take a party of four — pre-made or built yourself — through an escalating ladder of
fights. Earn XP, level up, and collect weapons, armour, spells and magic items along
the way. It is a combat game: no exploration, no dialogue, no overland travel.
Everything between fights exists to serve the next fight.

**Status: pre-alpha.** Kickoff was 2026-08-11. Nothing is playable yet.

## What it aims to be

- **Full tactical grid** — squares, movement, reach, cover, opportunity attacks,
  conditions, concentration, area-of-effect spells.
- **Levels 1–5**, the tier the SRD supports best for pure combat.
- **A persistent party of four** that advances permanently across a run.
- **Faithful to SRD 5.2.1**, including 2024-era Weapon Mastery and the published
  XP-budget encounter-building tables.

## Layout

```
src/SRDCombat.Core        pure rules — no I/O
src/SRDCombat.Content     content packs: DTOs, loading, validation, schema
src/SRDCombat.Game        the gauntlet: party persistence, encounter ladder, rewards
src/SRDCombat.Console     text client
tools/SrdExtract          SRD PDF -> JSON. Build-time only, never shipped.
data/                     generated + hand-corrected SRD content
docs/                     design documents
```

## Build and test

```bash
dotnet build SRDCombat.sln -c Debug
```

```bash
dotnet test SRDCombat.sln -c Debug
```

Both must produce **0 warnings** — `TreatWarningsAsErrors` is on.

## Documentation

[The design and development plan](docs/2026-08-11-design-and-development-plan.md) is
the governing document. Read it before starting work.

## Licensing

Game content is derived from SRD 5.2.1 under CC-BY-4.0 — see [NOTICE.md](NOTICE.md)
for the required attribution. The source code carries no licence file, so all rights
are reserved.
