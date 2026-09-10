# Design — monster behaviour profiles, morale, and telegraphed intent

**Date:** 2026-09-10. **Written against:** `bb5eabd` (`main`).

**Answers part of** [#543](https://github.com/brandonifco/SRD_Combat/issues/543) ("Design: how each monster group should actually fight, from what the creature is"). It settles how behaviour is *structured*, *assigned* and *made legible*; it does **not** settle #543's first requirement — the roster of groups and which creatures are in each — which §5 leaves open and #750 routes to `designer`. Against [`docs/2026-08-27-enemy-ai-audit.md`](2026-08-27-enemy-ai-audit.md) §6 it answers Q3 (should anything flee) outright, answers the *leadership* half of Q1 (role as an axis is still open), and lifts Q5's hold — Brandon waived the baseline objection, which is not the same as the seam arriving; #314 is still open and still paused, and Q5's actual question, *what is the measurement story*, is unanswered. The
audit reported what the code does and stopped, by mandate. **This document is the design
that was deliberately left out of it.**

**Model and provenance, per the precedent the audit itself set:** written in the main
session on **Opus 5**, not by the `designer` agent on Fable 5. CLAUDE.md's team table
assigns design judgement to `designer`. Brandon worked through the decisions
conversationally in-session and made every judgement call himself; the session's role was
to frame options, measure, and record. **This is a stated deviation**, noted so a later
reader knows whose judgement they are reading. The decisions below are Brandon's, dated
2026-09-10: eleven he was asked and answered, plus D4 (a consequence of D3, recorded separately because a later reader needs it stated outright) and D13 (added after review, when a fresh-context pass found a shipped leader mechanism this design had failed to reconcile with).

**Every number in §4 was measured**, in this session, over the real loaded corpus. The
method is in [§7](#7-how-the-numbers-were-taken). No number here is carried over from an
earlier document.

---

## 1. What this is for

The audit's framing is the one to keep: `SimpleTacticsPolicy` (now 1,684 lines) is **not a
monster AI with gaps — it is a fight-completion driver that has been asked to stand in for
one.** The only per-monster variation in the whole engine is
`MonsterDoctrine.ChooseTarget`, 112 lines, three branches, deciding *which enemy a creature
walks toward* and nothing else. Steps 5 through 11 of a turn are byte-identical for a Wolf,
an Ogre, a Goblin Warrior and an Archmage.

The goal is **not to make monsters play better.** It is to make them play *differently*
and *legibly* — so a player can read what a creature wants, and counter it. The precedents
this design leans on are Gloomhaven (a published, deterministic focus rule that players can
compute), Into the Breach (telegraphed intent converts AI quality into player agency),
Baldur's Gate 3 (a dozen named archetypes shared across hundreds of creatures), and
F.E.A.R. (whose celebrated intelligence was substantially its *barks* — enemies announcing
intent — rather than its planner).

What is explicitly **not** the goal: a planner, a behaviour tree, learned behaviour, or
per-monster bespoke logic. The engine already contains a utility scorer (`WeaponValue`,
`SpellValue`, `ControlValue`, `ScoreSquares`); the design parameterises what exists rather
than replacing the paradigm.

## 2. The three mechanisms

Brandon's framing, 2026-09-10:

> *"goblins and goblin leaders should have the same base, but the leader has additional
> priorities, and the other goblins' morale is strengthened by the presence of the leader,
> but breaks when the leader goes down, that sort of thing. groups of the same monster have
> group priorities that affect individual priorities"*

That is three separable mechanisms with different costs, and they are kept apart on
purpose:

1. **Profile composition** — a base profile plus one role overlay. Cheap.
2. **Morale** — the first mechanism in this project that wants *memory*. The doctrine
   classes are pointedly stateless (`PartyDoctrine`'s doc comment: *"a blackboard that
   remembered things would be a second copy of the fight to keep honest"*). This is the
   one that costs something.
3. **Group priorities** — the side-level judgement individuals defer to, mirroring
   `PartyDoctrine.Converge`.

## 3. The decisions

Each records what was chosen, why, and what was rejected. Decisions 5 and 10 were **amended
after measurement** — see §4.

### D1 — A broken monster flees the board and counts as defeated

It runs for the nearest edge and is removed on reaching it, with **full XP and loot
credit**.

Credit is the whole decision. Without it, players rationally chase fleeing goblins across
28×18 squares, which is the pursuit slog morale was meant to avoid, wearing a different
costume. With it, **breaking a warband becomes an alternate win condition the player can
aim for** — which is the kind of player-facing choice F3 exists to create.

**This is not new — it is the generalisation of something already shipped.** `ObjectiveKind.KillLeader` has, since 2026-08-15, ended one fight in five when the marked creature dies, narrating *"The fight ends: the leader is down and the rest break off"* (`Encounter.cs:2937`) and paying full rewards. D13 reconciles the two.

*Rejected:* cowering in place, which re-opens the [#278](https://github.com/brandonifco/SRD_Combat/issues/278)
stall shape — a standing-but-harmless creature that no targeting path wants and no
end-condition counts, the exact bug `FinishTheDowned` exists to prevent. *Rejected:*
degrade-only, which never delivers the visible collapse.

*Pacing consequence, raised and waived:* fights ending early means less attrition. Brandon,
2026-09-10 — *"forget the pacing. with the battlefield designer when its finished, i will
custom make most of the combat scenarios"*. Recorded as an accepted change, not an
oversight.

### D2 — `Frightened` is the marker

Morale break applies the printed condition rather than a bespoke state or a new
`ConditionType`.

The machinery is already there and already trusted: `ConditionRules.Executable` lists
Frightened, the engine enforces Disadvantage while the source is in sight **and forbids
willing movement closer to the source unconditionally**, durations print and serialize, and
`ConditionImmunities` is extracted per monster and enforced at `Combatant.cs:1636`. That
last one is the prize — **43 of 330 monsters print immunity to Frightened**, and the list runs the right way: animated armour, golems, puddings, fungi and gelatinous cubes among the mindless, devas and balors among the proud. It is not a tidy set — it also holds Ettin, Hydra, Lich, Kraken, Shadow, Ghost, Mummy and both Sphinxes, and the summary above is a characterisation of it rather than its contents. One consequence worth stating outright: **Knight is on D5's officer list and is immune to Frightened**, so a Knight-led warband has a commander who can never break — correct, and free. Mindless things and proud things do not rout, straight off the printed page,
with nothing authored.

**The hazard, and its containment.** 14 monster entries and 17 spells already reference
Frightened. If Frightened *alone* meant "routed", every existing fear effect would become a
delete-the-enemy-and-bank-the-XP button. So **rout requires Frightened plus a morale
precondition** (D9). A fear spell on a goblin whose boss is alive and whose warband is
intact makes it fight badly; the same spell after the boss falls makes it run. Fear becomes
a combo that cashes in a broken warband, and the player has to earn it.

*Divergence from print, stated:* nothing in SRD 5.2.1 imposes Frightened for morale. The
SRD prints no morale rule at all, so morale is this project's design either way — the same
shape as `LootTable`'s award rate. Recording it here is the sign-off.

### D3 — A morale check is a Wisdom saving throw, on the shared dice stream

Most idiomatic thing the system could do: it uses printed Wisdom and the existing save
machinery, and it staggers breaks across a warband naturally rather than flipping every
creature on the same tick.

*Cost, accepted:* inserting rolls mid-fight shifts every subsequent roll, so existing seeds
stop producing the fights they used to. **`FrozenTranscriptTests` regenerates once** (read
the churn with the `transcript-churn` skill first — it is 155 lines and it will move
wholesale), and the `112ed19` pacing baseline stops being comparable.

*Rejected:* a separate AI `IRandomSource`, which would have kept the transcript stable at
the cost of a second seed to thread and serialize. It was the right answer while pacing
comparability mattered; D1's waiver removed the reason. *Rejected:* a deterministic
threshold, which is more learnable (the Gloomhaven property) but flatter.

### D4 — The AI consumes dice

Follows from D3, recorded separately because it is the fact a future reader needs: **the
tactics policy is now a dice consumer.** Anything that changes how many rolls a decision
takes changes every seeded fight downstream of it.

### D5 — The leader is a curated officer list *(amended after measurement — see §4)*

**Originally decided** as derivation from CR margin: the single highest-CR creature, where
its CR clearly exceeds the rest. The printed corpus looked cooperative — Goblin Boss CR 1
against Goblin Warrior 0.25, Bandit Captain CR 2 against Bandit 0.125, Hobgoblin Captain CR
3 against Hobgoblin Warrior 0.5, margins of four to sixteen.

**Amended to a curated list** on the census in §4, which found CR margin anointing an Ogre, a Gargoyle, an Awakened Tree, an Ochre Jelly and a Giant Vulture. (A Spy also surfaces, and *is* on the curated list below — the objection is not that CR margin never finds an officer, it is that it cannot tell one from an ooze.) The
corpus holds **14 leader-shaped stat blocks at CR ≤ 4, every one of them INT 9–14 and
named by rank** — Guard Captain, Tough Boss, Hobgoblin Captain, Knight, Warrior Veteran,
Bandit Captain, Cultist Fanatic, Priest, Druid, Berserker, Goblin Boss, Spy, Scout, Priest
Acolyte. That is a tractable list in the pattern of `ConditionRules.Executable`,
`MonsterTraitRegistry` and `EncounterThemes` — **not** the 73-entry table the audit's Q1
feared, because officers are rare, not because the curation was made lax.

A warband with no officer correctly has no leader, and falls back to D9's casualty trigger.

### D6 — Leader presence is a command radius

Alive and within N feet grants **advantage on the morale save**. One number, two uses (D10
reuses it as the group boundary), and it dodges group-*membership* entirely: no roster of who belongs to which unit, no new data structure for it, no save-format change. It does not dodge leader identity — `EncounterObjective.LeaderId` already names a single leader, and D13 is what the radius hangs off.

Because a check fires on a trigger and its result is a condition with a duration, **presence
is sampled once, at check time.** The flicker that would plague a continuously-recomputed
morale value does not arise.

*Rejected:* radius plus line of sight, which would have made terrain feed morale and reused
`VisionRules`. It remains the most interesting option and is the natural later extension.
*Rejected:* alive-anywhere, which has no positional layer. **Note:** sight *alone* was never
a real option — `VisionRules.CanSee` is pure geometry with **no distance falloff** (distance
enters only via Blindsight, for Blinded viewers), so on open ground it collapses into
alive-anywhere.

### D7 — Rally only near a living leader

Broken is permanent for the encounter unless a living leader comes within the command
radius. Since the usual trigger *is* the leader dying, this is permanent in the common case
— correctly, because there is nobody left to restore order. It earns its keep in the other
case: a warband that breaks from casualties while its officer lives can be rallied, which
gives the officer a job after the line wavers and the player a reason to focus him even
when he is not the biggest threat.

*Rejected:* a repeat save each turn — the printed grammar for Frightened riders, and free,
but broken creatures turning around mid-flight read as indecision rather than fear.

### D8 — Intent is telegraphed on a new `CombatStep` field

Not in `Narration`. The frozen fixture is built as
`string.Join('\n', encounter.Log.Select(step => step.Narration))` — it reads **only** that
property, so a sibling field is invisible to it with no change to the test. `CombatStep`
already carries structured detail this way (`AttackName`, `Hit`, `Damage`, `Ranged`), so
the shape is idiomatic rather than novel.

Brandon asked for this *"at least for development"*. Putting it on the step rather than in a
dev-only sink means "players see it too" is later a display toggle, not a rebuild.

*Rejected:* writing intent into `Narration`, which would churn the frozen fixture on every
profile tweak and degrade `transcript-churn` from a safety discipline into noise agents
learn to skip. *Rejected:* an `IDecisionLog` sink, cleaner if this were permanently a dev
instrument, but it cannot be saved, replayed or shown in the Godot client.

### D9 — Checks fire on leader death and on a casualty threshold

Two events: the leader falling, and the side dropping below a fraction of its starting
strength.

The casualty trigger is what keeps morale visible at all. Under D5 most warbands have no
officer — and `WildPack` (wolves), `Undead` (zombie shambles) and `DungeonVermin` (swarms,
oozes) structurally never will. Leader-death alone would make the whole system invisible
across a third of the encounter themes. With the casualty trigger, **morale fires in every
fight** and the leader layer is a bonus on top.

*Rejected as the default:* adding the creature's own wounds as a third trigger — the most
organic staggering, but roughly double the rolls. **The trigger set is itself a profile
field**, so a `cowardly` profile may add it and a `fanatic` profile may drop the casualty
one. This decision fixes the baseline, not the ceiling.

### D10 — Inside the radius, the leader's Intelligence stands in — *taking the better of the two* *(amended — see §4)*

Today coordination requires *individual* intelligence: `MonsterDoctrine` converges only at
INT ≥ 8, so a goblin at 10 coordinates and an ogre at 5 charges. That rule stays. What
changes is that **a leader can do the thinking for creatures that cannot** — an ogre under
a hobgoblin captain fights like something being directed, and killing the captain visibly
makes the ogres stupider again. One substitution, reusing `Converge`, which already exists
and is already measured.

**Amended:** the substitution takes `max(leader, own)`, never a straight replace. The census
found that where CR margin named a leader, **55.5% of the time that "leader" had lower
Intelligence than its best subordinate** — an Ogre at INT 5 over goblins at INT 10. A
straight replace would have made troops *dumber* under command, which is backwards. D5's
curated list (all INT 9–14) makes this rare, and the guard makes it impossible.

*Rejected:* leader-issued named orders (*"Goblin Boss orders: focus the cleric"*) — the best
possible telegraph and the most expressive option, but the most machinery, and adjacent to
`EngagementPhase` ([#125](https://github.com/brandonifco/SRD_Combat/issues/125)), which shipped and never paid. Orders are a richer payload
through the same seam, and can arrive later.

### D11 — Discipline derives from Intelligence

The chance a creature ignores group focus for the local greedy option comes off the printed
stat, like leadership comes off rank and morale off Wisdom. Nothing to author, and a profile
may override where the stat lies.

Every precedent tunes its AI *down* — XCOM's aliens do not focus-fire your wounded ranger to
death — and most projects do it accidentally and then cannot find the knob. This is the knob,
deliberate from the start.

It composes with D10: a captain makes his troops **both smarter and steadier**, and killing
him degrades both at once. One rule, two visible effects.

### D12 — Definitions in code, assignment in data

Profile *definitions* are a C# registry. Profile *assignment* is scenario data.

**`data/` is print; code is judgement.** All eight files in `data/srd` are extractor output;
every curated judgement in this project already lives in C# — `MonsterTraitRegistry`,
`SpeciesTraitRegistry`, `ClassFeatureRegistry`, `MagicItemRegistry`,
`ConditionRules.Executable`, and `EncounterThemes` with its hand-written
`["Goblin Minion"] = [GoblinoidWarband]` mappings. Profiles are judgement. A hand-authored
`data/doctrine/profiles.json` was proposed early in the session and **withdrawn** on this
reasoning: it would have been the first hand-authored file in a directory whose meaning is
"the printed page", and it would have needed the full load-path treatment (required members,
`UnmappedMemberHandling.Disallow`, shape tests) for no gain.

Definitions in code get compile-checked rule names and knockout verification. Assignment in
scenario data gets authorability: a scenario says `leader: "warlord"` by name, and the
battlefield designer never needs to know more than the names.

**Proposed merge semantics** (not decided; the cheapest thing to change later): at most two
layers, base plus one role overlay; every rule carries a name and an explicit priority
number; an overlay adds rules or overrides one *by name*, then the merged set sorts by
priority. Positional insertion is rejected — it is unreadable in a diff and fragile under
edit.

### D13 — Morale subsumes `KillLeader`; the objective becomes its special case

**This decision was added after review**, when a fresh-context pass found that the engine
already ships a leader mechanism this design had never reconciled with.

What already exists, since 2026-08-15: `ObjectiveKind.KillLeader` marks one creature
(`EncounterObjective.LeaderId`); `EncounterFactory.Resolve` derives it as **the dearest
monster by printed XP**, a stated reading argued in that method's own doc comment;
`Gauntlet` schedules it on slot `FightsPerCycle - 1`, so **6 of 30 fights**; the rung forces
a minimum of three monsters, *"leader plus a pair"*; `PartyDoctrine` already converges the
party on the marked creature; and `Encounter.cs:2937` ends the fight with *"the leader is
down and the rest break off"*, paying full rewards.

**That is D1, already shipped, for one fight in five.** So the two do not compete — morale
generalises what the objective special-cases. The reconciliation:

- The **officer** (D5's curated registry) is who holds a warband together, everywhere.
- On a `KillLeader` rung the objective marks a creature and the fight still ends when it
  dies; that rung is the case where an officer is *guaranteed present and explicitly
  marked*, rather than a second, competing notion of leadership.
- The XP derivation stays as the *marking* rule for the objective. Where a curated officer
  is present it should be what gets marked, so the two never disagree on the field —
  **that consequence is scoped to S2 and is not settled here.**

*Rejected:* keeping two independent leaders under different names, which leaves a Goblin
Boss and a marked Ogre on the same field answering different rules. *Rejected:* letting the
objective's marked target own morale on those rungs, which makes an officer standing beside
it grant nothing.

**This is the decision that changes §4's headline number** — see reading 3.


## 4. The census that amended D5 and D10

Run in this session against the real loaded corpus, over **1,800 built encounters** — three
difficulties × five party levels × 120 seeds, party of four, `MonsterPool.Draw` at the
`Playable` floor:

```
pool=78 encounters=1800
CR-margin leader present      : 281  (15.6%)
  ...leader DUMBER than its best troop: 156  (55.5% of those)
curated STRICT leader present : 238  (13.2%)
  ...also the highest CR present: 166 (69.7%)
curated BROAD leader present  : 245  (13.6%)
singleton (1 monster)         : 225  (12.5%)
uniform (one species)         : 294  (16.3%)
```

Top creatures CR margin named as leaders: Ogre 46, Spy 33, Hobgoblin Warrior 18, Gargoyle
16, Awakened Tree 16, Scout 13, Giant Vulture 13, Ochre Jelly 13, Berserker 13.

**Three readings, all load-bearing:**

1. **CR margin names the wrong creature more often than not.** An Ochre Jelly does not
   command a warband. → D5 amended to curation.
2. **A straight Intelligence substitution is backwards in the majority of cases** (55.5%).
   → D10 amended to `max()`.
3. **Detection is not the bottleneck; composition is — but the run composes more than
   this census saw.** Curation fires at 13.2%, *no better than* CR margin's 15.6%, so no
   detection rule can raise the rate. **This census called `EncounterBuilder.ForParty`
   directly and therefore did not measure the gauntlet's own path**, which runs through
   `EncounterFactory` and forces "leader plus a pair" on boss rungs. Corrected for that:
   `Gauntlet` schedules a `KillLeader` rung on 6 of 30 fights, where a leader is marked
   **by construction**, so under D13 the leader layer reaches roughly **6 + 13.2% of the
   remaining 24 ≈ 9 of 30 fights, about 31%** — not the one-in-seven this section
   originally claimed. The 13.2% is a true figure for *builder output in isolation* and
   nothing more.

Reading 3 is why D5's amendment accepts the rate rather than chasing it with a cleverer detector: officers are rare in *builder* output, D13 supplies them on boss rungs by construction, and hand-authored scenarios place them deliberately. Raising the rate in ordinary procedural fights is filed separately rather than folded in.

The pool figure of **78** is measured today and supersedes the audit's 73 at `4c806a3` —
the pool grew, which is the direction `MonsterPoolTests`' floor expects.

## 5. What is deliberately not decided

All downstream of the format, none blocking it: the command radius in feet, the casualty
fraction, how the morale DC is derived, whether the intent field is a string or a structured
record, the merge semantics above, and the roster of profiles itself.

**Three that review found missing, and that an engineer must not invent:**

- **What a morale `Frightened`'s source is.** The condition is source-relative:
  `ConditionRules` reads a null source as "in sight" (permanent Disadvantage) while
  `Encounter.Move` refuses a closer destination only when the source resolves. So a null
  source gives a marker that does half its job, and a *party member* as source can refuse
  every rout move whose nearest edge lies past that character — a stall, and one aimed at
  by no criterion that only considers terrain. Decide it in S1.
- **How a procedurally drawn creature gets a profile at all.** D12 settles definitions and
  scenario assignment; it does not say what assigns a profile in the fights that happen
  before the battlefield designer ships, which is all of them today. Deriving it from a
  predicate is what CLAUDE.md's bug 2 and #543 both warn against, so this needs deciding
  rather than defaulting.
- **Whether a curated officer, where present, becomes what `KillLeader` marks** (D13's last
  bullet). Scoped to S2.

Also not decided, and larger: **whether `EncounterBuilder` should compose officers into
themed warbands** (§4 reading 3), and whether the group layer later carries named orders
(D10's rejected option).

## 6. Sequencing

The audit's Q5 named two blockers — [#314](https://github.com/brandonifco/SRD_Combat/issues/314)'s `ITacticsPolicy` seam for A/B measurement, and
an unsettled pacing baseline. **Both have moved:** Brandon ruled on the baseline on
2026-09-09 (*not too hard*, recorded on [#542](https://github.com/brandonifco/SRD_Combat/issues/542)), and waived pacing comparability for this
work on 2026-09-10. #314 remains the natural home for a swappable policy but no longer
gates the design.

Suggested slices, each one concern:

| # | Slice | Depends on |
| --- | --- | --- |
| S1 | Morale core — WIS save, Frightened marker, D9 triggers, rout movement, exit-and-credit | — |
| S2 | Leader layer — curated officer registry, command radius, advantage, rally, `max()` INT substitution | S1 |
| S3 | Telegraph — `CombatStep` intent field, and the firing rule's name as its payload | S1 |
| S4 | Profile registry — the priority-list shape, derived discipline, two-layer merge | S3 (names are the payload) |
| S5 | Scenario assignment — per-spawn profile and leader override | S4, battlefield designer |
| S6 | Builder composition — officers in themed warbands | S2 |

S1 carries the frozen-transcript regeneration that D3/D4 make unavoidable — the AI becomes a dice consumer, so every seeded fight shifts. It is not the only slice that *may* churn the fixture: S2 adds a d20 draw for advantage, and S4 churns wherever a shipped profile changes a decision. What is true is that the scripted skirmish hand-builds its four Raiders (`SkirmishScenario.cs:74-77`) and never calls `EncounterBuilder`, so **S6 cannot churn it at all**.

## 7. How the numbers were taken

Corpus counts (43 Frightened-immune of 330; 14 entries and 17 spells referencing Frightened;
the 14 leader-shaped stat blocks) — a census over `data/srd/monsters.json` and
`data/srd/spells.json`.

Encounter counts — a temporary xUnit test inside `SRDCombat.Content.Tests`, so it ran against
the real loaded corpus rather than a re-implementation of the gate, calling
`MonsterPool.Draw(Content.Monsters, 4m)` and then `EncounterBuilder.ForParty(pool, 4, level,
difficulty, new SeededRandomSource(seed * 7919 + level * 31 + (int)difficulty))` across the
grid described in §4. Deleted after the run, per the precedent in the audit's own §7.

Scoring definitions, so the figures can be re-derived:

- **"CR-margin leader"** — more than one creature present, exactly one at the highest CR,
  and that CR at least twice the next-highest present.
- **"curated STRICT"** — the encounter contains one of: Guard Captain, Tough Boss,
  Hobgoblin Captain, Bandit Captain, Goblin Boss.
- **"curated BROAD"** — STRICT plus Knight, Priest, Druid, Cultist Fanatic, Warrior Veteran.
- **"leader is dumber than its best troop"** — the CR-margin leader's Intelligence is below
  the maximum Intelligence among the other creatures present.

**The 14-name officer list in D5 is a hand-pick, not a census result.** It was drawn by
reading the CR ≤ 4 stat blocks for names denoting rank or office and checking their
Intelligence; "Berserker", "Druid", "Spy" and "Scout" are judgement calls about what leads a
warband, not readings of a printed field. The Intelligence range (9–14) *is* measured. The
STRICT/BROAD split above exists precisely because the boundary is a judgement — the two
bracket it, and they differ by 0.4 percentage points.
