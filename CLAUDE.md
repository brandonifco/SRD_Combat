# SRD_Combat

A turn-based tactical combat game on **SRD 5.2.1** (2024 rules, CC-BY-4.0). Party of
four, levels 1–5, full tactical grid, persistent gauntlet ladder of escalating fights
with XP, levelling, and loot. Combat only — no exploration, dialogue, or travel.

**Read [`docs/2026-08-11-design-and-development-plan.md`](docs/2026-08-11-design-and-development-plan.md) first.**
It is the governing design document: the kickoff decisions, the architecture and why
it diverges from `5eGoldBox`, the content pipeline, the phase plan, and the open
questions. Everything below is operational detail that doc doesn't carry.

## Where things stand

**2026-08-11 — Phases 0 and 1 complete.**

Phase 0, the content pipeline: `data/srd/` holds **330 monsters, all 38 weapons and all
13 armor entries**, extracted from the SRD PDF by `tools/SrdExtract` and loading with
**zero validation errors**.

Phase 1, the combat engine: a headless fight runs end to end. Grid movement, initiative,
action economy, attacks, damage, death saves and opportunity attacks all work, and a
three-adventurer / four-raider skirmish resolves over eight rounds with its 156-line
narration **pinned byte-for-byte** in `tests/SRDCombat.Core.Tests/Fixtures/`.

Debug and Release both build 0 warnings; **172 tests pass**, 1 skipped by design.

**A bug worth knowing about, because its shape will recur.** The extractor read the
Goblin Warrior's "plus 2 (1d4) Slashing damage *if the attack roll had Advantage*" as a
second unconditional damage component, so every goblin hit dealt it. Nothing failed —
the attack looked implemented. `AttackDamage.Condition` now carries the qualifier and
`AttackRules.RollDamage` evaluates it. **The general lesson: an entry that is partly
structured is more dangerous than one that is not structured at all**, because the
unstructured part is invisible rather than merely absent. That is what the effect model
below exists to prevent.

**Still not playable by a person** — there is no client and no character model. Phase 2
(species/class/background resolution, levels 1–5, spell slots, equipment, four pregens)
is next, then Phase 3's console client.

Decided at kickoff and no longer open: **six launch classes** (Fighter, Rogue, Cleric,
Wizard, Barbarian, Ranger — they cover every mechanical shape the engine must handle)
and **no code licence for now** (public repo, no `LICENSE`, all rights reserved by
default — deliberate).

### Working on the combat engine

- **The frozen transcript is the most valuable test here.** It pins the exact narrated
  sequence of a whole fight, so it catches interaction bugs no unit test reaches. When
  it fails, **read the diff before touching the fixture** — a change to the transcript
  is a change to how the game plays. Regenerate only once the new behaviour is intended:
  un-skip `TranscriptWriter`, run it, re-skip it, review.
- **It uses hand-authored combatants, not SRD monsters, on purpose** — so it fails when
  the *engine* changes, not when content is re-extracted. `RealMonsterCombatTests` in
  `SRDCombat.Content.Tests` covers the other direction, including a smoke test that
  every CR 0–4 monster can take a turn without throwing.
- **All randomness goes through `IRandomSource`.** Never reach for `Random.Shared`
  anywhere in `Core`; determinism is what the transcripts rest on. `ScriptedRandomSource`
  throws when a test rolls more dice than it scripted — if that fires, the test's premise
  changed (an Advantage roll consumes two dice, not one).
- **Rules verified against the printed SRD, not memory** — and the non-obvious ones are
  pinned by tests: Advantage and Disadvantage cancel rather than stack; a Critical Hit
  doubles the *dice* and adds the modifier once; a monster dies at 0 hit points while a
  character rolls Death Saves; Dodge lasts until the start of the dodger's *next* turn;
  and attacking an Unconscious creature from beyond 5 feet is a *normal* roll, because
  Unconscious grants Advantage while the Prone it carries imposes Disadvantage.

Three things a Phase 2 author should know before starting:

- **There is no versioned DTO mirror, deliberately.** Content serializes straight from
  the `Core` definitions. The design doc explains why this diverges from 5eGoldBox, and
  what guards replace the mirror. Don't "restore" it without reading that section.
- **Monster prose is not yet mechanics.** Attacks are structured; saving-throw effects,
  recharge abilities and riders ("the target has the Prone condition") are still text
  on `MonsterEntry.Text`. Turning those into executable effects is an open scoping
  question, flagged in the design doc.
- **`ChallengeRatingRules` already exists** in `Core.Rules` with the full XP and
  proficiency-bonus tables, and the SRD's per-character encounter XP budget is on
  printed page 202 — the encounter builder implements a published table, not a guess.

## Related projects on this machine — context, not dependencies

- **`~/5eGoldBox`** — a mature layered C#/.NET 8 5e engine with Godot and console
  clients, SRD 5.1-era content, ~2,437 tests. **This project shares no code with it**
  (decided at kickoff), but its `CLAUDE.md` is a long, honest record of what went
  wrong building almost exactly this kind of engine. Worth reading before designing
  anything similar. The conventions and hard-won lessons carried over here are
  already captured in the design doc.
- **`~/5eData`** — a C# data library over 2014-SRD JSON. Different edition; not a
  content source for this project.

## Environment

- **.NET is snap-confined.** Use `DOTNET_ROLL_FORWARD=LatestMajor /snap/bin/dotnet`
  rather than bare `dotnet` if the bare command misbehaves. SDKs present: 8.0.129 and
  10.0.110; `global.json` pins 8 with `latestMajor` roll-forward, which in practice
  means **SDK 10 is what actually runs locally** while CI installs 8.0.x. Both build
  the `net8.0` targets fine, but the version gap is real — see the next point.
- **`dotnet new sln` under SDK 10 produces a `.slnx`, which .NET 8 cannot read.** Hit
  during setup: the solution has to be `SRDCombat.sln` in the classic format, or CI
  (pinned to 8.0.x) fails to find a project file at all. `dotnet new sln --format sln`
  forces it. The same version gap means **templates default to `net10.0`** and write
  `TargetFramework`/`Nullable`/`ImplicitUsings` into each new `.csproj`, silently
  overriding `Directory.Build.props` — strip those three lines from any project
  created by a template.
- **Godot 4.7 stable mono** at `~/.local/bin/godot`. Not used until Phase 7.
- **`pdftotext`** (poppler) is installed and is the extraction workhorse.
- **A real X11 display exists** (`DISPLAY=:1`, Xorg — not headless), but no
  `xdotool`. GoldBox's own notes describe driving a GUI via `python-xlib` + `XTest`
  in a throwaway venv, including the trap that window activation must go through the
  EWMH `_NET_ACTIVE_WINDOW` client message or a synthetic click can land in a
  different application entirely. Relevant from Phase 7.

## The SRD source and the extraction pipeline

The source PDF is `~/Downloads/SRD_CC_v5.2.1.pdf` (364 pages). It is **not** in this
repo and must not be committed.

`reference/` holds local text extractions and is **gitignored**. Regenerate with:

```bash
pdftotext /home/brandon/Downloads/SRD_CC_v5.2.1.pdf reference/SRD_raw.txt
```

**The pages are two-column, and whole-page extraction interleaves adjacent stat
blocks into nonsense.** Crop each column separately — the page is 594pt wide:

```bash
pdftotext -f 262 -l 262 -x 0 -y 0 -W 297 -H 783 /home/brandon/Downloads/SRD_CC_v5.2.1.pdf -
```

That command is for eyeballing a page. **The real pipeline does not use `pdftotext` at
all** — `tools/SrdExtract` reads the PDF with PdfPig, which gives per-word coordinates
and font names. Regenerate the content with:

```bash
dotnet run --project tools/SrdExtract -- --out data/srd
```

It refuses to write when validation reports errors (`--force` overrides). A clean run
reports 330 monsters, 38 weapons, 13 armor, 0 errors, and exactly one warning — the
Archmage's XP, which is a real SRD inconsistency and is expected.

**Why fonts matter more than text here.** The SRD's typography is a reliable parsing
signal, and the parser is built on it (`StatBlockFonts`): `GillSans-SemiBold` at ~10.2pt
is a monster name while the *same font* at ~12.3pt is the A–Z group heading above it;
`Optima-Bold` is a stat line; `GillSans` at ~8.3pt is a section header while the same
font at ~4.2pt is the `MOD SAVE` column label; `Optima-BoldItalic` opens an entry, and
that font boundary is the only thing separating an entry's name from its prose on the
same visual line. Match these names **exactly** — `GillSans`, `GillSans-SemiBold` and
`GillSans-SemiBold-SC700` are three different signals and a substring test conflates
them.

**Source-format variances already handled** — check here before assuming a new one is a
parser bug: distances appear as both `5 ft.` and `5 feet`; four blocks print
`CR 3 (700 XP; PB +2)` with the fields flipped; some damage is flat (`Hit: 1 Piercing
damage`, no dice); 19 attacks are `Melee or Ranged Attack Roll` (the regex alternation
must list that **first**, or it matches `Melee` and then fails on the `or`); and the
ability table renders as three side-by-side MOD/SAVE pairs with names split oddly
(`De x 12 +1 +1`), so triples are matched positionally rather than by name.

Useful page ranges (printed page numbers, which match the PDF's own indices):
classes 28–82, character origins 83–86, feats 87–88, equipment 89–103, spells
104–175, rules glossary 176–191, gameplay toolbox 192–203 (**combat encounter XP
budgets are on 202**), magic items 204–253, monsters 254–343, animals 344+.

## Build and test

```bash
dotnet build SRDCombat.sln -c Debug
```

```bash
dotnet test SRDCombat.sln -c Debug
```

**0 warnings expected** in both Debug and Release — `TreatWarningsAsErrors` is on in
`Directory.Build.props`.

## Standing conventions

- **`git add` specific paths, never `-A` or `.`**
- One narrowly-scoped branch per concern; branch → PR → wait for CI → merge.
- Gate before merge: focused tests → full suite → Debug **and** Release build, both
  0 warnings → `git diff --check` clean.
- **Content changes land in both layers in one commit.** Adding a field to a `Core`
  definition does nothing until the versioned `Content` DTO mirrors it — unmapped
  JSON properties are dropped silently, not rejected. Check the regenerated schema
  actually contains the new field before moving on. This bit GoldBox twice.
- **Frozen transcript tests for combat**: a scripted fight's exact narrated step
  sequence, diffed byte-for-byte. These require the RNG to be seeded and injectable,
  which is why `Core` owns its randomness behind an abstraction.
- When a design decision here or in the plan doc turns out to be wrong, **correct the
  doc in the same commit as the code**, not as a follow-up pass.

## Attribution obligation

SRD 5.2.1 is CC-BY-4.0, so derived content **can** be shipped — but the attribution
in [`NOTICE.md`](NOTICE.md) is required and must stay accurate. Per the SRD's terms,
do not add any other attribution to Wizards of the Coast beyond that statement.
