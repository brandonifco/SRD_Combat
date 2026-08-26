# Design — the battle builder and simulator

**Date:** 2026-08-26. **Author:** `designer`.

**Model, recorded per protocol:** this spec was authored on **Opus 5, not Fable 5**.
Brandon's Fable quota was exhausted at ~20:15 UTC on 2026-08-26 and he chose, asked and
answered, to proceed on Opus rather than hold the work. CLAUDE.md's team table assigns
Fable to the `designer` role deliberately — "where the work is judgement (design,
adversarial review, sequencing)" — so this is a **stated deviation from project
doctrine**, recorded here so a later reader knows which model's judgement they are
reading.

**Mandate:** Brandon's request, 2026-08-26 — a custom **battle builder and simulator**:
a surface where he can compose and test multitudes of combat scenarios at will. His
answers to the scope questions, verbatim in substance:

1. **All four authoring axes**: enemy cast, party composition (the four characters —
   classes, species, levels, gear — not just a level on the fixed pregens), battlefield
   (site, density, deployment formation), and objective & difficulty (win condition and
   encounter budget, rather than the hard-coded Moderate).
2. **An in-game builder UI** — chosen over a scenario file and over more CLI flags,
   knowing it was described to him as the most expensive option.
3. **Both outputs** — play one scenario by hand, *and* run many headless over a seed
   range with a statistical summary.

This document is the spec. The implementation slices are filed as issues (listed in
[§11](#11-slices-and-sequencing)); each carries its own acceptance criteria and
measurement gate. Nothing here is implemented yet. The battlefield overhaul design
(`docs/2026-08-25-battlefield-overhaul-design.md`) is the model for this document's
shape and its slicing.

---

## 1. What exists today, measured

`--spawn` (#456, merged as PR #459) is the whole of it. `RosterParser` turns
`"Ogre, 2 Goblin Warrior"` into monster definitions; `EncounterFactory.BuildChosen`
builds a `Fight` from that explicit cast, reusing `Assemble` — the layout, spawn-fitting
and terrain tail that the budgeted `Build` also uses — so a spawned fight stands on
exactly the board a drawn one would. `--level=1..5` sets the pregenerated party's level.
#463 is in flight, hardening that flag's refusal semantics and extracting
`ScenarioArguments` as the parse seam beside `RosterParser`; its own doc comment already
anticipates this work, naming "the composition, terrain and repeat-count flags Brandon
has asked for next as this grows into a custom battle builder."

So exactly **one** of the four axes is authorable today, and only against the fixed
pregenerated four. Of the other three:

- **Party composition.** Nothing. `PregeneratedParty.Build` is the only party a spawned
  fight can have. The machinery to do better already exists and is used elsewhere:
  `CharacterDraft` is a plain serializable record of every choice, `CharacterResolver`
  derives every number from it, `GauntletRun.Start` already takes an
  `IReadOnlyList<CharacterDraft>` overload, and `CreateMode` authors drafts in the Godot
  client today.
- **Battlefield.** Nothing authorable; every value is drawn. `TerrainGenerator.Generate`
  draws its density tier as its **first** roll (`DrawDensity`, sparse 25 / standard 50 /
  cluttered 25 — landed in #433/PR #451); `EncounterFactory.DrawLayout` draws the
  `BattleLayout`, consuming no die at all below level 3. `Generate` already carries an
  unwired `protectedSquares` parameter for a later slice's gaps and fords. Sites,
  `TerrainPiece` and `BattlefieldTheme` do **not** exist yet — they are #435–#439.
- **Objective and difficulty.** `BuildChosen` hard-passes `objective: null` and has no
  budget at all, by design: it records `Budget = Spent = summed printed XP` because
  nothing was budgeted. `ObjectiveSpec` (the serializable form; `EncounterObjective`
  itself is factory-constructed and get-only, so it cannot round-trip) and
  `EncounterDifficulty` both exist and are already carried as data on `LadderStep`.

And there is no batch anything. `tools/PacingMeasure` sweeps seeds, but its unit of
observation is a **run** — thirty fights, figure = fights cleared — not a fight.

## 2. What Brandon actually asked for, read carefully

Axis 4 as he stated it — "win condition and encounter budget, rather than the
hard-coded Moderate" — is two requests, and the second one is the interesting one.
Naming an objective is a small addition to `BuildChosen`. Naming a *budget* is a
different mode of scenario entirely: instead of "this exact cast", "a Moderate fight for
a level 3 party, drawn 120 different ways." That question cannot be asked of an explicit
roster, and it is the more valuable of the two for balance work.

So the enemy axis is not one thing but a choice of two:

- **An explicit cast** — `BuildChosen`'s path. The question is *this fight*.
- **A budgeted draw** — `Build`'s path, with the difficulty, the party level the budget
  prices against, the CR cap, the horde flag, and the four `MonsterPool.Draw` axes
  (coverage floor, CR, plausibility, tradition) all authorable. The question is *this
  kind of fight*.

The second mode is where the surface earns its keep beyond testing: "what does the
ladder look like if casters are admitted" is an F4 question (#312) that today has no
instrument at all. Exposing the pool's axes as scenario parameters is not bending the
allowlist — the pool's cuts stay exactly what they are for the shipped game; a scenario
just gets to ask what happens on the other side of them, and the answer is labelled as
what it is.

## 3. The interface decision

**Answers 2 and 3 pull against each other, and this is the resolution.**

A batch of 120 seeds is headless by definition — no display, no clicks, runs in CI or a
terminal. An in-game UI cannot be that. And a scenario worth running 120 times is worth
re-running next month, diffing, quoting in a bug report, and checking into the tree as a
fixture. None of that is possible if a scenario exists only as fields on a Godot node.

So:

> **Decision.** A scenario is a **value**, not a screen. `BattleScenario` is the product.
> The builder UI is one **author** of that value — the one Brandon chose, and the primary
> one. A serialized `.scenario.json` file is the value's **artifact**: saved by the
> builder, loaded by the builder, and read by the headless batch runner and the
> play-one-by-hand path. Everything that consumes a scenario consumes the value, not the
> screen.

**This is not the scenario-file interface he rejected, and the difference is load-bearing.**
What he declined was *authoring by hand-writing a file* — opening a text editor and typing
JSON as the way to make a fight. That is not proposed, and the spec is deliberately built
so it never becomes the expected path:

- The format is **strict machine JSON** on the existing `ContentSerializer`, with
  `UnmappedMemberHandling.Disallow` — a typo is refused, not skipped. It is a save
  format, not an authoring grammar. It gets a pinned shape test, not a hand-authoring
  guide.
- The **first scenario files come from the shipped game, not from a text editor**: the
  existing `--spawn` path gains the ability to write out the fight it just built
  ([S9](#11-slices-and-sequencing)), and a live fight can be captured as a scenario.
  Nobody has to type a file to get a file.
- No new per-axis CLI flags. The CLI gains exactly **one** new flag, `--scenario=<path>`,
  which *plays* a scenario. More flags per axis is precisely what he rejected, and it is
  not what the sequencing below builds.

**What I would ask him if I could ask once more:** *is the scenario file purely the UI's
artifact, or does he expect to open one and edit it by hand?* That single answer decides
whether the format stays strict machine JSON (this spec's assumption, because it matches
every other serialized thing in this tree and because he declined the file interface) or
earns a tolerant parser, a documented grammar and human-aimed validation errors. If the
answer turns out to be "yes, by hand", the change is additive — the strict format stays
and a lenient front end is filed on top — so nothing here is wasted either way. The
question is flagged in [§12](#12-judgement-calls-reserved-for-brandon), not silently
decided.

**And the honest note about sequencing.** He chose the expensive option. The way an
expensive option gets built without a long dark period is to build the value first and
the screen second: the model, the runner and the batch tool have no dependency on #327
and give him usable capability in the first slice, while the UI — which is gated on
#327 either way ([§9](#9-the-327-gate)) — lands into a value type that is already
proven, already tested, and already has two other consumers. The UI is not deferred;
it is the last thing built on a substrate it needs regardless.

## 4. The design test for an instrument

The project's decision test — "does the player face a choice where both options are
defensible" — is about player-facing design and does not apply to a tool. The analogous
test, and the one every slice below is judged against:

> **Does it answer a question that cannot be answered today, and does its answer
> distinguish between hypotheses?**

`PacingMeasure`'s `ended:` line is the model: a change that makes fights unresolvable and
a change that makes them lethal move every other figure identically, and only that line
tells them apart. A batch summary that reports a win rate and nothing else fails this
test.

## 5. The scenario model

`BattleScenario` lives in `SRDCombat.Game` beside `RunSave` and `ScenarioArguments`, and
is serialized by `ContentSerializer` — the same strictness that guards content and saves.
Its parts:

**Party.** Either a **named preset** (`Pregenerated`) or an **explicit list of
`CharacterDraft`** plus a level per member. Drafts, never resolved sheets, for exactly
the reason `SavedRun` gives: `CharacterResolver` computes every number, so storing sheets
would store values that can drift from the rules that make them. The preset case exists
so a scenario can say "the pregenerated four" without forking their drafts — otherwise
every scenario in the library freezes a copy of `PregeneratedParty` and a change there
silently stops applying to any of them. Gear needs no separate axis: `WeaponIds`,
`WeaponMasteryIds`, `ArmorId`, `HasShield` and `MagicItems` are already draft fields.

**Enemies.** Either an **explicit roster** (monster ids and counts — what `RosterParser`
produces) or a **budgeted draw** (difficulty, the level the budget prices against, CR
cap, horde flag, and the four `MonsterPool.Draw` axes). See [§2](#2-what-brandon-actually-asked-for-read-carefully).

**Objective.** An `ObjectiveSpec` — the serializable form the ladder already carries —
with one model growth: `KillLeader` today marks the dearest monster by printed XP, and a
scenario wants to be able to name *which* creature is the leader. That is a named shape
the model grows ([S5](#11-slices-and-sequencing)), not an allowlist bent.

**Battlefield.** An optional block of **overrides**: layout, density tier, and — as each
overhaul slice lands — site, deployment formation, theme. Every field is nullable and
`null` means "let the seed draw it", so the block grows one field per landing slice
without a format break, exactly as `SavedRun`'s own stated rule for adding a field
allows.

**Provenance, not identity.** A scenario records the content fingerprint it was authored
against and, optionally, one seed — the seed a capture came from. Neither is used to
build anything: the fingerprint warns, the seed is a bookmark.

Three traps, each an acceptance criterion rather than a hope:

1. **`ContentSerializer` sets `IgnoreReadOnlyProperties = true`.** A get-only property on
   the scenario record is silently *not written* and silently absent on read. Every real
   field must have an `init` accessor. This is the "write the validator that asserts the
   shape of what should have been found" lesson pointed at a new record: the guard is a
   pinned shape test in the `SavedRunShapeTests` pattern, not a code review.
2. **`EncounterObjective` cannot round-trip** (private constructor, get-only properties)
   and **`Battlefield` is never serialized** — the overhaul design pins that it is
   rebuilt from the seed on load. A scenario therefore stores the *specification* of both
   and never the resolved thing, which is the same discipline as drafts-not-sheets.
3. **Content drift is warned, not refused — a stated divergence from the save's rule.**
   `GauntletRun.Resume` refuses a fingerprint mismatch outright, correctly: a run in
   progress whose numbers shift underneath it is a corrupted game. A scenario is a
   *question you are asking the current build*, and refusing every scenario in the
   library after every extractor regeneration would make the surface useless within a
   week. So: the fingerprint is reported as a mismatch notice, and `ContentDrift.Require`'s
   per-id checks are what actually refuse — a scenario naming a weapon id that no longer
   exists still fails loudly. The reasoning is written into the field's doc comment, per
   the `AreaTargeting` model. (#355 already wants the run's own refusal demoted to a
   notice; these are the same argument arriving from two directions, and this document
   does not pre-empt that issue's decision.)

## 6. The generation contract: a scenario overrides draws, it never bypasses generation

This is the single most important constraint in the document, because getting it wrong
means a second terrain generator, a second placement path, and a spawned fight that
stands on a board the real game cannot produce.

> **Every value a scenario authors is a value the generator would otherwise have drawn
> from the fight's own dice. The scenario supplies the value; the generator's code path is
> otherwise untouched.**

`BuildChosen` already set this precedent by reusing `Assemble` verbatim rather than
writing a second board builder, and every slice below extends it. Two consequences:

**An overridden draw still consumes its dice.** `TerrainGenerator.Generate` draws density
as its first roll and everything downstream — terrain placement, then `Encounter.Start`,
`RollInitiative`, and every combat roll of the fight — sits on that same stream. Skipping
a roll because a scenario pinned its outcome would re-time the entire fight. So an
override reads and discards. This is the discipline the overhaul design already states
for rejection ("rejection never re-times the dice"), applied to authoring, and it buys a
property test worth writing:

> **The pinning trip-wire.** For a scenario with a battlefield override left null, run it
> at seed *S* and record the drawn values. Then set the override to exactly those values
> and run it again at seed *S*. The two fights must be **identical** — same board, same
> initiative, same narration. If they diverge, an override skipped a die, and every batch
> number taken with an override set is measuring a different fight than the one that was
> authored.

This is the `#412` pattern: assert the invariant a reading rests on, so the first
counter-example forces a decision.

**A scenario may compose fights the ladder never generates — and that is the point, but
it must be labelled.** `DrawLayout` gates `CornerGroups` and `Surrounded` below level 3;
a scenario can pin `Surrounded` at level 1 anyway, which is a legitimate thing to want to
test and an illegitimate thing to quote as a fact about the shipped game. The batch
report's header states which overrides were in force, for exactly this reason.

## 7. The batch instrument

`tools/ScenarioMeasure` — a committed tool, so the methodology is code rather than
archaeology, the rule `PacingMeasure` was built under (#132).

**Its unit of observation is a fight, not a run.** That is why it is a second tool and not
a flag on the first: `PacingMeasure`'s entire vocabulary — median fights cleared,
cleared-all, reached-L4, per-band hp-left — is about a thirty-fight run and means nothing
about a single encounter. What transfers is not the vocabulary but the **discipline**:

- All dice through `IRandomSource`; one fight per seed; `(scenario, seed)` reproduces a
  fight exactly, the same promise `(seed, fight number)` makes for a run.
- `SimpleTacticsPolicy` plays both sides, and **every number it produces is a floor set by
  a placeholder policy**. The bot does not use cover, does not hold a door, does not kite a
  ford. Regressions are real; "fun" is Brandon's to judge from a played run. That sentence
  belongs in the tool's own header comment, not only here.
- **Report the shape, not a summary.** A win rate saturates at 0% and 100% exactly the way
  the median saturates at 30, and for the same reason: runs pile at the ends. The report
  must carry a distribution and an outcome breakdown, not a percentage.

The report's shape, as an acceptance criterion:

```
scenario "ogre-and-goblins" x 120 seeds  [party: pregenerated L3 | enemies: explicit
  roster | overrides: layout=Surrounded (level gate bypassed), density=drawn]
  won 87 of 120 (72.5%)
  ended:  Victory 87   Defeat 31   RoundLimit 2
  rounds: median 6   p10 4   p90 11   max 19
  party hp left at end: median 41%   p10 12%
  downed/fight 1.34   deaths/fight 0.22
  monsters left standing on defeat: mean 2.4
  reproduce: --scenario ogre-and-goblins.scenario.json --seed 17
```

Three of those lines carry weight beyond decoration:

- **`ended:`** is the hypothesis-distinguishing line, for the reason `PacingMeasure`'s is.
  A non-zero `RoundLimit` is the stall signal and must be impossible to miss.
- **The header's override list** is what stops a number about a fight the game never
  generates being quoted as a number about the game.
- **`reproduce:`** is the line that makes this a builder-and-simulator rather than a
  statistics printout. A batch that finds a bad fight must hand back the exact command
  that opens that fight by hand.

**Explicit non-goal, stated because someone will otherwise do it:** a `ScenarioMeasure`
run **never** substitutes for the gameplay PR gate. CLAUDE.md requires
`tools/PacingMeasure -- --seeds 1-120` and `200-320` against a same-build baseline; a
scenario batch measures one fight under authored conditions and cannot speak to a
thirty-fight ladder's curve. The tool's header says so, and the tracking issue says so.

## 8. Constraints — what every slice must hold

- **Nothing silently approximate.** Every scenario field either resolves to real
  generation input or is refused by name, in `RosterParser`'s and `ScenarioArguments`'
  established voice — the value typed and the accepted range. No clamps, no fallbacks.
- **The clients hold no rules.** The builder UI authors a `BattleScenario` and hands it
  to `SRDCombat.Game`; every decision about what a scenario *means* lives in the engine
  where a test can reach it. This is why `RosterParser` was put in `SRDCombat.Game`
  rather than the client in the first place (#456), and the same reasoning covers all
  four axes.
- **Measurement gates, per slice, stated honestly.** Most slices here are *additive*: a
  new caller of existing generation, touching no path the ladder uses. Those satisfy the
  gate with `--seeds 1-20` against a same-build baseline **only with the structural proof
  written in the PR body** — the standing waiver's requirement, precedent #356–#358. Any
  slice that touches a shared path (`Assemble`, `TerrainGenerator.Generate`'s draw
  sequence, `DrawLayout`) pays the full two canonical ranges. Every slice, additive or
  not, must leave the **frozen transcript unchurned** — it uses hand-authored combatants
  and no generator, so this is verify-don't-assume, not assume.
- **Determinism.** Same scenario, same seed, same fight, byte-for-byte in narration.
  Pinned by a test, not asserted in prose.
- **No new modal surface before #327.** See below.

## 9. The #327 gate, and why it applies to a thing it does not name

CLAUDE.md's F3 entry gate reads: *"the PlayMode modal/state refactor (#327) lands before
any new modal surface."*

**The builder is not a `PlayMode` modal, and should not be one.** The client's three
screens are top-level `FightScreen` subclasses chosen by a three-way ternary in
`Main._Ready`; a builder has no fight, no run and no shop, so it belongs there as a
fourth — reached by a `--build` flag, one line in `Main`. Landing it as a modal *on*
`PlayMode` would be the wrong architecture independently of any gate.

**Which means the gate's wording does not cover it — and the gate's reasoning does.**
That gap is worth stating plainly rather than resolving quietly in either direction:

- The gate exists because "landing five of them on the 39-field class first would pay for
  the refactor twice." A separate screen never touches that class, so by the letter the
  builder walks past the gate.
- But `PlayMode`'s problem is not *where* its modals live, it is that its focus handling
  is a hand-written Esc cascade with six unenforced edit sites per new modal. A builder
  screen needs nested focus (library → edit this scenario → edit one character), and a
  builder that hand-rolls its own cascade leaves the project with **two** hand-rolled
  modal stacks instead of one. That is strictly worse than the situation #327 exists to
  fix, arriving by a route the gate's wording misses.

So the ruling, with the reason attached rather than by blanket application:

> **Every UI slice below (S10–S12) is gated on #327**, because each depends on something
> #327 actually delivers: S10 and S12 need a modal/focus structure to build on rather
> than a second cascade to hand-roll, and S11's party editor needs `CreateMode` reusable
> — which is #327's own second criterion, plus two small changes the refactor is the
> right moment for (its `Keep()` hands its result over by constructing `PlayMode` itself
> and free-ing itself, and its party size is the literal `4` in two places).

**Three notes for the steward and for #327's architect**, filed rather than left in prose:

1. **The gate's wording should say "surface", not "modal surface"** — or the next new
   screen walks past it the same way. A doctrine-wording finding, not a blocker.
2. **The builder makes the case for moving #327 sooner, not later** — it adds four
   surfaces to the tally the gate was priced on.
3. **#327's numbers are stale, in the direction that strengthens it.** The issue and
   CLAUDE.md both quote the 2026-08-21 review's "2,570 lines, 39 fields, 233-line focus
   stack." Measured 2026-08-26 on `main`: **2,661 lines, 48 instance fields**, and
   `_UnhandledInput` at **207 lines** (984–1190). Fields grew by nine in five weeks. Also:
   the builder's modals **nest**, and a flat focus stack and a nested one are different
   shapes — a structure that only handles one level deep would have to be rebuilt.

## 10. Dependencies on unlanded work

**The battlefield overhaul.** S1 (#433) is merged, so **density tier and layout are
authorable today** — those are the two overrides the first battlefield slice ships.
Everything else in the axis rides slices that do not exist: `TerrainPiece` and the
structure vocabulary (#435), the crossing and central-wall sites (#436), boulder field and
ruined rooms (#437), deployment formations (#438), `BattlefieldTheme` (#439). Rather than
one blocked-forever slice, the axis is **two** slices: what is available now, and a
follow-on explicitly gated on #437/#438/#439. The follow-on adds fields to an existing
nullable block, which is not a format break.

**This spec does not duplicate any of that work.** It adds no terrain code. Each site
generator, when it lands, grows one seam — *take the value if given, draw it otherwise* —
and that seam is the follow-on slice's whole content.

**Multi-square occupancy (#429).** S0–S3 are merged, so the engine already places Large
and Huge bodies: `SpawnPlacement.Fit` takes spans, `EncounterFactory.Assemble` threads
them, and `MonsterPool.LargestSpan` answers 3 today (the Awakened Tree). A scenario
containing an Awakened Tree therefore *builds and runs* correctly, headless, right now.
What is missing is #430 (S4/S5): the clients do not yet render, click-target or preview
multi-square creatures. **This is a stated limitation, not a blocker** — the batch runner
is headless and unaffected, and the play-by-hand path shows such a creature exactly as
the client shows it today, which is #430's problem and not this surface's. No slice here
gates on it.

## 11. Slices and sequencing

Filed as issues, one concern each, in dependency order; tracking issue **#472**. The first four have **no #327
dependency and no unlanded-work dependency** and can start immediately; they are also
where most of the value is.

**One client seam serves every slice.** `FightScreen.ResolveFight(int seed)` is the
single funnel into `EncounterFactory.BuildChosen`, and it reads argv directly through
static helpers. Giving it an overload that takes a `BattleScenario` — leaving
`ResolveFight(seed)` as "parse argv into one of those, then call the overload" — means
`--spawn`, `--scenario` and the builder screen all feed one path, and none of them
duplicates the other's decisions. That overload is S2's client-side deliverable and every
later slice reuses it. It lands **after #463 merges**, which is currently editing that
method.

| # | Slice | Issue | Blocked on |
| --- | --- | --- | --- |
| S1 | `BattleScenario`: the model and its serializer | #473 | — |
| S2 | `ScenarioRunner`: build a `Fight` from (scenario, seed) | #474 | S1 |
| S3 | `tools/ScenarioMeasure`: the headless batch runner | #475 | S2 |
| S4 | `--scenario=<path>`: play one scenario by hand | #476 | S2, #463 |
| S5 | Objective authoring, including a named leader | #477 | S1 |
| S6 | Battlefield overrides I: layout and density tier | #478 | S2 |
| S7 | Battlefield overrides II: site, formation, theme | #479 | S6, #437, #438, #439 |
| S8 | Party starting state: wounds, spent resources, the dead | #480 | S1 |
| S9 | Capture a scenario from a live fight | #481 | S8 |
| S10 | UI: builder shell and scenario library | #482 | **#327**, S4 |
| S11 | UI: cast and party editors | #483 | S10, **#327** |
| S12 | UI: battlefield and objective editors, and run-batch-from-the-UI | #484 | S11, S6, **#327** |

**Why this order.** S1–S4 deliver the whole of answer 3 (both outputs) plus two of the
four axes, with no gate to wait on. S5–S9 complete the remaining axes as engine work,
each independently useful from the CLI. S10–S12 are the authoring surface Brandon chose,
landing after #327 into a value type with two proven consumers and a test suite. S9 is
called out separately because it may be the highest-value single feature here and it needs
no UI at all: Brandon plays a run, hits a fight that feels wrong, and saves *that fight*
as a scenario to replay by hand and batch over 120 seeds.

## 12. Judgement calls reserved for Brandon

1. **Hand-editing.** Is the scenario file purely the UI's artifact, or does he expect to
   open one and edit it? This spec assumes the former ([§3](#3-the-interface-decision));
   the latter is additive if he wants it.
2. **Where scenarios live.** A committed `scenarios/` directory in the tree (shareable,
   diffable, quotable in an issue) or a user directory beside the save file (private
   scratch)? This spec assumes a path argument with no opinion, and the UI defaults to a
   user directory.
3. **The library's size.** "Multitudes" — does he want a flat list, or tags/folders? The
   spec assumes flat until it hurts.
4. **The pool axes in budgeted mode.** Exposing them lets a scenario ask what the ladder
   would look like with casters admitted (#312) — genuinely useful, and genuinely a way to
   produce a number about a game we do not ship. Is the labelled-header treatment
   ([§6](#6-the-generation-contract-a-scenario-overrides-draws-it-never-bypasses-generation))
   enough, or does he want the non-shipped configurations harder to reach?
5. **Batch size and patience.** 120 seeds of one fight is fast; 120 seeds of a
   twenty-round warband is not. Is a progress line enough, or does he want parallelism?

## 13. Relation to the rest of the backlog

- **#456 / #459** — `--spawn` and `RosterParser` are this surface's first slice, already
  shipped. The roster grammar is reused verbatim by S1's explicit-cast mode.
- **#463** (in flight) — `ScenarioArguments` is the parse seam S4's `--scenario` flag
  extends; its refusal voice is the voice every field in S1 uses.
- **#464** (RosterParser hardening) — lands before or with S1; the scenario model inherits
  the grammar's trip-wire.
- **#443** (`--one-fight` hard-codes level 3/Moderate) — stays its own concern; S4 does
  not fix it, and a scenario is not the answer to a flag that ignores its arguments.
- **#327** — the gate. See [§9](#9-the-327-gate), including the two notes for its
  architect.
- **#433 merged; #435–#440** — the battlefield vocabulary S7 overrides. See
  [§10](#10-dependencies-on-unlanded-work).
- **#429/#430** — the engine half is landed and used; the client half is a stated
  limitation of S4's rendering, not a gate.
- **#312** (no enemy magic in thirty fights) and **#314** (`ITacticsPolicy` A/B) — both are
  F4 questions this surface is the natural instrument for: S3 over one scenario with two
  pool configurations, or two policies, on the same seeds.
- **#355** (demote the content-version refusal to a notice) — the same argument as
  [§5](#5-the-scenario-model)'s trap 3, arriving from the run's direction. Neither
  pre-empts the other.
- **#190** (client behaviour tests) — S10–S12 land after #327 and should carry behaviour
  tests against its new structure, as #327's own criteria ask for.

## 14. Phase placement — a recommendation, for the steward to rule on

**Recommendation: a new designation, `phase:FI-instrument`, running alongside F2–F4 the
way F5 does, with individual slices pulled forward by the work that needs them.**

The reasoning, and the alternatives rejected:

- **Not F3.** F3's exit is "measured curve holds through the back half on both ranges; a
  human run report exists for every new system." A battle builder cannot be exited against
  either criterion — it does not touch the curve and it is not a run system. An item that
  cannot be measured by its phase's exit test does not belong in that phase, however many
  of its dependencies live there.
- **Not F2.** The battlefield axis touches F2's work, but as a *consumer* of vocabulary F2
  is landing, not as part of it. Putting it in F2 would put a testing tool inside the phase
  whose exit is "a watcher can narrate a fight with the log covered."
- **Not F5.** F5 is "the tree is trustworthy" — automated tests, suite speed, CI gates.
  This is a human instrument, not a gate. It runs when Brandon runs it.
- **Not the Definition of Finished.** Nothing in v1.0's six criteria requires it. A
  stranger downloading a release does not need a battle builder, and this should not
  become a reason v1.0 slips.

The precedent is `tools/PacingMeasure` itself: it has never had a phase, because it was
built when the pacing series needed it (#132) and justified by that work. Instruments in
this project are pulled by the work they serve. A designation makes that explicit rather
than leaving twelve issues unlabelled or mislabelled, and lets the steward pull S1–S4
forward now — where they immediately serve F2's battlefield slices and F4's pool
questions — while S10–S12 wait for #327 in F3's window.

**If the steward prefers not to mint a phase**, the fallback with the least distortion is:
S1–S9 labelled `phase:F4-depth` (the phase whose questions they answer) and S10–S12
labelled `phase:F3-run-game` (the phase whose gate they wait behind). That fallback is
worse — it puts a tool inside two content phases' exit criteria — but it is honest about
the sequencing, which the single-phase alternatives are not.
