# SRD_Combat — design and development plan

**Written 2026-08-11, at project kickoff.** This is the governing design document.
Read it before starting work; update it when a decision here turns out to be wrong.

## What this is

A turn-based tactical combat game built on the **System Reference Document 5.2.1**
(the 2024 rules, CC-BY-4.0). You take a party of four — pre-made or built yourself —
through an escalating ladder of fights, earning XP, levelling up, and collecting
weapons, armour, spells and magic items along the way.

It is a **combat game**, not a CRPG. There is no exploration, no overland travel, no
dialogue, no quest state. Everything between fights exists to serve the next fight.

## Decisions taken at kickoff

These were decided with the user on 2026-08-11 and are not open questions.

| Decision | Choice | Why |
| --- | --- | --- |
| Relationship to `5eGoldBox` | **Fresh standalone repo, no shared code** | GoldBox is SRD 5.1-era and shaped around scenarios/exploration. Borrow its conventions (layered architecture, JSON content packs, zero-warning gate), not its types. |
| Stack | **C#/.NET 8, console client first, Godot later** | Matches the existing toolchain. GoldBox's own history is unambiguous that UI built without a live client needs a correction round every time — so the engine gets proven through text first. |
| Rules depth | **Full tactical grid** | Squares, movement, reach, ranged attacks, cover, opportunity attacks, conditions, concentration, area-of-effect templates. |
| Progression | **Persistent gauntlet ladder** | One saved party advances permanently. Defeat means reload, not reset. Encounters are authored as data so a roguelite run mode could be layered on later. |
| Party size | **4** | What makes 5e tactical combat work: action economy, roles, positioning. |
| Level range | **1–5** | Tier 1. Covers Extra Attack, 3rd-level spells, a subclass for every class, and a real difficulty curve — without high-tier spells that are non-combat or engine-breaking. Tables are authored to extend. |

### Decided the same day, after the table above

| Decision | Choice | Why |
| --- | --- | --- |
| Launch class roster | **Six: Fighter, Rogue, Cleric, Wizard, Barbarian, Ranger** | Covers every mechanical *shape* the engine must handle — weapon mastery, sneak attack, prepared divine casting, prepared arcane casting, rage, half-casting. The other six SRD classes then become content rather than engineering. |
| Code licence | **None for now** | The repository is public with no `LICENSE`, so all rights are reserved by default. Deliberate, not an oversight. The SRD's CC-BY-4.0 obligation is separate and satisfied by `NOTICE.md`. |

## Why SRD 5.2.1 is a better rulebase than 5.1

Worth stating, because it changes how much of this is authoring versus parsing:

- **Monster stat blocks are machine-readable.** The 2024 format states attacks as a
  fixed grammar — `Melee Attack Roll: +6, reach 5 ft. Hit: 10 (2d6 + 3) Piercing damage.`
  — with `AC`, `HP 52 (8d8 + 16)`, `Speed`, `Initiative −1 (9)`, and
  `CR 2 (XP 450; PB +2)` on predictable lines. Verified against real pages during
  kickoff, not assumed. 5.1's prose stat blocks were far worse.
- **Conditions are a closed, well-specified set** with mechanical text, and Exhaustion
  is a single numeric track rather than a table of six distinct effects.
- **Encounter building is a published XP budget**, not a hand-wave. The
  Gameplay Toolbox gives budget *per character* by party level and difficulty
  (levels 1–5: Low 50/100/150/250/500, Moderate 75/150/225/375/750,
  High 100/200/400/500/1,100). This is the difficulty ladder — it does not need
  inventing, only implementing.
- **Weapon Mastery** (Vex, Topple, Graze, Nick, Sap, Slow, Cleave, Push) gives
  martial classes real per-attack tactical decisions. In a combat-only game this is
  disproportionately valuable — it is most of what stops a Fighter's turn being
  "attack, attack, end."
- **It is CC-BY-4.0.** Derived content can be committed and shipped with attribution.
  GoldBox had to write original prose for anything the SRD was thin on; here the
  constraint is only attribution, which `NOTICE.md` carries.

The SRD contains 12 classes (one subclass each), 9 species, 4 backgrounds, feats,
the full weapon/armour/gear tables, spells, magic items, and roughly 500 stat blocks
including a separate animals section.

## Architecture

Four assemblies plus a client, following GoldBox's layering because it demonstrably
held up there — with one deliberate divergence.

```
src/SRDCombat.Core        pure rules. No I/O, no JSON, no randomness it doesn't own.
src/SRDCombat.Content     content packs: versioned DTOs, loading, validation, schema.
src/SRDCombat.Game        the gauntlet: party persistence, encounter ladder, rewards.
src/SRDCombat.Console     text client. The engine is playable here first.
src/SRDCombat.Godot       later, its own .sln (as in GoldBox).
tools/SrdExtract          SRD PDF -> JSON content. Build-time only, not shipped.
```

**The divergence: `Content` is its own assembly**, where GoldBox folded content
loading into `Application`. The reason is the extraction pipeline — content here is
*generated from a source document* and re-generated whenever the extractor improves,
so the DTO layer, the schema, and the validators are a real subsystem with their own
test surface rather than a corner of the orchestration layer.

**Correction, made while building Phase 0: there is no versioned DTO mirror, and that
is deliberate.** This document originally specified one, copying GoldBox. Building the
extractor showed the reasoning does not carry over.

A DTO mirror exists to keep an on-disk format stable while runtime types churn — worth
its cost when content is hand-authored and must survive refactors. SRD content here is
*generated*: change a definition, re-run the extractor, every file is rewritten. The
mirror would protect nothing, while importing the exact failure GoldBox hit twice — an
unmapped property is dropped **silently** rather than rejected, so a field added to the
runtime type simply never arrives.

Content is therefore serialized straight from the `Core` definitions, with two guards
that are louder than the mirror was:

- `UnmappedMemberHandling.Disallow` — an unknown property in a content file is an
  error, not something skipped. This is strictly stricter than the mirror's default.
- `ContentSerializerTests` pins the serialized shape, so a change to the on-disk format
  fails a test naming the field that moved.

Hand-authored game content — encounter ladders, pregenerated characters, loot tables —
is a separate question and may well earn a DTO layer when it arrives.

### What lives in Core

- **Dice and randomness** behind a seeded abstraction, so every fight is reproducible
  and every test is deterministic. Frozen-transcript tests were the single most
  valuable thing in GoldBox's suite; they only work if the RNG is injectable.
- **D20 tests** — ability checks, saving throws, attack rolls, advantage/disadvantage
  as a tri-state that composes correctly (any advantage + any disadvantage = neither).
- **The combatant model** — ability scores, proficiency, AC, HP, temp HP, hit dice,
  speeds, senses, damage resistance/vulnerability/immunity, condition immunities.
- **Conditions** as a closed enum with real mechanical effects, plus Exhaustion levels.
- **The grid** — square grid, 5 ft squares, occupancy by creature size, difficult
  terrain, cover determination, line of sight, reach and range with the
  normal/long disadvantage band.
- **The round** — initiative order, turn structure, the action economy (Action,
  Bonus Action, Reaction, Movement) as tracked per-turn resources.
- **Resolution** — attacks, damage rolls, criticals, saving throws, death saves,
  concentration (including the DC 10-or-half-damage check), opportunity attacks.
- **Advancement** — XP thresholds, proficiency bonus by level, hit points per level,
  spell slots by class level, ASI/feat at 4, subclass at 3.

### What lives in Game

Everything that persists between fights: the party's roster and inventory, the
gauntlet's position on the ladder, XP total, loot awarded, the save file. Also the
**encounter builder** — given a party level and a target difficulty, select monsters
against the SRD's XP budget.

**Rests are a real design problem in a combat-only game** and are decided here rather
than deferred: a **short rest after every fight**, a **long rest at camp milestones**
every third fight. Without this, spell slots and hit dice make the ladder unplayable
by the fourth encounter; with unlimited rests, resource management — most of what
makes 5e classes differ — stops mattering. The interval is content, not code, so it
can be tuned.

**XP is awarded per-character, not party-wide.** GoldBox pooled it party-wide as a
deliberate simplification mirroring its shared purse. That reasoning does not carry
here: this game's whole loop is character advancement, and per-character XP is what
the SRD's own tables are written against.

## The content pipeline

`tools/SrdExtract` turns the SRD PDF into `data/` JSON. It is a build-time tool that
never ships, and its output is committed so the game does not depend on the PDF.

The technique is already proven — validated during kickoff, not planned on paper:
the pages are two-column, so extracting the whole page interleaves adjacent stat
blocks into nonsense. Cropping each column separately (`pdftotext -x 0 -W 297` and
`-x 297 -W 297` at 594pt page width) yields clean, correctly-ordered text. The
ability-score table needs finer sub-column cropping still, since it renders as three
side-by-side pairs.

Extraction order, easiest and most load-bearing first:

1. **Monsters** — most regular, and a combat game is mostly monsters.
2. **Weapons, armour, gear** — plain tables, plus the Mastery property column.
3. **Spells** — regular headers (`Level N School`, casting time, range, components,
   duration) over prose bodies.
4. **Species, classes, subclasses, feats** — heavily prose and table mixed. Expect
   these to be **extractor-assisted but hand-finished**; do not plan for clean
   automation here.

Every generated file is validated on load by the same validator the game loads
through, and the extractor's output is diffed rather than blindly overwritten, so an
extractor improvement shows exactly what it changed.

## Development phases

Each phase ends somewhere real: something playable, or something provable.

**Phase 0 — Repo and pipeline. Complete, 2026-08-11.** Solution skeleton, CI, the
extractor, monsters and equipment extracted and validated. `data/srd/` holds **330
monsters, all 38 weapons and all 13 armor entries**, loading clean with zero validation
errors. What the extraction actually cost, recorded because the next section will pay
the same kind of tax:

- **Five source-format variances, none of them guessable in advance**, all found by
  running the extractor rather than by reading the PDF: distances written both
  `5 ft.` and `5 feet`; `CR 3 (700 XP; PB +2)` with the fields flipped in 4 of 331
  blocks; flat damage printed as `Hit: 1 Piercing damage` with no dice; `Melee or
  Ranged Attack Roll` on 19 attacks; and page 364 holding content past where the
  Animals section appeared to end.
- **One genuine SRD inconsistency, preserved rather than corrected:** the Archmage
  prints `CR 12 (XP 8,000)` where the SRD's own CR table says 8,400. The printed value
  ships and the validator reports it as a warning. Silently overriding the source would
  be worse than carrying the discrepancy.
- **One extraction artifact, corrected through an auditable list:** the Young White
  Dragon's Intelligence save loses its minus glyph in the PDF's text layer.
  `KnownCorrections` repairs it, states why the correct value is certain from the rules,
  and *reports rather than applies* a correction whose expected value no longer matches
  — so a parser improvement that makes a correction unnecessary surfaces loudly.

**Phase 1 — The combat engine. Complete, 2026-08-11.** Core, headless. Grid,
initiative, action economy, attacks, damage, conditions, death saves, opportunity
attacks. A three-adventurer / four-raider skirmish now fights to a conclusion over
eight rounds with no client, and its 156-line narration is pinned byte-for-byte.

What the engine does, all of it verified against the printed SRD rather than from
memory:

- **The grid** as the SRD defines it — 5-foot squares where a diagonal step costs the
  same as an orthogonal one, so distance is Chebyshev, not Euclidean and not 1.5×.
  Difficult terrain, blocked squares, pathing around them, and the rule that you may
  cross an ally's square but never finish your move on one.
- **Attack rolls** with Advantage and Disadvantage that *cancel* rather than stack, a
  natural 20 that hits and crits regardless of AC, a natural 1 that always misses, and
  Critical Hits that double the damage **dice** while adding the modifier once.
- **Damage** with resistance halving and rounding down, vulnerability, immunity,
  temporary hit points that absorb first and never stack, and massive damage.
- **Dying properly**: a monster dies the instant it hits 0, a character falls
  Unconscious and rolls Death Saving Throws — three successes to stabilise, three
  failures to die, a natural 1 costing two failures, a natural 20 restoring a hit
  point, and damage taken at 0 costing a failure (two from a crit).
- **Opportunity Attacks** that fire only when reach is genuinely left, resolve *before*
  the mover vacates the square, cost the attacker its Reaction, and are avoided by
  Disengage.

Two rules interactions worth recording, because both are easy to implement wrongly and
both are now pinned by tests:

- **Attacking an Unconscious creature from more than 5 feet away is a normal roll.**
  Unconscious grants Advantage, but Unconscious also imposes Prone, and Prone gives an
  attacker further than 5 feet Disadvantage. They cancel exactly.
- **Dodge lasts until the start of the dodger's *next* turn**, not until the end of
  their current one, so it has to survive every intervening combatant's turn.

**Deliberately not built, and none of it is an oversight:** spells and concentration,
cover, areas of effect, Multiattack (the SRD states it as prose, so it is part of the
open question below), weapon mastery effects, the Help/Hide/Ready actions, conditions
beyond the set the engine actually applies, and the size rule that lets a creature move
through a much larger or smaller hostile's space.

**One divergence from the SRD, taken knowingly:** initiative ties are broken by
initiative bonus and then by combatant id, rather than by a reroll or player choice.
The order has to be reproducible from the seed alone or the frozen transcripts mean
nothing.

**Phase 2 — Characters.** Species/class/background resolution, levels 1–5, spell
slots, prepared spells, equipment and attunement. Four pre-made characters authored.
*Ends when:* a real party resolves to correct sheets at every level 1–5.

**Phase 3 — First playable fight.** Console client: initiative display, a grid you
can read, move/attack/cast/end-turn, a scrolling combat log that narrates every roll.
*Ends when:* you can sit down and win or lose a fight.

GoldBox's combat log arrived late and was immediately called "messy." Here it is a
Phase 3 requirement, not a polish item: **every roll, save, and damage number
visible, in a log that appends rather than replaces.**

**Phase 4 — The gauntlet.** Encounter ladder as data, the XP-budget encounter
builder, rewards, level-up, loot, save/load, rests. *Ends when:* a run from level 1
to level 5 is playable start to finish.

**Phase 5 — Character creation.** Build your own party.

GoldBox's creation wizard was rejected on first playtest for a specific, avoidable
reason: **every option was a bare name in a list**, so anyone who doesn't already
know 5e was guessing. Here the SRD's own descriptive text is CC-BY and can be shipped
verbatim, so **every choice carries its description from the start**, and nothing
auto-advances on selection — browsing an option and committing to it are different
actions.

**Phase 6 — Monster tactics.** Enemies that use their action economy, focus fire,
position, and use their own abilities. This is what makes fights feel authored rather
than random, and it is deliberately after a playable ladder rather than before it.

**Phase 7 — Godot client.**

## Working conventions

Carried over from GoldBox because they earned it there:

- `dotnet build -c Debug` and `-c Release`, **0 warnings** (`TreatWarningsAsErrors`).
- Full test suite green before any merge. `git diff --check` clean.
- **`git add` specific paths, never `-A` or `.`**
- One narrowly-scoped branch per concern; branch → PR → CI → merge.
- **Frozen transcript tests for combat.** A scripted fight's exact narrated step
  sequence, diffed byte-for-byte. In GoldBox these repeatedly proved a refactor was
  behaviour-preserving when nothing else could.
- When a decision in this document turns out to be wrong, **correct it here in the
  same commit as the code** — not as a follow-up pass that never happens.

## Open questions

1. **Is there an economy?** Gold, and a shop between fights, or is loot the only
   source of equipment? Affects Phase 4, not before.
2. **How much of a monster's prose becomes mechanics?** Phase 0 extracts attacks
   structurally and keeps everything else — saving-throw effects, recharge abilities,
   riders like "the target has the Prone condition" — as text. Turning those into
   executable effects is a real, separate scoping question, and the answer probably
   differs between "every CR 0–4 creature the ladder actually uses" and "all 330".
   Needs an answer during Phase 1, not before.
3. **Which monsters does the ladder draw from?** 330 is far more than a tier-1 game
   needs. Curating a subset the encounter builder is allowed to pick from is likely
   better than exposing all of them; that is a Phase 4 decision.
