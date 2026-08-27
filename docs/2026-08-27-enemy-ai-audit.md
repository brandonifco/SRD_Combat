# Audit — what a monster actually does on its turn

**Date:** 2026-08-27. **Measured at:** `4c806a3` (`main`).

**Model, recorded per the precedent set by** `docs/2026-08-26-battle-builder-design.md`**:**
this audit was written in the main session on **Opus 5**, not by the `qc` or `designer`
agent on Fable 5. CLAUDE.md's team table assigns adversarial review and design judgement
to Fable deliberately. Brandon asked for the read in-session and asked for it back in the
same conversation, so it was done there. **This is a stated deviation**, recorded so a
later reader knows whose judgement they are reading — and it is one more reason this
document is an *audit* and not a spec: it reports what the code does, and stops.

**Mandate:** Brandon, 2026-08-27, mid-run:

> *"we also have to have a look at enemy AI. there's serious consideration to be put into
> how each particular monster acts under various circumstances."*

Asked whether to audit first or go straight to a design, he chose the audit: *"Read and
report first… No code changes. You judge from facts rather than my proposal."* So this
document contains **no recommendations and no proposed design.** Section 6 lists the gaps
as questions, not answers. The design that follows is a separate document and a separate
decision.

**Every number here was measured**, by census over `data/srd/monsters.json` and over
`MonsterPool.Draw(Content.Monsters, 4m)` — the actual bag the gauntlet draws from — not
estimated from reading. The census ran as a throwaway test against the loaded corpus and
was deleted; its query is reproduced in [§7](#7-how-the-numbers-were-taken) so anyone can
re-run it.

---

## 1. The shape of a turn

[`SimpleTacticsPolicy.TakeTurn`](../src/SRDCombat.Core/Combat/SimpleTacticsPolicy.cs) —
1,557 lines — takes the turn of **every combatant not being driven by a human**. In the
Godot client that is every monster; in `PacingMeasure` and the frozen transcripts it is
both sides.

The sequence is fixed. Read it as a straight line, because that is what it is:

| # | Step | Where |
| --- | --- | --- |
| 1 | Escape a Grapple — always, at the cost of the whole Action | `TakeTurn` |
| 2 | Stand up from Prone, unless immobile | `TakeTurn` |
| 3 | Spend class features and spells — **characters only** | `UseCharacterFeatures` |
| 4 | Choose a target | `ChooseTarget` → `MonsterDoctrine` / `PartyDoctrine` |
| 5 | Sidestep to a cheaper firing square, if the shot from here is penalised | `ImproveFiringPosition` |
| 6 | Spend a Bonus Action entry if one reaches | `TryUseBonusEntry` |
| 7 | Cast a damaging spell, if it beats the swing it would replace | `TryCastDamagingSpell` |
| 8 | Attack, if anything reaches and closing would not pay better | `TryAttack` |
| 9 | Use a limited-use entry, if the Action reached nothing | `TryUseLimitedEntry` |
| 10 | Walk toward the target | `MoveTowards` |
| 11 | Re-target and attack again from wherever the walk ended | `TakeTurn` |
| 12 | If nothing else happened, finish a downed enemy | `FinishTheDowned` |
| 13 | End turn | `TakeTurn` |

**There is no branch anywhere in that sequence on what kind of creature is taking the
turn.** Step 3 is gated on being a *character*, which is a side, not a species. Steps 5
through 11 read the creature's attacks, its reach and its movement, and nothing else about
it.

The class's own doc comment says as much, and has said so since it was written:

> *"Deliberately without Dodge, Disengage or a retreat — real tactical judgement in that
> sense is its own phase of work… What it still does not do is the point: it exists to
> drive a fight from start to finish without a client, which is what makes an end-to-end
> engine test possible at all."*

That is the honest framing to hold onto. **This is not a monster AI that has gaps. It is a
fight-completion driver that has been asked to stand in for one**, and it has been
extended eleven or twelve times — focus fire, firing position, spell valuation, the
stuck-turn last resort — each time to fix a specific observed failure, never to give a
creature a character.

## 2. The only per-monster variation in the engine

One function: [`MonsterDoctrine.ChooseTarget`](../src/SRDCombat.Core/Combat/MonsterDoctrine.cs).
Three branches, in order:

1. **Pack Tactics** — take the enemy an able packmate already stands within 5 feet of,
   preferring one already in reach. The trait gate outranks the Intelligence gate on
   purpose: a Wolf is INT 3 and flanks anyway, because for a pack the flank *is* focus
   fire.
2. **Intelligence ≥ 8** — converge on the party's shared kill, via `PartyDoctrine.Converge`,
   exactly as a character does. The threshold is a stated reading: 8 is the bottom of the
   humanlike range.
3. **Everything else** — greedy: the weakest enemy already in reach, else the nearest.

That is the whole of it. **Three behaviours, and they differ only in what the creature
walks toward.** Once a target is chosen, steps 5–11 are identical for a Wolf, an Ogre, a
Goblin Warrior and an Archmage.

### 2a. How the 73 divide

The gauntlet does not draw from all 330 monsters, nor from the 217 at CR ≤ 4. It draws
from what `MonsterPool.Draw` admits at the `Playable` coverage floor, after the
plausibility and genre cuts: **73 creatures** at `4c806a3`.

| Branch | Creatures in the drawable pool |
| --- | --- |
| Pack Tactics — flank what a packmate has already engaged | **11** |
| Intelligence ≥ 8 — converge on the shared kill | **28** |
| Greedy — weakest in reach, else nearest | **34** |

Three behaviours across seventy-three creatures, selected by one ability score and one
trait name.

## 3. What every creature in the pool does identically

Each of these was checked in the code, not inferred from the absence of a feature.

**It never Dodges, Disengages, retreats, or flees.** No such call exists in the policy.
Filed as [#314](https://github.com/brandonifco/SRD_Combat/issues/314), which scopes the
`ITacticsPolicy` seam and then those three actions "where measurement says they pay."

**It never withdraws from melee.**
[`ValueAt`](../src/SRDCombat.Core/Combat/SimpleTacticsPolicy.cs) discounts an attack for
being at *long range* and for the target's damage responses. It does not price the
close-combat Disadvantage that a ranged attack takes within 5 feet of an able enemy. So an
archer dragged into melee ranks its bow at full value and keeps shooting at Disadvantage,
rather than stepping back or switching to a blade. `WouldRatherClose` exists; there is no
`WouldRatherWithdraw`.

**It has no self-preservation and no morale.** `IsBadlyHurt` exists and is consulted only
for characters, for Rage and healing. A monster at 1 hit point fights exactly as it did at
full. Nothing protects a leader, nothing breaks, nothing runs.

**It never uses its Reaction for anything but an Opportunity Attack.** `Encounter.Entries`
refuses Reaction entries outright — the code comment is explicit: *"Reactions, Legendary
Actions and Traits stay refused."* The corpus holds 24 Reaction entries; **6 creatures in
the drawable pool own one**, and none of those six can ever use it.

**Legendary Actions do not arise.** 82 legendary entries exist in the corpus and none fire
— but **0 of the 73 drawable creatures has one**, so this costs the gauntlet nothing
today. It is a CR-5-and-up concern, filed under
[#413](https://github.com/brandonifco/SRD_Combat/issues/413) with the rest.

**Trait auras never fire**, also #413.

**Three traits execute in total.** `MonsterTraitRegistry` holds Pack Tactics, Magic
Resistance and Flyby, and nothing else. Across the drawable pool's **47 Trait entries**,
that covers **11** Pack Tactics, **1** Magic Resistance and **4** Flyby. And Flyby is only
a *permission* — it exempts the creature from provoking — which nothing in the policy
exploits: no creature hit-and-runs, because `MoveTowards` only ever moves closer.

**Positioning means "get closer," with two narrow exceptions.** `ImproveFiringPosition`
sidesteps to clear cover *on the creature's own shot*, and only when the sidestep provokes
nothing. `MoveTowards` scores candidate squares with `Shelter` as a low tiebreak beneath
provoked damage, own-shot penalty and distance. Nothing takes cover deliberately, holds a
chokepoint, guards a corridor, or uses the terrain that battlefield S1 and S2 just built.

**Bonus Actions are thin.** 11 of the 73 have a Bonus Action entry; corpus-wide, 60 of 76
Bonus Action entries are `Unmodelled`.

## 4. What it does do, and does well

Stated plainly, because an audit that only lists absences is not an audit.

- **Focus fire.** `NearestEnemy` takes the weakest enemy *already in reach* rather than the
  nearest. Its doc comment records why: four combatants spreading damage across five
  enemies kill none and take five creatures' worth of attacks back. Deliberately narrow —
  it does not chase a wounded enemy across the field.
- **It closes when closing pays.** `WouldRatherClose` compares the same figure `TryAttack`
  sorts on, so a Fighter walks in behind a Greataxe instead of lobbing a Javelin forever.
  That one fix moved full clears from 2 of 120 back to 38.
- **It prices spells against the swing they replace** rather than casting as a last resort,
  and skips an area entry whose shape would catch its own side.
- **It is fully deterministic.** Every tie breaks on an explicit ordering — identifier
  last — because the frozen transcripts rest on it. Any future policy inherits that
  constraint absolutely.
- **It cannot stall.** `FinishTheDowned` is the road out of the two-Basilisk stalemate, and
  the current pacing baseline reports zero `Stalled` on both canonical seed ranges.

## 5. Where the decisions live

| Decision | File |
| --- | --- |
| The whole turn | `src/SRDCombat.Core/Combat/SimpleTacticsPolicy.cs` |
| Which enemy a monster hunts | `src/SRDCombat.Core/Combat/MonsterDoctrine.cs` |
| Which enemy a character hunts | `src/SRDCombat.Core/Combat/PartyDoctrine.cs` |
| Which traits exist at all | `src/SRDCombat.Core/Rules/MonsterTraitRegistry.cs` |
| Which creatures can be drawn | `src/SRDCombat.Core/Rules/MonsterPool.cs` |
| Whether an entry may be used | `src/SRDCombat.Core/Combat/Encounter.Entries.cs` |

## 6. The open questions

Questions, not proposals — per the mandate.

1. **Is role the right axis?** The pool has no notion of archer, brute, skirmisher, lurker,
   leader or controller, and a creature's printed stat block is where such a thing would
   have to be derived from or curated against. Deriving it invites bug 2 from CLAUDE.md's
   list — the "does this look mechanical?" keyword filter that let Flyby through as inert.
   Curating it invites a 73-entry table maintained by hand. Neither is obviously right.
2. **What should the six Reaction-owning creatures do?** Executing reaction triggers is
   #413's scope; whether the policy should then *choose* among reactions is a separate
   question this audit does not answer.
3. **Should anything flee?** Morale is a rule the SRD does not print, so it would be this
   project's design, stated as such — the same shape as `LootTable`'s award rate.
4. **How much of "positioning" is terrain?** S1 and S2 have just given the board structure
   worth standing behind. Whether the policy should read it, or whether that is the
   deployment-formation slice's job (#438), is undecided.
5. **What is the measurement story?** #314's `ITacticsPolicy` seam exists precisely so two
   policies can be A/B'd on the same seeds. Nothing here can be judged without it — and the
   pacing baseline moved on 2026-08-26 to 32 of 120 clearing all thirty, a difficulty shift
   Brandon has not yet ruled on. **AI work lands on top of an unsettled baseline**, which is
   a sequencing fact, not a technical one.

## 7. How the numbers were taken

Corpus-wide counts: a census over `data/srd/monsters.json`, grouping `entries` by
`section` and `mechanics`.

Pool counts: `MonsterPool.Draw(Content.Monsters, 4m)` with the default `Playable` floor and
both cuts on — i.e. exactly what `EncounterBuilder` is handed — then counted by branch, by
`Trait` name, and by `section` presence. Written as a temporary test inside
`SRDCombat.Content.Tests` so it ran against the real loaded corpus rather than a
re-implementation of the gate, and deleted afterwards.

Result, for the record:

```
pool=73 pack=11 tactical=28 greedy=34 reaction=6 bonus=11 legendary=0
magres=1 flyby=4 traits=47
```

The pool figure agrees with `MonsterPoolTests`' floor of 73, which is the expected
coincidence: the floor was ratcheted to the measured count on 2026-08-25.
