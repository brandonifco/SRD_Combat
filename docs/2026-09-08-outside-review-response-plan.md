# The 2026-09-08 outside review: adjudication and response plan

**What this is.** On 2026-08-24 Brandon committed to sourcing a genuinely independent
review *before F3's build starts*, paired with his own long played run against its
findings (CLAUDE.md, "The team"). This is that review's adjudication. The review was
produced by Codex (GPT-6) against commit `e253c9d` (merge of PR #700), with its own
adversarial harness, the full validation gate, and the Godot probes; its text and
evidence bundle live outside the repository
(`~/Documents/Codex/2026-09-08/analyze-github-com-brandonifco-srd-combat/outputs/`).
The played-run half of the commitment is **still owed** and is the first item in
the plan below, because two of the decisions the review forces cannot be taken without
it.

**How to read this.** §1 is the verdict on every concrete claim, checked against the
code on 2026-09-08 by four read-only verification passes, with what each pass found
*beyond* the review and where the review was wrong. §2 is the plan: six tracks that
mirror the reviewer's six priorities, each resolved into existing issue numbers or
new issues to file, with owners and sequencing. §3 lists the decisions only Brandon
can take. §4 is the issue manifest. §5 records the edits this plan makes to the
finishing plan. The finishing plan remains the authority on phases; this document
re-orders work *within* them and adds items, it does not add a phase.

**The review's thesis, accepted.** "The project's main process danger is mistaking a
documented exception for a fulfilled promise." Every one of the six concrete findings
is an instance: a save format whose recovery path is advertised but not reached, a
shop the probe visits and cannot fail on, a spell the allowlist calls verified and its
own comment calls approximate, a coverage grade that reads one section and names the
whole creature, a capture named for a refusal that never happened, a pacing label
that says "won" over rows that include losses. The 2026-08-24 critique found the same
shape in the parser (credit-by-label) and the answer there was a mechanism, not a
patch. The answer here is the same: **every promise gets a check that can go red at
the boundary where the promise is made** — the probe asserts the refusal code, the
layout test asserts the button is on screen, the allowlist test asserts the spell
does what its label says. That is Track E, and it is why Track E is not last.


## 1. Adjudication

Every claim was verified against `e253c9d` by reading the code and the issue queue.
"Confirmed, wider" means the verification found more than the review reported.

| # | Claim | Verdict | Tracked? |
| --- | --- | --- | --- |
| 1 | Explicit JSON `null` for `ladder`/`members` passes the required-member check, throws `NullReferenceException`, and escapes `SaveFile.TryReadRun`'s catch so the `.bak` is never tried; same shape in `ScenarioFile.FromJson` | **Confirmed, wider** | No — #526 (closed) fixed *missing* members and never mentions explicit null |
| 2 | The first shop at 1920×1080 lays 25 offers below the window; no scroll, no paging; wheel is dead outside `Phase.Fighting` | **Confirmed** | No |
| 3 | Spirit Guardians executes as a one-shot Emanation sweep plus concentration bookkeeping; the allowlist header says "verified to execute faithfully" | **Confirmed** | No — only a doc comment at `PreparableSpells.cs:55` |
| 4 | `Playable` reads only Action entries; CR ≤ 4 pool is 78 = 46 Complete + 32 Playable; 17 Large/Huge occupy Medium space | **Confirmed, numbers exact** | Yes — #231, #429/#430/#449 |
| 5 | `play-2-refused.png` is a byte-copy of `play-1-turn-ready.png`; `CaptureFrame` prints a save failure instead of failing | **Confirmed** | Half — #521 (open) is the step; the `CaptureFrame` half is unfiled |
| 6 | `RefreshAfterAction` runs 504 `FindPath` calls per refresh; ~172 ms measured | **Confirmed** | Yes — #328 |
| 7 | `PacingMeasure` appends a fight before checking the run's outcome, so losses enter "won"/"cleared" rows and their averages | **Confirmed, narrower** | No |
| 8 | Ladder repeats one five-slot template six times; loot auto-equips; the shop filter rejects sidegrades | **Confirmed** | Yes — #306–#311 (all `paused:balance-design`) |
| 9 | Six classes offered; 17 spells; Ranger has one spell; no species trait executes | **Confirmed** | Yes — #291, #315; no Ranger issue |
| 10 | Client README contradicts itself about CI; finishing plan still recommends the fixture approach #694 rejected; root README quotes 2026-08-21 pacing figures; "seed is a complete bug report" overclaims | **Confirmed** | No |
| 11 | Seeds 1 and 2 clear all thirty at level 4, so the level-5 tier is unreached | **Confirmed by the evidence bundle** | Yes — #310 |
| 12 | 53,001 production lines; Encounter partials 6,098; 37% comment-leading lines | **Confirmed, all seven counts exact** | `docs/status.md` is stale by 5,499 lines (generated at `83ae869`) |

### What the verification found beyond the review

- **Saves (claim 1).** The blast radius is `Resume` and the play loop, not `FromJson`.
  A null *element* in `ladder` or `members`, a null `draft`, or a null
  `abilityScoreImprovements` survives `FromJson` and crashes minutes into a fight at
  `Gauntlet.cs:492`/`:622`/`:876`, past both clients' `InvalidDataException or
  ArgumentException` filters (`Program.cs:110`, `PlayMode.cs:381`). Worse by this
  project's doctrine: `"spellSlotsRemaining": null` is **silently repaired** to a full
  complement (`Combatant.cs:1412`). No test anywhere feeds a null collection — every
  existing theory *removes* the property, which produces the `JsonException` that *is*
  caught. `SaveFileTests.cs:156` is the exact near-miss.
- **Shop (claim 2).** Escape works regardless of layout (`PlayFocus.Shop.Escape ⇒
  CloseSelf`), so this is "offers unreachable", not a softlock. The probe **already
  visits this shop** on seed 1 after fight 1 (`PlayMode.Probe.cs:230-238`) — and clicks
  Back by its *computed centre*, so it would click a button at y=1400 and pass. The
  codebase already clamps the combat log to `ScreenHeight` (`FightScreen.cs:2712`);
  the shop is an omission, not a missing idiom. `project.godot` sets no minimum window
  size.
- **Spirit Guardians (claim 3).** Core **already has** a start-of-turn emanation
  lifecycle — `AuraEffect`/`AuraClock.StartOfVictimTurn`, `Encounter.FireAuras`, nine
  tests in `TraitAuraTests.cs` — built for the Ghast's Stench. It is monster-entry-typed
  and lacks end-of-turn, enters-the-area and Speed-halving clocks, so a faithful aura
  is a bounded extension, not a new subsystem. Ending concentration on Spirit
  Guardians today does *literally nothing*, because the spell imposes no condition
  for `SweepConcentrationConditions` to remove. The one-line honest fix exists next
  door: `DescribeSpecies` already appends "(not yet implemented)" per trait
  (`CreateMode.cs:1038`); `DescribeSpell` at `:1051` does not.
- **Movement (claim 6).** `MovementRules.FindPath` **is already a Dijkstra with a
  predecessor map** (`MovementRules.cs:149-166`) that early-returns at the destination.
  Draining it instead yields every reachable square and its path in one search — which
  is also the data #303's path preview needs. `SimpleTacticsPolicy.ScoreSquares` runs the
  identical per-square loop (`:433`, `:1505`), and `MovementRules.cs:200-208` already
  documents the same pattern turning a scan "from seconds into tens of minutes". One
  public `Reachable` call fixes the client, the AI and the preview together.
- **Pacing (claim 7).** Only the `by monster count` and `per band` blocks are
  contaminated, at exactly one row per defeated run (`Stalled`/`NoFight` already break
  before the append). `shape:`, `ended:`, median and cleared-all read `results` and are
  clean. So the quoted per-band hp-left curve in CLAUDE.md's Pacing row is understated
  by a band-varying amount; the rest of the row stands. The `rounds` average is
  affected too. `tools/PacingMeasure` has no tests at all.
- **Shop filter (claim 8).** The mastery-trade refusal is **not** in the offer filter:
  `TradesAwayAMastery` gates only `AutoBuy` (`Shop.cs:284`), and its comment says the
  stall still shows those swaps to a human. What blocks sidegrades is the `after <=
  before` average-damage gate at `Shop.cs:453`. #308 already names retiring it.
- **Party breadth (claim 9).** "Six classes" hides a spread of one to nine executing
  features: Barbarian 9, Rogue 8, Fighter 8, Cleric 5 (+8 spells), Ranger 5 (all shared
  with Fighter/Rogue, +1 spell), Wizard 1 (+10 spells). The Ranger is mechanically a
  Fighter with fewer options; the Wizard's identity is entirely its spells. So "complete
  fewer identities" resolves to one concrete class first.
- **Footprints (claim 4).** S0–S3 have landed (#434, #444, #445, #448: the model, the
  moving of bodies, the reserving of spawn). The S4 flip is one field
  (`Combatant.cs:294`) plus an inventory of readers that must switch in the same PR,
  with #449's area-origin rulings already made. #429's body says 15 of 73; today it is
  17 of 78 because Ankheg and Ettin re-entered.
- **`paused:balance-design`.** Six of the eight F3 design issues carry a label whose
  only definition is its GitHub description ("Paused 2026-08-28"). No document says why,
  or what un-pauses it. §3 makes that a decision.

### Where the review was wrong, so nobody chases it

- Escape closes the shop; the review implied only Escape *might*. Severity is "offers
  unreachable", real but milder.
- `ended:` and `shape:` are not contaminated by the pacing label bug.
- Equal-damage weapons *with a mastery change* are already shown to a human; the bot
  declines them. The strictly-better damage gate is the actual blocker.
- The client README's contradiction has a true half and a false half: line 589 (in
  the solution, CI runs it) is true; line 613 ("nothing in CI compiles this project")
  is a fossil from before 2026-08-15. `validate.sh` builds the whole solution on both
  legs.
- `RunDice.cs:19-34` states its claim carefully ("a retry that made the identical
  choices from here would see the identical fight unfold"). CLAUDE.md's flat "seed
  12345 is a complete bug report" is the loose one. Save + seed *does* reconstitute
  the next fight; what nothing records is the player's actions inside it.
- #581 is a docs-accuracy follow-up, not a shop issue; it does confirm the shop landed in
  `PlayMode.Draw.cs`/`Input.cs`, not `Run.cs` as the refactor design doc says.
- "No persistent-area lifecycle" — there is one, see above. The Spirit Guardians fix is
  materially cheaper than the review implies.
- The review presented #521 as a finding; it has been open since 2026-08-26 with the
  same mechanism and a decision section.


## 2. The plan

Six tracks, in the reviewer's order because that order is right: trust first, then
one fight, then five, then roster, then evidence, then a stranger. **Tracks A and B
are the active phases' work re-ordered; Track C is F3; D is F4 reframed; E is F5's
continuous lane with three new items; F pulls one F6 item forward.** Owners are the
charters in `.claude/agents/`. "NEW-n" refers to §4's manifest.

### Track A — Restore trust at the boundaries (this week; all small; parallel)

Nothing in this track needs a design decision except A4's label wording, and each
item ships as its own PR with a knockout-verified test.

| Item | Issue | Owner | Acceptance |
| --- | --- | --- | --- |
| A1 Explicit-null saves and scenarios refused, not crashed | NEW-1 (refs #526) | engineer | `RunSave.FromJson` refuses a null `Ladder`, `Members`, `Casualties`, any null element of `Ladder`/`Members`, a null `Draft`/`State`, and null `BaseAbilityScores`/`AbilityScoreImprovements`/`SpellSlotsRemaining`/`Potions` with `InvalidDataException` naming the property — which is already in `TryReadRun`'s filter, so the `.bak` path starts working with **no change to `SaveFile.cs`**. `ScenarioFile` gets `case null:` arms for `party`/`enemies` and per-element checks. The `SpellSlotsRemaining` silent repair becomes a refusal. Theories mirror the existing removal theories with `JsonValue` null. One **executable-boundary** test in `SRDCombat.Console.Tests` proves `--continue` against a null-poisoned primary loads the backup. Knockout table in the PR. Do **not** widen the catch — that would hide programmer bugs as corruption |
| A2 The shop fits the window | NEW-2 | engineer, then `probe-diff` | A static `ShopRowsThatFit(screenHeight, effectCounts)` in the `BarTop`/`GroundLine` style, pinned at 1080p and 720p for 0–40 offers; Back and the purse always on screen; wheel and PageUp/PageDown page the list (or `Shop` becomes a `RowMenu` so `TakesRowKeys` gives it keys); a "N more…" affordance. The probe's shop step asserts `_shopBackButton` bottom ≤ `ScreenHeight` *before* clicking — a computed-centre click on an off-screen rect is a fault. `project.godot` gains a minimum window size. Captures re-baselined per the skill |
| A3 The probe's refusal step refuses | #521 + NEW-3 | designer decides, engineer builds | #521's decision: retarget the step at a refusal the button row actually surfaces (recommended: arm an attack, click a square out of reach, assert the `attack.*` refusal code in `_notice`), or the debug-only doomed action. NEW-3 is independent and has no design question: `CaptureFrame` **throws** on `SavePng` failure so `ProbeFaults` exits 1, and every *required* probe step asserts a state predicate (refusal code seen, resources unchanged, expected focus layer) — `ReportSkip` stays for unreachable optional steps only. `client/README.md:515` and `probe-diff/SKILL.md:67` corrected in the same diff |
| A4 Spirit Guardians says what it is | NEW-4 (label), NEW-5 (aura) | engineer (label, now); architect → engineer (aura) | **Now:** `DescribeSpell` appends the same "(not yet implemented: persistent aura, halved Speed, saves each turn)" tag `DescribeSpecies` uses; the allowlist header stops saying "verified to execute faithfully" over an entry it exempts; a test pins that four `EndTurn`s after a cast deal no damage and the Ogre keeps 40 ft — so the gap is a red test the moment the aura lands. **Then (Track B window):** the aura itself, see §3 decision 1. Withdrawing the spell is not recommended — it would strip the pregen Cleric's level-5 identity and the whole point is to make labels true |
| A5 `PacingMeasure` counts what it labels | NEW-6 | analyst specifies, engineer builds | The fight tuple carries `Won`; both `GroupBy` blocks filter on it; labels match. A minimal test project pins one scripted defeated run's exclusion. Then **re-run seeds 1–120 and 200–320 once** — this is a re-baselining act, not a per-PR measurement — and correct the per-band and by-monster-count lines in CLAUDE.md's Pacing row, noting inline that the earlier series was contaminated so the two are never compared as if alike. `shape:`/`ended:`/median stand |
| A6 Docs say what is true | NEW-7 | steward | One PR: `client/README.md:613-615` deleted; root `README.md:21-34` stops quoting figures and points at the Pacing row and `docs/status.md`; finishing plan's fixture paragraph (`:189-194`, with its own 34-vs-27 contradiction) replaced by #694's finding; the outside-review commitment recorded in the finishing plan, which currently never mentions it; CLAUDE.md's "seed is a complete bug report" becomes "save + seed reproduces the next fight; nothing records the choices inside it"; `docs/status.md` regenerated; `paused:balance-design` defined in the framework doc with its un-pause condition (§3 decision 2); #581's addendum |

**Exit:** all six merged; A1–A3 each with a knockout table; A5's corrected per-band
line in CLAUDE.md. Roughly one agent-week of Sonnet work plus one designer call.

### Track B — Make one fight understandable (F2, re-ordered)

F2 is the active phase and already holds every item the reviewer asked for. What
changes is the order: **B1 first**, because it removes the per-hover cost that every
preview would otherwise pay and produces the path data #303 needs.

| Order | Issue | What lands | Owner |
| --- | --- | --- | --- |
| B1 | #328 + NEW-8 | `MovementRules.Reachable(field, mover, budget, combatants)` returning every reachable square with its `MovementPath`, refactored out of `FindPath`'s existing Dijkstra so the two cannot drift (`FindPath` becomes the early-exit caller). `RefreshAfterAction`'s 504-call loop and `SimpleTacticsPolicy.ScoreSquares`' two loops become one call each. Pinned: `Reachable(...).Keys` equals the set `FindPath` answers non-null for, on a fixture board; frozen transcript byte-flat. Measured before/after per #328's own criterion | architect (seam), engineer (call sites) |
| B2 | #304 | The latency half is one constant (`HoverDelaySeconds = 2` → 0.5) and ships first as its own PR; the terrain-vocabulary half follows | engineer |
| B3 | #303, #301, #302 | Path preview from B1's predecessor map; opportunity-attack threat squares from `FindOpportunityAttackers` (zero client references today); range envelope and exact `AreaTargeting` coverage on hover. In that order — each composes with the last, and #302 is the one that needs new focus *states*, so it routes through `FocusStack<T>` per the F3 entry rule | engineer, `probe-diff` per PR |
| B4 | #299 | Health thresholds and death-save pips; downed tokens draw no bar today and `DeathSave` has zero client references | engineer |
| B5 | #305, #329, NEW-9 | Log space; font scale off a viewport-derived factor instead of 68 literal `fontSize:` sites; **NEW-9: a layout-invariant test family** — every interactive rect on every screen is inside the viewport at 1080p and 720p, the generalisation of A2's fix so the next long list cannot overflow | engineer |
| B6 | #495 | Keyboard `M - Move` stepping — after B3, since it re-uses the preview | engineer |

Art and audio (#300, #460, #462, #437–#440's art asks) stay sequenced last per
Brandon's 2026-08-26 direction. The battlefield slices (#437, #438, #440, #587, #588,
#592) proceed in parallel as capacity allows; they are not on the critical path to
the F3 slice.

**F2 exit gains one line** (§5): *no interactive control on any screen lies outside
the viewport at 1080p or 720p, pinned by NEW-9.*

### Track C — Make five fights worth repeating (F3)

The entry gate (#327) closed 2026-08-30 and the rule outlives it. Every F3 design
issue exists (#306–#311). What the review adds is **build order and a stop**: build
the systems into fights 1–5 as one slice, have a human play that slice, and only
then extend the pattern across the six cycles. This is exactly the reviewer's "test
it with a human before extending", and it is cheaper than the plan's implicit
"design each system for thirty fights, then measure".

| Order | Issue | Note |
| --- | --- | --- |
| C0 | #542 + the played run | **Brandon's long played run against this review's findings** — the other half of the 2026-08-24 commitment — and his verdict on the `112ed19` difficulty shift. C1–C4 are `paused:balance-design`; §3 decision 2 is whether the run un-pauses them |
| C1 | #308 | Shop trade-offs. Smallest, fully specified: the gate becomes "not strictly worse" and a sidegrade prints its property and mastery delta. First because the shop is also A2's surface |
| C2 | #307 | Loot as pick-one-of-three, a new surface through `FocusStack<T>`; "a handful of items that change a turn" is the content half and can trail |
| C3 | #306 | Route choice between two or three revealed rungs. Design spec first (what an option reveals; seed reproducibility; interaction with the rest cadence) |
| C4 | #309, #311, #310 | Stakes (attempt counter, run summary, opt-in ironman); the `Survive(3)` rung; the XP curve. #310 is where seeds 1 and 2 "cleared at level 4" lands — the review confirms the issue's premise with two fresh runs |
| C5 | NEW-10 | **The five-fight slice**: C1–C3 wired into cycle 1 only, a human run report on it (template from Track F), and a written go/no-go before cycle 2–6 extension. Per-cycle variety (the site weighting the battlefield overhaul left here) rides on the extension |

**F3 exit is unchanged** and is still the re-baselining checkpoint — now run on the
corrected `PacingMeasure` from A5, which is why A5 precedes C.

### Track D — A deliberately limited roster (F4, reframed from counts to roles)

The reviewer's rule — "complete fewer identities that actually play differently, do
not chase book-wide counts" — is already the project's rule for spells (#292) and
becomes the rule for classes, species and enemies. F4's exit ("distinct-creature
measurement re-run") stays but is joined by a roles target.

| Item | Issue | Note |
| --- | --- | --- |
| D1 The Ranger is a Ranger | NEW-11 | Hunter's Prey, Favored Enemy, and two or three Ranger spells (Hunter's Mark is the obvious first, if `srd-lookup` confirms its 2024 text is executable as a rider). The weakest offered class by the feature table; additive registry work under the "add a name only alongside the code" rule |
| D2 One species trait executes | #291 slice | Darkvision, once the registry keys on owning species (the Dwarf 120 ft vs 60 ft trip-wire at `OriginContentTests.cs:166` is already in place) — the first trait that makes species a choice with a combat consequence, and the one fog slice 2 (#545) will need anyway |
| D3 The grade names what it grades | #231, sequenced after #390's last shape | `Playable` reads every section, with the demotion table produced *first* and the `MonsterPoolTests` floor lowered with a transitional annotation, because #390 ratchets the same number up. Plus a doc sentence distinguishing *entry* completeness from *whole-creature* fidelity (the footprint gap is the example), and the four named behaviours — Nimble Escape, Undead Fortitude, Redirect Attack, Split — as the first four mechanics to model because they are the ones the review could name from a census |
| D4 Bodies are the size print says | #429 (S4), #430 (S5), #449 | The flip and its inventory in one PR per #429's criterion 9, then the clients. The named new stall class (a Large creature wedged in generated terrain) gets its direct demonstration per the standing convention |
| D5 Three enemy roles | #312, #543, #314, NEW-12 | NEW-12 states the target: before F4 exits, the pool fields at least one **healer/protector**, one **space controller**, and one **urgent-priority target** (a caster is the natural third), each with a doctrine in #543's sense; #312 admits the casters, #314 gives the policy Dodge/Disengage/retreat behind an `ITacticsPolicy` seam so two policies A/B on the same seeds — which also answers the review's "the same policy plays both sides" objection |
| D6 Six unoffered classes | #315 | Recommend **cut for v1.0** (§3 decision 4). Twelve classes at one-to-nine executing features each is the count-chasing the review warns against |

### Track E — Evidence that can go red (F5, continuous)

F5 already runs alongside. Three additions come straight from the review's "explanations
outrun evidence" section, and #528 (three strikes on instruments nobody verifies) is
the mechanism issue they answer.

| Item | Issue | Note |
| --- | --- | --- |
| E1 Probe postconditions | NEW-3 (from A3) | Generalised: a required step is a predicate, not a filename. Closes the "capture's filename is not an assertion" shape for good |
| E2 Executable-boundary recovery tests | NEW-1 (from A1), #317 | The console test project grows the `--continue`-against-corruption family; the Godot half stays probe-only until #190/#490's live `PlayMode` gap moves |
| E3 Independent rule exemplars | NEW-13 | A small hand-written fixture set — twenty or so stat-block entries and five spells with expectations **derived from the printed page by `srd-lookup`, not from the parser** — so the corpus tests' "parser and fixtures agree" has one lane where they cannot agree on the same mistake. Grows only when a misattribution is found, the same rule as the page fixtures |
| E4 A replay bundle | NEW-14 | `--bug-report` (both clients) writes save + seed + content version + build hash + the fight number into one file, and CLAUDE.md's claim is rewritten to what that bundle actually reproduces. Recording the action sequence inside a fight is **not** in scope — it is the battle-builder's capture slice (#481) |
| E5 Suite time | #694 | Unchanged; the reviewer confirms its premise |
| E6 Contract before history | NEW-15 | A convention, not a purge: a `///` comment leads with the current reading and moves incident narrative longer than a paragraph to `docs/history/` with a link. Applied opportunistically when a file is touched; a steward pass over the five files the review named (Encounter partials, `FightScreen`, `EntryMechanicsParser`, `Combatant`, `SimpleTacticsPolicy`). 37% comment lines is not the problem; a reader unable to find the rule under the story is |

### Track F — A stranger plays five fights (F6, one item pulled forward)

The reviewer's last priority, and the finishing plan's own Definition of Finished, is
the same sentence: a stranger downloads, understands, plays. F6 ships that at the end;
the review's point is that **the evidence it produces is needed by C5, not after it**.

| Item | Issue | Note |
| --- | --- | --- |
| F1 A tester build | #326 (first slice) | A Linux build (Windows if the Godot export is already configured) of `main` after Track B, with `--seed` and the replay bundle from E4, distributed as a release asset, not a clone. The attribution screen (#323) and the art-licence line (#324) land with it because a distributed binary needs them; the masters strategy (#325) does not block it |
| F2 A run-report template | NEW-16 | What the tester decided and why, where they were confused, what they thought a control did versus what it did, and whether they wanted another fight — recorded per fight, not per run. Used by C5's human run and by every F3 "human run report" acceptance criterion, which today has no form |
| F3 One outside player on the five-fight slice | C5 | The go/no-go evidence for extending C1–C3 to cycles 2–6 |

### Sequencing

```
week 1     A1 A2 A3 A4-label A5 A6            (parallel, all small)
           C0: Brandon's played run begins
weeks 2–3  B1 → B2 → B3 (#303, #301, #302) → B4 → B5     A4-aura (architect design → engineer)
           D1, D2, E3, E6 as capacity allows (no dependency on B)
after C0   decisions 1–5 in §3 → C1 → C2 → C3 → C4        D3 after #390's last shape; D4
           E4, F1 prepared against post-B main
C5         five-fight slice + F2 template + F3 tester → go/no-go → cycles 2–6 → F3 exit re-baseline
then       D5 (roles), F4 exit, F5 push, F6 ship (second outside review + played run)
```

What this sequencing deliberately does not do: it does not start a rules-engine
extraction, an event system or a schema mirror (the review rejects them too); it does
not run pacing on any PR (the checkpoint rule stands; A5 is the one re-run and it is a
correction, not a measurement); it does not touch art or audio.


## 3. Decisions Brandon owns

Listed with a recommendation each, so they can be answered in one sitting.

1. **Spirit Guardians.** Label now (A4, no decision needed) and **implement the aura**
   (NEW-5) in the Track B window — recommended, because Core has the lifecycle and the
   alternative, withdrawal, deletes the pregen Cleric's level-5 identity. The design
   question inside it is the "designate creatures to be unaffected" clause: refuse it
   (the caster's allies are always affected unless the reading is written) or read it
   as "allies are exempt" and write that reading down. Recommend the second; it is
   what every table plays.
2. **Un-pausing `paused:balance-design`.** The label was applied 2026-08-28 with no
   written condition. Recommend: the pause lifts for F3's design issues (#306–#311) the
   day the played run's report exists, and the report plus #542's verdict are recorded
   in the framework doc as the un-pause. F4's balance issues stay paused until the F3
   checkpoint re-baselines.
3. **The `112ed19` difficulty verdict (#542)** — only the played run answers it. The
   review's two automated clears at level 4 are consistent with the baseline's 32 of
   120, not evidence against it.
4. **#315, the six unoffered classes.** Recommend **cut** for v1.0 and say so in the
   character creator ("six classes in this release"), which is the honest product
   boundary the review asks for. Reopen after F6 if a played run wants a class the six
   cannot give.
5. **Pulling the tester build forward (F1).** Recommend yes: one Linux release asset
   after Track B, well before F6's packaging proper. The cost is #323/#324 landing
   early, which they must anyway.
6. **The comment convention (E6).** Recommend adopt, applied on touch, never as a
   sweep — the archive already holds the long form and the convention only says where
   the next paragraph goes.


## 4. Issue manifest

Issues to file with the `file-issue` skill once §3 is answered (Track A's do not wait
on any decision and can be filed at once). Titles are working titles.

| Ref | Title | Phase | Owner | Refs |
| --- | --- | --- | --- | --- |
| NEW-1 | An explicit JSON `null` in a save or scenario is refused by name, and the backup actually loads | F5 (worked now) | engineer | #526, #317 |
| NEW-2 | The shop fits the window: a bounded, pageable list, Back always on screen, pinned at 1080p and 720p | F2 | engineer | #581 |
| NEW-3 | Probe steps assert postconditions; `CaptureFrame` fails the run on a save error | F5 | engineer | #521, #528, #180 |
| NEW-4 | Spirit Guardians is labelled as approximate at the point of choice, and the gap is a red test | F2 | engineer | #375 |
| NEW-5 | Spirit Guardians executes as the printed aura: end-of-turn and enters-area saves, halved Speed, dropped with concentration | F2 | architect → engineer | NEW-4, `FireAuras` |
| NEW-6 | `PacingMeasure` excludes lost fights from rows labelled won and cleared; per-band baseline corrected once | F5 | analyst, engineer | #542 |
| NEW-7 | Docs sweep from the 2026-09-08 review: client README CI claim, root README pacing figures, fixture paragraph, bug-report claim, `paused:balance-design` definition, status regeneration | F5 | steward | #694, #581, #542 |
| NEW-8 | `MovementRules.Reachable`: one bounded search yields every reachable square and its path, for the client, the policy and the preview | F2 | architect | #328, #303 |
| NEW-9 | Layout invariant: every interactive rect on every screen is inside the viewport at 1080p and 720p | F2 | engineer | NEW-2, #329 |
| NEW-10 | The five-fight slice: route, loot and shop trade-offs in cycle 1 only, human-tested before cycles 2–6 | F3 | designer | #306, #307, #308 |
| NEW-11 | The Ranger plays like a Ranger: Hunter's Prey, Favored Enemy, and a spell list longer than one | F4 | designer spec → engineer | #315 |
| NEW-12 | Three enemy roles before F4 exits: a healer or protector, a space controller, an urgent-priority target | F4 | designer | #312, #543, #314 |
| NEW-13 | Independent rule exemplars: hand-derived expectations for ~20 entries and 5 spells that the parser cannot have produced | F5 | qc | #189 |
| NEW-14 | `--bug-report` writes a replay bundle (save, seed, content version, build hash, fight); the doc claim is narrowed to what it reproduces | F5 | engineer | #481 |
| NEW-15 | Contract before history: a `///` comment leads with the reading; incident narrative longer than a paragraph moves to `docs/history/` on touch | F5 | steward | — |
| NEW-16 | A run-report template for human playtests: decisions, confusions, expectations per fight | F3 | designer | #306–#311 |

Existing issues re-sequenced by this plan: #328 first in F2; #304's latency half split
out and shipped immediately; #521 gets its decision; #231 sequenced after #390; #315
gets a recommendation; #326 gains an early first slice; #542 gains the played run as
its explicit input.


## 5. Edits to the finishing plan

Made in the same PR as this document, so the plan and this adjudication agree:

- **F2 exit** gains the layout invariant line (Track B).
- **F3** records the outside-review commitment and points here; its build order becomes
  the five-fight slice first (Track C).
- **F4** gains the roles target (Track D, NEW-12) alongside the distinct-creature
  re-run.
- **F5** loses the fixture paragraph in favour of #694's finding (A6 does the edit).
- **F6** notes the tester build pulled forward (Track F).

**What this plan does not change.** The phases, their order, the checkpoint rule for
pacing, the art-and-audio-last sequencing, the honesty rule, and the standing law of
one concern per PR. The review confirms all of them; what it found is the gap between
what those rules promise and what a boundary check would have caught, and the plan
closes that gap at the boundaries rather than by adding rules.
