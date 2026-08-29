# Engine guide — combat, characters, and spells

Moved out of `CLAUDE.md` on 2026-08-29. **Read the relevant half before editing
`src/SRDCombat.Core` or the character/spell resolution path.** CLAUDE.md keeps the
invariants you can violate without knowing you needed this file; everything below is
the detail that only becomes actionable once you are in the code.

## Working on the combat engine

- **The frozen transcript is the most valuable test here.** It pins a whole fight's
  narration byte-for-byte and has churned five times, each time catching a real
  gameplay change — twice catching shipped bugs no unit test found. **Read the diff
  before touching the fixture**; regenerate only once the new behaviour is intended
  (un-skip `TranscriptWriter`, run, re-skip, review). It uses hand-authored
  combatants on purpose, so it fails when the *engine* changes, not the content;
  `RealMonsterCombatTests` covers the other direction.
- **All randomness goes through `IRandomSource`.** Never `Random.Shared` in `Core`.
  `ScriptedRandomSource` throws on surplus rolls — if it fires, the test's premise
  changed (an Advantage roll consumes two dice).
- **Rules verified against print, pinned by tests** — the non-obvious set:
  Advantage/Disadvantage cancel; crits double dice only; monsters die at 0 while
  characters roll Death Saves; Dodge lasts to the start of the dodger's *next* turn;
  Unconscious-at-range is a normal roll (Advantage and Prone's Disadvantage cancel
  exactly); ranged within 5 feet of *any* able enemy has Disadvantage.
- **Cover is judged where the battlefield is known** (`Encounter` computes,
  `AttackRules` applies); Total Cover refuses targeting on every path *before*
  anything is spent, and Opportunity Attacks filter it because a reach weapon can
  genuinely span a wall.
- **Movement**: occupancy is "not dead"; the printed pass-through clauses execute
  (allies, the Incapacitated); the one deliberate contradiction of print is ending a
  move on a *fallen ally* (asked twice from play, scoped exactly that narrowly), and
  `ClearSharedSquares` displaces on wake-up. The pathfinder tie-breaks against
  wandering (it pays real pacing via fewer provoked attacks).
- **Encounter building is three published steps** — `EncounterBudget` (printed page
  202, exactly), `EncounterBuilder` (spends it; count bounds and taste weights are
  ours and stated), `EncounterFactory` (places it; layouts draw from level 3).
  `MonsterPool` decides what may go in the bag on four separate axes — coverage
  (derived from the accounting), plausibility, aquatic, genre — and nothing in the
  pool weights an encounter. Printed XP wins over derived (the Archmage).
- **Rests are a table, not a reset** (`RestRules`, each with citation): Rage and
  Second Wind one use on Short, all on Long; Action Surge either; slots Long-only;
  a Long Rest restores *all* Hit Dice (2024 change). The opening cycle rests Long
  throughout — a GM's-call reading that fixed the level 1 wall; both rests need a
  hit point to start.
- **XP award is a stated reading** — printed XP split evenly among the fighters —
  chosen because it makes the two published tables agree, with a test asserting it.
- **A run owns its state; the engine owns the fight.** Nothing about `GauntletRun`
  leaks into `Encounter`.

## Working on characters and spells

- **`CharacterResolver` derives everything.** No number on a `CharacterSheet` is
  stored independently of the rules that make it. Only choices the engine cannot
  make (ability spending, skills, fighting style, spell plans, ASI plans) come from
  the draft; levelling is re-resolving the draft at the new level, never a sheet
  edit; the new maximum leaves damage taken.
- **Ability increases come from the *background*, not the species** (a 2024 change).
- **The curated allowlists** — a printed name maps to an executed effect **only
  alongside the code that does the thing**; everything absent stays visibly reported:
  `ClassFeatureRegistry` (→ `CharacterSheet.UnimplementedFeatures`),
  `SpeciesTraitRegistry` (also → `UnimplementedFeatures`; empty today — none of the 33
  printed species trait instances execute, and both creation flows tag each one "(not
  yet implemented)" where its text is shown, via `CharacterCreation.TraitExecutes`),
  `WeaponMasteryRules.Executed` (6 of 8; Push and Nick refused with reasons),
  `MonsterTraitRegistry` (Pack Tactics, Magic Resistance — spells only, Flyby),
  `MagicItemRegistry` (13 names; unregistered items are *refused at equip*),
  `PreparableSpells` (the casting menu — shape data would offer partially-executing
  spells, the Goblin Warrior bug wearing a spell list), and `TraditionalFoes` /
  `PlausibleFoes` (the pool's taste and plausibility cuts).
- **Casting works**: attack spells against AC, save spells against the caster's DC
  halving on success, slots spent, upcasting structured at extraction (a save spell
  carries damage in `Damage` *and* `Save.FailureDamage` — grow both or you silently
  un-upcast every save spell), Concentration tracked and broken by damage or by
  gaining Incapacitated by any route — a save-imposed rider, a repeat-save
  escalation, or damage that downs the concentrator (#289) — single-target healing
  only, refusals with reasons everywhere else.
- **`SpellcastingRules.AbilityFor` is a curated map, not Primary Ability** — right
  for six classes, quietly wrong for two if derived.
- **Subclasses are derived, not chosen** — the SRD prints one per class; the
  extraction boundary is the single backwards step in the feature-level sequence.
- **Extra Attack and Multiattack are the same rule**: the Attack action buys several
  attacks, never several actions. A Multiattack constrains which attacks compose it;
  one naming an attack the creature lacks is dropped entirely.
- **Potions**: `PotionRules` is a curated transcription (the potencies live in
  body-text print); drinking and administering both cost the Bonus Action (page
  204); refusals fire *before* the potion is spent.
- **Loot rates are this project's design; the items are the book's.** The SRD prints
  no award rate; `LootTable` states ours. Equipping is a draft change re-resolved —
  found gear rides the save for free and cannot drift.
