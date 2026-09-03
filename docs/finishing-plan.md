# The finishing plan

Moved out of `CLAUDE.md` on 2026-08-29. It is the project's roadmap — 12 KB of phase
detail that entered every agent's context before it read a line of code, while most
tasks need only the active phase and its gate. CLAUDE.md now carries that summary and
links here; this document is the authority on phase content and sequencing.

The plan is built from [`docs/2026-08-21-project-review.md`](2026-08-21-project-review.md),
the independent four-viewpoint audit. Every item cites a finding there.


## What the review found wanting

**What the review found wanting** (full detail in the review doc): the fight has
almost no feedback — one-frame monster art, no audio at all, hit and miss visually
identical; the run has no route choice, loot decisions or ironman stakes; the honesty
rule's Multiattack accounting has four breaks closed — the *replace-clause* hole
(#290), alternative compositions that were summed instead of chosen between (#342),
the fourteen sub-sentence composition clauses folded inside a composition sentence
that read as fully modelled (#341), and enumerated fixed compositions ("one Bite
attack and one Claw attack") that recorded `AnyCombination: true` for want of
per-name counts on `MultiattackEffect`, so a Brown Bear could double-Bite and nothing
said so — closed by #343, which also corrected the review's estimate of nineteen
affected creatures to the actual **21** (post-#341/#342, the Barbed Devil's and the
Medusa's kept branch became a clean fixed enumeration too) — the spell lane is
answered by retiring a signal that could not be derived rather than faking one
(#292), and species
traits are no longer a silent one: none of the 33 printed trait instances execute, but
`SpeciesTraitRegistry` and `CharacterSheet.UnimplementedFeatures` now say so at
creation and on the sheet (#291); the undocumented rules gap the review found —
concentration surviving Incapacitated — is fixed (#289); a reload used to re-roll the
ladder because the seed was not saved, and a save-vs-content mismatch used to crash
instead of refusing — both closed (#286, #287, and the last Loot and resume
indexers, #350/#366); the art
pipeline is unrepeatable and one sprite in ~60 matches the project's own palette —
repeatability was answered by the committed script (#294/#427), but the
palette-matching half of that finding was **retired as a goal on 2026-08-26**:
after three colour-step rejections in a row (#238's revert, PR #461's regeneration
redone as lossless mirrors, PR #446 withheld with "looks like it's made of metal"),
Brandon ruled the pipeline mechanical-only — palette coherence comes from his hand,
not a script (closed #458 by policy).
**The phases below are the ordered answer.**

## Definition of Finished (v1.0)

A stranger can download a release, build a party, and play a thirty-fight run with
mouse or keyboard — and it holds up:

1. **Nothing lies.** Every printed rule executes or refuses with a named code; the
   honesty accounting holds for monsters, species, classes, and items — and for spells
   by their own curated menu rather than clause accounting, stated as the exception it
   is (see "The rule this project runs on"); the player is told, at the point of
   choice, what does not work yet.
2. **Every fight ends and every save survives.** No stalls, atomic saves, a reload
   that replays the same fight, content drift refused with a message rather than a
   crash.
3. **Everything on screen reads.** Every creature that can appear has art in one
   coherent style; a hit, a miss, a death, and a spell are visibly and audibly
   different events; the player can see threat, range, and area before committing.
4. **The run is a game.** Decisions between fights (route, loot, shop trade-offs),
   stakes within them (attempts are counted, ironman exists), and a difficulty curve
   that holds through the back half.
5. **The tree is trustworthy.** The extractor and both clients have real tests, the
   suite runs in minutes not tens of minutes, CI gates it all.
6. **It ships legally.** In-game CC-BY attribution, LICENSE/NOTICE accurate including
   the art, a tagged release with binaries.

## The phases

Ordered by dependency and by cost-of-delay, not by appeal. Each phase's items must
exist as GitHub issues before its work starts (**F0 is that filing pass**). Existing
issue numbers are cited; unnumbered items come from the review doc.

**F0 — File the backlog.** The steward turns every item below and every review
finding into an issue with acceptance criteria and a phase label. Exit: the plan and
the queue agree; this table cites only issue numbers thereafter.

**F1 — Integrity (closed 2026-08-25 at `8ca55aa`, PR #421 the closing merge).**
Cheap, compounding correctness debts, every one worked as an issue. The planned
items all landed: atomic save write plus backup, hardened through three adversarial
rounds into a crash-recoverable rotation (#285, #332, #361, #367); the run's seed
persisted so `--continue` after defeat retries *the same fight* (#286); content
version stamped into the save with drift refused via `TryGetValue` on every resume
path (#287, #350, #366); the level-4 ASI plan carried as a fixed plan (#330);
concentration broken by Incapacitated (#289); the Multiattack replace-clause,
summed-alternative and sub-sentence holes (#290, #342, #341); species traits
surfaced as unimplemented at creation and on the sheet (#291) and the
species-table interleave fixed (#374); `SpellDefinition.IsFullyModelled` retired
(#292 — `PreparableSpells` is the authority); the stall class (#256) and
immunity-blind targeting (#224); the doc-drift sweep (#379, plus the stale
upcasting pair it missed, #400). Mid-phase, the 2026-08-24 outside critique's
adjudication (`docs/2026-08-24-span-accounting-brief.md`) grew the phase by the
span-accounting arc: characterization fixtures first (#189's first slice, PR #384,
the safety net), then coverage-by-consumption replacing credit-by-label (#382,
stages 0–6, PRs #385–#389) — `UnmodelledClauses` became computed residue,
`MatchesStructuredForm`/`IsAccountedFor` were deleted, and the census over the
closed corpus ended the goblin shape's omission class by construction. The
semantic fixes landed on top: success-tier scoping (#370), or-tier alternative
damage (#371), plural condition conjunctions (#372), section-gated rider claims
(#373), Spirit Guardians' or-as-and (#375), and printed ranges enforced on entry
saves (#386, whose PR also closed #405 with a knockout-verified policy test). The
#382 regeneration demoted 12 tier-1 monsters (22 lost pool admission across all
CRs), each carrying an always-printed rider the engine never executed; the
transitional `MonsterPoolTests` floor has since ratcheted 68→73 as #371 restored
the swarm/Blood Hawk/Chimera tiers, with the last demotions held by #390's
remaining shapes and #409. Exit evidence: the Pacing row above is the promoted
F1-exit baseline; qc's exit audits of the honesty lanes filed only non-blocking
findings (#400–#402). **Named carries, with reasons:** #393/#394 (narrow
save-rotation crash windows, rated below must-fix) go to #414's
crash-point-enumeration harness in F5 rather than holding the phase; the census
exhaust (#390, #409, #413) goes to F4, where restored creatures land next to the
fill-ins; the exit mechanism review's process issues (#415–#417) go to F5; the
doctrine rewrite of "The rule this project runs on" (#419) is the steward's,
directly after this close; #189's broader extractor harness returns to F5.

**F2 — Feel.** The largest gap per hour of work — **but its two asset lanes are deliberately sequenced last**. Brandon, 2026-08-26: *"save audio and visual art for last… i don't mean visual mechanics, i mean actual image work."* So the audio pass (#300) and every art item (#460, #462, PR #446) wait, while the *mechanics* — damage numbers, hit/miss/death, the health readout, previews, threat marking, the active-ring blink (#494, shipped) and the battlefield slices — proceed at normal priority. He draws all the art himself, so that lane is human throughput best spent once the mechanics using it have settled. The paragraph below still describes the pipeline work first; read that as scope, not as running order. One committed master→sprite pipeline
script — **mechanical-only since 2026-08-26** (facing, crop, downscale, hard alpha;
the palette and de-grain steps were removed at Brandon's direction after PR #446's
"made of metal" verdict — colour is his alone, and no script reinterprets it) —
Brandon approves before/after for every batch. **The "~23 unshipped masters" item is
closed (#295, shipped `ec86756`, 2026-08-25) and the count was wrong**: all but two had
already shipped in intervening batches, and fifteen pool names mapped at gitignored
Craftpix folders were retired to the honest circle-and-letter token rather than left
pointing at art no release build carries. Remaining art lanes: fix the stature clamp
(an Ogre must not render shorter than a Goblin); integer-snap ground scaling. Board
feedback: floating damage numbers, hit/miss/death visually distinct, health readout
carrying state, an audio pass (a dozen sounds: hit, miss, death, cast, UI — silence is
currently total). Foresight: opportunity-attack threat marking, range/AoE previews,
path preview, tooltip latency to ~0.5 s, terrain hints, log-space fix. Added
2026-08-25, from Brandon's played-run verdict on the battlefields: the
battlefield-generation overhaul (`docs/2026-08-25-battlefield-overhaul-design.md`,
slices #433, #435–#440 — sites, density tiers, whole-board terrain, deployment
formations; supersedes #243, whose two items it absorbs). Exit: probe
screenshots show it; a watcher can narrate a fight with the log covered.

**F3 — The run becomes a game.** The largest design gap; Fable-led design, spec'd
before built. **Entry gate (2026-08-24): the PlayMode modal/state refactor (#327,
pulled from F5)** lands before any new modal surface, running parallel to the phase's
design specs so it costs no calendar — every F3 system below is a new modal, and
landing five of them on the 39-field class first would pay for the refactor twice.
Stated escape hatch: if F2's foresight work (#301–#303) needs new modal *states*
rather than new drawing, the refactor pulls forward into F2 instead. Route choice
(pick the next rung from 2–3 revealed options); loot as a
pick-one-of-three moment, and at least a handful of items that change a turn rather
than a stat; shop trade-offs (retire the strictly-better gate); failure stakes
(attempt counter, run summary, opt-in ironman); XP curve so level 5 arrives around
fight 24 rather than never (only 53 of 120 and 53 of 121 runs reach level 4 — the
Pacing row above, current baseline); reprice or redesign the free `Survive(3)` rung;
per-cycle variety so the six cycles are not one cycle six times (#192 — and, since
2026-08-25, the per-cycle site weighting the battlefield overhaul deliberately left
here; #243 itself was superseded into F2, see the F2 note above). Exit:
measured curve holds through the back half on both ranges; a human run report exists
for every new system. **This exit is a re-baselining checkpoint** — with F4's
variety re-run and #542, it is where the comprehensive pacing evaluation that PRs no
longer carry actually happens (see Standing conventions).

**F4 — Depth and variety.** Spellcasting enemies enter the pool (all ten CR ≤ 4
casters are currently filtered out — thirty fights contain no enemy magic); the
`Playable` grade reads all sections, not just Actions (#231); CR-band fill-ins (#267
— the boss band holds two, Guard Captain and Red Dragon Wyrmling, since the census
demoted Ettin); retune `ClassicMonsterWeight` (it now double-penalises the 14 surviving
genre-appropriate Beasts); fog slice 2 (**#545** — #244 was slice 1, shipped as
`PartyVision` and closed 2026-08-27; slice 2 is Stealth, Hide and Surprise, and it is
the slice that moves the visibility predicate from a display judgement into a `Core`
rule, disturbing three readings that are correct only because no sight model exists);
policy growth where measurement pays: Dodge/Disengage/retreat, behind an
`ITacticsPolicy` seam so two policies A/B on the same seeds. Decide the six
unoffered classes: ship or cut, not linger. Exit: distinct-creature measurement
re-run; a property test that every generated encounter resolves.

**F5 — Confidence.** (Two items pulled forward on 2026-08-24: the extractor test
project's first slice, #189, to F1 as the span refactor's safety net, and the
PlayMode refactor, #327, to F3's entry gate — the broader page-fixture harness still
grows here.) Client behaviour
tests grown from the probe harness (#190 — the test project landed 2026-08-26 with
the reachable half: log colouring, sprite metrics, draw scale, and — since #500–#502
and #473/#474 — the focus stack, the router and the scenario seam. **#473's spec type
landed.** What is still unreachable is `PlayMode` as a live Godot node — nothing
constructs it or calls `OnReady` in a test — so #490 covers that as well as the argv
boundary, and `client/README.md` says the same);
console client tests (1.9k lines,
currently untested, #317); shared test-support project; xUnit content
fixtures (**34 executable `ContentLoader.Load` call sites** across the suite, measured at `8988604`: Content 13, Game 18, SrdExtract 3. `TestContent.Srd` — the shared holder #473 introduced, which three classes already read instead of loading their own — is the seam this issue flips; `Game.Tests` took **6m59s** Debug /
4m25s Release measured 2026-08-27 in an uncontended worktree — the suite has regressed 3x
against the 2m14s recorded at the
2026-08-25 exit run, down from the 7m22s this item was filed at — the fixture case
stands on the 27 loads, not the wall clock); the
`Encounter` guard-preamble helper, and the action seam if the class list grows —
trigger-based, with #369 (Turn Undead) the likeliest trigger.
Runs continuously alongside F2–F4; has its own closing push. Exit: suite under ~3
minutes; a parser edit fails a test on a machine without the PDF.

**F6 — Ship.** In-game attribution screen (CC-BY requires notice in the distributed
artifact, not just the repo); NOTICE covers the art and the masters' licence; an
LFS-or-release-assets strategy for the 356 MB masters tree, 139 files; packaging
(Linux + Windows), a player-facing README, a tagged release. Exit: a stranger downloads and
plays without cloning.

**Sequencing rationale.** F1 first because every later phase builds on saves,
accounting, and honest baselines, and each item is small. F2 before F3 because a run
worth choosing must be a fight worth feeling — and because the pipeline script
unblocks Brandon's drawing to proceed in parallel with everything else. F3 before F4
because new content lands better inside structures that give it meaning. F5 runs
throughout (tests land with their features) but earns a dedicated push before F6.
