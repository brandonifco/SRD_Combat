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

**As of 2026-08-21, at PR #282.** All numbers verified, not estimated.

| | |
| --- | --- |
| Tests | **972 passing**, 1 skipped by design (the transcript fixture writer) |
| Build | Debug and Release, **0 warnings** (`TreatWarningsAsErrors`) |
| Content | 330 monsters · 339 spells · 12 classes · 9 species · 4 backgrounds · 38 weapons · 13 armor · 258 magic items (13 executed) |
| Playable | The whole gauntlet, console and Godot clients, character creation in both, autosave/`--continue`, fog of war, 28 × 18 board |
| Pacing | Seeds 1–120: median 18 of 30, 27 clear all, 57 reach level 4; seeds 200–320: 18/31/50. Per-band hp-left 83→76→68→73→67→69% (2026-08-21) |
| Party depth | 6 of 12 classes offered, 17 of 339 spells execute, 6 of 8 masteries, ~24 class-feature names, 13 magic item names |
| Coverage gaps | 41% of production code untested (`client/` 7.2k, `tools/SrdExtract` 5.4k, `Console` 1.8k lines) |
| Work queue | `gh issue list`. **Not this file, not chat.** |

**What works.** A whole run, end to end, in both clients: grid combat with cover
degrees, opportunity attacks, conditions with printed durations, concentration,
areas, warbands of 6–10, three objectives, generated terrain and layouts, Weapon
Mastery, an economy, loot, rests, XP levelling, death and revival — all measured on a
committed instrument, all print-faithful or refused with a named code.

**What the review found wanting** (full detail in the review doc): the fight has
almost no feedback — one-frame monster art, no audio at all, hit and miss visually
identical; the run has no between-fight decisions and no failure stakes (a reload
re-rolls the ladder because the seed is not saved); the honesty rule has one known
break left (Multiattack sub-sentence composition clauses — #341) — the Multiattack
*replace-clause* hole is closed (#290), the spell lane is answered by retiring a
signal that could not be derived rather than faking one (#292), and species
traits are no longer a silent one: none of the 33 printed trait instances execute, but
`SpeciesTraitRegistry` and `CharacterSheet.UnimplementedFeatures` now say so at
creation and on the sheet (#291); the undocumented rules gap the review found —
concentration surviving Incapacitated — is fixed (#289); the art pipeline is
unrepeatable and one sprite in ~60 matches the project's own palette. **The finishing
plan below is the ordered answer.**

## The finishing plan

### Definition of Finished (v1.0)

A stranger can download a release, build a party, and play a thirty-fight run with
mouse or keyboard — and it holds up:

1. **Nothing lies.** Every printed rule executes or refuses with a named code; the
   honesty accounting holds for monsters, spells, species, classes, and items alike;
   the player is told, at the point of choice, what does not work yet.
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

**F1 — Integrity.** Cheap, compounding correctness debts. Atomic save write plus one
backup; persist the run's seed so `--continue` after defeat retries *the same fight*;
stamp content version into the save and refuse drift via `TryGetValue`
(`PregeneratedParty`, `Gauntlet`, `Loot`, `Shop`); ask for the level-4 ASI plan in
both creation flows (created parties currently forfeit it silently); break
concentration on Incapacitated; close the Multiattack replace-clause hole
(`MatchesStructuredForm`) and regenerate; surface Multiattack sub-sentence
composition clauses folded into the composition sentence itself (#341); surface
species traits as unimplemented at creation and on the sheet; retire
`SpellDefinition.IsFullyModelled` (#292 — measured, not derivable on spell prose;
`PreparableSpells` is the authority); fix
the stall class (#256) and immunity-blind targeting (#224); one doc-drift sweep
(gauntlet cycle arithmetic, mastery weapon count, stale headers and citations, client
README). Exit: re-baseline both seed ranges; QC audits the three honesty lanes clean.

**F2 — Feel.** The largest gap per hour of work. One committed master→sprite pipeline
script per #238's own diagnosis, applied to every sprite and terrain tile —
Brandon approves before/after for every batch; ship the ~23 finished-but-unshipped
masters (they cover most creatures now rendering as circles); fix the stature clamp
(an Ogre must not render shorter than a Goblin); integer-snap ground scaling. Board
feedback: floating damage numbers, hit/miss/death visually distinct, health readout
carrying state, an audio pass (a dozen sounds: hit, miss, death, cast, UI — silence is
currently total). Foresight: opportunity-attack threat marking, range/AoE previews,
path preview, tooltip latency to ~0.5 s, terrain hints, log-space fix. Exit: probe
screenshots show it; a watcher can narrate a fight with the log covered.

**F3 — The run becomes a game.** The largest design gap; Fable-led design, spec'd
before built. Route choice (pick the next rung from 2–3 revealed options); loot as a
pick-one-of-three moment, and at least a handful of items that change a turn rather
than a stat; shop trade-offs (retire the strictly-better gate); failure stakes
(attempt counter, run summary, opt-in ironman); XP curve so level 5 arrives around
fight 24 rather than never (57 of 120 runs currently die before level 4); reprice or
redesign the free `Survive(3)` rung; per-cycle variety so the six cycles are not one
cycle six times (#192, #243). Exit: measured curve holds through the back half on
both ranges; a human run report exists for every new system.

**F4 — Depth and variety.** Spellcasting enemies enter the pool (all ten CR ≤ 4
casters are currently filtered out — thirty fights contain no enemy magic); the
`Playable` grade reads all sections, not just Actions (#231); CR-band fill-ins (#267
— the boss band holds three creatures); retune `ClassicMonsterWeight` (it now
double-penalises the 14 surviving genre-appropriate Beasts); fog slice 2 (#244);
policy growth where measurement pays: Dodge/Disengage/retreat, behind an
`ITacticsPolicy` seam so two policies A/B on the same seeds. Decide the six
unoffered classes: ship or cut, not linger. Exit: distinct-creature measurement
re-run; a property test that every generated encounter resolves.

**F5 — Confidence.** The extractor gets a test project with page fixtures that run
without the PDF (#189 — the riskiest code has the least coverage); client behaviour
tests grown from the probe harness (#190); console client tests (1.8k lines,
currently untested *and* unfiled); shared test-support project; xUnit content
fixtures (the corpus is currently loaded 27 times; `Game.Tests` takes 7m22s); the
`Encounter` guard-preamble helper, and the action seam if the class list grows.
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
| `art-tech` | sonnet | The asset pipeline, palette conformance, stature, shipping masters |

**Protocol.**

- **The issue queue is the only work queue.** Nothing is worked without an issue;
  the steward triages into phase order.
- **Route by ambiguity.** An open judgement goes to `designer` (game) or `architect`
  (code); a written spec goes to `engineer`/`art-tech`. When in doubt route up — then
  write the acceptance criteria that let it route down next time.
- **One concern, one branch, one PR** (the standing law). Every gameplay-affecting PR
  quotes PacingMeasure on **both** canonical seed ranges against a same-build
  baseline. `qc` reviews before Brandon sees anything.
- **What stays human.** Brandon merges every PR, draws all art (agents never redraw
  it), plays runs, and owns taste. A played-run complaint outranks any measured
  number. Art batches land only with his before/after approval.
- **Docs are part of the diff.** A change that invalidates a doc-comment, a plan row,
  or a claim in this file fixes it in the same commit. History is archived to
  `docs/history/`, never deleted.

## The rule this project runs on

**Nothing may hold unimplemented rules silently.** A stat block's entries contain no
flavour text — `it has the Grappled condition (escape DC 13)` is a rule. So:

- Every entry, trait, class feature and spell is **classified**. `EntryMechanics` is
  the enum; `IsFullyModelled` is the test. There is no "just prose" state.
- Anything the model cannot express lands in `UnmodelledClauses` and is **counted**,
  including on entries that are otherwise structured.
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

**Three bugs produced that rule. Read them before touching a parser:**

1. **The Goblin Warrior's "plus 2 (1d4) damage *if the attack roll had Advantage*"**
   was read as unconditional, so every hit dealt it. Nothing failed — the attack
   *looked* implemented. **A partly-structured entry is more dangerous than an
   unstructured one**, because the missing part is invisible. This shape has recurred
   four times (rider gating, save-spell effects, Failure-tier sentences, and
   Multiattack replace-clauses — closed by #290). Assume a fifth exists — it did:
   a Multiattack's own composition sentence can fold an unexecuted rule inside itself
   (the Mummy's "and uses Dreadful Glare", the Kraken's "and uses Fling..."), which
   `DescribesTheComposition` waves through with an empty `UnmodelledClauses` because
   the composition it recognises still matches. Filed as #341.
2. **A "does this look mechanical?" keyword filter** let Flyby, Nimble Escape and
   Shape-Shift through as inert. The heuristic was **removed rather than tuned**: a
   keyword list always has false negatives, and a false negative loses a rule.
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

**When you touch `ConditionRules.Executable`, re-run the extractor.** The accounting
calls `CanBeImposed`, so the allowlist decides what lands in `UnmodelledClauses`;
skipping regeneration leaves content disagreeing with code, and the symptom is a
content test failing on an entry you did not edit.

**Coverage numbers are an internal check, not project status.**

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
means a green local build can compile on a different major than CI gates
(`.mise.toml` pins the SDK; `mise install` is the whole setup, and the
`mise activate` line in the shell profile is the step that actually does the work).
SDK 8.0.129's early compiler rejects syntax newer 8.0.x accepts (#27) — local green
does not prove CI green. `dotnet new sln` under SDK 10 emits `.slnx`, which .NET 8
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
complete bug report); `--level 1..5`, `--one-fight --difficulty low|moderate|high`,
`--create` for party creation; autosaves to `srdcombat-save.json` after every cleared
fight, `--continue` resumes. **A save is drafts plus progress, never resolved
sheets** — loading re-resolves at the level experience has earned, so a save cannot
smuggle a level and a reload cannot reroll history. Defeat does not touch the save.
The Godot client (`client/`) plays the same gauntlet with the mouse; `--watch` and
`--probe` are its read-only and self-driving modes. **The clients hold no rules** —
they call the engine's public actions, print `CombatStep.Narration`, and show every
refusal *with its code*.

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
