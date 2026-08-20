# SRD_Combat

A turn-based tactical combat game on **SRD 5.2.1** (2024 rules, CC-BY-4.0). Party of
four, levels 1–5, full tactical grid, persistent gauntlet ladder of escalating fights
with XP, levelling, and loot. Combat only — no exploration, dialogue, or travel.

**Read [`docs/2026-08-11-design-and-development-plan.md`](docs/2026-08-11-design-and-development-plan.md) first.**
It is the governing design document: the kickoff decisions, the architecture and why
it diverges from `5eGoldBox`, the content pipeline, the phase plan, and the open
questions. Everything below is operational detail that doc doesn't carry.

## Current state — read this first

**As of 2026-08-16.** All numbers here are verified, not estimated.

| | |
| --- | --- |
| Branch | `main` after the 2026-08-16 play session — the first in which a person drove the Godot client through real fights and said what was wrong. Four slices came out of it: the code is **MIT licensed** with `data/` staying CC-BY (#202), movement executes the printed *Moving around Other Creatures* clauses so bodies no longer wall a corridor off (#203), the encounter draw **favours the classic bestiary over ordinary animals** (#204), and the **opening cycle rests Long** so a level 1 party is not ground down by attrition it cannot heal (#205) |
| Tests | **945 passing**, 1 skipped by design (the transcript fixture writer) |
| Build | Debug and Release, **0 warnings** (`TreatWarningsAsErrors`) |
| Content | 330 monsters · 339 spells · 12 classes · 9 species · 4 backgrounds · 38 weapons · 13 armor · **258 magic items** (13 names executed; the rest counted) |
| Pacing | **The ladder has a difficulty curve for the first time, and warband rungs (#207) are what gave it one.** Current `main`, measured 2026-08-16, `tools/PacingMeasure`, loot on: seeds 1–120 read **median 18, 38 of 120 clearing everything, 56 reaching level 4, 3 dying by fight 4**; seeds 200–320 read **18, 43, 60, and 9**. **The median measures again** — it had been pinned at 30 of 30 for several slices, where a saturated statistic reads identically whether a change helped, hurt or did nothing. Read the `shape:` and per-band lines with it: `died-by-fight-4` for the opening, `cleared-all` for the ending, hp-left per band for whether a fight was ever close. **The per-band line is the one that changed**: party hit points left used to be flat at 75–81% in *every* band from fights 1–5 to 26–30 — fight 27 was not harder than fight 3, only longer — and now runs **86% → 79% → 72% → 74% → 70% → 71%**. The distribution moved with it: runs ending in the middle rather than at either extreme went **47 → 79** of 120, so the old die-early-or-clear-everything split is gone. Five slices on 2026-08-16, each against a same-build baseline taken immediately before: **movement's printed pass-through clauses** (#203) clears 72 → 76; **the classic-monster weight** (#204) clears 76 → 66, a *difficulty* gain from a flavour change, because a classic monster carries more mechanics per XP than an animal; **resting Long through the opening cycle** (#205) died-by-fight-4 15 → 1 and 14 → 6; and **warbands** (#207) clears 72 → 38 and 78 → 43. **A human played the warband ladder on 2026-08-16 and the verdict was "definitely tense.
got killed."** That is the figure this instrument cannot produce and the one the whole
slice rested on: the run cleared 18 rungs and died on fight 19, which is `18 % 5 = 3` —
a warband rung. So the fight that ends a good run is the new one, and it reads as tense
rather than as a lot of clicking, which was the live risk of putting six to ten creatures
on the board. Record it beside the counts, because "38 of 120 clear the ladder" and "the
fight was fun" are different claims and only one of them can be measured.
**Warbands also proved the cliff the XP budget cannot see**: five creatures leave the party at 75% of its hit points and down 0.25 of a character, **six leave it at 51% and down 1.11**. That is why the warband rung is budgeted *Low* — at Moderate it took clears to **12** and was simply unwinnable. Two standing lessons, earned expensively: **quote a bar you measured yourself, on the build in front of you** — this row once carried `median 24, 54 clears` across several slices while the build read 30/72/93; and **a played run is a first-class instrument** — two partial human sessions found four real bugs, and the 2026-08-16 session produced three of the day's five slices from complaints no automated measurement could have voiced. Earlier history, kept for its reasoning rather than its numbers: the economy transformed the tail (full clears 2 → 14), #127 spent 2 median deliberately teaching monsters their stat blocks, and seed-set × build interaction swings a 120-seed figure by a few points — measure on two ranges before believing one. |
| Work remaining | **Warband rungs landed (2026-08-16) and gave the run its first difficulty curve** — see the Pacing row. What is left: **per-cycle variety** (the ladder is still one five-rung cycle six times over, and the warband is the third shape in it rather than a fourth cycle), and **a mechanical threat model** — expected damage × accuracy × effective hit points, plus riders and simultaneity — built **as an instrument first and validated against the 120-seed outcomes already on disk** before it is ever allowed to shape an encounter; printed XP stays the budget currency, because print-faithfulness is this project's identity and `ExperienceRules` hangs off it. **The party-power phase (#83) should still not be picked up next**: it was right when the median was 4–8 and runs died at level 1, that problem is fixed, and every spell or feature added now makes the game easier rather than richer. The row used to claim party power was *the only lever that has ever raised pacing*; 2026-08-16 disproved it three times — a **flavour** change (#204), a **rest-cadence** change (#205) and a **count** change (#207) each moved more than any party slice ever did. Still open and unrelated: **`tools/` (5,775 lines of PDF parsing) and `client/` (6,052 lines) have no test project at all** (#189, #190) — a third of the codebase, and the riskiest third. The squad-AI series (#122–#127) is complete: gains were #123 (focus fire) and #126 (no-healer caution); #124/#125 measured themselves out, and those ladders are required reading before any positional work. |

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

**The level 1 wall is fixed, and it was never the ladder or the budget — it was how many
creatures a fight fields.** `EncounterBuilder.MaximumFor` capped an encounter at one more
creature than there are characters, level-blind, since it was written. That is a fair cap
for a party that can take a hit and a lethal one for a party that cannot, because **the
cost of being outnumbered is paid in characters *removed***: a level 1 character has 8–12
hit points and the creatures a level 1 budget buys hit for 8–9, so very nearly every landed
blow drops somebody and takes a quarter of the party's action economy with it. The same
fight at level 5 lands the same 9 damage on 40 hit points and removes nobody. **The budget
cannot see this, because XP prices a creature's worth and not its simultaneity** — five
creatures at 60 XP and one at 300 are the same purchase on printed page 202 and completely
different fights to four characters with 10 hit points. The cap now grows with the party's
capacity to absorb a hit: **three creatures at level 1, four at level 2, and the original
one-more-than-the-party from level 3 up**, sized against the party's *lowest* level. The
printed budget is untouched; the same XP is simply spent across fewer, individually dearer
creatures. Measured on seeds 1–120 against the same build: **runs dying by fight 4 fell
37 → 11, full clears rose 65 → 84, level-4 runs 73 → 95, and deaths in fight 1 stopped
entirely.** That is the largest single move the early game has ever recorded. **It was
found by instrumenting rather than guessing** — `PacingMeasure` now reports what the dying
runs were facing, and the answer was that they met **4.2–4.6 creatures where the average
draw is 3.0**, a fact invisible in "fights cleared" and invisible to the budget. Two
warnings for the next reader. **The +19 clears are not the whole game getting easier** —
the cap binds only at levels 1–2 and is byte-identical from level 3 — it is more runs
getting *past* the wall into a back half that was already a victory lap, which is now the
top design problem: 84 of 120 runs clear everything. And **the median is useless here**:
it was pinned at 30 before this change and after it, so the shape line is the only figure
that moved. Read `shape:`.

**The back half grew teeth the same day, from the same diagnosis run at the other end.**
With the wall down, 84 of 120 runs cleared everything, and instrumenting *why* found the
mirror image of the wall: the count draw was uniform over 1..max, so two fights in five
put one or two creatures against four coordinated characters — and those fights are free
(measured per-count: a lone creature ends with the party at **89%** of its hit points, two
at 83%, five at 70%, because focus fire deletes a lone creature's whole action economy).
Same root cause as the wall, other side: **XP prices worth, not simultaneity.** Two
changes, both count bounds, neither touching the printed budget:
`EncounterBuilder.MinimumFor` — from level 3, a fight of four characters aims for at
least two creatures (level 1–2 keep their floor of one; the fragile opening was fixed by
lowering its ceiling, and raising its floor would claw that back) — and **a boss fight
fields an escort**: a KillLeader rung builds at least three creatures, because a lone
marked leader compounds "ends when one creature dies" into the easiest fight on the
ladder. Measured on seeds 1–120, cumulatively: clears **84 → 76** (floor) **→ 72**
(escort), middle runs **25 → 33 → 35**, the opening untouched (died-by-fight-4 11 → 13,
inside the documented ±2). The day's whole arc, same seeds, same instrument: **35 / 31 /
54 in the morning, 13 / 35 / 72 now** — the early deaths quartered, the middle the
second-largest group for the first time. Still open: 60% of runs clear everything, and
the per-band line the instrument now prints (hp-left is flat ~78% in every band) says
late fights still are not close; the next teeth must come from somewhere other than
count, since four-plus-one is already the ceiling the action economy tolerates. The
median measured nothing through any of this — it has been pinned at 30 for three slices.

**Not every rung is a deathmatch any more, and the two that are not were measured apart.**
`EncounterObjective` is what ends a fight: `Defeat` (last side standing, the only rule
there used to be), `SurviveRounds` and `KillLeader`. Two rungs of every five carry one —
the High milestone is a boss fight where the **dearest creature by printed XP** is marked
and the rest break off when it drops, and one routine Low rung is a three-round holding
action. Three readings are on the type: **an objective can only ever win**, never lose, so
being wiped out still loses whatever it says and `CheckForCompletion` settles the
last-side-standing question *first*; **the other side's objective is always Defeat**, which
is what makes "survive three rounds" a different fight rather than a shorter one; and
**rewards did not move**, because `GauntletRun` already paid from `fight.Built.Monsters` —
the encounter as built, not the corpses — so a fight won by outlasting an enemy that walks
away pays exactly what killing it would have. The policy plays to them: `FocusTarget`
returns the marked leader for the objective's own side, because a doctrine that kept
picking on threat-per-hit-point would win a boss fight only by accident. Measured on
seeds 1-120 against the same build's 24/54: **Survive(3) alone 26/59, KillLeader alone
30/65, both 30/65** — the boss rung is the whole effect and the two do not add, because
**the median saturated**. That is the failure the instrument was warned about one slice
earlier: at 65 clears of 120 the median pins to 30 and can never move again, so the
`shape:` line is now the figure to read. **Objectives bought variety and cost difficulty**
— the back half got easier (middle runs 31 → 18) while **the opening did not move at all**
(died-by-fight-4 35 → 37), which is the level 1 wall being untouched by anything that
happens after it. Wiring them also found a real gap: `CheckForCompletion` was called by
Move, Attack and death saves but **never by the casting path**, so a spell that killed the
last enemy left the fight running until something else asked. A turn boundary now asks —
but only about the objective, because asking the whole question there ends the fight before
a downed character's turn begins, and their turn is where the Death Saving Throw and its
natural 20 live. The test that caught that is `ADyingCharacterRollsADeathSaveAtTheStartOfItsTurn`.

**Automated runs still lose, and the pacing history is worth reading as a whole.** Measured
over the same 40 seeds throughout (an unrecorded set — and 40-seed medians carry about
±2 of noise, a #132 finding this table predates), median fights cleared of 30:

| After | Median | Best | Runs reaching level 4 |
| --- | --- | --- | --- |
| the old every-third-fight-is-High ladder | 2.5 | 14 | — |
| #65's milestone shape | 4 | 23 | — |
| #72 potions | **7.5** | **29** | 5/40 |
| #52 livestock excluded | 6 | 18 | 0/40 |
| #75 aquatics excluded | **4** | 19 | 1/40 |
| ASI implemented | 4 | 19 | 1/40 |
| Weapon Mastery | 4 | **30 — cleared** | 1/40 |
| #85 policy casts on value | 6.5 | 29 | — |
| Subclasses (first three features) | 6.5 | 30 — cleared | 6/60 at L4 |
| Upcasting + cantrip upgrades | 6.5 | 30 — cleared | 7/60 at L4 |

**Two runs of forty have now cleared all thirty rungs**, which nothing had ever done
before Weapon Mastery landed: Sap and Vex fired 879 and 838 times across those forty runs,
and a Rogue whose Vex feeds its own Sneak Attack is a different creature. The median is
unmoved at 4 because the distribution is not a hump — most runs still die in the first
cycle, and the ones that survive it now go all the way.

**Why runs end where they do was measured rather than guessed, and the answer is not the
ladder.** Closing #79 meant testing its premises, and all three failed. The ladder's
arithmetic is *correct* — walking it and awarding each rung's budget reaches level 2 at
rung 5, level 3 at 10, level 4 at 18 and level 5 at rung 29, ending on 7,700 XP against
the 6,500 needed. Reshaping it does nothing: a gentler opening, starting at level 2,
starting at level 3 all land within half a fight of the median 4, and **starting higher is
worse** (best run 30 → 14), because **the XP budget re-prices every encounter against the
party's current level — the difficulty is scale-invariant and there is no pacing lever to
pull.** What the deaths actually say: **109 of 200 runs die at level 1**, in the first
cycle, and **Moderate rungs kill 120 to High's 33** — the routine fights, not the set
pieces.

**The cause is #83, and it is the one number that should worry a reader of this file.** The
budget prices a fight assuming both sides are whole. **The monster side is** — `MonsterPool`
admits a creature only when every Action entry is fully modelled — **and the party side is
not**: the Cleric holds nine spell slots at level 5 and can spend four, knows 4 spells of
the 109 on its list, and no character has a subclass at all. So a "fair" fight is priced
for a party that does not exist. The corroboration is Weapon Mastery: one modest
party-side feature took the best run from 19 to 30 and produced the first clears, while
every ladder change measured did nothing. **Party power is the only lever that has ever
moved this, so do not tune XP or the ladder against these numbers.**

Two more things in that table are worth more than the numbers. **The two plausibility fixes cost
as much pacing as potions bought** — 7.5 back down to 4 — and neither PR measured it,
because both looked cosmetic. They are not: a Camel or a flopping Piranha was *XP the
budget spent on something that could not hurt anybody*, so removing the chaff means every
encounter's full budget now goes to creatures that fight. **A pool change is a balance
change.** And **the Ability Score Improvement moved nothing at all**, because a run
essentially never reaches level 4 — which is #79, and the reason half this tier's content
has never been seen in play. Every figure is still a floor rather than a verdict, because
`SimpleTacticsPolicy` is playing the party.

**A second measured series exists, on the recorded seeds 1–40** (methodology fixed
at the #106 close-out, after the original table's seed set turned out never to have been
written down): pre-#106 median **3**, terrain+cover **3.5**, policy-uses-cover **4**,
creature cover **3.5**, Revivify **3.5**, OA-aware movement **4** — and then
**coordinated focus fire (#123) took it to 8**. The mechanism is the one the squad-AI
research promised: a dead enemy loses its whole action economy, so the party converging
on the most threat-per-hit-point kill beats the same attacks spread — and only the
party consults the doctrine, so the whole gain lands on their side. The gain is real,
but read the next paragraph before trusting any of this series' absolute numbers.

**The instrument is now code — `tools/PacingMeasure` — and re-baselining it (#132)
rewrote what the series' numbers mean.** The seeds 1–40 series above was measured on a
session-scratch harness that never passed `CompleteFight` a random source, so **no
potion or magic item ever dropped** — an instrument nobody chose, recovered from an old
scratchpad and reproduced exactly. Re-measured across three fresh 40-seed sets on one
build, its no-loot medians read 8, 4.5, 4 and its loot medians 4, 4, 6: **a 40-seed
median carries about ±2 of noise, not the ±0.5 this file used to treat as the floor**,
because the distribution is lumpy — of 120 loot-form runs on current `main`, 47 die by
fight 2, 18 clear exactly 8, and exactly one clears 5, so a 40-seed median teeters
between lumps. The canonical form is now **loot on (the game the player plays), seeds
1–120**: `main` *at that time* read **median 6, best 23, 6 of 120 runs reaching level 4**
(a 2026-08-14 figure, kept because the re-baselining argument rests on it — the current
build reads 30/72/93, see the status table),
and focus fire re-measured under it is **4 → 6** (no-loot: 4 → 5) — the series'
recorded "4 → 8" was half effect, half outlier. The #124/#125 dismantling ladders were
controlled comparisons on the retired instrument and their *verdicts* stand — every
wiring cost or tied, none paid — but the 8 they defended belongs to it. Measure any
future slice with the tool, bar and result from the same command, seeds written down:

```bash
dotnet run --project tools/PacingMeasure -- --seeds 1-120
```

(`--no-loot` reproduces the retired form for continuity; `--seeds a-b` picks the range.)

**And #124 measured the opposite lesson, term by term, before shipping none of it.**
The screening behavior — front liners standing in enemy lanes, the back rank holding
behind its screen, the Support engaging only what breaches — was built whole and then
dismantled under measurement, every variant deterministic on the same seeds: full
discipline **3.5** (the median halved), lane demoted to a tiebreak below closing **4**,
the Support's engagement restriction removed **6**, the hold-behind-screen ordering
removed **6.5**, and only with the last term — a lane *tiebreak* below Distance — gone
did it return to **8**. Even the mildest positional preference costs pacing in fights
this small, because the fastest way to protect a back rank at this scale is killing
things, which focus fire already coordinates. **Holding a position needs a reason to
hold** — a trigger, which is what engagement phases (#125) are — and #124 therefore
shipped as infrastructure only: `PartyRole` (from the kit, healing outranking all),
`EnemyLanes` (each enemy's cheapest path to the back rank, the asker lifted from the
board so its own body does not divert the lane it wants to stand in), and
`ScreenDistanceFeet`, all tested, none yet consulted by movement.

**#125 then built the trigger and measured it out too, which settles the question #124
left open.** A HOLD/COMMIT phase with a paying entry condition — the party's expected
ranged damage per round against the enemies', per the base-of-fire doctrine — was wired
into movement six ways on the same seeds, against main's 8: hold on any positive ranged
margin with front liners standing the lane, **4**; requiring the enemy to have no
ranged answer at all, **6** (an enemy with one bow otherwise duels from spawn while the
party's melee idles indefinitely); the screen placed forward as an interceptor, **3**;
the ranged-members-hold half alone, **8** — because a bow always reaches from spawn,
that half is a no-op, meaning *every* cost was the front line idling. The shipped form
prices holding honestly — the ranged margin must exceed the front-line output holding
idles, and contact must not be one enemy move away — and it never fires for the
pregenerated party (median exactly 8, policy byte-identical), while a ranged-heavy
created party it does fire for measured 3 against its own baseline 3. **The structural
finding: the sides start one move apart, so there are no standoff rounds for a phase to
spend, and holding is always a donation of melee output.** `EngagementPhase`,
`PartyDoctrine.Phase` and `RangedThreatPerRound` ship tested and unconsulted; a longer
battlefield or a monster doctrine that itself holds (#127) is what would give them a
theatre.

**And #126 — composition-aware doctrine — moved the median again, 6 → 8, with the
canonical instrument's first full clears.** The whole gain is one term: **a party whose
healer is down or dry fights more carefully** — `PartyDoctrine.HasHealer` reads the
side's *present* shape (a living character with a healing or revival spell *and a slot
left to cast it*), and without one, Second Wind and the potion in the pack fire at a
third of hit points gone rather than half. The mechanism is the death spiral itself:
most runs die in casualty cascades after the Cleric drops, which is exactly when the
cheaper remedies were being saved for a "badly hurt" that arrived too late. The second
term — **an area slot waits for a clump** (early in the fight, several enemies standing,
a slotted area spell that would catch fewer than two is held; patience expires after
round 3) — measured neutral where measurable, and ships wired under the Revivify
precedent: its customers, level 5 Fireballs and created AoE parties, sit mostly beyond
the automated instrument's reach, and it costs nothing measured. The issue's third
term, "a party with no ranged damage skips HOLD", was already true by #125's
arithmetic. A composition change to a *melee-heavier* party (Rogue swapped for a
Wizard) reads median 3 on both builds — party shape dwarfs doctrine, still. And the
measurement surfaced a genuine engine-adjacent bug fixed on its own branch first: **a
fight that could not end** — a wall pocket whose one doorway was plugged by an
unconscious character that `EnemiesOf` hid from every targeting path — now resolves,
because a *stuck* turn (nothing in reach, nowhere better to stand) attacks the nearest
downed enemy rather than idling to the round limit, with a boundary test pinning that a
creature mid-approach never diverts to stomp the fallen.

**And #127 closed the series by making the split real, spending pacing on purpose.**
`MonsterDoctrine` is the monsters' half: a Pack Tactics creature takes the enemy an
able packmate already stands beside — the exact condition the engine pays Advantage
for, so for a pack, flanking *is* focus fire — and a stat block with Intelligence 8 or
better (the stated threshold: the bottom of the humanlike range) converges through the
same `Converge` core the party uses, while everything dumber stays greedy-simple,
because a Boar should feel dumber than a squad. Monsters get none of the party's squad
judgements — no phases, no healer awareness, no patience — deliberately. Measured
per-band on the canonical instrument: the pack flank costs the party **nothing** on
median, tactical convergence costs **2** (8 → 6), and the tail counts (best, clears,
level 4) swing between builds the way #132 warned small tails do. The frozen
transcript did not churn — its hand-authored fighters now converge and choose the
same targets those bytes always recorded — and one fixture (the sidestep corridor)
had to make its hand-authored archer explicitly dumb, because at the test default's
INT 10 the doctrine re-aimed it mid-scenario.

**The run has an economy now, and it is the party-power thesis operating as designed.**
A cleared fight pays **one gold piece per ten points of the defeated monsters' printed
XP** — a stated rate like the loot table's, since the SRD prints monster XP and
equipment prices but no link between them — into a shared purse that rides the save,
and **each Long Rest a merchant sells mundane weapons, armor, shields and Potions of
Healing at their exact printed prices** (`Shop`, in `Game`). An offer must improve its
buyer with the resolver as judge: a purchase is a draft change re-resolved (the loot
pattern), gear outside the class's printed proficiency lines is never offered, and the
resolved sheet must come out strictly better — AC or same-kind damage up, neither the
other nor Speed down. The resolver also refuses a draft pairing a shield with a
Two-Handed weapon — "requires two hands when you attack with it", and a donned shield
is strapped to one — a gap that held silently until the shop sold Brenna a Maul to
carry beside her shield, because nothing before the merchant ever handed a
shield-bearer a two-hander. The Speed clause and the Barbarian are the gate's own lesson:
Chain Mail *is* a legitimate offer for an unarmored 14, and Heavy armor's Fast
Movement cost is what the AC number cannot see. The auto-buyer (`Shop.AutoBuy`, biggest
improvement first, then potions to a cap of 2 per member) runs in the canonical
instrument at every Long Rest, and the measurement above is its verdict: the median
held while **full clears went 2 → 14** — gold compounds for whoever survives to spend
it. Magic items deliberately stay loot-only: the SRD prices mundane gear to the copper
and prints no price for a wand.

**What does not exist yet.** `SimpleTacticsPolicy` is still a placeholder, but no longer a
naive one: characters converge on `PartyDoctrine`'s shared kill — most threat per hit
point left, falling back to their own reach when the focus target is beyond it, walking
at the same kill when nothing is — while monsters hunt what `MonsterDoctrine` says they
are (a pack flanks, a tactical mind converges, a beast charges); it heals a fallen ally,
rages, spends Second Wind, drinks and administers potions, casts when its weapon cannot
reach, reaches for a
limited-use entry — a thrown Rock, a breath weapon — when nothing else does, never one
whose area would catch its own side, and uses cover: a square the target has Total Cover
against no longer counts as a firing position, a sidestep that clears a wall is taken
even when it does not close distance, a legal-but-penalized shot — the target behind an
ally or a low obstacle — is worth a step sideways before anything is spent, a shooter
avoids ending beside an enemy because the engine would put that roll at Disadvantage,
and among what remains the clean shot outranks shelter, which outranks closeness. Since
#122 every walk also knows what an Opportunity Attack costs: destinations are scored
with the expected damage the pathfinder's route provokes (each distinct enemy once — a
Reaction is one per round — at its hardest melee attack's average), a provoking
"sidestep" is refused outright because the swing costs more than the +2 it saves, and
closing through provocation stays possible because the cost is a preference among
candidates, never a veto on moving at all. Measured: median 3.5 → 4 over the recorded
seeds — modest because both sides got smarter at once.

**Both bugs the display found are now fixed, and the second one was costing real
pacing.** **#164**: the Armor table's Strength score is enforced — "the armor reduces
your Speed by 10 feet unless your Strength is equal to or greater than that score",
checked against the *resolved* Strength so a background increase or a Belt counts, and
stacking with Fast Movement's own Heavy-armor gate rather than replacing it. Three suits
print one (Chain Mail 13, Splint and Plate 15), and the stall's existing Speed clause now
quietly stops selling Plate to a Strength 13 Cleric while leaving Chain Mail — which asks
exactly 13 — as the offer that armors him. Measured alone: medians unchanged, clears
33 → 31 canonical and **26 → 31** fresh, so a printed penalty cost nothing.
**#165**: `Shop.AutoBuy` no longer takes a swap that drops a mastery the character has
unlocked, because `Score` is average damage and cannot see a property — it had been
selling **Cleave**, a whole second attack, and the Rogue's **Vex**, which feeds its own
Sneak Attack, for one point of average damage apiece. The stall still lists those swaps:
a player can read the mastery line and choose, which is what the display is for. The two
together took clears **33 → 40** canonical and **26 → 34** fresh, and level-4 runs
44 → 51 and 36 → 44 — the auto-buyer really had been downgrading the party at every Long
Rest since the economy landed.

**The board answers to the keyboard too**: arrow keys walk a cursor over the squares
and Enter acts on it — through `ActivateSquare`, the *same* path a click takes, because
two ways of playing that decide separately are two places to disagree. **Attacking is a
button now** rather than knowledge that clicking the board works; with one attack it
arms targeting straight away and only a real choice opens the menu.
**The controls answer to the keyboard and show only what can be used.** `TurnOptions`
in `Game` decides both, so the two clients cannot drift: each action carries a key that
is **unique across the whole set**, so D is Dodge whenever Dodge is offered and never
anything else, and the row shrinks as a turn is spent — Dodge and Dash leave with the
Action, Second Wind with the Bonus Action, Action Surge appears only once there is no
Action left to surge past, Stand Up only while Prone. **This reverses the client's first
stance**, which drew everything and let refusals teach the rules; the status line still
reads `Action ✓ Bonus ✗`, so a row that has shrunk still explains itself, and the engine
still refuses anything that arrives by another road. The duplication of the engine's
refusals is the real cost and is guarded from the direction that hurts: a test asserts
that whatever `TurnOptions` hides, the engine refuses. The grid also **fogs every square
the acting character has Total Cover against**, since that cover refuses an attack, a
spell and an area alike. And `AttackChoice` no longer fires a bow point blank: a ranged
roll within 5 feet of an enemy has Disadvantage, so a penalised attack sorts below every
unpenalised one and the Rogue's blade wins the tie its bow used to take alphabetically.

**The row explains itself on hover, and ends itself when it is empty** (both 2026-08-15,
both asked for during the play session). Rest the pointer for two seconds and a panel
appears beside it: on a button, `TurnOptions.Hint` — what the action costs and what it
changes, in `Game` beside `Caption` for the reason `TurnBanner` is, so two clients cannot
word a rule differently, with a test asserting *every* action has one and that it is not
just the caption repeated; on a creature, the `TurnBanner` lines, so an enemy can be
sized up before anything is committed. The clock restarts only on real movement (three
pixels of slack, because a resting hand is never quite still), a click dismisses rather
than leaving the hint over its own result, and the panel flips above the pointer near the
window's bottom edge. **Arming an action aims it, and Tab walks the rest.** Every road into targeting goes
through one method, so the cursor is never left wherever the last action put it — it lands
on the nearest thing the armed action could be used on, and Tab cycles the ring, wrapping
round. Who is a candidate is `TargetChoice` in `Game`, beside `AttackChoice` and with the
same standing: **a convenience, not a rule.** Every predicate reads the engine's own
numbers rather than restating one — an attack's reach is `CombatAttack.CanReach`, the very
method `Encounter.Attack` refuses on, so the offer and the refusal cannot disagree — and
where a judgement is unavoidable it is the *generous* one, because a candidate the engine
then refuses costs a refusal message while one wrongly omitted costs a player a move they
never saw offered. Ties break on identifier so the ring is the same every time. **And a turn holding nothing but the way out of it ends itself** —
asking a player to click End Turn when the row holds only End Turn is asking them to
confirm a decision they were never offered. It first shipped gated on leftover movement
too, on the reasoning that `TurnOptions` is the buttons and **walking is not a button**,
so a row holding only End Turn says nothing about whether the character can still
reposition. Sound reasoning, wrong behaviour, and **the play session caught it within a
fight**: *attacking spends the Action and never the movement*, so a character who swings
from where they stand keeps a full Speed and every such turn — nearly every turn — still
had to be dismissed by hand. The row is now the whole question. The cost is stated rather
than hidden: a character who attacks *before* moving no longer steps away afterwards,
which is the XCOM convention and predictable, where a rule that sometimes ends the turn
on a number the row never showed is not. Anything half-started — an armed attack, an open
menu — holds the turn open. It is paced like any other turn rather than snapped through, and gated behind the
act queue, so the last blow's animation always finishes first. The probe now hovers a
button and captures the hint (`play-2b-hint.png`), through the real input path like every
other probe step.

**The screen says who is acting, and with what.** `TurnBanner` in `Game` composes it —
name, class and level when the actor is a character, armor class, hit points, and each
attack with its damage expression, a conditional component saying when it applies so
the Goblin Warrior's Advantage die is never shown as certain — in one place for the
same reason `OfferEffect` is: two clients formatting it separately would be two places
for it to drift. The Godot screen draws it under the grid for whichever side is up
(the class name rides `CombatantFeatures.ClassName`, put there by `FromCharacter`,
because a combatant otherwise has no road back to "Fighter"), and the console prints
the same lines in its turn header, where the banner's attack line replaced the bare
attack-names list it used to print.

**The log is colour-coded, off the fight's own names.** Party names blue, monster names
orange, the named thing being used — weapon, spell, feature, mastery — violet, damage
bright red and a miss yellow, with round-beginnings and the fight's end still gold.
`LogHighlighter` in the client builds its terms by *asking the encounter*: the
combatants' names, their attacks', their spells', their stat-block entries', and their
features' (off the `ClassFeature` enum, whose PascalCase is the printed name). **Nothing
parses the narration's grammar**, which is the point — a reworded sentence loses a
highlight rather than gaining a wrong one. Damage and the miss are matched as text
because neither is a name, and both fail the same safe way: no match leaves the line in
its base colour.

**The board can wear real art now, and the field fills the window under a camera
(2026-08-18).** The square is no longer a constant: `CellPixels` on `FightScreen` is the
camera's zoom, glided each frame toward framing every living combatant with padding and
a lean toward the actor — zooming in as the fight clumps and as far out as the fight's
spread needs — framing everyone outranks filling the window with playable tiles, a
floor that once existed and left a split fight's combatants cut off at the window's
edge — so the visible field is a moving window onto the whole one. The view may overscan a field edge by one square past
the *stage* (the ground between the overlays) — the first clamp held the whole window
on the field, and the first fight to reach an edge played out underneath the banner
strip because at the zoom floor that clamp pins the camera still. **The ground art runs
to the window's edges whatever the camera does** (also asked for from play, the same
day): `DrawGrid` lays terrain on every square the window can see, and the squares
beyond the field wear `BeyondFieldWash`, so the playable field reads at a glance while
the view is always full of battlefield and never of void. Rule washes and obstacles
never appear beyond the field; the bare no-art fallback keeps its old flat-colour
board. Every other element — heading, initiative, log, banner,
buttons — floats over the field on the shared translucent `Veil`, board space is told
from UI space by `GridLeft`/`GridTop` (derived from the camera) versus `UiLeft`/`UiTop`
(fixed), and a click on an overlay never reaches the square underneath it
(`OverOverlay`). The wheel zooms about the pointer and a middle-drag pans — manual hold
ends when the next act or turn starts — and wheeling all the way out shows the whole
field, the one framing automatic never picks. A probe or capture snaps the camera and
frames the whole field, for the same reason those runs freeze the animation clock. The tokens draw
as animated pixel-art figures: an idle loop, the walk cycle playing as the token glides
the engine's recorded path, a swing for every attack (Opportunity Attacks included,
faced at the target), a flinch as damage lands, and the body going down when a creature
drops — all queued in log order so each holds the next beat, from the free Craftpix
character packs, mapped in the client's
`SpriteLibrary`: party art by class name (all twelve classes covered), monster art by
**exact** stat-block name (goblins, skeletons, zombies, the Gladiator, the Knight, the
Mage, the Priests, the Scout, and only the two dragon colours the packs actually hold —
a red sprite on a Green Dragon Wyrmling would be the display lying). **The PNGs are
deliberately not in the repo**: Craftpix's free license permits use in a game but not
redistribution, and the repo is public — the same line the SRD PDF sits behind, and the
same fallback shape: `client/assets/sprites/` is gitignored, a machine without it draws
the circle-and-letter tokens it always drew, and `--probe`/`--capture` freeze the
animation clock so a verification image cannot depend on when the frame was taken. See
the client README for where the packs come from and where they go.

**A single drawing is a complete token, and a resting token faces the nearest enemy
(2026-08-16).** Two changes from one request. First, `LoadStrip` reads a sheet as frames
of height × height across, so anything *narrower* than it is tall was rejected outright —
which is exactly the shape of hand-drawn art for one creature, and meant such a file
loaded as null and kept drawing a lettered circle. A narrow sheet is now read as one
frame and padded to square, **horizontally centred and not vertically**, because the
packs are canvas-aligned with the feet on the bottom edge and every metric in
`SpriteLibrary` is measured from there. **Fifteen creatures ship this way** — Gnoll Warrior, Black
Bear, Brown Bear, Giant Wasp, Dire Wolf, Giant Eagle, Giant Hyena, Ape — chosen because
they are among the pool's most-drawn and every one was a bare circle beside a party in
full animation. **The asymmetry between narrow and wide art is the thing to know**: a
sheet narrower than one frame *cannot* be a strip, so padding it is an inference rather
than a guess and the loader does it; a *wide* sheet is genuinely ambiguous, since 640×128
is a five-frame walk and 64×46 is one drawing. The tempting rule — "a strip's width is an
exact multiple of its height" — was written, then thrown away on finding its false
negative already in the assets (`Wanderer Magican/Charge_1.png`, 576×128, four and a half
frames wide), which is this file's oldest lesson about heuristics arriving from a new
direction. Wide drawings are therefore squared **on disk** at install, bottom-aligned and
centred; unpadded, they load as a one-frame strip cropped to the left edge — most of a
wolf. **Stature is drawn rather than normalised, and that is a look rather than a bug.**
`NominalStature` is 64 and the board uses one shared pixel scale, so a figure drawn
taller simply is taller — `ScaleFor`'s oversize ceiling only engages near 96 pixels. The
installed set runs 38 (Giant Eagle) to 92 (Hobgoblin Warrior) with the humanoids mostly
at 60-67. **The hobgoblin was rescaled to match its neighbours and then restored**, on
the instruction that the art is the author's call and sizing would be handled later:
worth recording, because "the numbers disagree" is not on its own a reason to alter
somebody's drawing, and the same reflex had earlier declined a goblin drawing purely
because an animated pack existed. **Fifteen creatures now carry hand-drawn stills**,
including Goblin Warrior and Scout, which took drawings over packs by preference.
The second install-time step is the **flip**: these four were drawn facing left and
the convention is that art faces right, so a monster squared up to the party would
otherwise be mirrored away from it. They do not
animate and do not need to: each pose already falls back to Idle. **Goblins were asked
for and deliberately not taken**, because `Goblin_1/2/3` already cover all three goblin
stat blocks with full animation, and a still frame would have been a downgrade.
Second, **facing**. It ran off the token's *side* — monsters faced left, the party right
— which is right only because the sides spawn in columns, and wrong the moment anyone
walks past anyone; a creature standing east of the character it was about to bite was
drawn looking away from it. The swing already faced its victim and a walk faces its last
horizontal step, so `RestingFacesLeft` is the third case: stand still and look at the
nearest living enemy. Ties and a shared column keep the old side default, because a
figure drawn edge-on has no better answer and flipping on a tie makes tokens twitch as
others move around them; the dead are not looked at, the downed are.

**The whole board animates off one clock, at ten frames a second.**
`FightScreen.AnimationFramesPerSecond` is the single knob: idle, walk, swing, flinch and
fall all advance at that rate, a pose lasts as long as its own frames take at it (a
five-frame Goblin swing is half a second, a fourteen-frame Priest attack a second and a
half), and even the ground speed derives from it — a square costs the paces that cover
it, two fifths of a second, so a thirty-foot move is about two and a half. It shipped at
six, which was measured at four seconds for a full move and played too slow. Each of those was its own number before,
and they disagreed: idle ticked at eight a second, a walk cycle at twenty, and a pose was
squeezed into a fixed duration whatever its length, so the Priest's attack flickered past
at thirty frames a second while the Goblin's ambled at eleven. `SecondsPerTurn` is
deliberately outside it — that is the gap when nothing is animating, and dead air should
not grow with the animation.

**The log waits for the picture, and the walk is slow enough to see.** Both came from
playing it: a move was over in a third of a second — thirteen frames for five squares,
measured — which reads as teleporting rather than walking, so a square now takes a fifth
of a second to cross and the walk cycle advances with the *distance covered* rather than
a timer, which is what stops legs skating when either speed changes. And the narration
used to arrive ahead of its own animation, because the engine resolves an attack whole
the instant it is asked: roll, damage and death are all in the log before a frame of the
swing is drawn, which makes the animation decoration rather than the event. Each queued
act now remembers the log line it is the picture of, and the log is held there until the
act finishes — the rolled result and the damage print together on the swing's last
frame, with an act's *consequences* (Damage, Died, Downed, Condition) released alongside
it because they are one moment of the fight. Lines are delayed, never reordered or
dropped, and anything with no animation to wait for appears at once, the probe included.
**The tokens wait for the picture too (2026-08-15, from the first live play session).**
Holding the log was only half of it: the board itself drew from live state, and the
engine resolves a monster's whole turn the instant it is asked — so a player their attack
would fell was drawn on the floor, hit points empty, *before the monster took a step*,
and the walk, swing and fall then played over a corpse. `WithHeldAppearances` is
`WithWalk`'s idea applied to consequences: when a Damage, Died or Downed step is queued,
the victim's *shown* token — hit points, posture, conditions, never position — is held as
it last drew, and released the moment its flinch or fall act starts, so the hit points
drop as the flinch plays and the body drops when the fall does. The order on screen is
now the order of the fight: walk, swing, damage on the swing's last frame, then the fall.
Holds clear when the act queue drains (which also covers a victim whose art lacks the
strip), on scrub, and on a slice with nothing to animate; the probe never engages them,
because a capture read the instant after a click must show final state.
**The battlefield wears the terrain packs, and the grid lines are gone (2026-08-15).**
`SpriteLibrary.GroundTheme` is one look — a 16-pixel ground tile, a wall's scenery and a
low obstacle's — and one is chosen per battlefield **from the field's own shape**, so a
fight always redraws the ground it had and the next one differs. **The ground is a *set* of tiles, not one**, cut from the packs' own tilesets and chosen by
*seam continuity* — how well a tile's opposite edges match, so it repeats without seams —
and then by grain. That second filter is the one worth keeping: a tile with a distinct
motif (the mossy stone's green clumps) tiles seamlessly and still reads as **wallpaper**,
because the motif lands in the same place every sixteen pixels and the eye finds the
lattice. Fine cobble and gravel do not. The first attempt used flat single-colour fills to
dodge the problem entirely and was simply worse art; the ground recedes and the scenery carries the scene. That is also why the grid lines went — they were how a
*bare* board showed its squares, and over real ground they are a mesh laid on a picture;
squares stay legible from the cursor ring, the reachable highlight and a token centred in
its cell. **Difficult terrain keeps a wash over its tile**, because art may not cost a
player the one thing the square was telling them, and a wall says it with a tree filling
the square where a low obstacle says it with a bush that plainly does not. With no art
installed every square falls back to the flat colours and the outline it always had.

**Ranged attacks throw something across the board (2026-08-15).** `CombatStep.Ranged` is
a `RangedAttackKind` — None, Weapon or Spell — set from the engine's *own* predicate
(`CombatAttack.IsRangedAttackRoll`, the one the printed close-combat Disadvantage hangs
on), recorded for the reason `Path` is: **so no client works it out.** A client reading
the gap instead would call a Halberd's ten-foot reach a shot and would still have to know
which of an attacker's attacks was swung — both pinned by tests. Weapon and Spell are told
apart *here* rather than downstream because the only other way for a client to know is to
read the narration, and this project does not parse its own prose. The client flies the
art: an arrow for a weapon, a bolt for a spell, rotated along the flight (both sheets draw
their projectile pointing right, the same convention the walk's facing rests on), drawn
after the tokens so it passes in front, at a speed derived from
`AnimationFramesPerSecond` and floored at two frames so a point-blank shot is still seen.
The art lives in `client/assets/sprites/Projectiles/` — its own folder rather than
borrowed from the Skeleton Archer that ships the arrow, since tying a Rogue's shortbow to
a monster's presence on disk would be absurd — and, like every sheet, it is optional.
**The reveal order needed a second look**: with something in flight the swing earns only
its own line, because the roll is settled when the bow twangs but the damage is the
picture of the arrow *landing*. Verified by watching it — a temporary probe burst with
animation on, capturing fourteen frames across one shot; the permanent probe still runs
with animation off, so **nothing guards this from regressing** beyond the engine-side
tests.

**And the tokens draw in layers, from the same session.** Two combatants really can share
a square — `MovementRules` counts only creatures that are *not dead* as occupying, so a
corpse lies flat and is walked over — while `DrawTokens` iterated in initiative order, so
which of them landed on top was whatever the dice had decided that fight: a character who
stepped onto a fallen goblin was drawn *behind* it. Dead draw first, then the downed, then
the living, stable within each layer so everything else keeps initiative order. It settles
the gliding walker's overlap of squares it merely passes through, too.

**Three findings from measuring the art are worth not rediscovering, because each was a
bug the obvious approach shipped.** *The packs are canvas-aligned*: across every strip
the game draws, the figure's feet sit on the canvas's bottom edge, so a character is
measured **once** — from the strips in which it stands — and every strip is drawn
through that one transform. Measuring each strip on its own and centring it, which is
what the first slice did, both deletes the motion the artist drew (a Knight's swing
lunges twenty pixels forward) and *changes the figure's size mid-animation*, because an
extended sword widens the box the body is scaled to fit. *One pixel scale serves the
whole board*: the packs are drawn at the same resolution, a standing human being 64
source pixels whatever its canvas, so scaling everyone by the same ratio keeps the art
coherent and keeps a goblin correctly shorter than an orc — only a creature too big for
its square is cut down, and the ratio is snapped to a quarter step so enlarged pixel art
does not crawl. *A death strip's last frame is not a corpse*: every pack ends by sinking
or fading the body away, so holding the final frame left a killed goblin as a
seven-pixel smear; the body settles instead on the fullest frame in which it is actually
down. And the measurement that matters more than any of them: a **crouching idle** (the
Wild Zombie kneels to feed, and walks on all fours) is why stature is taken from the
taller of Idle and Walk rather than from Idle alone.

**Movement is visible now.** A `Move` step carries the squares the mover actually
occupied (`CombatStep.Path`, starting square first, cut short where an Opportunity
Attack dropped them) — the engine's own record of the route, so no client recomputes
one — and both Godot screens hop the token square to square, a tenth of a second per
square, instead of teleporting it. The play screen holds the next beat until the hop
lands, the watch screen animates during playback and snaps when scrubbing, and a probe
run keeps the animation off for the same reason its monsters hurry: a capture read the
instant after a click must show the token where it arrived.

**An offer says what it would change, and saying so found two more bugs.** A shop row
used to read `Chain Mail for Brenna — 75 GP` and nothing else, which is a price tag with
no goods behind it. `ShopOffer.Effect` now carries the comparison — armor class before
and after, Speed (so Heavy armor shows its cost in feet), the attack a swap replaces with
both damage expressions, their **ranges** and averages, and the mastery property the swap
silently changes — computed in `Game` off the two resolved sheets, because two clients
formatting it separately would be two places for it to drift. Building it surfaced
**#164** (armor's printed Strength requirement is extracted, validated and never
enforced, so Plate is offered to a Strength 13 Cleric for free) and **#165** (the
auto-buyer sells Cleave and Vex for one point of average damage, because `Score` counts
damage and cannot see a mastery). Both are the same lesson as the played run: *a number
nobody displays is a number nobody checks.*

**A human has now played this game, and it paid for itself in two fights.** On
2026-08-14 the Godot client was driven through the real input path — synthesized clicks,
a screenshot read between each — for the first time. Fight 1 turned up Rage ending on a
missed swing; the Cast menu of that same fight turned up a level 1 Cleric holding three
spells it had no slot for. Fixing the two (#159, #160) took the canonical median from
**6 to 10.5** and full clears from **16 to 33**. Two more things the play surfaced that
the automated instrument never could: **the log truncated its own narration exactly
where the outcome of the roll lived** — fixed by wrapping rather than cutting, since
whether an attack hit is the *last word* of the sentence and an ellipsis reliably ate it
(#161; the wrap helper moved to `FightScreen` so the creation panel and the log share
one) — and **a potion found by a character who
later goes down is stuck with them**, because administering reads the *actor's*
inventory — so loot handed to the squishiest member is loot the party may never drink.
The run itself reached fight 3 of 30 and is on disk; `--continue` resumes it.

**Picking up cold:** `gh issue list` is the work queue, and the order below is not the
order the issues were filed in. Take the top of it.

### Starting on a machine for the first time

Everything needed to build, test and play is committed. There is no content to generate
and no asset to fetch:

```bash
git clone https://github.com/brandonifco/SRD_Combat.git && cd SRD_Combat
curl -fsSL https://mise.run | sh            # once per machine, if mise is absent
eval "$(~/.local/bin/mise activate bash)"   # append this line to ~/.bashrc too
mise install                                # pins the SDK to the one CI gates on
./scripts/doctor.sh                         # confirms this machine agrees with CI
dotnet test SRDCombat.sln -c Debug          # expect 945 passing, 1 skipped by design
dotnet run --project src/SRDCombat.Console
```

**Those four middle lines are the only setup step, and the activation line is the one that
gets skipped** — it looks like shell decoration and it is the step that does the work.
Without it `mise install` still succeeds and `mise current dotnet` still reports the pinned
version, while `dotnet` goes on resolving to whatever is first on `PATH`. That combination
is what a silently ineffective pin looks like from the inside: on 2026-08-13 a machine
reported `✓ mise is managing dotnet 8.0.129` while compiling every target on .NET 10.
**`doctor.sh` is what catches it** — the check that fails is `This repository resolves to
SDK ...`, not any of the mise ones, which is why it is worth running even without mise: it
reports what you *have* rather than what you assume.

Activation is **directory-scoped**, which is the reassurance worth having before editing a
shell profile. `dotnet` resolves to the pinned 8.0.129 inside this repository and to
whatever the machine already had everywhere else, so nothing outside SRD_Combat changes.
Watching it switch is the fastest proof the pin is live:

```bash
cd ~ && dotnet --version && cd - && dotnet --version
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

**Nothing.** #108 closed the last of the cover work: creatures grant Half Cover as the
printed table says, under three stated readings on `CoverRules` — the dead grant nothing
(a fallen body lies flat, the same line `MovementRules` draws), crowds are not walls
(however many creatures the line crosses, creatures alone are Half, because the table
reserves the higher degrees for objects), and creatures never escalate obstacles. The
policy grew the understanding whose absence had deferred the rule: a legal-but-penalized
shot is worth a step sideways *before* anything is spent (`ImproveFiringPosition` —
without it, cover the archer could shoot through was cover it always shot through, since
a successful attack ended the turn before movement was ever considered), a shooter
avoids ending beside an enemy because the engine puts that roll at Disadvantage, and
`ReachOf` counts only attacks the actor could actually swing, so a creature whose thrown
Rock is spent plans like the melee creature it now is. Measured over the recorded seeds
1–40: median 4 → 3.5, best 19 → 21 — within 40-run noise, and symmetric by construction,
since both sides fan out and step around their own allies alike.

**#106 — random terrain and cover — closed just before it**, in three slices, and **its pacing was
measured before closing, unlike the plausibility fixes this file warns about.** A fresh
three-way comparison over seeds 1–40 — the historical table's own seed set was never
recorded, which is why the absolute numbers below do not line up with it and why any
future measurement should write its seeds down: pre-#106 median 3 (best 19), terrain +
cover with the naive policy 3.5 (best 21), policy using cover 4 (best 19). **Terrain and
cover cost nothing and probably pay a little**, which makes sense: both sides get the
same walls, and the party's archers benefit most from the policy learning to sidestep
and shelter. The slices, for the record: `TerrainGenerator` in `Game`
scatters obstacle clusters — walls, or low obstacles on a coin flip per cluster — and
Difficult Terrain patches across every generated battlefield, seeded through
`IRandomSource` so a seed still replays the fight, placed only strictly between the two
sides' columns, and admitted one square at a time under a connectivity check so no draw
can ever wall a side off. And **cover executes whole**: `CoverRules` judges the printed
degrees along the centre-to-centre segment (a wall crossed is Total, one low obstacle
Half, two Three-Quarters, corner touches slip by — the readings are on the class), Half
and Three-Quarters raise AC **and** Dexterity saves, Total refuses the targeting with a
named code on every path (`attack.total_cover`, `spell.total_cover`,
`entry.total_cover`), areas exclude squares behind Total Cover per the glossary, the
Wand of the War Mage's "ignore Half Cover" is real after shipping vacuous, and Sacred
Flame's printed "gains no benefit from Half Cover" rides `SaveEffect.CoverIgnored`,
structured at extraction. And the policy uses it: a square the target has Total Cover
against is not a firing position, a sidestep that clears a wall is taken even when it
does not close distance — without which an archer stood forever behind the one square it
could not shoot past — and among firing squares the best-sheltered one wins, shelter
ranking above closeness because once a square delivers the attack, closing further buys
a ranged creature nothing. The third slice also caught a bug the second had shipped:
**a reach weapon's Opportunity Attack can genuinely cross a wall square**, and the
regenerated transcript printed a hit through Total Cover — `MakeOpportunityAttack` now
declines to swing, the way it declines against a charmer. Creatures granting Half Cover
was deliberately deferred out of #106 into #108 — shipping the +2 before the policy
understood standing behind someone would only have made every ranged attack quietly
worse — and #108 is now closed too, above.

(#96 — ranged attacks in close combat — was found by the play client's probe
and fixed the same day: `RangedAttackInCloseCombat` sits beside `AtLongRange` in
`AttackCircumstances`, "who can see you" is read as any enemy without Blinded — the same
shape of reading Frightened records — and a dual-mode attack inside its reach stays a
melee roll.) The next
work is Phase 5 (character creation), Phase 6 proper (separate monster tactics from party
tactics — one policy still plays both sides), or Phase 7's polish — `client/` now **plays
the whole gauntlet with the mouse** (move, attack, features, spells, potions, refusals
shown with codes, interludes between fights, autosave and `--continue`, the policy
playing the monsters; `--one-fight` for a single encounter, `--watch` for the read-only
screen, `--probe` for its verification loop — see its README), which is the phase's
printed end state met — and the polish list is now empty: area spells aim at bare
squares in both clients (wiring that caught the point path skipping the printed range
check the creature-aimed path always made), the Godot client grew the Attacks menu the
console's `attack <letter> [name]` always had, and a slotted spell can be deliberately
upcast in both clients (`CastSpell`'s `slotLevel` burns exactly the slot named or
refuses with a reason; the console reads a trailing number, the mouse client asks
which level to burn when there is a real choice). And no human has yet played a run
to its end — the client now removes the last excuse.

- **#83 is closed (2026-08-12, after upcasting and subclasses landed) but its direction
  is not finished**: the encounter budget prices a fight assuming both sides are whole,
  the monster side is, and the party still is not — the Cleric executes 6 spells of the
  109 on its list, and most class features past the first few levels stay reported on
  `UnimplementedFeatures`. Party power is the only lever that has ever moved pacing, so
  when the next engine slice is chosen, this is still where the evidence points.

Beyond those the next work is a phase, not a fix: Phases 1–4 are done, Phase 6 is done for
monsters and open-ended for the party, Phase 7 has met its printed end state (the whole
gauntlet plays under the mouse) with polish remaining, and Phase 5 has never been
started. **Do not
read a short queue as "nearly finished":** the ladder has been cleared twice in forty
seeded runs and never by a person, and no human has played more than a few rungs by hand.

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

**The frozen transcript has churned exactly five times** — the fifth when movement
learned what an Opportunity Attack costs (#122) — and every churn was the fixture
catching a real change to how the game plays. Once when the tactics policy learned to
focus fire — where the failure that mattered was not the byte-for-byte diff but
`TheFightExercisesTheHardParts`, which noticed the adventurers now won quickly enough that
nobody went down and the fight covered no Death Saving Throws at all. Once when cover
landed (#106): the skirmish field's middle wall had always been drawn and never mattered,
and the moment it granted Total Cover the opening archer's shot through it was refused, so
both sides' archers had to reposition before shooting. Once when the policy learned to
*use* cover — a regeneration that also caught a shipped bug, a reach weapon's Opportunity
Attack narrated straight through a wall. And once when creatures started granting Half
Cover and the policy learned to step around its allies (#108). The scenario's seed has
moved three times over these, composition unchanged, dice moved — the history is on
`SkirmishScenario.Seed`. Read the diff before regenerating, every time — twice now it has
been the thing that caught what the unit tests did not.

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
allowlist, exactly like `ClassFeatureRegistry`, and holds twelve conditions: Prone,
Poisoned, Grappled, Restrained, Incapacitated, Unconscious, Blinded, Charmed, Frightened,
Paralyzed, Stunned and, since #230's close, **Petrified** — its whole printed page 186:
Incapacitated brought with it, Speed 0, Advantage on attack rolls against it (and note
**no Critical Hit clause** — Paralyzed and Unconscious print one, stone does not),
auto-failed Strength and Dexterity saves, **Resistance to all damage** executed in
`DamageRules.Apply` under the printed order-of-application and no-stacking rules (page
17: Resistance second, Vulnerability third, multiple Resistances count once), and
Immunity to Poisoned as a gate in `Combatant.AddCondition`. Deliberately absent:
Deafened and Invisible, each needing a model (hearing, sight) that does not exist.
**Add a condition there only alongside the code that gives it effects.** Forty-five
attacks satisfy both checks —
20 Prone, 12 Poisoned, 9 Grappled, and one each of Charmed, Frightened, Paralyzed and
Incapacitated and 2 Restrained tied to their grapples — and the failed-save
riders land: 8 Frightened, 5 each of Grappled and Poisoned, 4 each of Blinded and
Restrained, 2 each of Charmed, Prone and Stunned, one each of Incapacitated and
Paralyzed, **plus the two Petrifying Gazes' escalating Restrained → Petrified riders**
— three of the earlier ones riding the stat blocks' own repeat-save clock
(`ConditionDuration.RepeatSaveUpToOneMinute`): the Quasit's Scare, the Doppelganger's
Unsettling Visage, and the Chuul's Poisoned alone under the Whelm precedent, its
chained Paralyzed still refused. The Vrock's Spores stays refused the conservative
way — its poison prints no automatic-success cap, only a Holy Water out the model
cannot express. The
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
state its end within its own sentence — and the rule now has one carved exception,
taken when repeat saves became a modelled way out: the exact printed repeat sentence
("At the end of each of its turns, the target repeats the save, ending the effect on
itself on a success.") is *joined back onto the rider before it* prior to splitting,
the wrapped-spell-line lesson applied to riders, which is how the Quasit's Scare —
this rule's own original example — finally rides. The join is anchored to the exact
sentences, and the duration lands only alongside the printed cap ("After 1 minute, it
succeeds automatically."); a repeat with no clock is still refused. And in an *attack*
entry a "Failure:" sentence belongs to an embedded saving throw — riding the attack
with it directly would paralyze on every hit with no save rolled. That rule has its
structured exception too, closed with #146: the Ghast's Claw carries
`EmbeddedAttackSave` whole — the "non-Undead creature" gate (a creature type the stats
can test), the printed DC 10, and the Failure rider — rolled by `ResolveAttack` after
the damage, its span lifted from the rider pass so the sentences are structured once,
and it made the Ghast the pool's first `Complete` embedded-save creature (canonical
measurement unmoved, 7/30/9/25). The Ghoul's Claw is one word beyond the bar —
"isn't an Undead **or elf**" names a species no combatant carries — and stays refused
with its sentences counted, as do the Cockatrice's tiers, the Death Dog's rest-denying
poison, the Bearded Devil's wound and the lycanthropes' curses. A third rule joined with #22: **a rider behind a deeper failure tier — "Second
Failure: The target has the Unconscious condition for 1 minute" — is refused whatever
its duration**, because the save model rolls one failure and the rider would land a
whole tier early: a wyrmling's breath putting targets to sleep on the first failed save.
That rule is what separates the timers that ride (the Solar's "Blinded for 1 minute",
the Pseudodragon's "Poisoned for 1 hour" — checked by hand against their follow-on
sentences) from the ones that must not (every Sleep Breath). **The tier rule now has
its own carved exception** (#230's close): the exact two-sentence pair the two
Petrifying Gazes print — "First Failure: The target has the Restrained condition and
repeats the save at the end of its next turn if it is still Restrained, ending the
effect on itself on a success. Second Failure: The target has the Petrified condition
instead of the Restrained condition." — is matched to the letter and structured as one
*escalating* rider (`AppliedCondition.EscalatesTo`,
`ConditionDuration.UntilSavedOrEscalated`): the repeat at the end of the bearer's turn
ends the effect on a success and swaps Restrained for Petrified on the failure, in
`Encounter.RollRepeatSaves`. The escalation resolves the repeat either way, which is
why "its next turn" needs no one-shot bookkeeping; a tiered sentence that differs by a
word still falls to the tier rule; and an escalating rider is only imposable when its
deeper condition is executable too (`ConditionRules.CanBeImposed`). And a fourth rule came
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
- **Weapon Mastery is the fifth curated allowlist, and six of the eight execute.** A
  weapon's mastery property reaches the attack **only when the wielder has unlocked that
  kind of weapon** — the printed rule is "usable only by a character who has a feature
  ... that unlocks the property" — so `CombatAttack.Mastery` is null for everyone else.
  Four of the eight execute: **Sap** and **Vex** (a per-creature flag consumed by the next
  attack roll, with the printed expiries — Sap ends at the start of *the sapper's* next
  turn, Vex at the end of *the vexer's*, and reading those possessives backwards would
  swap them by most of a round), **Topple** (a Constitution save at 8 + the attack's
  ability modifier + proficiency, which is why `CombatAttack` carries its
  `AbilityModifier`) and **Graze** (the modifier as damage on a miss, no dice rolled).
  #81 added **Cleave** — its own second attack roll against an enemy beside the first
  and within reach, whose damage subtracts the *positive* ability modifier ("unless that
  modifier is negative"), once per turn, chosen by the engine because declining a free
  swing is never right — and **Slow** (10 feet off the victim's Speed until the start of
  the author's next turn, capped at 10 however many Slows land, and a Slowed Dash gains
  the reduced Speed). The Barbarian's Greataxe finally does what its stat block says.
  **Push and Nick stay refused with reasons on `WeaponMasteryRules`**: Push is a real
  choice a player would sometimes decline (pushing an enemy out of your own reach) and
  the engine models no way to decline, and Nick needs two-weapon fighting, which does
  not exist.
- **Subclasses need no draft choice, and the split is derived, not curated.** The SRD
  prints exactly one subclass per class, so a level 3+ character simply *has* it — the
  Champion, the Berserker, the Thief, the Life Domain. The extraction split rests on the
  printed levels: a class's feature headings climb "Level 1:" to "Level 20:" and the
  subclass's start over at "Level 3:", so **the single backwards step in the sequence is
  the boundary**, with `SubclassTests` asserting the shape (every class ≥ 4 subclass
  features, all carrying levels, minimum 3). Three subclass features execute — **Improved
  Critical** (crits on 19, and the 19 must still beat AC because only the 20 auto-hits),
  **Frenzy** (Rage-Damage-bonus d6s on the first Reckless melee hit each turn), and
  **Disciple of Life** (+2 + slot level on every slot-cast heal). The Thief's level 3
  features genuinely do nothing in a fight — Fast Hands picks locks, Second-Story Work
  climbs — and stay on `UnimplementedFeatures`, with a test asserting exactly that.
- **There is one spell-preparation path, and the pregens used to dodge it.** A draft's
  `ChosenSpellIds` is a *plan* that `SpellPreparation` reads under the printed Cantrips
  and Prepared Spells columns, skipping anything whose level has no slots yet — and the
  pregenerated party used to take a second path instead, reading a curated per-class
  list straight onto the sheet with no level filter at all. **The level 1 Cleric walked
  into fight 1 carrying Hold Person, Revivify and Spirit Guardians**, every one of them
  refusable and nothing else, which is what the Cast menu of the first played run
  showed. The curated list now rides the Cleric draft's own `ChosenSpellIds` and the
  fallback is gone, so a caster who chose nothing prepares nothing; the level 5 loadout
  is unchanged, and the plan simply arrives as the slots do.
- **Rage's Duration clause is the first thing a played run caught, and it had been
  costing the Barbarian its whole feature.** Two readings were wrong. The printed
  duration is "The Rage lasts until the end of **your next turn**", so the turn a Rage
  is entered on never has to extend it — the engine checked the extension at the end of
  that very turn, so a Barbarian who raged and swung could lose the Rage in the same
  turn it spent a use on. And the first printed extension is "**Make an attack roll**
  against an enemy" — the roll, not the hit — while the flag was set only where damage
  landed, so a miss ended it. Both now ride the stamped-turn clock Vex and Guiding Bolt
  use (`RageBeganOnTurn`), a missed swing counts, and the other two printed extensions
  execute: forcing an enemy to make a saving throw (`ResolveSaveEffect` and Topple), and
  **taking a Bonus Action to extend** — which is what `Rage()` now does when the
  Barbarian is already raging, spending the Bonus Action and no use, in place of the
  `feature.rage.already_raging` refusal that used to imply the choice was a mistake. The
  Incapacitated half of the early-end clause is checked at the same boundary rather than
  the instant it lands, a stated approximation; donning Heavy armor cannot happen inside
  a fight.
- **Divine Order is a draft choice whose both roles execute, and its Protector half is
  the options API learning about drafts.** The choice follows the Fighting Style
  pattern — validated against the granted features, `Unspecified` the honest default —
  with one deliberate difference: an unchosen Divine Order **stays on
  `UnimplementedFeatures` although the registry maps the name**, because a mapped name
  whose choice nobody made would otherwise vanish from the report while nothing
  executed. Protector grows the printed proficiency lines, and since those lines are
  read by `CharacterCreation.WeaponOptions`/`ArmorOptions` — the one gate creation
  menus and the shop share — the whole effect is that API taking the draft's choice:
  a Protector Cleric may be offered and sold Martial weapons and Heavy armor
  (proficiency at resolution stays assumed, the reading on the resolver's attack
  builder). Thaumaturge grows the Cantrips column by one (`SpellAllowances`, which
  `SpellPreparation` now reads so the two cannot disagree) and puts its Wisdom-based
  bonus (minimum +1) on the sheet's Arcana and Religion skills. **The pregen Cleric
  takes Protector** — Strength 13 meets Chain Mail's printed requirement exactly, and
  the shop then armors the party's healer to AC 18 with the run's own gold; Thaumaturge
  has no second executable attack cantrip on the Cleric menu to spend its pick on.
  Both creation clients offer the choice with the SRD's own sentence, and the Godot
  probe takes Protector so its walk exercises the widened menus. Measured on both
  ranges: medians hold 6/6 and full clears rise on both (13 → 16, 12 → 13) — the tail
  gain the economy predicts, since mail costs deep-run money.
- **Channel Divinity executes as Divine Spark; Turn Undead is a written refusal.** The
  spark is a Magic action at another creature within 30 feet — 1d8 + Wisdom as a heal,
  or a Constitution save for that much Necrotic or Radiant, half rounding down — with
  the uses column read off the class table and restored one-on-Short-all-on-Long, the
  Second Wind pattern the text prints word for word. Three readings worth keeping:
  **Magic Resistance applies although it is no spell** (the printed feature calls
  itself divine energy fuelling *magical effects* — the one non-spell path that passes
  `magicalEffect: true` into the shared save loop), **Disciple of Life does not feed
  it** ("a spell you cast with a spell slot", and this is neither), and every refusal
  fires before the use is spent, the potion precedent. Turn Undead's rider prints
  three early outs the condition model cannot express — ends on any damage, on the
  Cleric's Incapacitation, on the Cleric's death — plus a flee behaviour, so it stays
  refused with the reason on the registry, and Sear Undead stays reported with it. The
  policy uses only the heal, on fallen allies — spending the cheapest revival resource
  on damage is the trade the slot-reserve measurements warn against — and it moved
  everything at once: median 7 → 8, clears 12 → 14, level-4 runs 24 → 31 (#151).
- **The Ability Score Improvement is a draft choice, and the count comes from the class
  table.** "+2 to one ability, or +1 to two, never above 20", taken at level 4 by every
  class. Two readings are written down on the resolver: **how many the character is
  entitled to is counted from the printed rows**, because `ResolveFeatures` collapses
  repeats and this is the feature the SRD grants most often (four times for most classes,
  six for a Fighter); and **a draft may name more improvements than its level has earned**,
  taking the first N, because one draft has to describe the character at every level —
  that is what makes levelling a re-resolve rather than a sheet edit. A draft naming
  *fewer* is legitimate too, since the printed feature is "the Ability Score Improvement
  feat **or another feat of your choice**" and no other feat is modelled; the shortfall is
  counted on `CharacterSheet.UnspentFeatChoices` rather than forgotten.
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
- **Upcasting is the definition growing, not the rolls being patched.** The two
  dice-shaped scaling sentences are structured at extraction — "increases by 2d8 for
  each spell slot level above 1" (32 spells) and the Cantrip Upgrade at levels 5/11/17
  (10 cantrips) — with three guards keeping them honest: the shape must match exactly,
  the printed "above N" must be the spell's own level, and the extra die must be the
  same size as the base effect's. At casting time `ApplyScaling` returns the spell with
  more dice, so every resolver sees an ordinary spell and a Critical Hit doubles the
  upcast dice with the rest. **Two traps are pinned by tests**: a save spell carries its
  damage in *both* `Damage` and `Save.FailureDamage` and the resolver reads the second,
  so growing only the first silently un-upcasts every save spell; and Disciple of Life
  reads "the spell slot's level" — **the slot spent, not the spell's own** — so an
  upcast heal feeds it too. The narration now names the slot actually burned, which it
  had always claimed was the spell's level even when it was not. The engine had been
  quietly spending higher slots whenever the lower ones were dry; they simply bought
  nothing until now, and a level 5 Sacred Flame was still 1d8.
- **"Refused rather than silently doing nothing" was untrue for 66 of the 339 spells
  until `spell.save_effect_not_modelled` existed**, and it is the best example in the
  project of bug 1's shape. A spell that *forces a save* was treated as understood, so
  Hold Person, Bane, Sanctuary, Sleep, Command and sixty-one others spent their slot,
  printed a failed saving throw, and **did nothing whatever** — the structured half hid
  the missing half, and the log read like it had worked. A save spell now has to have
  damage, healing, or a condition `ConditionRules` can impose; the rest are refused.
  **Extraction knew all along**: Hold Person's Paralyzed rider was extracted with an
  `UnmodelledRequirement` and the casting path simply never looked at conditions.
- **The policy casts on value now, not as a last resort (#85), and two bugs were hiding
  under the old rule.** "Cast only when the weapon cannot reach" made Touch spells
  unreachable by construction. Fixing it exposed that **"Touch" and "Self" both parse to
  no range at all**, and a null range means *unlimited* everywhere it is checked — so
  Inflict Wounds was legally castable across the room. `SpellDefinition.TargetRangeFeet`
  reads Touch as 5 feet; **Self stays null on purpose** and `IsSelfRanged` is how a caller
  tells "cast on myself" apart from "no limit". A spell is now weighed against the swing
  it replaces — a cantrip only has to be better, a slot has to be **1.5×** better — and an
  area that catches an ally is **a trade rather than a veto**, scored as damage times
  (enemies − friends).
- **A healer holds its slots, and that is measured.** Reserving slots the moment anybody
  is badly hurt clears a median of **6.5** fights; reserving them only once somebody is
  already at 0 clears **5**; the first version of this change, with no reserve at all,
  clears **4** and burned the Cleric's slots on damage while the party bled. The cautious
  healer wins because a slot spent on damage is gone when the character who needed it
  drops. Median over the whole change: **4 → 6.5**.
- **What creation may offer is enumerated in `Game`, so the clients hold no rules.**
  `CharacterCreation` returns whole definitions — the charter's "every choice carries
  its description" is served by shipping the SRD's own CC-BY text verbatim — and two
  printed lines are read rather than modelled, with the readings stated on the class
  and checked against the closed set of twelve printed lines by a test: the Weapon
  Proficiencies line's three shapes ("Simple", "Simple and Martial", "… that have the
  Finesse or Light property") and the Armor Training line's category names.
  `AbilityScoreRules` carries the two deterministic printed generation methods —
  Standard Array and the 27-point Point Cost table, transcribed from printed page 21
  the way `PotionRules` is — plus one unprinted fact worth knowing: buying the
  Standard Array costs exactly the 27-point budget. Random Generation (4d6 drop
  lowest) is deliberately not offered yet: it is the only method needing dice, and
  creation runs before a run's seed exists.
- **A draft chooses its spells now, and the menu is the sixth curated allowlist.**
  `CharacterDraft.ChosenSpellIds` carries the plan under the same reading as its
  Ability Score Improvements — resolving at a level prepares the first entries the
  class table's printed Cantrips and Prepared Spells columns allow, and a spell whose
  level has no slots yet is skipped for now rather than refused. **The gate is
  `PreparableSpells`, curated by hand, not the shape data — and the reason is worth
  not re-learning:** `SpellcastingRules.HasExecutableEffect` (the casting path's own
  refusal tests as one predicate) says yes to Bestow Curse, whose extracted
  save-plus-damage shape is a sliver of a printed effect the engine cannot express,
  and the spell-level `UnmodelledClauses` accounting is **not populated** the way the
  stat-block accounting is — Bestow Curse reads as fully modelled. A menu filtered on
  shape would offer spells that execute partially: the Goblin Warrior bug wearing a
  spell list. The registry's bar and its per-spell exclusion reasons are on the class,
  including the pregen-blessed entry whose gap is stated rather than silent: Spirit
  Guardians is cast as a one-time Emanation rather than its printed persistent aura.
  (Guiding Bolt's Advantage rider was the other stated gap until #155 — it executes
  whole now, structured at extraction like Sacred Flame's cover clause and spent by
  the next attack roll against the lit target, anyone's, on the caster's turn-stamped
  clock.) **The first
  widening passes took the Wizard from seven to nine, then ten with Hold Person** — Shatter's "A Construct has
  Disadvantage on the save" now rides `SaveEffect.ConstructsSaveAtDisadvantage`
  against the stats' own creature type, and Ray of Sickness's Poisoned rider is
  parsed whole by the spell grammar (the shared grammar's head-clause rule rightly
  refuses "On a hit," sentences — spells print that where stat blocks print `Hit:`)
  and imposed by the same path a bite's rider takes. **Do not re-survey the launch
  lists hoping for free adds**: every remaining condition rider on them is
  extraction-refused for a modelled reason — repeat-save outs, "for the duration",
  chooser's-choice conditions — so the next widening needs the model to grow a shape,
  and the survey's best find is filed as an issue (Revivify).
- **Hold Person executes whole, and it grew the condition model two shapes.** A
  failed save imposes Paralyzed with three printed ways out, whichever comes first:
  the caster's Concentration breaks (`ConditionDuration.WhileConcentrating` — the
  sweep runs wherever Concentration ends), the bearer's repeated save succeeds
  (`RepeatSaveAtTurnEnd` — rolled at the end of each of the bearer's turns, skipped
  turns included, honouring the auto-fail clause so a Strength-save variant would
  never open), or the tenth bearer turn ends ("up to 1 minute"). The extraction
  template matches the corpus exactly twice — Hold Person and Hold Monster — and Hold
  Person's "Choose a Humanoid" rides `SpellDefinition.TargetCreatureType`, refused at
  casting with `spell.wrong_target_type`. Hold Monster stays off the menus: a level 5
  spell in a game whose Wizard slots stop at level 3, the same line the loot table
  draws at Very Rare. Wiring save-spell riders on also armed a latent Eyebite bug —
  its per-turn effect *menu* read as three clean riders — defused at extraction with a
  chooser's-choice refusal. And the completion rule was wrong the moment holding
  worked: a side of held creatures used to read as defeated (`IsActive`), which made
  Hold Person an instant-victory button; standing now means alive and above 0 hit
  points, and what ends the fight for a held creature is the enemy walking over and
  finishing it, which the stuck-turn rule already does. The policy prices it now
  (#145): a hold is worth the target's `ThreatPerRound` for two held rounds — a
  stated crude constant, since the repeat save gives the victim a fresh roll every
  turn — discounted by the save chance and competing against the swing under the
  same 1.5× slot margin, with the printed target-type gate filtered before the
  engine is asked and the cautious healer's reserve still outranking every hold.
  Measured: median 7 held, **full clears 9 → 12** — the slot pays where the economy
  paid, in runs deep enough to afford it.
- **The pregen Cleric prepares seven of its printed nine, and the shortfall is not a
  choice.** Of the 109 spells on the Cleric list, eight have an effect the engine
  executes: Sacred Flame, Guiding Bolt, Cure Wounds, Healing Word, Inflict Wounds,
  Hold Person (on the creation menu since the repeat-save slice, and in the pregen's
  own loadout since #145 taught the policy to price it), Spirit Guardians and, since
  #119, Revivify — "a creature that has died within the
  last minute returns to life with 1 Hit Point", the minute being ten rounds on the
  same clock conditions ride, the death round stamped by the encounter, and a death
  the fight never saw reading as too long ago because refusing a legal revival is
  recoverable and granting a forbidden one is not. **Spirit Guardians is still never
  cast** (a self-centred Emanation the priorities never choose), and **Revivify's
  automated value is locked behind reaching level 5** — measured: level-1 pacing
  unchanged (median 3.5, the ASI's story again), and at level-5 starts the policy
  walks to the corpse, holds its last level 3 slot against upcast heals (the first
  measured runs fired **zero revivals across forty-five deaths** because every slot
  that could answer a death had already bought hit points — the cautious-healer
  finding, one clause deeper), and revives when death and survival line up. Its real
  customers are human play and created parties starting high.
- **Area geometry is a stated interpretation, not a derivation — with two exceptions.**
  The SRD describes areas for a table with a ruler; `AreaTargeting` documents how each
  becomes squares. Cylinder is not modelled and a spell using one is refused. The
  exceptions are printed: the **Emanation's excluded origin square** (glossary, page
  181) — the Cone's and Line's exclusions are the inferred ones, so do not "tidy" the
  three into one rule, they agree today by different authority — and the **exclusion of
  squares behind Total Cover** (the glossary's Areas of Effect entry), where the printed
  rule tests "all straight lines" from the point of origin and the engine tests the one
  centre-to-centre line it measures everything else with, a stricter stated reading on
  `CoverRules.LineBlocked`.
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
  are on the registry's doc comments: the Wand's "ignore Half Cover" is **real since
  cover landed** — a spell attack past one low obstacle rolls against the bare AC, Half
  exactly so Three-Quarters still counts, after shipping vacuously for as long as no
  cover model existed — and Elven Chain's training override is satisfied by construction
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
- **The Sorcerer's Class Features column wraps, and it was worse than it looked.** Its
  table carries two extra columns, so its cells are the chapter's narrowest and the only
  ones that wrap. #78 was filed for five visibly broken rows ("Ability Score" for
  "Ability Score Improvement"); fixing it revealed levels 1 and 2 were *also* wrapped
  and silently missing Innate Sorcery and Metamagic — invisible because "Spellcasting"
  alone still matched a heading. Two rules in the fix: **the join happens on the raw
  cell and re-splits**, because the comma deciding whether a continuation is a suffix or
  a new feature lives at the end of the first line and the split has already eaten it;
  and **only the line directly under a parsed row may join** — the first version had no
  adjacency check and gobbled body prose into ten other classes' rows. The validator
  that should have existed all along is `class.feature.no_heading`: every level-table
  name must match a feature heading in the class's own prose.
- **The two-column pass slices the full-width table into itself, and the leak wore
  prose's clothes for the chapter's whole life (#116).** Once a feature heading opened,
  every later column line was appended to its prose — including the sliced fragments of
  the class's advancement table, so ten features across nine classes ended in runs of
  bare per-level numbers, and the Wizard's Signature Spells carried six kilobytes of
  spell-list table. Invisible until Phase 5 put feature text verbatim on a creation
  screen; caught by the Godot probe's own capture. The fix is a font test, per this
  file's oldest lesson: prose in the player-facing chapters is the **Cambria** family
  and every table is **GillSans**, so a feature appends only Cambria lines — which also
  drops the mis-glued sidebars ("Breaking Your Oath" had been scrambled into Channel
  Divinity) and in-feature sub-tables like Font of Magic's costs, absent and honest
  where appended they were garbled digits. The validator is `class.feature.table_noise`:
  five consecutive bare numbers occur in no legitimate feature prose, because print
  punctuates its lists.

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
Wizard, Barbarian, Ranger — they cover every mechanical shape the engine must handle).

**The kickoff's other decision was reversed on 2026-08-16: the code is MIT.** It had
been "no code licence for now" — public repo, no `LICENSE`, all rights reserved by
default, deliberate rather than an oversight. What that missed is that the default is
not neutral: it applied all-rights-reserved to the one artifact here with value outside
this repository. An SRD 5.2.1 engine in C# needs no Wizards licence to exist — that is
the whole point of CC-BY content, and it is why Solasta needed a deal and this does not
— and `src/SRDCombat.Core` has **no project references at all**, so it is genuinely
liftable rather than theoretically so. Unlicensed, none of that could be used.
**The licence is scoped to code, and the scope is the part to keep right**: `LICENSE`
is verbatim MIT so GitHub's detector recognises it, and the code/content split is
stated in `README.md` and at the top of `NOTICE.md` instead of inside the licence
text. `data/` stays CC-BY-4.0. A blanket MIT over the repository would have been
lawful — CC-BY-4.0 permits adapted material under other terms — and would have implied
this project may relicense Wizards' content, which it may not.

## Working on the combat engine

- **The frozen transcript is the most valuable test here.** It pins the exact narrated
  sequence of a whole fight, so it catches interaction bugs no unit test reaches. When
  it fails, **read the diff before touching the fixture** — a change to the transcript
  is a change to how the game plays. Regenerate only once the new behaviour is intended:
  un-skip `TranscriptWriter`, run it, re-skip it, review. It has churned five times —
  focus fire, cover landing, the policy using cover, creatures granting cover, and
  Opportunity-Attack-aware movement — and earned its keep every time. Twice the failure
  that mattered was not the byte-for-byte diff:
  `TheFightExercisesTheHardParts` noticed the focus-fire fight no longer downed anybody
  and covered no Death Saving Throws, and the cover-policy regeneration's diff showed a
  reach weapon's Opportunity Attack narrating a hit straight through Total Cover — a
  shipped bug no unit test had caught, because "melee reach means nothing in between" is
  false for a Halberd. Each time the composition was kept and only the seed moved — the
  seed is chosen for coverage, and `SkirmishScenario` carries the history.
- **It uses hand-authored combatants, not SRD monsters, on purpose** — so it fails when
  the *engine* changes, not when content is re-extracted. `RealMonsterCombatTests` in
  `SRDCombat.Content.Tests` covers the other direction, including a smoke test that
  every CR 0–4 monster can take a turn without throwing.
- **The battlefield is 18 × 12 with the sides 60 feet apart — both axes doubled on
  2026-08-17, at the player's direction.** The note `EncounterFactory` carried said
  exactly when to do this: widening measured worth double the clears to a level 3 party
  and ruinous to a level 1 one, "worth revisiting the moment the level 1 wall is fixed,
  and not before" — and #205 fixed it. Both spawn columns are centred with
  `MarginSquares` (3) of flank on each side, so going round is a real option. Measured
  on both ranges: clears **31 → 22** and **29 → 13**, median **21 → 14** and **18 → 13**,
  **no stalls on 241 runs** — the board is crossable, just dearer to cross, because the
  approach costs two rounds instead of one and anything with a bow or a breath weapon
  collects on both. The early game is *fine* (died-by-fight-4 held at 4–11, opening band
  hp 85%) and the late bands are the ones paying (67% hp-left, 6.9 rounds), which is a
  curve pointing the right way. **The automated numbers are floors, and this change more
  than most is for the human player**: an 18 × 12 field is where positioning, kiting and
  screening can exist at all.
- **A fight's opening shape is drawn, not fixed (2026-08-19, asked for from play).**
  `BattleLayout` rides the built `Fight`: half of draws keep the classic facing columns,
  a quarter split the monsters into two corner groups converging at full separation, and
  a quarter surround the party — a centre block with monsters at all four compass
  points, 30 feet out (`SurroundedSeparationFeet`), deliberately half the standard
  distance because a surround at 60 feet is just four unhurried column fights. **Below
  level 3 every fight opens as columns and no die is spent**, so a level 1–2 fight
  replays byte-identically — the same boundary every count bound draws, for the same
  measured reason: a fragile party pays for being flanked in characters removed.
  `TerrainGenerator` needed no change: its band generalises to "between the outermost
  spawn columns", and its connectivity guarantee never cared about the shape. Measured
  on both canonical ranges against a same-build baseline: medians pinned (18/18,
  13/13), the opening untouched by construction (died-by-fight-4 4 → 4 and 15 → 15),
  clears **16 → 19** and **16 → 22**, level-4 runs **48 → 51** and **36 → 43** — a mild
  party *buff* on both ranges, plausibly focus fire collecting on split groups (defeat
  in detail) and on a ring that walks into the party's full reach at once. It ships for
  variety in human play under the printed-kit precedent, and the numbers are floors set
  by the placeholder policy: a human surrounded at 30 feet is not the instrument's
  surrounded.
- **HOLD was wired, measured on the wide board with the armed party, and unwired again
  — #125's verdict survives its premises changing.** Both reasons it originally lost are
  gone (one bow between four; a board one move wide), so it got its fair test:
  `PartyDoctrine.Phase` consulted at the top of the policy's close-or-shoot gate, seeds
  1–120 — **byte-identical outcomes, the phase never fired once.** The arithmetic says
  why, and it is worth keeping: holding earns the *ranged margin* and idles the front
  line's whole melee output, but the back rank shoots whether the party holds or commits,
  so the margin can never cover a front-liner's greatsword unless the party is
  ranged-heavy — which the pregens are not, javelins or no. The phase machinery stays
  built and unconsulted, per the Revivify precedent: its customer is a created
  ranged-heavy party, not this one.
- **"Attack from where we stand if anything reaches" was a load-bearing accident, and
  equipping the party from the printed starting kits exposed it.** Asked from play: "why
  is Sable the only character with a ranged weapon option?" The answer was that only the
  Rogue's loadout matched the book — the printed Fighter gets **8 Javelins** (option A) or
  a **Longbow** (B), the printed Barbarian gets **4 Handaxes**, and both were simply
  missing. Adding them took full clears from **38 of 120 to 2**.
  **The equipment was not the bug.** The policy's turn attacked from the current square
  whenever *any* attack reached, and only moved when nothing did. That is correct exactly
  as long as a melee character owns nothing but melee attacks — nothing reaches, so it
  walks. Give a Fighter a Javelin that reaches 120 feet at long range and the test
  inverts: something always reaches from the spawn square, so the front line never closed
  and spent whole fights lobbing its weakest attack at Disadvantage instead of walking in
  behind a Greataxe. `WouldRatherClose` is the guard — walk when a harder-hitting attack
  exists than anything reaching from here and there is movement left to go and use it —
  and a **tie deliberately keeps the creature still**, which is what leaves a genuine
  archer shooting, since the Rogue's Shortsword and Shortbow average the same.
  `ReachOf` was fixed alongside it for the same reason: it planned around the
  *longest*-reaching attack where `TryAttack` swings the *hardest*-hitting one, so the two
  disagreed about what the walk was for.
  **Measured, and the honest figure is not a win.** Against the same build without the
  thrown weapons: clears **38 → 31** and **46 → 29**, median **23 → 21** and **23 → 18**,
  died-by-fight-4 **1 → 5** and **7 → 6**. So the printed kit still costs the *automated*
  party something even with the guard in. It ships because the equipment is what the book
  prints and because a human wants the option — the standing caution applies exactly here,
  that every pacing figure is a floor set by a placeholder policy rather than a verdict.
  That gain is now taken, asked for from play — a Barbarian throwing a Handaxe 40 feet
  read as broken mechanics until the log named the weapon: `ValueAt` halves an attack's
  average damage when the roll would be at long range, and both `TryAttack` and
  `WouldRatherClose` rank by it, so a thrower walks in behind its harder weapon, an
  archer stranded beyond her normal band closes into it (pre-discount that comparison
  was a tie, and a tie stands still — she shot at Disadvantage from the spawn square all
  fight), and with no movement left the long throw is still taken, a preference and
  never a veto. Half is a stated crude constant: Disadvantage roughly squares a typical
  hit chance. Measured on both ranges against same-build baselines, and the two moved in
  opposite directions — median 14 → 18 with clears 22 → 15 on seeds 1–120, median 13 flat
  with clears 13 → 18 on seeds 200–320 — which is the documented seed-set × build
  interaction saying the change is pacing-neutral; it ships for correct play, not for
  numbers. The measurement also surfaced a pre-existing stall the discount merely
  re-rolled into view (#224): the policy cannot see damage immunity, so the last hero
  standing swung a Longsword into a Slashing-immune Ochre Jelly for fifty rounds with a
  Piercing Javelin on her belt.
- **A creature at 0 hit points still occupies its square, and may be walked *through*
  but never stopped on.** Reading occupancy as "active" let a mover end its turn standing
  on an unconscious creature, which was invisible until healing existed — the downed
  creature then stood up *inside* someone else and the next path find threw on two
  combatants in one square, taking down a whole run mid-fight. Two of sixty seeded runs
  crashed. `MovementRules.FindPath` treats anyone not dead as occupying, and keys its
  blockers as a lookup so that a duplicated square is survivable rather than fatal
  whatever produces it.
- **The printed *Moving around Other Creatures* rule is executed, and two of its clauses
  were missing for the chapter's whole life.** "During your move, you can pass through the
  space of an ally, a creature that has the Incapacitated condition, a Tiny creature, or a
  creature that is two sizes larger or smaller than you"; "another creature's space is
  Difficult Terrain for you **unless that creature is Tiny or your ally**"; "you can't
  willingly end a move in a space occupied by another creature". The pathfinder had only
  ever exempted **allies**, and charged them Difficult Terrain it should not have. So
  **a downed enemy walled a corridor off** — the printed clause names a *condition*, not a
  side — and **squeezing past your own front line cost double**, quietly shortening every
  repositioning move the party made. Both now follow print; Tiny and the two-size clause
  stay unmodelled and so still block, which is the conservative direction since modelling
  them can only make more squares passable. **Measured, seeds 1-120, against a
  same-build baseline taken immediately before:** clears **72 → 76**, level-4 runs
  **86 → 95**, died-by-fight-4 **13 → 11**, median pinned at 30 throughout. A party
  buff, as expected — they are the side that clusters and repositions under fire, so
  the ally exemption lands on them. Note the direction: this makes an already-easy
  game slightly easier, and it shipped anyway, because the bar here is what the book
  prints and the remedy for "too easy" is not declining to execute a printed rule.
- **That fix belongs to a bug the tactics policy had been carrying.** The stalemate #126
  found — "a fight that could not end", a wall pocket whose one doorway was plugged by an
  unconscious character — was a *movement* gap, and the stuck-turn last resort was a
  workaround for it. The last resort is still needed and still tested (walls alone can
  seal a pocket, an able enemy can hold a corridor), but its original scenario no longer
  stalemates, so `StalemateTests` seals its pocket with stone instead — and the test that
  had to be constructed for it is worth knowing about: **a sealed side of the field is
  not stuck**, because the policy simply repositions within it. Stuck means a cell whose
  only non-wall neighbour is the body itself, diagonals included.
- **A move may end on a fallen comrade — the engine's one deliberate contradiction of a
  printed sentence — and it brought the displacement rule with it.** The print is explicit:
  "You can't willingly end a move in a space occupied by another creature." Asked for
  during the 2026-08-16 play session, **twice, after the printed reading had been
  explained**, and shipped as the player's call: standing over a fallen friend is what a
  player expects to be able to do, and being refused reads as the grid being broken rather
  than as a rule. **Scoped as narrowly as the request was** — only a *fallen ally*
  (`CanEndOn` = ally **and** Incapacitated), so a downed enemy still refuses and the
  printed sentence governs every other case. The narrowness is load-bearing twice over:
  it was implemented for any downed creature first, and that broke both stalemate tests,
  because a monster able to *stop* on the body it is trying to get past deletes the only
  scenario the stuck-turn last resort is tested against.
  **This is also the bullet that reversed** — an earlier version of this file, written
  the same day, argued the wake-up case was impossible and needed no rule. That was true
  only while ending a move on anyone was refused. Allowing it reopens two able creatures
  in one square, which is exactly the crash that took down two of sixty seeded runs when
  occupancy was last read as "active", so `Encounter.ClearSharedSquares` now displaces
  whoever is standing on a creature that comes round, swept beside `EndBrokenGrapples` at
  every state-change point. **Who stays is a stated reading**: fewest hit points keeps the
  square, ties on identifier so a seed replays — in practice the one who just came round
  is at 1 hit point and the one standing over them is not, which puts the move on the
  character who chose to stand there, as asked. Displacement is free (no movement spent,
  no Opportunity Attack, because the creature did not choose to go) and is **narrated**,
  since a token moving on its own is otherwise indistinguishable from a bug. One trap for
  the next test-writer: **the sweep runs from `Encounter.Start`**, so two combatants
  constructed in the same square are separated before a test body begins — the stacking
  has to be done by moving. Measured neutral: median 18 → 19, clears 38 → 35, no stalls.
- **A cheapest route is not automatically a sensible-looking one, and the tie-break that
  fixes it paid for itself.** Every square costs the same five feet, diagonals included,
  so whenever one axis decides the distance a route may drift sideways and back *for
  free*: `(1,2)` to `(6,1)` came out as `(2,1) (3,0) (4,1) (5,1) (6,1)`, five steps and
  twenty-five feet like the straight route, visibly strolling to the top row and back.
  Nobody noticed while tokens teleported; the moment they walked, it read as the
  pathfinder being broken. `FindPath` now carries a second key — how many times a step
  moves *away* from the destination on an axis — which orders equal costs and can never
  beat cost, so the route is still the cheapest one. **It is not cosmetic:** measured on
  the canonical instrument against a same-build baseline, median **19 → 24**, clears
  **51 → 54**, level-4 runs **60 → 63**. The mechanism is Opportunity Attacks — a route
  that does not wander spends fewer steps leaving a threatened square — and it lands on
  the party because they are the side closing distance under fire.
- **All randomness goes through `IRandomSource`.** Never reach for `Random.Shared`
  anywhere in `Core`; determinism is what the transcripts rest on. `ScriptedRandomSource`
  throws when a test rolls more dice than it scripted — if that fires, the test's premise
  changed (an Advantage roll consumes two dice, not one).
- **Rules verified against the printed SRD, not memory** — and the non-obvious ones are
  pinned by tests: Advantage and Disadvantage cancel rather than stack; a Critical Hit
  doubles the *dice* and adds the modifier once; a monster dies at 0 hit points while a
  character rolls Death Saves; Dodge lasts until the start of the dodger's *next* turn;
  and attacking an Unconscious creature from beyond 5 feet is a *normal* roll, because
  Unconscious grants Advantage while the Prone it carries imposes Disadvantage. A ranged
  attack rolled within 5 feet of *any* able enemy has Disadvantage (printed page 15 —
  "an enemy", not "the target"), "who can see you" is read as any enemy without Blinded,
  and a dual-mode attack used inside its reach is a melee roll that escapes it.
- **Cover is judged where the battlefield is known, never inside `AttackRules`.**
  `CoverRules.Between` needs the `Battlefield`, so `Encounter` computes the degree and
  passes it in; `AttackRules.Resolve` just adds the bonus to the AC it compares and
  records the degree on the `AttackRoll` for narration. Total Cover is refused before
  anything is spent on every targeting path, which is why `ResolveAttack` can assume it
  never sees one — and the Opportunity Attack is a filter rather than an assumption,
  because "melee reach means nothing in between" is false for a reach weapon: a
  Halberd's Opportunity Attack spans a square that can be a wall, which the regenerated
  transcript caught printing a hit through Total Cover. `MakeOpportunityAttack` declines
  to swing, the way it declines against a charmer. The save half rides
  `ResolveSaveEffect` against the effect's **point of origin** (the erupting point for a
  Sphere or Cube, the creature for everything else — `AreaTargeting.PointOfOrigin`), and
  a non-Dexterity save gains nothing. Since #108 the combatants are part of the
  judgement: a living creature the line crosses is Half Cover, under three readings
  stated on `CoverRules` — the dead grant nothing, crowds are not walls, and creatures
  never escalate obstacles. `AreaTargeting`'s Total-cover exclusion stays terrain-only
  by construction, since creatures cannot provide Total.

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
- **A monster's Bonus Action entries execute now (#230), and the policy spends them.**
  `UseEntry` spends the Bonus Action for a `BonusAction`-section entry and the Action
  for an Action one — the gate that stopped at Action was why the Basilisk never
  petrified anybody: its gaze is printed under Bonus Actions, as the Medusa's is, and
  no path could reach it. The policy uses a **limited-use** Bonus Action entry beside
  its Action (the gaze before the bite), under the same own-side area judgement as a
  breath weapon — and **never one whose save would change nothing**
  (`HasExecutableEffect`: failure damage, or a rider `ConditionRules` can impose),
  because a Bonus Action spent narrating an effectless save is an unimplemented rule
  pantomimed. Reactions and Legendary Actions stay refused — each needs a trigger or an
  economy the engine does not model — and **the pool's Playable grade still reads only
  Action entries**, so a creature whose signature lives elsewhere is still admitted at
  full printed XP while missing it: that half of #230 is its own open question.
  **Measured on both canonical ranges against a same-build baseline**: seeds 1–120 read
  18/15→16/48 (median/clears/L4, opening 4→4) and seeds 200–320 read 13/18→16/41→36 —
  neutral on one range, slightly harder on the other, which is the expected size of the
  move: only two creatures in the pool of 117 carry a gaze, so this slice is fidelity
  and variety rather than a pacing lever. What a played run should notice is different
  in kind — a fight with a basilisk now carries a second clock, and a petrified
  character is out for the fight but back for the next one, because conditions end with
  the encounter (the stated rescue reading, on `ConditionRules`' Petrified bullet).
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
  The ground between them is generated too: `TerrainGenerator` (in `Game`, beside the
  factory) seeds walls and Difficult Terrain from the fight's own dice, keeps every
  feature strictly between the spawn columns, and refuses any wall square that would
  disconnect one spawn from another — so the 30 feet stays crossable by construction,
  and a bare field remains a possible draw on purpose.
- **Rests differ per feature, so restoring them is a table and not a reset.** Verified
  against print: Rage and Second Wind each return **one** use on a Short Rest and all on
  a Long; Action Surge returns whole on **either**; spell slots on a Long Rest only. And
  a 2024 change worth not re-learning — **a Long Rest restores *all* spent Hit Point
  Dice**, where earlier editions returned half. `RestRules` holds each with its citation.
- **The opening cycle rests Long throughout, and it is the largest single fix the early
  game has had since the creature cap.** Reported from play on 2026-08-16 — "level 1
  characters die too quick, especially for the first few matches" — and the instrument
  agreed, `died-by-fight-4` being the run's largest failure cohort. **The cause is neither
  the ladder's difficulty nor the budget**: a level 1 character has exactly **one Hit
  Die**, a Short Rest spends it, and Hit Dice return **only on a Long Rest** — so the
  party got one real heal per five-fight cycle and then fought rungs 2, 3 and 4 on the
  remainder, against budgets priced for a party at full strength, because **the budget
  cannot see hit points**. Resting Long here **costs no fidelity at all**: how often a
  party rests is the GM's call, which is to say this project's, exactly like `LootTable`'s
  award rates — nothing about what a rest *restores* moves. It is tied to the cycle rather
  than to party level because the ladder is built once and never sees a level, and by its
  own XP arithmetic the opening cycle is levels 1–2. Measured on **two** seed ranges:
  died-by-fight-4 **15 → 1** (seeds 1–120) and **14 → 6** (seeds 200–320) — the second
  range is why the "1" is not the number to quote — with the opening band's hit points
  left rising 78% → 87% and 79% → 85%. **Two weaker variants were measured and rejected**:
  one extra Long Rest at fight 3 (deaths 15 → 10) and a Hit Die returned on Short Rests at
  levels 1–2 (15 → 11, and a house rule where this is not). This beat both on early deaths
  *and* on back-half inflation. **That inflation is the honest cost and every variant had
  it**: clears rise 66 → 72 and 71 → 78, because more runs survive to reach an ending that
  is already too easy (#192). **The opening and the ending are one problem wearing two
  faces** — the ending needs its own teeth rather than a lethal first cycle standing in
  for them, which is the argument for horde encounters at level 3+ rather than for
  clawing this back.
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
- **Genre is a fourth axis, it is taste rather than a rule, and it bought difficulty for
  free.** Coverage says what the engine can run, the budget says how much a fight costs,
  `PlausibleFoes` says what the SRD prints as property — and none of them has an opinion
  about whether a fight *feels* like fantasy. A correctly built, perfectly plausible,
  fully modelled fight kept opening with an Ape, reported from play on 2026-08-16 as "I
  don't like fighting apes or other random wild animals". `EncounterBuilder.ClassicMonsterWeight`
  makes a non-`Beast` candidate three times likelier than a Beast when a slot is filled.
  **Derived from the book's taxonomy, not a list of names** — `Beast` is the printed type
  for ordinary animals, so it is one enum comparison and a renamed stat block cannot
  escape it; the cost of deriving rather than curating is that genre-appropriate animals
  (a wolf pack, a giant spider) are swept up too, which is why it is a weight.
  **A weight and never a filter, and the pool's shape is the reason**: of 117 creatures at
  CR ≤ 4, 46 are Beasts, and **at CR 0 nineteen of twenty-two are** — excluding them would
  leave three creatures at the bottom of the ladder and break the pool's own "at least
  five at every CR" floors. Measured over 6,000 built encounters, the Beast share of
  drawn creatures falls **43.1% → 23.7%**. And the pacing result is the interesting part:
  on seeds 1–120 against a same-build baseline, clears **76 → 66**, level-4 runs
  **95 → 83**, died-by-fight-4 **11 → 15**. **The genre preference is a difficulty
  increase**, because a classic monster carries more mechanics per XP than an animal does
  — multiattacks, riders, bosses — so the same budget buys a harder fight. It moves the
  back half toward #192 and the opening *away* from the level 1 complaint in the same
  breath; those are separate levers and the second one is still open.
- **The dearest band is chosen by price, not by count, and the count version was hiding
  half the bestiary.** `EncounterBuilder` fills each slot from the dearer end of what fits
  its share — the printed "spend as much of your XP budget as you can" — and it used to
  take the first third of a list sorted by price *and then by identifier*. That cut falls
  inside a tie: where a dozen creatures cost the same, only the alphabetically earliest
  entered the band and the rest **could never be drawn at all**. Measured over 6,000
  generated encounters, the game fielded **68 distinct creatures out of a pool of 117**,
  and the most frequently drawn read Ankheg, Archelon, Azer, Awakened, Bandit, Basilisk —
  which is not a coincidence but an alphabet. Found while ranking which monsters most
  deserved hand-drawn art, which is the second time a *display* question has surfaced an
  engine bug (#164/#165 were the first). The count now only decides the price to beat and
  everything at that price comes with it; nothing about spending the budget is given up,
  since every creature admitted still costs at least what the count demanded. The
  identifier still orders what survives, because `PickByTaste` walks the candidates
  accumulating weight and a seed has to replay exactly — it is only the *cut* that must
  not fall inside a tie, and the dice stream is unchanged in length because `Roll` takes
  one value whatever its bound. **Measured: distinct creatures 68 → 83 of 117**, the
  common draws now spread across the alphabet (Hippogriff, Spy, Goblin Boss, Giant Eagle,
  Sphinx of Wonder), and on pacing, both ranges moving together: median **18 → 23**,
  level-4 runs **56 → 72** and **60 → 78**, full clears **38 → 38** and **43 → 46**,
  died-by-fight-4 **3 → 1** and **9 → 7**. Runs go *deeper* without finishing more often,
  so the middle band stays fat — the newly reachable creatures are on average a little
  less dangerous per XP than the alphabetically early ones they now share the band with.
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

**Do not read this section to find out what is on your machine — run the script.**

```bash
./scripts/doctor.sh
```

It reports the SDK this repository actually resolves to, whether that agrees with the one
CI gates on, and what optional tooling is missing, exiting non-zero when something will
bite you. It exists because **every environment problem this project has had was silent**,
and because prose can only ever describe one machine at one moment — the entry below said
"snap, SDKs 8.0.129 and 10.0.110" while the machine it was re-read on had apt, no .NET 8
at all, and a green local build compiled by .NET 10. That is the extraction pipeline's own
lesson ("write the validator that asserts the shape of what should have been found")
pointed at the desk instead of the SRD. The rest of this section is *why* each check is
there, which the script cannot tell you.

- **Which .NET runs has flipped four times**, and the fourth flip is the instructive one.
  Snap-confined at kickoff, apt-only SDK 8 at PR #30, snap again at PR #91, and then an
  apt install at `/usr/bin/dotnet` carrying **SDKs 9 and 10 and no .NET 8 whatever**.
  Because `global.json` pins 8 with `latestMajor` roll-forward, that machine did not
  complain: it rolled forward and built clean, 0 warnings, **on a different major version
  than the one gating the merge**. `TargetFramework` is `net8.0`, so `LangVersion` stays
  at C# 12 and most syntax drift is caught — but the analyzers are not the same analyzers,
  and `TreatWarningsAsErrors` is on. **`.mise.toml` pins the SDK so this stops happening**;
  `mise install` on a new machine is the whole setup.
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
- **Godot 4.7 stable mono**, on `PATH` — `/usr/local/bin/godot` where this was last
  checked, `~/.local/bin/godot` on another machine. Not used until Phase 7, so its absence
  elsewhere costs nothing yet; `doctor.sh` looks it up rather than assuming a path, which
  is how the stale one above was caught. It is deliberately **not** pinned in `.mise.toml`:
  an unresolvable pin would break `mise install` for everyone to serve nobody, so it goes
  in alongside the branch that starts the client.
- **Phase 7 was proved buildable before being started**, so the next author inherits facts
  rather than an experiment. A throwaway `Godot.NET.Sdk` project referencing
  `SRDCombat.Game` **compiled clean and ran headless against the real engine**, printing
  its monster count through the actual `ContentLoader`. Three things that fell out of it:
  the Godot SDK is a NuGet package, so **the build needs no Godot installed and no .NET 8
  SDK** (the net8.0 reference assemblies restore themselves); a live X display exists
  (`DISPLAY=:1`), so a windowed run can be watched and captured; and **the engine's public
  API is already enough for a client** — `Battlefield` for the grid, `MovementRules.FindPath`
  per square for reachable-square highlighting, `Combatants`/`TurnOrder`/`ActiveCombatant`
  for state, `Log` for events, every action returning an `ActionRefusal` to display. A
  Godot client needs **no change to `Core`, `Content` or `Game`**, and the day one is
  needed is the day the work has left its lane.
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
and **15 warnings, all expected**:
the Archmage's XP, which is a real SRD inconsistency, twelve spells whose component
line is truncated at a column break in the source, and two magic items (Figurine of
Wondrous Power, Ioun Stone) whose "Rarity Varies" tiers live in a table in the body
rather than on the type line. (This paragraph said "12 warnings, nine spells" for some
time while the machine said 15 — checked when #116's fix was suspected of adding three
and turned out to have added none. Trust the run over this prose.)

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
`--difficulty low|moderate|high`; `--create` builds your own party of four at the
keyboard first (Phase 5) — every option shown with its printed SRD text, browsing and
committing as separate actions per the charter, the drafts riding the ordinary save;
the seed is printed at the start of every
run, so *"it happened on seed 12345"* is a complete bug report. The content directory is
found by walking up for `data/srd`, so it runs from anywhere in the repo. Creation runs
before the seed's dice are touched, so a created party replays exactly like a pregen one.

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
