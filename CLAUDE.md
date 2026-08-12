# SRD_Combat

A turn-based tactical combat game on **SRD 5.2.1** (2024 rules, CC-BY-4.0). Party of
four, levels 1–5, full tactical grid, persistent gauntlet ladder of escalating fights
with XP, levelling, and loot. Combat only — no exploration, dialogue, or travel.

**Read [`docs/2026-08-11-design-and-development-plan.md`](docs/2026-08-11-design-and-development-plan.md) first.**
It is the governing design document: the kickoff decisions, the architecture and why
it diverges from `5eGoldBox`, the content pipeline, the phase plan, and the open
questions. Everything below is operational detail that doc doesn't carry.

## Current state — read this first

**As of 2026-08-12.** All numbers here are verified, not estimated.

| | |
| --- | --- |
| Branch | `main` at PR #77 (aquatic foes, closing #75) |
| Tests | **570 passing**, 1 skipped by design (the transcript fixture writer) |
| Build | Debug and Release, **0 warnings** (`TreatWarningsAsErrors`) |
| Content | 330 monsters · 339 spells · 12 classes · 9 species · 4 backgrounds · 38 weapons · 13 armor · **258 magic items** (13 names executed; the rest counted) |
| Work remaining | **No open GitHub issues.** The next work is a phase, not a fix — see the plan doc. |

**What works today.** A fight runs end to end, headless. Grid movement, initiative, the
action economy, attacks, damage, death saves and opportunity attacks. Characters resolve
from real content — species, class, background, levels 1–5 — and fight alongside
monsters, with sixteen implemented class features and working spellcasting (attack spells,
save spells with areas, slots, Concentration, and **healing**). A wolf's bite knocks a Medium creature
Prone and a Huge one not, a Giant Centipede's poison lasts until the start of the
centipede's next turn and no longer, a Giant Frog's grapple holds a bandit until it
rolls Acrobatics against the printed escape DC, an Ape throws its Rock once and then
waits on the recharge die, an Ankheg's Acid Spray fills its printed 30-foot Line and
makes everyone caught roll against DC 12, a Wolf bites with Advantage while its
packmate stands beside the target, a Sprite's arrow leaves its victim unable to shoot
back at the Sprite, and an Otyugh's Tentacle Slam stuns until the start of the Otyugh's
next turn — while a Ghoul's paralysis stays where the book put it, behind an embedded
save the model does not express. All from the stat blocks' own words. A frozen
transcript pins one whole eight-round fight byte-for-byte.

**A whole run is playable.** `dotnet run --project src/SRDCombat.Console` climbs a
thirty-fight gauntlet, each rung **built to the SRD's printed XP budget**, with wounds,
spent resources and the dead carried between fights, rests restoring exactly what the
printed rules say, **levels earned by experience rather than handed out on a schedule**,
and a fallen character rejoining at the next Long Rest.
`--seed <n>` makes a fight reproducible, which is a complete bug repro. The run is
**persistent**: it autosaves after every cleared fight, `--continue` resumes it, and
defeat means reload rather than reset — the save deliberately keeps the state after the
last fight the party *won*. **Each High milestone cleared drops one magic item** —
chosen from what would actually improve somebody, equipped by re-resolving the finder's
draft, riding the save for free because a draft is what a save holds — and **each
Moderate rung drops a Potion of Healing**, handed to whoever carries the fewest. The client is
deliberately thin — it calls the engine's public actions and prints `CombatStep.Narration`,
**recomputing no rule**, and it shows a refusal *with its code* rather than swallowing it.

**Automated runs still lose, but they get much further than they did.** Measured over the
same 40 seeds throughout: the old every-third-fight-is-High ladder cleared a median of
**2.5** with 23 of 40 deaths on a High rung; #65's milestone shape — four routine fights
alternating Low and Moderate, then a High set piece entered fresh off a Long Rest — took
that to **4** with only 7 of 40 deaths on High; and **potions took it to 7.5, best 29**,
which is the largest single jump anything has produced. **68 of the 110 potions used were
administered to a fallen ally**, so it is the cheap way back off the floor doing the work
rather than the self-heal. **Nothing has yet cleared all thirty**, and every figure here is
a floor rather than a verdict, because `SimpleTacticsPolicy` is playing the party.

**What does not exist yet.** `SimpleTacticsPolicy` is still a placeholder, but no longer a
naive one: it focuses fire on the weakest enemy already in reach, heals a fallen ally,
rages, spends Second Wind, drinks and administers potions, casts when its weapon cannot
reach, and reaches for a
limited-use entry — a thrown Rock, a breath weapon — when nothing else does, never one
whose area would catch its own side.

**Picking up cold:** `gh issue list` is the work queue, and the order below is not the
order the issues were filed in. Take the top of it.

### Starting on a machine for the first time

Everything needed to build, test and play is committed. There is no setup step, no
content to generate and no asset to fetch:

```bash
git clone https://github.com/brandonifco/SRD_Combat.git && cd SRD_Combat
dotnet test SRDCombat.sln -c Debug     # expect 570 passing, 1 skipped by design
dotnet run --project src/SRDCombat.Console
```

`data/srd` is in the repo, which is why none of that needs the SRD PDF. **The one thing
that does is re-extracting content** (`tools/SrdExtract`), and the PDF is deliberately not
in the repo and never will be — see the Environment section. If you are not re-extracting,
its absence costs you nothing.

Two things worth knowing before the first commit on a new machine: **CI installs .NET
8.0.x while your machine probably runs something newer** through `global.json`'s
roll-forward, so a green local build is not a green CI build (#27 is the standing example);
and the conventions at the bottom of this file are not optional — narrow branches, a PR
per concern, and the gate before merge.

### What is open now

**Nothing.** The issue queue is empty for the first time since it was opened, so
`gh issue list` will not tell you what to do next — the plan doc's phase list will.
Phases 1–4 are done, Phase 6 is done for monsters and open-ended for the party, and
Phases 5 and 7 have never been started. **Do not read an empty queue as "the project is
finished":** nobody has ever cleared the thirty-rung ladder, and no human has played more
than a few rungs by hand.

**A caution before tuning anything against numbers:** the party in an automated run is
played by `SimpleTacticsPolicy`. It uses features, spells and focus fire now, but it is
still a placeholder, so every pacing figure in this file is a floor rather than a verdict.

### How the rules backlog was done, and why in that order

Every item below is **closed**. It is kept because the reasoning is the expensive part —
each entry records what the work turned out to rest on, and several correct a thing memory
got wrong. Ordered by dependency rather than by how valuable each looked on its own.

1. **#15 condition durations — done.** The condition record took an expiry *and* the
   combatant who imposed it in one pass, so #16 does not reopen it. Worth knowing why
   Poisoned joined the allowlist in the same branch: of the fifteen riders whose duration
   became modellable, only one was on a condition the engine executes, so **the clock
   would have shipped with nothing running on it**. Eleven were Poisoned, and Poisoned is
   five lines in `AttackRules`.
2. **#16 Grappled and Restrained — done.** Nine riders started landing. Reading the
   printed rules corrected two things memory had wrong: Grappled is Disadvantage only
   **against targets other than the grappler**, and there is **no generic Escape action**
   — escaping is a Strength (Athletics) or Dexterity (Acrobatics) check against a flat DC.
3. **#19 monster entry actions, with #8 recharge — done, in one branch as argued.**
   `Encounter.UseEntry` resolves a named Action entry or refuses with a named code, and
   one `UsageState` per combatant gates every path by entry name. Two shapes needed the
   gate in two places: the Ape's Rock (Recharge 6) is locked *out* of its Fist
   Multiattack, so `UseEntry` is its only road, while the Minotaur's Gore (Recharge 5)
   is a plain attack the `Attack` path had to gate. The tactics policy reaches only for
   **limited-use** entries — the other locked-out attacks are the lycanthropes'
   form-gated ones, and choosing one would silently decide the creature's form.
4. **#6 saving-throw effects — done.** One loop (`Encounter.ResolveSaveEffect`) now
   resolves both a spell's save and an entry's, and the riders are a *parameter*: an
   entry imposes every rider the engine executes, a spell still passes none, so sharing
   the loop changed no spell behaviour. Three things doing it decided: a Line no longer
   covers its own origin square (the same exclusion `InCone` always made — a breath
   weapon caught its breather); a Grappled rider from a save carries **no range**, so an
   engulf-style grapple ends only by escape or the grappler's incapacity; and the save
   path now sweeps `EndBrokenGrapples`, which the spell path had silently never done.
   Whether an **Emanation** includes its origin was left *unverified against print* here
   and is now settled — see #29 below; the reading the engine shipped with was wrong. Of
   the follow-ons that slotted around it, **#21 (execute Blinded, Charmed, Frightened,
   Paralyzed, Stunned) is done** — the conditions section below carries what the
   glossary corrected — **#22 (timed durations) is done**: "for 1 minute" is ten of
   the bearer's turns on the same clock, "for 1 hour" outlasts the fight, and the
   Failure-tier rule below decides which printed timers may actually ride — and **#24
   ("until the grapple ends") is done**: a two-condition sentence splits into one
   clause per rider, the tied condition lives and dies with its sibling grapple, and
   the companion-clause rule below is what the split made necessary. Every follow-on
   to step 1 is closed.
5. **#9 passive monster traits — done for what the engine can express.**
   `MonsterTraitRegistry` is the fourth curated allowlist: a printed *trait name* maps to
   an executed effect only alongside the code. Three landed — Pack Tactics ×18 (ally
   able to fight within 5 feet of the target, Opportunity Attacks included), Magic
   Resistance ×7 (**spells only** — a stat block's save entry is read as not magical,
   the reading is on the registry), Flyby ×7 (no movement modes exist, so a Flyby
   creature is read as always flying). Spider Climb, Swarm, Sunlight Sensitivity et al.
   stay deliberately absent — each needs a model (verticality, space-sharing, light)
   that does not exist; Undead Fortitude is the best next one, needing only a hook where
   damage would drop the creature. The registry works off entry *names*, so content
   counted these entries `Unmodelled` until #28 reclassified them `Passive` and regenerated.
6. **#10 class features — done for what needs no new machinery.** Danger Sense
   (Advantage on Dexterity saves, folded into the shared save loop beside Magic
   Resistance), Fast Movement (+10 feet derived in `CharacterResolver`, gated on Heavy
   armour), and Steady Aim (a Bonus Action; "haven't moved" is read as "has spent no
   movement", so standing up counts, and forfeited Speed stays 0 through a later Dash).
   The rest were refiled as #32 and are **now done bar one**: Fighting Style (Archery,
   Defense) and Expertise ride the draft choices described below, Cunning Strike's Trip
   executes, and Tactical Mind hooks the one ability check a fight rolls. **Favored
   Enemy stays blocked** on a Hunter's Mark effect the spell grammar does not model, and
   the sheet keeps reporting it. Worth correcting the issue's own premise: **Cunning
   Strike's Poison did *not* become implementable when #22 landed** — it prints "for 1
   minute" *and* "the Poisoned target repeats the save", and the repeated save is an
   early out the condition model still cannot express, so imposing it would hold a
   target for a minute the book lets them escape. It also needs a Poisoner's Kit, which
   no inventory models.
7. **#29 the Emanation's origin — done, and the engine was wrong.** The glossary is
   explicit: "An Emanation's origin (creature or object) isn't included in the area of
   effect unless its creator decides otherwise" (printed page 181). The engine had
   covered the origin square for 21 monster Emanation entries and every emanation spell.
   **This is the one geometry rule in `AreaTargeting` that is printed rather than
   interpreted** — and it went unverified for two slices only because the source PDF was
   off the machine, which is the argument for checking the book the moment it is
   reachable. A Sphere deliberately keeps its centre: it is centred on a point, not
   extending from a creature.
8. **#11 curate the monster pool — done, last as argued.** It is **derived, not
   hand-written**: `MonsterPool` grades each stat block from the content's own
   `IsFullyModelled` accounting, so implementing a trait enlarges the pool at the next
   regeneration with nothing to edit. The grade turns on **where** the gap is, not how
   many there are — `Complete` (nothing unmodelled), `Playable` (every *Action* entry
   modelled, something outside them not), `Diminished` (an action loses part of its
   printed text — the Boar's Gore without its charge), `Unusable` (no action the engine
   can resolve). **Admission is Playable or better**: the creature's whole turn is
   exactly what the block prints. Tier-1 today is **131 monsters, at least five at every
   CR from 0 to 4**, and the tests assert floors rather than exact counts so good news
   never fails a build. Two CR 0 creatures are `Unusable` and both are faithful readings
   checked against print — the Shrieker Fungus has only a Reaction, the Seahorse only a
   swim action — so `Admits` refuses them at *any* floor.

**Conditions were the most-reopened type in that list** — #6 imposes them on a failed
save, #9 has passives referencing them, #10 has Cunning Strike applying them. That is why
steps 1 and 2 came before anything else, and why they were worth doing as one design.

**The frozen transcript churned exactly once**, when the tactics policy learned to focus
fire, and it caught the right thing when it did: not the byte-for-byte diff but
`TheFightExercisesTheHardParts`, which noticed the adventurers now won quickly enough that
nobody went down and the fight covered no Death Saving Throws at all. Read the diff before
regenerating, every time.

## The rule this project runs on

**Nothing may hold unimplemented rules silently.** A stat block's action entries contain
no flavour text — `it has the Grappled condition (escape DC 13)` is a rule, and calling
it prose only describes the format it is printed in. So:

- Every entry, trait, class feature and spell is **classified**. There is no "just prose"
  state to fall into. `EntryMechanics` is the enum; `IsFullyModelled` is the test.
- Anything the model cannot express lands in `UnmodelledClauses` and is **counted**,
  including on entries that are otherwise structured.
- `Narrative` — "confirmed to do nothing in a fight" — is **only ever set from a curated
  list**, never inferred. Pack Tactics, Sunlight Sensitivity and Flyby all look inert and
  all change how a fight goes.
- An action the engine cannot resolve is **refused with a named code**, not silently
  skipped. See `spell.not_implemented`, `spell.area_not_modelled`.
- Where a rule is a judgement call rather than a derivation, **write the reading down**.
  `AreaTargeting` is the model for this.

**Three bugs produced that rule. Read them before touching a parser:**

1. **The Goblin Warrior's "plus 2 (1d4) damage *if the attack roll had Advantage*"** was
   read as a second unconditional component, so every goblin hit dealt it. Nothing
   failed — the attack *looked* implemented. **A partly-structured entry is more
   dangerous than an unstructured one**, because the missing part is invisible.
2. **A "does this look mechanical?" keyword filter** let Flyby, Nimble Escape and
   Shape-Shift through as inert. The heuristic was **removed rather than tuned**: a
   keyword list will always have false negatives, and here a false negative loses a rule.
3. **Reusing the stat block classifier on spells** read every metadata field correctly
   and found **zero of 300 saving throws** — a monster prints an explicit DC and a
   precomputed average, a spell prints neither. Silent, and visible only because the
   extractor counts what it modelled.

**Whether a condition rider lands is two questions, kept apart on purpose.** *Does the
model express it?* — two qualifiers are modelled, the size gate and a turn-boundary
duration, and anything else printed with the condition (a charge requirement, a pull, a
chained second condition, a duration of another shape) goes to
`AppliedCondition.UnmodelledRequirement` and makes the rider unusable rather than
approximate. *Does the engine execute it?* — `ConditionRules.Executable` is a curated
allowlist, exactly like `ClassFeatureRegistry`, and holds eleven conditions: Prone,
Poisoned, Grappled, Restrained, Incapacitated, Unconscious, Blinded, Charmed, Frightened,
Paralyzed and Stunned. Deliberately absent: Deafened, Invisible and Petrified, each
needing a model (hearing, sight, objects) that does not exist. **Add a condition there
only alongside the code that gives it effects.** Forty-five attacks satisfy both checks —
20 Prone, 12 Poisoned, 9 Grappled, and one each of Charmed, Frightened, Paralyzed and
Incapacitated and 2 Restrained tied to their grapples — and thirty-one failed-save
riders land: 6 Frightened, 5 Grappled, 4 each of Blinded, Poisoned and Restrained,
2 each of Charmed, Prone and Stunned, one each of Incapacitated and Paralyzed. The
Water Elemental's Whelm is still the working example of the per-rider split — its
Grappled lands while its Restrained sentence, which chains suffocation and recurring
damage, is refused — and the Purple Worm's Bite is the counterpart where both halves
ride: one sentence, Grappled plus a Restrained that ends when the grapple does.

**The two questions are independent, and the Phase Spider still proves it — but read its
sentence before citing it.** Its bite poisons "for 1 hour", Poisoned *is* executable, and
the rider still cannot be imposed — not because of the hour (timed durations are
modelled since #22) but because the sentence opens with "If this damage reduces the
target to 0 Hit Points" and chains "While Poisoned, the target also has the Paralyzed
condition": a gate and a chained condition the model has no vocabulary for. Until #22
this entry was described as refused on the duration alone; the gate was always there
too. The Swarm of Ravens is the mirror image: its Cacophony Deafened rider is completely
modelled, duration and all, and refused because the engine does not execute Deafened.
(The Sprite's Charmed held that role until #21; today its rider rides the bow, and the
Sprite is instead the reason `Encounter` knows a Charmed creature cannot attack its
charmer.)

**What the glossary corrected when the five landed, worth not re-learning from memory:**
**Stunned has no Speed 0 and no automatic-crit clause** — memory adds both, the print has
neither; Paralyzed has both, and its crit-within-5-feet clause is the same one Unconscious
carries. **Paralyzed, Stunned and Unconscious all auto-fail Strength and Dexterity saving
throws, and the auto-failure consumes no die** — the clause replaces the roll, which the
scripted-dice tests depend on. **Charmed's clause heading is "Can't Harm the Charmer"**,
so "damaging" is read as qualifying both "abilities" and "magical effects": attacks on
the charmer are refused outright (Opportunity Attacks included — the rule names the
attack, not the action), damaging spells and entries that would catch the charmer are
refused before anything is spent, and non-damaging effects are allowed. **Frightened
rests on two written-down readings**: sight is unmodelled, so the source is always
"within line of sight" while on the field, dead or alive; and "can't willingly move
closer" is judged at the destination square, not along the path. All of it is on
`ConditionRules`' doc comments.

**A rider printed in a "Failure:" sentence carries two extra extraction rules** — the
fourth occurrence of bug 1's shape found them. In a *saving-throw* entry the rider must
state its end within its own sentence: the Quasit's "Failure: The target has the
Frightened condition." puts the way out ("repeats the save at the end of each of its
turns") in the *next* sentence, where sentence-scoped parsing cannot attach it, and
imposing the rider without it would make the condition permanent. And in an *attack*
entry a "Failure:" sentence belongs to an embedded saving throw — the Ghast's Claw rolls
a DC 10 Constitution save gated on "non-Undead creature", both printed in sentences of
their own — so riding the attack with it would paralyze on every hit with no save
rolled. A third rule joined with #22: **a rider behind a deeper failure tier — "Second
Failure: The target has the Unconscious condition for 1 minute" — is refused whatever
its duration**, because the save model rolls one failure and the rider would land a
whole tier early: a wyrmling's breath putting targets to sleep on the first failed save.
That rule is what separates the timers that ride (the Solar's "Blinded for 1 minute",
the Pseudodragon's "Poisoned for 1 hour" — checked by hand against their follow-on
sentences) from the ones that must not (every Sleep Breath). And a fourth rule came
with #24's clause-splitting, caught the same day it was nearly shipped wrong: **a
rider-free head clause must be fully accounted for by the entry's other grammar — a
"Hit:" or "Failure:" damage statement — or every rider in the sentence is refused with
it.** Splitting "the balor pulls the target up to 25 feet straight toward itself, and
the target has the Prone condition" at the comma leaves a clean Prone clause, and
imposing it without the pull fires part of a printed sentence; the Phase Spider's
0-hit-point gate sits in a head clause the same way. The refusals are in
`EntryMechanicsParser`, with the safe direction chosen; duration-less riders in
sentences of their own (the Gladiator's Prone, the Water Elemental's Grappled, the
Otyugh Bite's Poisoned disease) are untouched, because those conditions carry their own
printed way out. A grapple-tied rider is also **only as modelled as its sibling
grapple**: the Chain Devil's "from one of two chains" refuses its Grappled, so the
Restrained that would ride a grapple that can never land is refused with it — and at
runtime `ImposeConditions` re-checks the tie, so a grapple refused by a size gate takes
its dependent down with it there too.

**Durations hang off a turn counter, not a countdown.** An `ActiveCondition` carries who
imposed it and a `ConditionExpiry` — whose turns are counted, which boundary, and at which
turn number, fixed at application as *the owner's count plus `TurnsAhead`*. One is the
whole of "next", and it is why one wording works in both places it appears: applied on the
devil's own turn, or during someone else's on an Opportunity Attack, "until the start of
the devil's next turn" means different moments and needs no special case. **A timed
duration is the same clock set further out**: "for 1 minute" is ten of the *bearer's*
turns ending at an end of turn (`ConditionDuration.ForMinutes`), and "for 1 hour" or
longer is `BeyondTheFight` — imposable, recorded, and expiring with the encounter rather
than being rounded to a number no fight reaches. **"until the grapple ends" is a duration
with no clock at all** (`UntilTheGrappleEnds`): the tied condition is imposed only while
the same creature's grapple holds the target, and `Encounter.EndGrapple` sweeps it away
with the grapple however it ended. All three are stated interpretations on
`ConditionDuration`'s doc comments. **The clock ticks for every creature whose turn comes
round, dead or Unconscious included** — a duration measured against a creature that never
acts again still has to end.

**Read the possessive.** "until the end of *its* next turn" is the creature carrying the
condition; "until the start of *the devil's* next turn" is the creature that imposed it.
Both are common, and swapping them changes the duration by most of a round.

**Two grapple rules that memory gets wrong — both were caught by reading the glossary.**
Grappled is Disadvantage on attack rolls "against any target **other than the grappler**",
not a blanket penalty, so hitting back at whatever has hold of you is the one attack a
grapple does not hamper — and it is the only entry in `AttackCircumstances` that depends
on *who* is being attacked. And **this SRD has no generic Escape action**: escaping is a
Strength (Athletics) *or* Dexterity (Acrobatics) check, the creature's choice, against a
flat DC rather than a contest. A grapple also ends on its own when the grappler is
Incapacitated or dead, or when the two are further apart than the grapple's range —
`Encounter.EndBrokenGrapples` sweeps for all of that, from every point where either could
have changed. A grapple that outlives its grappler is invisible: the victim simply never
moves again.

**When you touch `ConditionRules.Executable`, re-run the extractor.** The entry accounting
calls `CanBeImposed`, so which conditions are executable decides what lands in
`UnmodelledClauses`. Changing the allowlist without regenerating leaves the content
disagreeing with the code, and the symptom is a content test failing on an entry you did
not edit.

Two findings from this work worth not rediscovering. **Gating riders cost coverage** —
342 tier-1 entries down to 322 — because thirteen attacks had read as fully modelled while
their whole entry was one sentence containing `Attack Roll:`, so the accounting matched on
that and the `and the target has the Poisoned condition until ...` on the end was
invisible. Bug 1's exact shape, third occurrence. **And a clock nothing runs on proves
nothing**: of the fifteen riders whose duration became modellable, exactly one sat on a
condition the engine executed, which is why Poisoned went on the allowlist in the same
branch rather than a later one.

**Coverage numbers are an internal check, not project status.** The extractor prints them
so *it* can tell what is left; they do not belong in a status report.

## Working on characters and spells

- **`CharacterResolver` derives everything.** No number on a `CharacterSheet` is stored
  independently of the rules that make it, so AC and armour cannot drift apart. Only
  choices the engine cannot make — how the background's ability increases were spent,
  which skills were taken — come from the draft.
- **Ability increases come from the *background*, not the species.** A 2024 change; a
  species grants no ability scores at all.
- **`ClassFeatureRegistry` is a curated allowlist**, exactly like the extractor's inert
  list. A printed feature name maps to an implemented `ClassFeature` only if the engine
  really does the thing. **Add a name here only alongside the code that implements it** —
  everything absent is reported on `CharacterSheet.UnimplementedFeatures` and stays
  visible. Two printed names may map to *one* feature when they are the same rule: the
  Rogue's `Expertise` and the Ranger's `Deft Explorer` both grant `ClassFeature.Expertise`.
- **A feature that spends a resource on a *conditional* success must roll before it
  spends.** Tactical Mind adds 1d10 to a failed ability check and "if the check still
  fails, this use of Second Wind isn't expended" — so the die is rolled, the total
  compared, and only then is the use decremented. It hooks `Encounter.Escape`, the one
  ability check a fight rolls; any future check should call it too.
- **Cunning Strike pays in dice removed *before* rolling**, never deducted from the
  total afterwards — a spent die must never be rolled and never doubled by a Critical
  Hit. Only Trip is executed, and it reads its size gate before calling for the save,
  because the printed sentence puts the gate first: a Huge target is never asked to roll
  rather than rolling and being filtered. `ScriptedRandomSource` caught that as a
  surplus die, which is exactly what it is for.
- **The draft carries the choices the rules cannot make, and the resolver refuses ones
  the character was never granted.** `FightingStyle` and `ExpertiseSkills` are the first
  two; both are validated against the *granted features*, not the class name, so the
  Rogue's two picks at level 1 and the Ranger's one from Deft Explorer need no special
  case. `FightingStyle.Unspecified` is the honest default — a character may have taken a
  printed style the engine does not execute (Great Weapon Fighting, Two-Weapon Fighting),
  and the feature then stays reported as unimplemented rather than silently doing nothing.
- **Casting works.** Attack spells roll a spell attack against AC; save spells make
  every creature in the area roll against the caster's DC, halving on a success. Slots
  are spent (cantrips are free), Concentration is tracked and broken by damage, and a
  spell whose effect is not modelled is **refused with a reason** rather than silently
  doing nothing.
- **Area geometry is a stated interpretation, not a derivation — with one exception.**
  The SRD describes areas for a table with a ruler; `AreaTargeting` documents how each
  becomes squares. Cylinder is not modelled and a spell using one is refused. The
  exception is the **Emanation's excluded origin square, which is printed** (glossary,
  page 181) — the Cone's and Line's exclusions are the inferred ones. Do not "tidy" the
  three into one rule: they agree today by different authority.
- **`SpellcastingRules.AbilityFor` is a curated map, not Primary Ability.** A Paladin's
  primary abilities are Strength *and* Charisma and it casts on Charisma — reading it
  from the Core Traits table would be right for six classes and quietly wrong for two.
- **Spells need their own effect grammar, not the stat block one** — see bug 3 above.
  `SpellEffectParser`, not `EntryMechanicsParser`.
- **A spell has three effect shapes: an attack roll, a saving throw, and healing.**
  Healing was missing until 2026-08-12 and its absence was not small — with nothing able
  to restore hit points, a character who dropped was gone for good and a run died out
  within a few fights however easy they were. Only **single-target** healing is modelled;
  the mass spells say "choose up to six creatures", which is a chosen set rather than an
  area and needs a casting call taking several targets, so they stay `Unmodelled` and
  counted rather than being approximated as healing one creature of six. Healing a
  character at 0 hit points brings them back up for free, because
  `Combatant.RegainHitPoints` already clears the dying state, the Death Saving Throws and
  Unconscious.
- **Extra Attack and Multiattack are the same rule to the engine**: the Attack action
  buys several attacks rather than several actions. `CombatantStats.AttacksPerAction`
  resolves both. Modelling them as extra actions would also wrongly allow a second Dodge
  or Dash.
- **A Multiattack constrains which attacks it is made of.** `AllowsInMultiattack` refuses
  a swing the stat block does not license, and a Multiattack naming an attack the
  creature does not have is **dropped entirely** rather than granting phantom swings.
- **Magic items are the fifth curated allowlist.** The whole A–Z chapter (printed pages
  209–253, 258 entries — the count is asserted exactly, cross-checked independently) is
  extracted with name, category, rarity, variants and attunement; `MagicItemRegistry`
  maps a printed name to executed powers **only alongside the code that does the thing**,
  and the resolver *refuses* a draft equipping anything unregistered — a worn item doing
  nothing would be an unimplemented rule holding silently. Thirteen names execute:
  +1/+2/+3 weapons, armor and Shields, Ring and Cloak of Protection, Bracers of Defense,
  Wand of the War Mage, the three ability-setters (a **floor**, not a bonus — "Your
  Strength is 19"), Adamantine Armor (crits demoted in `AttackRules`), Vicious Weapon
  and Elven Chain. Attunement is enforced from print — **no more than three, no
  duplicate copies** — and read as happening at the rest between fights. Two readings
  are on the registry's doc comments: the Wand's "ignore Half Cover" is vacuous while no
  cover model exists, and Elven Chain's training override is satisfied by construction
  because armour training is not modelled.
- **A potion is the one thing a fight spends that no rest brings back**, which is why it
  lives on `CharacterState` beside the resources rather than on the draft beside the
  choices — and why `InventoryState` is a sibling of `FeatureState` rather than a field on
  it. `PotionRules` is a **curated rules map, not extracted content**, deliberately: the
  chapter prints one entry ("Potions of Healing", type line "Potion, Rarity Varies") whose
  four potencies live in a table inside its body text, and a body-text table grammar with
  one customer is worse than a transcription checked against print. Drinking and
  **administering cost the same Bonus Action** (printed page 204), which is the whole
  point — one Bonus Action puts an Unconscious ally back up without touching your Action,
  and it is what moved the median run from 4 fights to 7.5. Reach is a *stated reading*:
  the SRD sets no range on administering, and this engine requires 5 feet. Every refusal
  fires **before the potion is spent**, because a consumable poured onto a corpse cannot
  be given back the way a mis-declared Action can.
- **Loot rates are this project's design; the items are the book's.** The SRD prints no
  award-rate table ("Adventures hold the promise—but not a guarantee—of finding magic
  items"), so `LootTable` states the choice: one permanent item after each High
  milestone, rarity gated by the finder's level (Uncommon always, Rare at 3+, nothing
  dearer in a game that ends at level 5), drawn only from candidates that would improve
  somebody — no Headband of Intellect, because nobody in this party casts on
  Intelligence. A +N item already owned upgrades in place; **one enchantment per worn
  suit of armour**, because "+1 Armor" and "Adamantine Armor" are different suits and
  the model has one body to put a suit on. Equipping is a draft change and a re-resolve
  — never a sheet edit — so found gear rides the save for free and cannot drift.

## Extraction traps — read before parsing another SRD chapter

Every one of these failed **silently** and was caught by a validator or by checking
output against the book, never by the parser complaining.

- **Typeface differs by chapter.** The player-facing chapters use **Cambria**; the
  bestiary uses **Optima**. Match the *style suffix* (`BoldItalic`), not the whole font
  name. The first origins run produced nine species with zero traits between them.
- **Weight differs within a table.** Core Traits keys are semi-bold and their wrapped
  values are lighter. Matching only the bold face truncated the Barbarian's six-skill
  list to one. Match the family (`GillSans`), not the face.
- **A class page mixes two layouts** — two-column body plus a full-width table at the
  bottom. `ClassParser` reads each page twice for exactly this reason.
- **Don't split key from value on a gap.** `Weapon Proficiencies` overflows its column,
  so its value starts after an ordinary word gap; the split was missed and the row was
  swallowed into the list above. Match against the closed set of known keys instead.
- **Table header columns are 12pt+ apart; words within a column are 2–5pt.** The margin
  is narrower than it looks — a 20pt threshold merged the Cleric's `Level` and `Bonus`.
- **Not every caster uses the same table.** The Warlock has `Spell Slots`/`Slot Level`
  columns, not nine per-level ones, and must not be forced into the common shape.

**The general lesson: write the validator that asserts the shape of what should have
been found.** Every one of these was caught that way — "every species has at least one
trait", "every class table has 20 rows with the advancement table's proficiency bonus".

**And the one place that lesson was never applied is where the next bug was waiting.**
There was no validator on the spell count, so the extractor dropped **39 of the book's
339 spells from Phase 0 until 2026-08-12** — Cure Wounds, Detect Magic, Hold Person and
Aid among them — while reporting "300 spells" as though that settled it. Two causes, both
already warned about elsewhere in this file:

- **38 spells whose class list wraps.** `Level 1 Abjuration (Bard, Cleric, Druid,
  Paladin,` / `Ranger)`. The type grammar was anchored on its closing bracket, so a
  wrapped line matched nothing and the spell was never detected at all — and **a spell
  that is never detected raises no diagnostic**, which is why it was silent. Wrapped
  lines are now rejoined before parsing.
- **Acid Splash**, the one spell heading set in `GillSans-SemiBold-SC700`. Small caps
  reach the text layer as `Ac i d Sp lASh`, letters split and case scrambled. Repaired
  from a curated one-entry map keyed on that exact text, so a better reader stops
  matching rather than being silently overridden.

**Two lessons worth more than the fix.** *A number the pipeline prints about itself is
not a check* — it agrees with the code by construction. And *a floor is the wrong shape
for a count fixed by the source*: the test read `Spells.Count >= 300` for months and was
satisfied by exactly the broken number. Floors belong on things that should grow as the
engine models more, like the monster pool; the book's spell count is not one of them.
`SpellValidator.ExpectedSpellCount` now asserts it exactly.

Decided at kickoff and no longer open: **six launch classes** (Fighter, Rogue, Cleric,
Wizard, Barbarian, Ranger — they cover every mechanical shape the engine must handle)
and **no code licence for now** (public repo, no `LICENSE`, all rights reserved by
default — deliberate).

## Working on the combat engine

- **The frozen transcript is the most valuable test here.** It pins the exact narrated
  sequence of a whole fight, so it catches interaction bugs no unit test reaches. When
  it fails, **read the diff before touching the fixture** — a change to the transcript
  is a change to how the game plays. Regenerate only once the new behaviour is intended:
  un-skip `TranscriptWriter`, run it, re-skip it, review. It churned once, when the policy
  learned to focus fire, and it earned its keep doing so: the failure was not the
  byte-for-byte diff but `TheFightExercisesTheHardParts`, which noticed the adventurers
  now won quickly enough that **nobody went down and the fight covered no Death Saving
  Throws at all**. The composition was kept and the seed moved to one that still reaches
  them — the seed is chosen for coverage, and `SkirmishScenario` says so.
- **It uses hand-authored combatants, not SRD monsters, on purpose** — so it fails when
  the *engine* changes, not when content is re-extracted. `RealMonsterCombatTests` in
  `SRDCombat.Content.Tests` covers the other direction, including a smoke test that
  every CR 0–4 monster can take a turn without throwing.
- **A creature at 0 hit points still occupies its square.** Reading occupancy as "active"
  let a mover end its turn standing on an unconscious creature, which was invisible until
  healing existed — the downed creature then stood up *inside* someone else and the next
  path find threw on two combatants in one square, taking down a whole run mid-fight. Two
  of sixty seeded runs crashed. `MovementRules.FindPath` now treats anyone not dead as
  occupying, and keys its blockers as a lookup so that a duplicated square is survivable
  rather than fatal whatever produces it.
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

Things worth knowing before touching the engine or the content pipeline. The list has
outgrown the phase it was written for; each entry is here because getting it wrong once
cost real time:

- **There is no versioned DTO mirror, deliberately.** Content serializes straight from
  the `Core` definitions. The design doc explains why this diverges from 5eGoldBox, and
  what guards replace the mirror. Don't "restore" it without reading that section.
- **Most monster prose is mechanics now.** Attacks, Multiattack, usage limits,
  saving-throw effects, the gated riders and the registry's passive traits all
  execute, and since #28 the accounting agrees with the engine: an imposable rider on
  a save entry is credited, and a registry-implemented trait is
  `EntryMechanics.Passive` rather than counted. What remains text on
  `MonsterEntry.Text` is in `UnmodelledClauses`, never silently held.
- **Encounter building is three published steps, split across three types.** Choose a
  difficulty (the caller's), `EncounterBudget` cross-references printed page 202 and
  multiplies by party size, `EncounterBuilder` spends it, `EncounterFactory` places the
  result. **`MonsterPool` decides what may go in the bag; the budget decides how much.**
  Keep them apart — coverage is not difficulty, and nothing in the pool weights an
  encounter. **The XP spent is the creature's *printed* value, not one derived from its
  CR**, because step 3 says "every creature has an XP value in its stat block"; the two
  disagree once (the Archmage) and the printed number wins.
- **Three encounter interpretations the page does not settle, all stated in code.**
  *How many creatures:* the SRD caps nothing, and every extra monster is another whole
  turn of attacks each round, so `EncounterBuilder.MaximumFor` allows one more creature
  than there are characters. *Which creatures:* **the count is chosen before them**, and
  each slot is filled from the dearer end of what costs between half its share and all of
  it. Both bounds earn their place — a floor alone produces a swarm of rats, a ceiling
  alone produces a single monster every time, and the first version had neither, picking
  uniformly among everything affordable. That sounds even-handed and is not: a cheap
  creature is affordable at every step, so a low-difficulty fight for four level 1
  characters came to **5.4 creatures, hitting the cap a quarter of the time**. It is 3.0
  now, and reads like the book's own examples. *Placement:* **the sides start 30 feet
  apart**, the number deciding whether ranged attacks and breath weapons matter at all.
- **Rests differ per feature, so restoring them is a table and not a reset.** Verified
  against print: Rage and Second Wind each return **one** use on a Short Rest and all on
  a Long; Action Surge returns whole on **either**; spell slots on a Long Rest only. And
  a 2024 change worth not re-learning — **a Long Rest restores *all* spent Hit Point
  Dice**, where earlier editions returned half. `RestRules` holds each with its citation.
- **Both rests need a hit point to start**, so a downed character cannot rest their way
  back. That would strand a party, which is why the stated reading of "a Stable creature
  regains 1 Hit Point after 1d4 hours" is that **the gap between two rungs is at least
  four hours** — a survivor who went down is conscious at 1 hit point when the next
  fight begins.
- **The one link in the advancement chain the SRD does not print is the award.** It
  publishes the thresholds and each monster's worth, and for the step between says only
  that experience is "awarded by the Game Master". `ExperienceRules` states the reading —
  **a defeated monster's printed XP is split evenly among the characters who fought** —
  and the argument for it is checkable: it makes the two published tables agree, since
  dividing a fully-spent encounter by the party size returns exactly the per-character
  figure the budget table printed. There is a test asserting that at every level and
  difficulty.
- **Levelling is re-resolving the draft at the new level**, never editing a sheet, so a
  levelled character cannot hold a number that disagrees with the rules that made it. The
  new level's hit points arrive as a bigger *maximum* — damage already taken stays taken,
  which is all "your Hit Point maximum increases" promises. **Characters level
  individually**, because a party diverges the moment somebody dies and stops earning,
  which is also why `EncounterBudget.ForLevels` sums each character's own figure.
- **A rung names no level.** It used to, and that meant the ladder *granted* levels on a
  schedule; the ladder now says only how hard a fight should be, and experience decides
  how strong the party is when it arrives.
- **A run owns its state; the engine owns the fight.** `GauntletRun` seeds fresh
  combatants from `CharacterState` through `CombatantCarryOver` and reads them back when
  the fight ends. Nothing about a run leaks into `Encounter`, which stays one
  self-contained fight — exactly what the frozen transcripts need it to be.
- **Coverage is not appropriateness either — that is `PlausibleFoes`, the third axis.**
  The builder used to field a Camel: mechanically `Complete`, narratively absurd. **Most
  of the fix is derived rather than judged**, which is why it is worth knowing about: the
  Equipment chapter's *Mounts and Other Animals* table (printed page 100) prices eight
  animals with a carrying capacity and a cost in gold, and says a mount's "primary purpose
  is to carry gear" — the SRD naming its own equipment. Only Cat and Goat are a judgement,
  and the reading is on the class. Nothing else is excluded on temperament, deliberately:
  **a weak wild animal is a poor fight, not an absurd one**, so the Rat, the Raven and the
  Deer stay in and this never becomes a model of which animals are cross. Elephant and
  Mastiff were argued over and left excluded because the line is the printed table rather
  than a per-animal debate, and **excluding them costs nothing since this governs only the
  random draw** — `MonsterPool.Draw` takes `plausibleFoesOnly: false`, and
  `EncounterBuilder` takes any authored sequence. Names are matched **exactly**: a Giant
  Goat is a wild charging creature and a substring test would take it out with the farm
  animal. The guard that the list cannot outlive a renamed stat block is a content test,
  **not** `MonsterValidator` — that validates whatever list it is handed, single stat
  blocks included, so a whole-corpus check there fails on every partial list.
- **The second exclusion is a derived rule, and the obvious version of it is wrong.**
  A creature with nowhere to fight — a Killer Whale on dry land — is caught by
  `PlausibleFoes.IsAquatic`: **a token land speed (≤ 5 feet), a Swim speed, and no other
  movement mode.** All three clauses earn their place, and the middle one is the whole
  lesson: "walks 5 feet or less" *alone* also catches the Bat, the Owl, the Animated
  Flying Sword, the Swarm of Bats, the Will-o'-Wisp, the Ghost, the Wraith and both
  Fungi. A token land speed says only "not on foot"; what makes a creature aquatic is
  that swimming is the only thing it has **instead**. Checked against all 330 before
  being trusted — it catches exactly nine, with no false positive or negative — and the
  boundary is the book's own, since the nearest creatures on the other side (Merfolk,
  Merrow, Aboleth, Giant Octopus) all walk 10. **The pool went 131 → 123 → 116**, every
  CR band still above its floor. This one is an exclusion for want of anywhere to put
  them: a battlefield with water would make all nine playable and delete this rule.

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

**Everything in this section describes one particular machine, and this project is
developed on more than one.** Treat it as a record of what was true where it was written,
not as a description of the machine you are on. Nothing below is needed to build, test or
play — see "Starting on a machine for the first time" — so a mismatch is a nuisance rather
than a blocker.

- **Which .NET runs has flipped three times.** Snap-confined at kickoff, apt-only SDK 8 at
  PR #30, snap again as of 2026-08-12. Where it was written, bare `dotnet` resolved
  through `/usr/local/bin/dotnet` to the snap, carrying SDKs 8.0.129 and 10.0.110, with
  the apt `/usr/bin/dotnet` a bare host holding **no SDKs at all**. `global.json` pins 8
  with `latestMajor` roll-forward, so **whichever machine you are on, the newest installed
  SDK is what actually runs** while CI installs 8.0.x. Check with `dotnet --list-sdks`
  before believing any of that.
- **One lesson survives every flip (#27).** SDK 8.0.129's early C# 12 compiler rejected a
  collection-expression `Split` call in `MonsterParser` that CI's newer 8.0.x accepted,
  which is why that call is written as an explicit array. **Building locally on a newer
  SDK does not prove CI's compiler agrees** — this is the failure that gets caught in CI
  rather than at the desk.
- **The source PDF is not in the repo and never will be**, and neither is `reference/`;
  both are gitignored. `~/Downloads/SRD_CC_v5.2.1.pdf` is where the tooling expects it.
  **Only `tools/SrdExtract` needs it** — `data/srd` is committed, so build, test and play
  all work on a machine that has never seen the PDF. If you mean to re-extract, fetch it
  first; if you do not, ignore its absence.
- **`dotnet new sln` under SDK 10 produces a `.slnx`, which .NET 8 cannot read.** Hit
  during setup: the solution has to be `SRDCombat.sln` in the classic format, or CI
  (pinned to 8.0.x) fails to find a project file at all. `dotnet new sln --format sln`
  forces it. The same version gap means **templates default to `net10.0`** and write
  `TargetFramework`/`Nullable`/`ImplicitUsings` into each new `.csproj`, silently
  overriding `Directory.Build.props` — strip those three lines from any project
  created by a template.
- **Godot 4.7 stable mono** at `~/.local/bin/godot` on the machine this was written on.
  Not used until Phase 7, so its absence elsewhere costs nothing yet.
- **`pdftotext`** (poppler) is the extraction workhorse for eyeballing pages. Needed only
  alongside the PDF.
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
reports 330 monsters, 339 spells, 38 weapons, 13 armor, **258 magic items**, 0 errors,
and **12 warnings, all expected**:
the Archmage's XP, which is a real SRD inconsistency, nine spells whose component
line is truncated at a column break in the source, and two magic items (Figurine of
Wondrous Power, Ioun Stone) whose "Rarity Varies" tiers live in a table in the body
rather than on the type line.

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

## Running the game

```bash
dotnet run --project src/SRDCombat.Console
```

`--seed 12345` replays a run exactly; `--level 1..5` starts partway up the ladder;
`--one-fight` plays a single encounter instead of the run, with
`--difficulty low|moderate|high`; the seed is printed at the start of every
run, so *"it happened on seed 12345"* is a complete bug report. The content directory is
found by walking up for `data/srd`, so it runs from anywhere in the repo.

**The run autosaves** to `srdcombat-save.json` (or `--save <path>`) after every cleared
fight, and `--continue` resumes it. **A save is drafts plus progress, never resolved
sheets** — `RunSave` serializes through `ContentSerializer`, so an unknown property or
another format version is refused with a reason, and `RunSaveTests` pins the shape.
Loading re-resolves every draft at the level its *experience* has earned, so a
hand-edited save cannot smuggle in a level, and levelling uses average hit points
precisely so a reload cannot reroll history. **Defeat does not touch the save** — the
file keeps the state after the last fight the party won, which is what the design doc's
"defeat means reload, not reset" turns out to mean in practice.

**The client holds no rules.** It calls the engine's public actions, prints
`CombatStep.Narration`, and shows a refusal with its named code rather than hiding it —
a refusal is the engine explaining a rule, and swallowing one would make the client a
second place rules live. Two constraints worth keeping: **the log appends and never
replaces** (5eGoldBox's replaced its contents and was immediately called messy), and
`Labels` gives every combatant a unique letter, because the first fight ever played had
an Animated Flying Sword, an Ape and a Cleric called Aldous all drawing as `A`.

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
- **One narrowly-scoped branch per concern; branch → push → open a PR → wait for CI →
  merge it yourself once CI is green** (`gh pr merge <n> --merge`, merge commits, not
  squash). Confirmed with the user 2026-08-12 — an earlier version of this line said the
  user merges, and it was stale. Never push to `main` directly.
  (The first six commits went straight to `main` before this was being followed, which
  is why early history has no PRs. From PR #1 onward it is the workflow.)
- **Merging intermittently returns HTTP 504 while succeeding.** Re-check with
  `gh pr view <n> --json state,mergedAt` after a merge error rather than assuming it
  failed, and always confirm a PR really is merged before branching from `main` — a
  stale base silently drops the previous slice's work from the working tree.
- **File found-but-deferred work as a GitHub issue**, not in this file and not in chat.
  `gh issue list` is the work queue.
- Gate before merge: focused tests → full suite → Debug **and** Release build, both
  0 warnings → `git diff --check` clean.
- **There is no versioned DTO mirror and no generated schema in this project.** Content
  serializes straight from the `Core` definitions. This is a deliberate divergence from
  5eGoldBox — the design doc explains why — and the guards that replace it are
  `UnmappedMemberHandling.Disallow` (an unknown property is an error, not skipped) and
  `ContentSerializerTests`, which pins the on-disk shape. **Adding a field to a `Core`
  definition is enough; re-run the extractor and the files rewrite.** Don't go looking
  for a DTO layer to update, and don't reintroduce one without reading that section.
- **Frozen transcript tests for combat**: a scripted fight's exact narrated step
  sequence, diffed byte-for-byte. These require the RNG to be seeded and injectable,
  which is why `Core` owns its randomness behind an abstraction.
- When a design decision here or in the plan doc turns out to be wrong, **correct the
  doc in the same commit as the code**, not as a follow-up pass.
- **Check that an edit to this file actually applied.** A scripted find-and-replace over
  prose silently does nothing when the text has drifted, and this file changes on most
  branches. Two edits no-opped that way in one afternoon: one left a sentence with its
  opening clause missing, and the other left the status section claiming a permanent-death
  rule that had just been replaced. Both read as confident and were false, which is worse
  than a merge conflict would have been.

## Attribution obligation

SRD 5.2.1 is CC-BY-4.0, so derived content **can** be shipped — but the attribution
in [`NOTICE.md`](NOTICE.md) is required and must stay accurate. Per the SRD's terms,
do not add any other attribution to Wizards of the Coast beyond that statement.
