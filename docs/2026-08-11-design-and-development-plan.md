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
| Code licence | **MIT** (was: none, reversed 2026-08-16) | Originally "none for now" — public repo, no `LICENSE`, all rights reserved by default, deliberate rather than an oversight. Reversed because that default quietly destroyed the project's most reusable asset: an SRD 5.2.1 (2024 rules) engine in C# needs no Wizards licence to exist, which is exactly what makes it worth building on, and all-rights-reserved made it legally unusable by anyone who wanted to. `src/SRDCombat.Core` has no project references, so it is genuinely liftable. The licence is scoped to code; `data/` stays CC-BY-4.0 under `NOTICE.md`, which the SRD's obligation requires and which MIT would have obscured. |

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
than deferred: a **short rest between routine fights**, with **long rests bracketing
each High milestone** — the fifth fight of every cycle, entered fresh and recovered
from (the cadence was every third fight until #65 measured that shape ending nearly
every run). Without rests, spell slots and hit dice make the ladder unplayable by the
fourth encounter; with unlimited rests, resource management — most of what makes 5e
classes differ — stops mattering. The interval is content, not code, so it can be
tuned.

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

**Phase 2 — Characters. In progress, sliced into small PRs.** Species/class/background
resolution, levels 1–5, spell slots, prepared spells, equipment. Four pre-made
characters authored. *Ends when:* a real party resolves to correct sheets at every
level 1–5.

- **Slice 1 — Character Origins. Done.** All 9 species and 4 backgrounds extracted and
  validated. Both are found structurally — a species is a heading followed by a
  `Creature Type:` line, a background one followed by `Ability Scores:` — rather than
  matched against a list of expected names, so content the SRD adds is picked up rather
  than silently missed. Species traits go through the same classification as stat block
  entries, and **0 of 33 are fully modelled**: every one is real mechanics (Darkvision,
  Poison resistance, Advantage grants) that the effect model has no vocabulary for yet.
  Counted, not hidden.
- **Slice 2 — Classes. Done.** All **12** SRD classes extracted: Core Traits, the full
  1–20 level table, and every class feature's prose. The extractor deliberately reads
  all twelve rather than only the six the game launches with — it has no reason to know
  which the game uses, and filtering there would mean editing the extractor whenever
  that decision changes. **0 of 232 class features are fully modelled**, the same
  honest count as species traits.

  The check that makes this trustworthy is the **proficiency bonus**: the Character
  Advancement table fixes it by level independently of any class, so a Features table
  that disagrees was misread. It plays the role hit-points-versus-hit-dice plays for
  monsters. New `AdvancementRules` carries that table plus the XP thresholds, which the
  gauntlet needs anyway.

  Four parsing lessons, all found by checking output against the book rather than by the
  parser complaining:

  1. **A class page mixes two layouts.** Core Traits and feature prose are in the two
     text columns; the Features table spans the full width at the bottom. Each page is
     read twice, because either mode alone gets one of them wrong.
  2. **`Weapon Proficiencies` is wide enough to overflow its column**, so its value
     begins after an ordinary word gap rather than a wide one. A gap heuristic missed
     the split and swallowed the whole row into the skill list above it. Matching
     against the closed set of known keys has no such failure mode.
  3. **Header columns are told apart by gap size, and the margin is narrow.** Words
     within a column sit 2–5pt apart, separate columns 12pt or more. A 20pt threshold
     merged the Cleric's `Level` and `Bonus` (13pt); the Rogue's `Sneak Attack` is
     printed side by side with no stacked row, so "only Class Features merges" was too
     narrow too.
  4. **The Warlock is not an ordinary caster.** Its table has `Spell Slots` and
     `Slot Level` columns rather than nine per-level ones. It correctly reports no spell
     slots and keeps Pact Magic's real columns, rather than being forced into a shape
     the SRD does not use.
- **Slice 3 — Resolution and class feature mechanics. Done.** `CharacterDraft` →
  `CharacterResolver` → `CharacterSheet`: ability scores with the background's
  increases, hit points, AC, saves, all 18 skills, weapon attacks, spell slots, and
  features — at every level 1–5. Characters become combatants and fight.

  **Nine class features are now implemented**, not merely extracted: Extra Attack, Rage
  (bonus action, physical resistance, bonus damage, and the sustain-or-end rule),
  Unarmored Defense, Reckless Attack, Sneak Attack (once per turn, with the
  Advantage-or-adjacent-ally condition), Cunning Action, Uncanny Dodge, Second Wind and
  Action Surge.

  Two design points worth keeping:

  - **Everything on a sheet is derived.** Nothing is stored independently of the rules
    that produce it, so a character's AC and their armour cannot drift apart. Where the
    SRD offers a choice the engine cannot make — how the background's increases were
    spent, which skills were taken — the draft supplies it and nothing else.
  - **The gap is on the object.** `CharacterSheet.UnimplementedFeatures` lists the
    printed features this engine does not do, so a level 5 Cleric's sheet says outright
    that Spellcasting, Divine Order, Channel Divinity and Sear Undead have no effect.
    Same rule as the content model: never an absence nobody can see.

  **The largest remaining gap is spellcasting**, and it is a content gap before it is an
  engine one — spells are not extracted yet. A Cleric or Wizard currently resolves to a
  correct sheet with correct slots and no way to spend them.

- **Slice 4 — Spells extracted. Done.** All **300** SRD spells with level, school,
  class lists, casting time, range, components, duration, concentration, ritual flag and
  description. 121 saving-throw effects and 21 attack spells are structured, with areas
  and damage; **140 of 300 are fully modelled**.

  Two things worth recording:

  - **Spells need their own effect grammar.** Reusing the stat block classifier read
    every metadata field correctly and detected **zero of 300 saving throws** — a monster
    prints an explicit DC and a precomputed average, a spell prints neither. The failure
    was silent and only visible because the extraction report counts what it modelled.
  - **Six spells are truncated in the source.** Barkskin, Contagion, Divine Smite, Find
    Steed, Guidance and Resistance each sit at the foot of a column with their Components
    and Duration lines simply absent from the PDF's text layer — confirmed with two
    independent extractors. Reported as warnings rather than invented, the same call made
    for the Archmage's XP.

  Upcasting is kept as text rather than structured, because it is not implemented and
  structuring it would imply otherwise.

- **Slice 5 — Casting. Done.** Attack spells roll a spell attack against AC; save
  spells make every creature in the area roll against the caster's DC and halve on a
  success. Slots are spent and cantrips are free, Concentration is tracked and broken by
  damage (DC 10 or half the damage), and a spell whose effect the model cannot express is
  **refused with a named reason** rather than silently doing nothing.

  **Area geometry is a stated interpretation.** The SRD describes areas for a table with
  a ruler, and its grid rules do not say how to square them. `AreaTargeting` writes down
  the reading used for each shape — a Cone is squares within 45° of the direction cast, a
  Line is length-along by width-across — so the choice is auditable rather than implicit.
  Cylinder needs a height this engine has no concept of and is refused.

- **Multiattack executed (issue #7). Done.** 63 tier-1 monsters now make the attacks
  their stat block says they do, instead of one. Modelled as the Attack action buying
  several swings, exactly as Extra Attack already was, so the two share one code path.
  A Multiattack naming an attack the creature has no way to make is dropped rather than
  granting phantom swings.

  Fixing this also fixed the parser twice over: "makes one Beard attack **and one
  Infernal Glaive attack**" had been read as a single attack because the second clause
  has no verb of its own, and "two **Javelin or Morningstar** attacks" had kept the
  choice inside one weapon name. Usable tier-1 Multiattacks went 56 → 63.

  **Coverage fell 342 → 336 while correctness rose**, because six entries previously
  recorded as one-attack Multiattacks — which is not a Multiattack — are now honestly
  reported as not understood. Worth remembering that the metric can move the wrong way
  for the right reason.

- **Slice 6 — Pregens**, and the four pre-made characters the game ships with.

**Everything still outstanding is tracked as GitHub issues** rather than in this
document. The open-questions list below is for genuine product decisions only.

**A parsing trap this phase found**, recorded because it will recur in every remaining
chapter: the player-facing chapters are set in **Cambria** while the bestiary uses
**Optima**. Matching a whole font name works within one chapter and fails *silently*
across chapters. The first origins run produced nine species with zero traits between
them, and nothing would have reported it had a validator not required every species to
have at least one trait. Match the style suffix, and keep writing validators that
assert the shape of what should have been found.

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

**Phase 7 — Godot client.** *Ends when:* a fight can be played with a mouse.

**~~Keep the Godot project out of `SRDCombat.sln`.~~ Reversed 2026-08-15 — the Godot
project belongs *in* the solution.** The original reasoning was that CI runs bare
`dotnet restore`, `build` and `test` from the repository root, so a client outside the
solution could never break the gate protecting the engine on a runner with .NET 8 and no
Godot. **The premise contradicted the trial recorded two paragraphs below**: the Godot
SDK is a NuGet package, so the build needs no Godot installed at all. Verified on a cold
build — `GodotSharp.dll` resolves from `~/.nuget/packages/`, not from the Godot on
`PATH`, and the client compiles on net8.0 with 0 warnings. The exclusion bought nothing
and cost the gate 5,065 lines of the only graphical client, which no test covers either.
**The lesson is this file's own: when two documents disagree about something buildable,
build it rather than re-reading them.**

**Started 2026-08-12 with a read-only slice.** `client/` watches a seeded fight: the
engine resolves it once, forwards, and the viewer scrubs through per-turn snapshots —
grid, tokens, initiative, the appending log — with a `--capture` flag that renders one
frame to a PNG so a change to the screen can be checked without a person watching it. It
touched no engine code, as the trial predicted.

**The mouse landed the same day.** Playing is now the client's default screen: the
party's turns wait for a click — a square walks, an enemy is attacked with the
hardest-hitting attack that reaches (the console's own default, shared as
`AttackChoice` in `Game` so the clients cannot drift), buttons carry the untargeted
actions — while the policy plays the monsters one turn per beat, and every refusal is
shown with its code. The highlight is `MovementRules.FindPath` asked once per square,
advice rather than rule: the click is sent to the engine either way. A `--probe` flag
drives one commanded turn through the real input path with synthesized clicks —
including a refusal on purpose — and captures each step, which is this screen's version
of the capture loop; its first run caught a real engine gap (#96, ranged attacks in
melee lacked their printed Disadvantage — fixed the same day, and the first rule a
client found that forty automated runs never surfaced, because the policy rarely shoots
point-blank and nothing was checking the roll's mode when it did).

**Spells, features and potions followed in the next slice**, so a whole fight is now
playable: a second button row carries what the character brought (filtered by granted
features — display, while whether an action may happen now stays the engine's answer, so
absent is honest where inert would not be), Cast opens the spell list and arms the next
click as the target, Drink and Give Potion spend the weakest potion carried (the
console's own default), and a line under the buttons reads out slots, feature uses and
potions straight off the engine's state.

**The run followed, and the phase's end state — a fight played with a mouse — is met.**
The gauntlet is the client's default exactly as it is the console's: `GauntletRun` owns
rests, experience, levelling, loot and the save, and the screen only shows what the run
reports in interludes between fights, with a Continue button to march on. Autosave after
every cleared fight, `--continue` to resume, defeat leaving the save untouched — all the
console's semantics, because they are the same `Game` types. The probe now plays fight 1
out through the synthesized-click path and captures whichever side of the fight comes:
seed 1 clears it (interlude, save written), the fixed default seed loses it (defeat
screen, save untouched). What remains is polish rather than the phase: an area spell
cannot yet be aimed at a bare square (Spirit Guardians centred on the caster included),
the attack a click means cannot be overridden by name, and there is no slot choice when
casting. And still no human has played a run to its end — the client now removes the
last excuse.

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

## The effect model — prose *is* mechanics

**Added 2026-08-11, replacing an open question that turned out not to be one.** This
document previously asked "how much of a monster's prose becomes mechanics", as though
fidelity were a dial. It isn't. A stat block's action entries contain no flavour text:
`it has the Grappled condition (escape DC 13)` is a rule, and calling it prose only
describes the format it is printed in.

**The failure this prevents, which had already happened.** The Goblin Warrior's scimitar
deals "plus 2 (1d4) Slashing damage *if the attack roll had Advantage*". The extractor
captured the dice and dropped the qualifier, so every goblin hit dealt it. Nothing
failed, because the attack *looked* implemented. **A partly-structured entry is more
dangerous than an unstructured one** — the missing part is invisible rather than merely
absent.

So the rule is not "implement more". It is **nothing may hold unimplemented rules
silently**:

- Every entry is classified — `Attack`, `SavingThrow`, `Multiattack`, `Reaction`,
  `Narrative`, or `Unmodelled`. There is no "just prose" state to fall into.
- Any clause the model cannot express is recorded on `MonsterEntry.UnmodelledClauses`
  and counted, including on entries that are otherwise structured.
- `Narrative` — "this genuinely does nothing in a fight" — is only ever set from a
  **curated list**, never inferred. It currently holds three names.

**A heuristic was tried for that last point and had to be removed, which is the lesson
worth keeping.** An earlier version screened sentences through a "does this look
mechanical?" keyword test. The data showed exactly why that fails: Flyby ("doesn't
provoke Opportunity Attacks"), Nimble Escape ("takes the Disengage or Hide action") and
Shape-Shift all came through as apparently inert. A keyword list will always have false
negatives, and here a false negative silently loses a rule. Unfiltered reporting gives a
worse-looking number and a true one.

**Where coverage actually stands** — printed by the extractor on every run, and floored
by a test so it cannot regress:

| | CR 0–4 (the band the gauntlet uses) |
| --- | --- |
| entries fully modelled | **332 / 611 (54%)** |
| attacks, of which 50 carry clauses the model cannot express | 264 |
| unmodelled entirely | 182 |
| Multiattack | 64 |
| saving-throw effects, 42 with clauses beyond the model | 62 |
| reactions, 5 with clauses beyond the model | 12 |
| confirmed inert | 27 |

**That number went down when condition riders were gated, and the drop is the point.**
It was 342. Nine entries were gained — the size-gated Prone riders the engine now
imposes — and twenty-three were lost, every one of them an entry that had been claiming
to be fully modelled while carrying a condition nothing would ever apply. Thirteen were
attacks whose entire entry is one sentence containing `Attack Roll:`, so the accounting
matched on that and the `and the target has the Poisoned condition until the start of
its next turn` hanging off the end was invisible. That is the goblin bug's exact shape,
found a third time, in data that had already been checked twice.

**Two separate questions decide whether a rider lands, and keeping them apart is the
design.** The model asks whether it expresses everything printed with the condition: two
qualifiers are modelled — a size gate and a turn-boundary duration — and a charge
requirement, a pull, a chained second condition or a duration of any other shape puts the
whole clause in `AppliedCondition.UnmodelledRequirement`, which makes the rider unusable
rather than approximate. The engine then asks whether it executes the condition at all,
from `ConditionRules.Executable`, a curated allowlist in the same spirit as
`ClassFeatureRegistry`. Forty-two attacks across the bestiary satisfy both: twenty Prone,
twelve Poisoned, nine Grappled, one Incapacitated.

**The Phase Spider is the case that shows the two questions are independent.** Its bite
poisons "for 1 hour". Poisoned is a condition the engine executes, and the rider still
cannot be imposed, because an hour is not a turn boundary and there is nothing to round it
to that would not be a different rule.

**Grappled was the instructive absence, and is now implemented.** For two slices its
riders were completely modelled and still refused, because nothing in the engine gave the
condition an effect and a Grappled creature would have walked away at full speed while its
sheet said otherwise. It took a Speed of 0, an Escape action against the printed DC, and
the grapple ending with its grappler. Charmed and Frightened now hold that place: fully
modelled riders on the Sprite and the Oni, refused because the engine does not execute
them.

**The clock, and why the condition record carries a source nothing reads yet.** A
condition is held as an `ActiveCondition` — the condition, who imposed it, and a
`ConditionExpiry` naming whose turns are counted, which boundary ends it, and at which
turn number. Every combatant counts its own turns, and the number is fixed at the moment
of application as *the owner's count plus one*. That is the whole of "next", and it is
what makes one wording work in both places it appears: a rider applied on the devil's own
turn and one applied during somebody else's, on an Opportunity Attack, both read "until
the start of the devil's next turn" and mean different moments. The clock ticks for every
creature whose turn comes round, including one that is dead or Unconscious and cannot take
it — a duration measured against a creature that never acts again has to end anyway.

The source is on the record before anything reads it, which is a deliberate exception to
this project's habit of not building ahead. The grapple needs it, the grapple is next, and
adding it later means reopening every call site that applies a condition rather than one.

**Whose turn "next" refers to is read off the possessive, and the two readings are not
interchangeable.** "until the end of *its* next turn" is the creature carrying the
condition; "until the start of *the devil's* next turn" is the creature that imposed it.
Getting them the wrong way round changes how long the condition lasts by most of a round,
and both wordings are common in the bestiary.

## Open questions

1. **Is there an economy?** Gold, and a shop between fights, or is loot the only
   source of equipment? Affects Phase 4, not before.
2. **Which monsters does the ladder draw from?** 330 is far more than a tier-1 game
   needs, and coverage is uneven — a creature whose entries are largely unmodelled is a
   poor choice for an authored encounter regardless of its CR. Curating the pool the
   encounter builder may pick from, weighted by coverage, is a Phase 4 decision.
3. **What closes the remaining 47%?** Answered — see *The order the remaining engine work
   goes in*, below. Kept in this list because it is the question a reader arrives with.

## The order the remaining engine work goes in

**Ordered by what each piece rests on, not by how much it is worth on its own.** An
earlier draft of this section listed saving-throw effects first, because 62 tier-1
entries is the largest single block of dead mechanics in the bestiary. That was the wrong
answer: it is the item with the most unbuilt prerequisites, and starting there would have
meant building the condition model and the monster action economy incidentally, inside a
branch about something else, and then reopening both.

**1. Condition durations (#15) — done.** The condition record took **an expiry and the
combatant who imposed it in the same pass**, for the reason set out above: the source is
what Grappled needs, and splitting them would reopen `Combatant`, the condition
collection and every call site twice.

Doing it turned up something the issue had not: **a clock nothing runs on proves nothing.**
Of the fifteen riders whose duration is now modelled, only one — the Cloud Giant's
Incapacitated — was on a condition the engine executes, so the whole subsystem would have
shipped with a single CR 9 monster exercising it. Eleven were Poisoned, so Poisoned went
on the allowlist in the same branch: it is Disadvantage on attack rolls, five lines in
`AttackRules`, and nothing in a fight rolls the ability check the other half of the
condition would need. That is the difference between a duration model and a duration model
anyone can see working.

**2. Grappled and Restrained (#16) — done.** It was the smallest real consumer of the
condition model and it did its job as a test of it: the grapple used the source, the
Escape action used removal-by-something-other-than-a-timer, and nothing in the record had
to change to support either. Nine riders started landing.

Reading the printed rules rather than working from memory corrected two things that would
have shipped wrong. **Grappled gives Disadvantage on attack rolls "against any target
other than the grappler"** — not a blanket penalty, so hitting back at whatever has hold
of you is the one attack a grapple does not hamper, and it is the only circumstance in
`AttackCircumstances` that depends on who is being attacked rather than on the attacker
alone. And **there is no generic Escape action in this SRD**: escaping is a Strength
(Athletics) *or* Dexterity (Acrobatics) check — the creature's choice, so the engine takes
the better and says so — against a flat DC rather than a contest.

That is also the first ability check the engine has ever rolled in a fight, which retired
a caveat written down one slice earlier: Poisoned went on the allowlist noted as "complete
for every roll the engine makes today, and the one entry to revisit the moment an
in-combat ability check exists". Escaping while Poisoned now rolls with Disadvantage.

*Conditions are the most-reopened type in the whole queue — saving-throw effects impose
them on a failure, passive traits reference them, Cunning Strike applies them. Settling
them once, first, is the whole argument for this ordering.*

**3. A way for a monster to use a stat-block entry (#19), together with recharge tracking
(#8) — done.** The prerequisite no issue had named: `UsageLimit` was never read in `Core`,
and every `Encounter` action was either hardcoded or gated on `Stats.Character`, so a
monster had no way to use an entry at all. Now `Encounter.UseEntry` resolves a named
Action entry — dispatching on `EntryMechanics` and refusing anything it cannot resolve
with a named code, the same shape as `spell.not_implemented` — and one `UsageState` per
combatant gates every path by entry name, with the Recharge d6 rolled and narrated at the
start of the creature's turns while spent. Saving-throw entries refused with
`entry.save_not_implemented` at this point; that refusal was the exact seam step 4
replaced.

Three things doing it turned up:

- **The two shapes of a limited attack need the gate in two places.** The Ape's Rock
  (Recharge 6) sits under Actions but *outside* its "two Fist attacks" Multiattack, so
  `Attack` refused it outright and `UseEntry` is its only road; the Minotaur's Gore
  (Recharge 5) is a plain attack with no Multiattack in the way, so the gate had to hold
  on the `Attack` path too. One state keyed by entry name serves both, which is the
  "build them together or write them twice" argument having been right.
- **The tactics policy chooses only among limited-use entries, deliberately.** The other
  attacks locked out of a Multiattack are the lycanthropes' form-gated ones — "Bite (Wolf
  or Hybrid Form Only)" — and the engine has no concept of form, so the policy choosing
  one would silently decide what shape the creature fights in. A client may make that
  call through `UseEntry`; the policy never does. Written down in
  `SimpleTacticsPolicy.TryUseLimitedEntry`.
- **The d6 is rolled only while the ability is spent.** The SRD says to roll at the start
  of each of the monster's turns; a roll for a charged ability would change nothing and
  still consume a die, and the dice stream is what the frozen transcripts pin. A stated
  interpretation, recorded on `Encounter.RollRecharges`.

**4. Saving-throw effects (#6) — done.** It landed with nothing left to invent, as
predicted: `Encounter.UseEntry` dispatches a `SavingThrow` entry into the same loop that
already resolved a save spell, now shared as `Encounter.ResolveSaveEffect`. The riders
are a *parameter* of that loop rather than read from the effect: an entry imposes every
rider the engine executes (on a failure, or either way for "Failure or Success"), while
a spell still passes none — so sharing the loop changed no spell behaviour, and executing
spell conditions remains its own undecided piece of work. Sixty-two tier-1 entries,
thirty-three with an area, thirty-one imposing a condition on a failure.

Five decisions doing it recorded:

- **A Line no longer covers its own origin square.** `InCone` always excluded it
  explicitly; `InLine` did not, so every breath weapon caught its breather. The stated
  interpretation in `AreaTargeting` now excludes the origin for both self-originating
  shapes. Nothing pinned the old reading — no test, no transcript line.
- **Whether an Emanation includes its origin is left unverified and unchanged.** The
  printed glossary rule could not be checked because the source PDF is absent from this
  machine, and this project does not correct geometry from memory. The engine still
  covers the origin square; the tactics policy meanwhile refuses any area that would
  catch the user's own side — which keeps every Emanation entry unchosen until the
  glossary is read. Filed as its own issue.
- **A Grappled rider from a save carries no range.** The SRD ends a grapple when the two
  are further apart than "the grapple's range"; an attack's grapple takes the attack's
  reach, but a save effect prints no reach to measure. Whelm's engulf holds until the
  escape check succeeds or the elemental is incapacitated, and never breaks by distance.
- **The save path sweeps `EndBrokenGrapples`, and the spell path now does too.** The
  spell save loop had never swept it — a Fireball that killed a grappler left the
  grapple holding its victim forever, invisibly. Sharing the loop fixed the gap rather
  than duplicating it.
- **The extractor's rider accounting was deliberately *not* extended in the same
  branch.** An imposable rider sentence on a save entry kept landing in
  `UnmodelledClauses` — over-reporting, the safe direction — because tightening
  `IsAccountedFor` forces a content regeneration and the PDF was absent. The accounting
  change and its regeneration landed together in #28 once the PDF was restored.

**5. Passive monster traits (#9) — done for what the engine can express.** The holding
argument was right: Magic Resistance landed the day after saves did. The vocabulary is
`MonsterTraitRegistry`, the project's fourth curated allowlist — a printed trait name
maps to a `MonsterTrait` the engine executes, added only alongside the implementing
code, resolved once per combatant from its entries. Three traits landed, covering 32 of
the tier-1 band's repetitions:

- **Pack Tactics ×18** — Advantage when an ally able to fight stands within 5 feet of
  the target. The engine asks `IsActive` of the ally rather than only "not
  Incapacitated" (a dying ally is no help), and the Advantage applies to Opportunity
  Attacks too — the printed rule names the attack roll, not the Attack action. Combined
  through `D20Test.Combine`, so it cancels against Disadvantage rather than overriding.
- **Magic Resistance ×7** — Advantage on saving throws against *spells only*. The SRD
  says "spells and other magical effects", but the model captures no marker for which
  stat block entries are magical, so a breath weapon is read as not one — narrower than
  print, deliberately, and recorded on the registry to revisit if the extractor ever
  captures a magical marker.
- **Flyby ×7** — no Opportunity Attacks provoked by moving, gated in
  `MovementRules.FindOpportunityAttackers` beside the Disengage exit. The engine has no
  movement modes, so a creature printed with Flyby is read as flying whenever it moves —
  every one of them has a fly Speed it would have no reason not to use.

Deliberately absent, each for want of a model rather than of effort: Spider Climb and
Incorporeal Movement (verticality, wall-passing), Swarm (space-sharing, hit-point-gated
damage), Sunlight Sensitivity (light). **Undead Fortitude is the best next addition** —
it needs only a hook at the moment damage would drop the creature, machinery
`DamageRules` nearly has. The registry works off printed names; #28's regeneration taught the extractor the same
names, so these entries are now `EntryMechanics.Passive` in content rather than counted
unmodelled.

**6. Class features (#10) — done for what needs no new machinery.** Three landed the day
saves did, which was the argument for the ordering: **Danger Sense** is Advantage on
Dexterity saving throws unless Incapacitated, folded into the same combined-mode
computation as Magic Resistance and Restrained in the shared save loop; **Fast
Movement** is +10 feet derived in `CharacterResolver` beside the armour-class
derivation, gated on Heavy armour exactly as printed; **Steady Aim** is a Bonus Action
whose two readings are written down — "you haven't moved during this turn" is read as
"has spent no movement", so standing up counts as moving, and the forfeited Speed is
locked at 0 so a later Dash buys nothing. Steady Aim's Advantage is consumed by the next
attack roll whether it hits or not.

What remains of #10 is one follow-up issue per blocker rather than a live umbrella:
Fighting Style and Deft Explorer's Expertise are *character-creation choices* the draft
does not yet carry — the first genuinely new machinery in the list; Cunning Strike can
land Trip today but its Poison rider prints "for 1 minute", which is #22's duration
shape; Tactical Mind wants an in-combat ability check, of which the engine rolls exactly
one (escaping a grapple); Favored Enemy needs Hunter's Mark, whose effect the spell
grammar does not model. The least architectural item on the list
and the only one on the character rather than the monster side, which makes it the safest
work to interleave when a lower-stakes branch is wanted.

**7. Curating the monster pool (#11) — last, deliberately.** Weighting the pool by how
completely a creature's mechanics are implemented means nothing until coverage stops
moving, and every step above moves it. Doing it earlier guarantees doing it twice.

**Steps 1, 3 and 4 all touch the turn loop**, so the frozen transcript may churn. It uses
hand-authored combatants carrying no riders and may well survive untouched — but if it
diffs, that diff is a change to how the game plays and gets read before the fixture is
regenerated.
