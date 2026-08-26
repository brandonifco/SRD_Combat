# SRD_Combat

A turn-based tactical combat game on **SRD 5.2.1** (2024 rules, CC-BY-4.0). Party of
four, levels 1–5, full tactical grid, persistent thirty-fight gauntlet with XP,
levelling, and loot. Combat only — no exploration, dialogue, or travel.

**Three documents govern this project.** Read them in this order:

1. **This file** — the finishing plan, the team, and the operational rules.
2. [`docs/2026-08-21-project-review.md`](docs/2026-08-21-project-review.md) — the
   independent four-viewpoint audit the plan is built from. Every plan item cites a
   finding there.
3. [`docs/2026-08-11-design-and-development-plan.md`](docs/2026-08-11-design-and-development-plan.md)
   — the original design doc: kickoff decisions, the architecture and why it diverges
   from `5eGoldBox`, the phase history.

**The full development narrative is archived, not deleted.** This file once carried
2,250 lines of measured history — every pacing table, the squad-AI series, the client
and art evolution, the closed rules backlog with its reasoning. All of it is preserved
verbatim at [`docs/history/2026-08-21-claude-md-archive.md`](docs/history/2026-08-21-claude-md-archive.md).
When a bullet below feels compressed, the archive has the long form with the evidence.

## Current state — read this first

**As of 2026-08-25, F1 closed at `8ca55aa` (PR #421).** All numbers verified, not estimated.

| | |
| --- | --- |
| Tests | **4,410 passing**, 1 skipped by design (the transcript fixture writer) — measured 2026-08-25 at `8ca55aa`: `SrdExtract.Tests` 3,328 (the #189/#382 harness — characterization fixtures, whole-corpus round-trip, verbatim-invariant and glue-census checks), `Core.Tests` 618, `Content.Tests` 226, `Game.Tests` 238 |
| Build | Debug and Release, **0 warnings** (`TreatWarningsAsErrors`) |
| Content | 330 monsters · 339 spells · 12 classes · 9 species · 4 backgrounds · 38 weapons · 13 armor · 258 magic items (13 executed) — counts re-verified from `data/srd` at `8ca55aa`; F1's regenerations (#382, #370–#373, #421) changed entry structure and residue in `monsters.json`, never the roster |
| Playable | The whole gauntlet, console and Godot clients, character creation in both, autosave/`--continue`, fog of war, 28 × 18 board |
| Pacing | Measured at `8ca55aa` (`main`, the F1 closing merge), 2026-08-25 — the **promoted F1-exit baseline**, superseding the provisional post-#347 entry and the #382-branch measurement. Seeds 1–120: median 18 of 30, 43 clear all, 53 reach level 4, died-by-fight-4 10; ended Cleared 43 / Defeated 77. Seeds 200–320: median 18, 43 clear all, 59 reach level 4, died-by-fight-4 8 (of 121); ended Cleared 43 / Defeated 78. **Zero `Stalled`** in both. Per-band hp-left 84→77→70→72→75→74% (1–120) and 83→77→71→72→75→72% (200–320). Against the #382 stage-4–6 measurement (clears 32/35, level-4 48/46): clears and level-4 attainment recovered past the census dip on both ranges — the direction the semantic fixes predict, since the two largest (#370, #371) each removed systematic *over*-damage against the party (full damage on successful saves; full swarm damage while Bloodied). The median saturates at 18 — read `shape:`, `ended:` and the per-band lines, per the standing convention |
| Party depth | 6 of 12 classes offered, 17 of 339 spells execute, 6 of 8 masteries, ~24 class-feature names, 13 magic item names |
| Coverage gaps | 24% of production code untested (`client/` 7.4k, `Console` 1.9k lines, of 38.7k total) — down from 41%: `tools/SrdExtract` (7.0k) gained its harness in F1 (#189's first slice); the clients are F5's remaining gap. The Godot client gained `tests/SRDCombat.Viewer.Tests` on 2026-08-26 (#190) — the log highlighter, the sprite metrics and the draw scale, every test knockout-verified — but its argument wiring and `PlayMode`'s own state are still pinned by nothing (#490), pending the seam #491/#473 own; the console client's 1.9k lines remain untested *and* unfiled |
| Work queue | `gh issue list`. **Not this file, not chat.** |

**What works.** A whole run, end to end, in both clients: grid combat with cover
degrees, opportunity attacks, conditions with printed durations, concentration,
areas, warbands of 6–10, three objectives, generated terrain and layouts, Weapon
Mastery, an economy, loot, rests, XP levelling, death and revival — all measured on a
committed instrument, all print-faithful or refused with a named code.

**What the review found wanting** (full detail in the review doc): the fight has
almost no feedback — one-frame monster art, no audio at all, hit and miss visually
identical; the run has no route choice, loot decisions or ironman stakes; the honesty
rule's Multiattack accounting has three breaks closed and one still open — closed: the
*replace-clause* hole (#290), alternative compositions that were summed instead of
chosen between (#342), and the fourteen sub-sentence composition clauses folded inside
a composition sentence that read as fully modelled (#341); open: #343, where nineteen
enumerated fixed compositions ("one Bite attack and one Claw attack") record
`AnyCombination: true` for want of per-name counts on `MultiattackEffect`, so a Brown
Bear may double-Bite and nothing says so — the spell lane is answered by retiring a
signal that could not be derived rather than faking one (#292), and species
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
**The finishing plan below is the ordered answer.**

## The finishing plan

### Definition of Finished (v1.0)

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

### The phases

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

**F2 — Feel.** The largest gap per hour of work. One committed master→sprite pipeline
script — **mechanical-only since 2026-08-26** (facing, crop, downscale, hard alpha;
the palette and de-grain steps were removed at Brandon's direction after PR #446's
"made of metal" verdict — colour is his alone, and no script reinterprets it) —
Brandon approves before/after for every batch; ship the ~23 finished-but-unshipped
masters (they cover most creatures now rendering as circles); fix the stature clamp
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
fight 24 rather than never (only 53 of 120 and 59 of 121 runs reach level 4 — the
Pacing row above, current baseline); reprice or redesign the free `Survive(3)` rung;
per-cycle variety so the six cycles are not one cycle six times (#192 — and, since
2026-08-25, the per-cycle site weighting the battlefield overhaul deliberately left
here; #243 itself was superseded into F2, see the F2 note above). Exit:
measured curve holds through the back half on both ranges; a human run report exists
for every new system.

**F4 — Depth and variety.** Spellcasting enemies enter the pool (all ten CR ≤ 4
casters are currently filtered out — thirty fights contain no enemy magic); the
`Playable` grade reads all sections, not just Actions (#231); CR-band fill-ins (#267
— the boss band holds three creatures); retune `ClassicMonsterWeight` (it now
double-penalises the 14 surviving genre-appropriate Beasts); fog slice 2 (#244);
policy growth where measurement pays: Dodge/Disengage/retreat, behind an
`ITacticsPolicy` seam so two policies A/B on the same seeds. Decide the six
unoffered classes: ship or cut, not linger. Exit: distinct-creature measurement
re-run; a property test that every generated encounter resolves.

**F5 — Confidence.** (Two items pulled forward on 2026-08-24: the extractor test
project's first slice, #189, to F1 as the span refactor's safety net, and the
PlayMode refactor, #327, to F3's entry gate — the broader page-fixture harness still
grows here.) Client behaviour
tests grown from the probe harness (#190 — the test project landed 2026-08-26 with
the reachable half: log colouring, sprite metrics, draw scale; the parts behind
Godot statics wait on #491/#473's spec type, and #490 stays open for them);
console client tests (1.9k lines,
currently untested *and* unfiled); shared test-support project; xUnit content
fixtures (the corpus is currently loaded 27 times; `Game.Tests` took 2m14s in the
2026-08-25 exit run, down from the 7m22s this item was filed at — the fixture case
stands on the 27 loads, not the wall clock); the
`Encounter` guard-preamble helper, and the action seam if the class list grows —
trigger-based, with #369 (Turn Undead) the likeliest trigger.
Runs continuously alongside F2–F4; has its own closing push. Exit: suite under ~3
minutes; a parser edit fails a test on a machine without the PDF.

**F6 — Ship.** In-game attribution screen (CC-BY requires notice in the distributed
artifact, not just the repo); NOTICE covers the art and the masters' licence; an
LFS-or-release-assets strategy for the 291 MB masters tree; packaging (Linux +
Windows), a player-facing README, a tagged release. Exit: a stranger downloads and
plays without cloning.

**Sequencing rationale.** F1 first because every later phase builds on saves,
accounting, and honest baselines, and each item is small. F2 before F3 because a run
worth choosing must be a fight worth feeling — and because the pipeline script
unblocks Brandon's drawing to proceed in parallel with everything else. F3 before F4
because new content lands better inside structures that give it meaning. F5 runs
throughout (tests land with their features) but earns a dedicated push before F6.

## The team

Seven agents in [`.claude/agents/`](.claude/agents/), each with its charter in its
own file. Models are chosen by the shape of the work: **Fable** where the work is
judgement (design, adversarial review, sequencing), **Opus** where it is abstract but
bounded (architecture, statistics), **Sonnet** where it is well-specified execution.

| Agent | Model | Owns |
| --- | --- | --- |
| `steward` | fable | The queue, sequencing, doc truth, measurement ledger, release checklist |
| `designer` | fable | Rules readings, run/economy/encounter design, F3, sign-off on any print divergence |
| `qc` | fable | Adversarial review of every PR, honesty-accounting audits, transcript-churn reads |
| `architect` | opus | Seams and refactors, extractor test harness, threat-model instrument, save evolution |
| `analyst` | opus | PacingMeasure runs and interpretation, statistical discipline |
| `engineer` | sonnet | Scoped implementation with written acceptance criteria |
| `art-tech` | sonnet | The asset pipeline (mechanical-only — never colour), stature, shipping masters |

**Protocol.**

- **The issue queue is the only work queue.** Nothing is worked without an issue;
  the steward triages into phase order.
- **Route by ambiguity.** An open judgement goes to `designer` (game) or `architect`
  (code); a written spec goes to `engineer`/`art-tech`. When in doubt route up — then
  write the acceptance criteria that let it route down next time.
- **One concern, one branch, one PR** (the standing law). Every gameplay-affecting PR
  quotes PacingMeasure on **both** canonical seed ranges against a same-build
  baseline. `qc` reviews before Brandon sees anything.
- **What stays human.** Brandon draws all art (agents never redraw it), plays runs,
  and owns taste. A played-run complaint outranks any measured number. Art batches
  land only with his before/after approval. Merging is **not** on this list —
  Claude merges every PR itself once CI is green (`gh pr merge <n> --merge`, merge
  commits); an earlier version of this bullet claimed Brandon merges, which he never
  did, and he corrected it explicitly on 2026-08-24.
- **Docs are part of the diff.** A change that invalidates a doc-comment, a plan row,
  or a claim in this file fixes it in the same commit. History is archived to
  `docs/history/`, never deleted.
- **Three strikes escalates the mechanism** (2026-08-24). QC already counts
  recurrences of a bug shape; the **third** occurrence of the same shape auto-files a
  mechanism issue — "is the abstraction under this still right?" — which the steward
  must triage, even if the answer is "keep patching". The patch still ships; the
  question can no longer be silently deferred. The goblin shape reached fourteen
  occurrences before its mechanism (sentence-credit accounting) was put on trial, and
  the trial came from outside (#382).
- **Each phase exit includes a mechanism review** (2026-08-24). Alongside the
  correctness sweep and re-baseline, a bounded pass answered from the phase's own
  issue and PR history: which mechanism absorbed the most patches, which bug shapes
  recurred, which abstraction would we not rebuild the same way. The reviewer reads
  **code first, doctrine second**, and writes down where this file's framing misled.
  Output is filed issues, never inline action — this is a trigger for questions, not
  a license to refactor.
- **Outside review twice, plus Brandon's own** (2026-08-24, Brandon's commitment).
  Brandon sources a genuinely independent review **before F3's build starts** and
  **before F6 ships**, each paired with his own long played run against its findings.
  Internal adversarial review approximates epistemic independence; an outside
  artifact is the real thing — the 2026-08-24 critique produced the span-accounting
  decision (#382) and #327's re-sequencing, neither of which was arriving from inside
  on its own schedule.

## The rule this project runs on

**Nothing may hold unimplemented rules silently.** A stat block's entries contain no
flavour text — `it has the Grappled condition (escape DC 13)` is a rule. So:

- Every entry, trait, class feature and spell is **classified**; there is no "just
  prose" state. For the stat-block, trait and feature lanes `EntryMechanics` is the
  enum and `IsFullyModelled` is the test; spells are the stated exception below.
- **The stat-block accounting is coverage-by-consumption** (#382,
  `docs/2026-08-24-span-accounting-design.md`; the type is `EntryCoverage` in
  `tools/SrdExtract/Parsing/`). Every structured extraction **claims** the characters
  it consumed, and `UnmodelledClauses` is the uncovered **residue, computed by
  subtraction** — nobody credits a sentence by its label. A claim asserts the model
  *expresses* those characters — not that a regex matched them, not that a string was
  stored — so **the claim follows the code and never leads it**: text a permissive
  subexpression swallowed unread, and prose a field stores verbatim without an
  executing resolver, stay unclaimed and land in residue.
- **The glue rule is the mechanism's one rot risk, so it is deliberately tiny**: four
  punctuation marks, and "and"/"or"/"plus" only when bounded on *both* sides by
  claimed spans. Anything not provably glue is residue — residue is cheap (a counted
  clause, read once in a census), while a lazy glue match is bug 2 below rebuilt
  inside the mechanism meant to prevent it. Widen it only with the worked table in
  the design doc §4 open.
- **Spells are the one exception, and it is stated rather than silent.** A stat block
  entry is all mechanics in a printed grammar, so a leftover sentence is a lost rule;
  a spell description is prose that is mostly flavour, and the same accounting run over
  it is *anti-correlated* with the truth — measured at 7 of 154 "fully modelled", of
  which two are false positives and thirteen of the seventeen hand-verified spells come
  out false. So spells carry `UnclassifiedClauses` (non-empty exactly when nothing was
  classified) and **no completeness signal at all**: `PreparableSpells` is the authority
  (#292). The reasoning lives on `SpellDefinition.UnclassifiedClauses`.
- `Narrative` — "confirmed to do nothing in a fight" — is only ever set from a
  curated list, never inferred.
- An action the engine cannot resolve is **refused with a named code**, never skipped.
- Where a rule is a judgement call, **write the reading down** in the code's doc
  comments. `AreaTargeting` is the model.

**Three bugs produced that rule, and the first bug's fourteenth occurrence retired
a mechanism. Read this before touching a parser:**

1. **The Goblin Warrior's "plus 2 (1d4) damage *if the attack roll had Advantage*"**
   was read as unconditional, so every hit dealt it. Nothing failed — the attack
   *looked* implemented. **A partly-structured entry is more dangerous than an
   unstructured one**, because the missing part is invisible. That shape — an
   **omission**: a printed clause nothing read, hidden behind an entry that looked
   handled — reached **fourteen occurrences** (#382's tally) under the old
   sentence-credit accounting
   (among them rider gating, save-spell effects, Failure-tier sentences, Multiattack
   replace-clauses #290, the Clay Golem's summed alternative compositions #342 —
   five Slams a turn against a printed maximum of three — bundled composition
   clauses waved through with empty `UnmodelledClauses` #341, or-tiers dropped
   whole #371, plural condition conjunctions parsed as nothing #372, riders behind
   accounted labels #373), each patched at the instance while the credit rule that
   produced them survived. #382 put the mechanism itself on trial: credit-by-label
   is deleted, and under coverage-by-consumption over a closed 330-monster corpus
   **the omission class is closed by construction** — a clause nothing claims is
   visible residue, censused once, with nowhere left to hide. Do not hunt for the
   fifteenth omission; the mechanism shows it to you.
   What span coverage does **not** close is the **misattribution class**: a claim
   that consumed text under the wrong reading. Current case law — #370 (a success
   tier attached to the whole entry instead of the clause it governs), #375
   (or-as-and, a spell dealing both alternatives), #407 (polarity: immunity and
   removal prose recorded as an *imposition*), #412 (a rider claim correct only
   because of a corpus invariant asserted nowhere). Its guards are different:
   characterization fixtures (#189), verification against the PDF, **trip-wire
   tests** that assert the invariant a reading rests on so the first
   counter-example forces a decision (#412 is the pattern), and the three-strikes
   rule. Assume the next parser bug is a misattribution, and write its trip-wire.
2. **A "does this look mechanical?" keyword filter** let Flyby, Nimble Escape and
   Shape-Shift through as inert. The heuristic was **removed rather than tuned**: a
   keyword list always has false negatives, and a false negative loses a rule. (The
   glue rule above is this bug's standing temptation — that is why it is closed-set
   and both-sides-bounded.)
3. **Reusing the stat block classifier on spells** read every metadata field and
   found zero of 300 saving throws — a monster prints an explicit DC and average, a
   spell prints neither. Spells have their own grammar (`SpellEffectParser`).

**Whether a condition rider lands is two questions, kept apart on purpose.** *Does
the model express it?* — anything printed with the condition that the model lacks a
shape for goes to `AppliedCondition.UnmodelledRequirement` and refuses the rider
rather than approximating it. *Does the engine execute it?* —
`ConditionRules.Executable` is a curated allowlist (twelve conditions today,
Petrified included whole; Deafened and Invisible deliberately absent for want of
hearing and sight models). **Add a condition only alongside the code that gives it
effects.** The Failure-tier extraction case law — repeat-save joins, embedded attack
saves, tier rules, escalating gazes, head-clause accounting, grapple-tied riders —
is detailed in the archive and in `EntryMechanicsParser`'s and `ConditionRules`'
doc comments; read those before changing either.

**Durations hang off a turn counter, not a countdown** (`ConditionExpiry`: whose
turns, which boundary, fixed at application). The clock ticks for every creature
whose turn comes round, dead or Unconscious included. **Read the possessive**:
"until the end of *its* next turn" is the bearer; "until the start of *the devil's*
next turn" is the imposer — swapping them moves the duration most of a round.

**Two grapple rules memory gets wrong**, both caught by reading the glossary:
Grappled is Disadvantage only against targets *other than the grappler*, and this
SRD has **no generic Escape action** — escape is Athletics *or* Acrobatics against a
flat DC. `Encounter.EndBrokenGrapples` sweeps grapples broken by death, incapacity
or range, from every point where either could change.

**When you touch `ConditionRules.Executable`, re-run the extractor.** The rider
claims call `CanBeImposed`, so the allowlist decides what a rider may claim and
therefore what lands in `UnmodelledClauses` as residue; skipping regeneration leaves
content disagreeing with code, and the symptom is a content test failing on an entry
you did not edit.

**Test-coverage percentages are an internal check, not project status.** Span
coverage is the opposite — `UnmodelledClauses` residue *is* the honesty accounting,
and its movements are reviewed in every regeneration.

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

## Extraction traps — read before parsing another SRD chapter

Every one of these failed **silently**, caught only by a validator or by checking
against the book:

- **Typeface differs by chapter** (Cambria player-facing, Optima bestiary); match
  the style suffix, not the whole font name.
- **Weight differs within a table**; match the family (`GillSans`), not the face.
- **A class page mixes two layouts** — two-column body plus full-width table;
  `ClassParser` reads each page twice.
- **Don't split key from value on a gap**; match the closed set of known keys.
- **Table header columns are 12pt+ apart; words within a column 2–5pt.**
- **Not every caster uses the same table** (the Warlock's slot columns).
- **The Sorcerer's feature column wraps** — join on the raw cell, re-split, and only
  the line directly under a parsed row may join. Validator:
  `class.feature.no_heading`.
- **The two-column pass can slice the full-width table into feature prose** (#116) —
  prose is Cambria, tables are GillSans, so features append only Cambria lines.
  Validator: `class.feature.table_noise`.
- **The origins chapter has the same trap, and a wide column makes it worse** (#374):
  Draconic Ancestors, Elven Lineages and Fiendish Legacies are full-width tables
  inside the two-column species pages, sliced and interleaved into whichever trait
  was open — `OriginParser` now appends only Cambria lines to a trait, same as
  `ClassParser`. The Elven Lineages table's first column is wide enough to cross the
  column boundary outright, landing its fragments in the *next* species entirely:
  Gnome's Gnomish Lineage carried Elf's table, and Human's Versatile carried
  Tiefling's Fiendish Legacies. Validator: `species.trait.table_noise`.
- **A wrapped class list dropped 39 of 339 spells for months** while a `>= 300`
  floor test stayed green. Two lessons: *a number the pipeline prints about itself
  is not a check*, and *a floor is the wrong shape for a count fixed by the source*
  — exact counts for the book's totals, floors only for what should grow.

**The general lesson: write the validator that asserts the shape of what should have
been found.**

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

## Environment

**Do not read this section to learn what is on the machine — run the script:**

```bash
./scripts/doctor.sh
```

Every environment problem this project has had was silent. The reasons behind each
check: the machine's .NET has flipped four times, and `global.json`'s roll-forward
used to mean a green local build could compile on a different major than CI gates
(`.mise.toml` pins the SDK; `mise install` is the whole setup, and the
`mise activate` line in the shell profile is the step that actually does the work).
SDK 8.0.129's early compiler rejects syntax newer 8.0.x accepts (#27) — that is why
CI's `setup-dotnet` step reads `global-json-file: global.json` rather than a floating
`dotnet-version: 8.0.x` (#428, after `main` drifted past the pin while CI, still on a
floating version, stayed green). Reading the pin from `global.json` alone did not
close the gap: with `rollForward: latestMajor`, `setup-dotnet` reads "the pin" as
"install the newest LTS channel," so CI kept silently resolving whatever SDK the
runner image preinstalled newest (up through .NET 10) — a second, narrower #428. The
closing fix set `rollForward: disable`: no SDK but the exact pinned patch satisfies
`global.json`, on the runner or on your desk, and a CI step right after every
`setup-dotnet` step fails the job outright if `dotnet --version` differs from the pin
at all. That hazard — a machine with no .NET 8 silently building on .NET 10 while
believing it built on the pin — is closed, not just documented as closed: local green
under the pin now predicts CI green, or CI fails loudly instead of drifting quietly.
`dotnet new sln` under SDK 10 emits `.slnx`, which .NET 8
cannot read (`--format sln`), and templates write `net10.0` properties that override
`Directory.Build.props` — strip them. The SRD PDF lives at
`~/Downloads/SRD_CC_v5.2.1.pdf`, is never committed, and only `tools/SrdExtract`
needs it. Godot 4.7 mono is on `PATH` for the client; the build itself needs neither
Godot nor a display (the SDK is a NuGet package). A real X display exists
(`DISPLAY=:1`) for windowed runs and captures.

## The SRD source and the extraction pipeline

The source PDF is `~/Downloads/SRD_CC_v5.2.1.pdf` (364 pages), not in the repo.
`reference/` holds gitignored text extractions
(`pdftotext ~/Downloads/SRD_CC_v5.2.1.pdf reference/SRD_raw.txt`; pages are
two-column — crop per column with `-x/-W` when eyeballing, the page is 594pt wide).

The real pipeline is PdfPig with per-word coordinates and fonts:

```bash
dotnet run --project tools/SrdExtract -- --out data/srd
```

It refuses to write on validation errors (`--force` overrides). A clean run reports
330 monsters, 339 spells, 38 weapons, 13 armor, 258 magic items, 0 errors, and
**15 warnings, all expected** (the Archmage's XP — a real SRD inconsistency kept
deliberately; twelve column-break-truncated spell component lines; two "Rarity
Varies" items). Trust the run over any prose count.

**Fonts matter more than text** (`StatBlockFonts`): the same font at different sizes
is different signals, and `GillSans` / `GillSans-SemiBold` / `GillSans-SemiBold-SC700`
are three signals a substring test conflates — match exactly. Source variances
already handled (check before assuming a parser bug): `5 ft.` and `5 feet`; four
blocks with CR fields flipped; flat damage with no dice; `Melee or Ranged Attack
Roll` must be first in the regex alternation; the ability table's positional
MOD/SAVE triples. `KnownCorrections` holds the one hand repair and self-invalidates
when stale.

Page ranges (printed numbers = PDF indices): classes 28–82, origins 83–86, feats
87–88, equipment 89–103, spells 104–175, glossary 176–191, toolbox 192–203 (**XP
budgets on 202**), magic items 204–253, monsters 254–343, animals 344+.

## Running the game

```bash
dotnet run --project src/SRDCombat.Console
```

`--seed <n>` replays a run exactly (the seed prints at start, so "seed 12345" is a
complete bug report — and within a run, `(seed, fight number)` reproduces that
fight's encounter and every dice roll in it, regardless of the play history that got
there; see `RunDice`'s remarks); `--level 1..5`, `--one-fight --difficulty
low|moderate|high`, `--create` for party creation; autosaves to
`srdcombat-save.json` after every cleared fight, `--continue` resumes. **A save is
drafts plus progress, never resolved sheets** — loading re-resolves at the level
experience has earned, so a save cannot smuggle a level and a reload cannot reroll
history. Defeat does not touch the save. The Godot client (`client/`) plays the same
gauntlet with the mouse; `--watch` and `--probe` are its read-only and self-driving
modes. **The clients hold no rules** — they call the engine's public actions, print
`CombatStep.Narration`, and show every refusal *with its code*.

## Build and test

```bash
dotnet build SRDCombat.sln -c Debug
```

```bash
dotnet test SRDCombat.sln -c Debug
```

**0 warnings expected in both configurations.** New machine:
`mise install && ./scripts/doctor.sh` first (see Environment).

## Standing conventions

- **`git add` specific paths, never `-A` or `.`**
- **One narrowly-scoped branch per concern; branch → push → PR → wait for CI → stop.**
  Brandon reviews and merges. Never merge your own PR, never push to `main`.
- **Confirm a merge really happened** before branching from `main`
  (`gh pr view <n> --json state,mergedAt` — the 504s lie), or a stale base silently
  drops the previous slice.
- **File found-but-deferred work as a GitHub issue**, not in this file and not in
  chat.
- **Gate before merge**: focused tests → full suite → Debug **and** Release at 0
  warnings → `git diff --check`.
- **Gameplay PRs carry measurement**: `tools/PacingMeasure -- --seeds 1-120` and
  `200-320`, same-build baseline, quoted in the PR body. The median saturates — read
  `shape:`, `ended:`, per-band and per-count lines.
- **A spot check stands in for the full ranges only when the change is structurally
  CR-pool-inert.** If a diff cannot move which monsters `MonsterPool.Draw`'s CR ≤ 4
  filter admits — proven structurally (the pool's CR ceiling, not just an eyeballed
  diff), not merely observed after the fact — `tools/PacingMeasure -- --seeds 1-20`
  against the same-build baseline satisfies the gate in place of both canonical
  ranges. Precedent: #356, #357, #358.
- **There is no versioned DTO mirror and no generated schema, deliberately.** The
  guards are `UnmappedMemberHandling.Disallow` and the serializer shape tests. Adding
  a field to a `Core` definition plus re-running the extractor is the whole change.
- **When a design decision proves wrong, correct the doc in the same commit as the
  code.** History moves to `docs/history/`, never to `/dev/null`.
- **Agents follow the team protocol** in [The team](#the-team) and their charters in
  `.claude/agents/`.

## Attribution obligation

SRD 5.2.1 is CC-BY-4.0: derived content ships, but the attribution in
[`NOTICE.md`](NOTICE.md) is required and must stay accurate — and per the SRD's own
terms, no other attribution to Wizards of the Coast may be added. The **code** is MIT
(scoped in `README.md` and `NOTICE.md`; `data/` stays CC-BY-4.0 — a blanket MIT
would imply this project may relicense Wizards' content, which it may not). F6 adds
the in-game attribution screen a distributed binary needs.
