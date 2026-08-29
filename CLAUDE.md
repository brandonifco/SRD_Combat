# SRD_Combat

A turn-based tactical combat game on **SRD 5.2.1** (2024 rules, CC-BY-4.0). Party of
four, levels 1–5, full tactical grid, persistent thirty-fight gauntlet with XP,
levelling, and loot. Combat only — no exploration, dialogue, or travel.

**This file is the governing document, and it is deliberately smaller than it was.**
On 2026-08-29 it carried 667 lines into every agent's context before that agent read a
line of code — most of it roadmap and measurements that the task at hand did not need.
What remains here is what is true regardless of which task you drew: the honesty rule,
the invariants, the conventions, the environment. Everything scoped to one subsystem now
sits behind the routing table below, one explicit read away. **Follow the table rather
than searching** — a grep for what used to be inline costs more than the read it
replaces.

## Start here

**Active phase: F2 (Feel) and F3 (The run becomes a game)**, with F5 (Confidence)
running alongside. F1 closed 2026-08-25 at `8ca55aa`. The whole plan, its phase exits
and its sequencing rationale: [`docs/finishing-plan.md`](docs/finishing-plan.md).

**The work queue is `gh issue list`. Not this file, not chat.** Phase labels
(`phase:F1`–`F6`) carry every open issue, so `gh issue list --label phase:F3` is the
board. File found-but-deferred work as an issue.

**The gate is one command:**

```bash
./scripts/validate.sh full
```

### Read before you edit

| If you are touching | Read first |
| --- | --- |
| `tools/SrdExtract`, `data/srd`, any parser | **"The rule this project runs on"** below, then [`docs/guides/extraction.md`](docs/guides/extraction.md) |
| `src/SRDCombat.Core` — combat, movement, cover, conditions | [`docs/guides/engine.md`](docs/guides/engine.md) |
| Characters, spells, levelling, items | [`docs/guides/engine.md`](docs/guides/engine.md) |
| The gauntlet, economy, encounter building | [`docs/guides/engine.md`](docs/guides/engine.md) + [`docs/finishing-plan.md`](docs/finishing-plan.md) F3 |
| `client/` (Godot) or `src/SRDCombat.Console` | [`client/README.md`](client/README.md). The clients hold no rules |
| Art, sprites, `client/assets` | Brandon draws all art. The pipeline is mechanical-only and never touches colour |
| Anything, before you commit | **"Standing conventions"** below |

### Repository map

| Area | Source | Tests |
| --- | --- | --- |
| Rules engine — pure, no I/O, no ambient randomness | `src/SRDCombat.Core` | `tests/SRDCombat.Core.Tests` |
| Content loading from `data/srd` | `src/SRDCombat.Content` | `tests/SRDCombat.Content.Tests` |
| Gauntlet, run state, economy, encounters | `src/SRDCombat.Game` | `tests/SRDCombat.Game.Tests` |
| Console client | `src/SRDCombat.Console` | *none — #317* |
| Godot client | `client/` | `tests/SRDCombat.Viewer.Tests` |
| PDF extractor | `tools/SrdExtract` | `tests/SrdExtract.Tests` |
| Pacing instrument | `tools/PacingMeasure` | — |

### Background, when you need the reasoning

Not required reading — linked so you never have to search for them.

- [`docs/status.md`](docs/status.md) — measured facts, **generated** by
  `./scripts/status.sh`. Never hand-edit it.
- [`docs/finishing-plan.md`](docs/finishing-plan.md) — the phases, their exits, and what
  the project review found wanting.
- [`docs/2026-08-21-project-review.md`](docs/2026-08-21-project-review.md) — the
  independent four-viewpoint audit the plan is built from. Every plan item cites it.
- [`docs/2026-08-11-design-and-development-plan.md`](docs/2026-08-11-design-and-development-plan.md)
  — the original design doc: kickoff decisions, the architecture, why it diverges from
  `5eGoldBox`.
- [`docs/history/2026-08-21-claude-md-archive.md`](docs/history/2026-08-21-claude-md-archive.md)
  — **the full development narrative, archived and not deleted.** This file once carried
  2,250 lines of measured history: every pacing table, the squad-AI series, the client
  and art evolution, the closed rules backlog with its reasoning. When a bullet here
  feels compressed, the archive has the long form with the evidence.

### Invariants you can break without noticing

The rest of each guide only matters once you are in that code. These four bite from
anywhere, so they stay here:

- **`Core` stays pure.** No I/O, no ambient time, no packages. **All randomness goes
  through `IRandomSource`** — never `Random.Shared` in `Core`. Determinism is what the
  frozen transcripts rest on.
- **Nothing may hold unimplemented rules silently.** An action the engine cannot
  resolve is **refused with a named code**, never skipped. See below.
- **Where a rule is a judgement call, write the reading down** in the code's doc
  comments. `AreaTargeting` is the model.
- **Docs are part of the diff.** A change that invalidates a doc-comment, a plan row,
  or a claim in this file fixes it in the same commit.

## Current state

**Measured facts are generated, not typed:** [`docs/status.md`](docs/status.md), from
`./scripts/status.sh`. Test counts, content counts and line counts live there because
every hand-maintained copy of them drifted — this file's table claimed 4,718 tests
against a measured 4,814 on the day it was replaced.

What a script cannot generate is the *reading* of a measurement, so those stay here.

| | |
| --- | --- |
| Playable | The whole gauntlet, console and Godot clients, character creation in both, autosave/`--continue`, fog of war, 28 × 18 board |
| Party depth | 6 of 12 classes offered, 17 of 339 spells execute, 6 of 8 masteries, ~24 class-feature names, 13 magic item names |
| Pacing | Measured at `112ed19`, 2026-08-27 — the **current baseline**, superseding the F1-exit entry, which #433/#451 (battlefield S1) moved. Seeds 1–120: median 18 of 30, **32 clear all**, 53 reach level 4, died-by-fight-4 9; ended Cleared 32 / Defeated 88. Seeds 200–320: median 18, **33 clear all**, 53 reach level 4, died-by-fight-4 14 (of 121); ended Cleared 33 / Defeated 88. **Zero `Stalled`** in both. Per-band hp-left 84→76→69→71→74→72% (1–120) and 82→75→70→71→…% (200–320). **The overhaul made the run markedly harder**: against the F1-exit baseline (43 clear all on both ranges, died-by-fight-4 10/8) roughly a quarter of previously-winnable runs now fail, and the second range's early deaths nearly doubled. #451 measured and quoted that deliberately — it is an accepted change, not a regression — but it is a difficulty shift of the size that wants Brandon's verdict, and S3–S7 land on top of it. #435/#527 (S2) then measured **byte-flat** against this baseline, as a vocabulary slice should. The median saturates at 18 — read `shape:`, `ended:` and the per-band lines, per the standing convention. **This row now moves at re-baselining checkpoints, not per PR** (2026-08-28): `112ed19` stands as the baseline until the next checkpoint measures against it, and work landing in between is not swept |
| Coverage gaps | The console client is the last wholly untested production code, filed as **#317** (line counts: [`docs/status.md`](docs/status.md)). **A blanket "N% untested" figure is retired rather than restated**: the old 24% was a directory proxy that counted a whole tree as untested the moment it had no test project, and it stopped being reproducible once `tools/SrdExtract` (#189) and the Godot client (#190) got theirs — `SRDCombat.Viewer.Tests` now pins the focus stack, the router, the log highlighter, sprite metrics and the draw scale, so `client/` is neither untested nor tested but partly each, and one number cannot say which. What is still true and still specific: **#490** — the live Godot argv boundary (`--spawn` / `--level` refusal wiring) is pinned by nothing, knockout-verified. What #500–#502 and #473/#474 pinned is the *extracted* seams — the focus stack, the router, the scenario type — **not `PlayMode` as a live node: no Viewer test constructs it or invokes `OnReady`**, and that half stays probe-only. Read #490 as covering both, with the argv boundary the sharper end |

**What works.** A whole run, end to end, in both clients: grid combat with cover
degrees, opportunity attacks, conditions with printed durations, concentration,
areas, warbands of 6–10, three objectives, generated terrain and layouts, Weapon
Mastery, an economy, loot, rests, XP levelling, death and revival — all measured on a
committed instrument, all print-faithful or refused with a named code.

**What the review found wanting**, and how much of it is answered, is the finishing
plan's opening section: [`docs/finishing-plan.md`](docs/finishing-plan.md).

## The finishing plan

[`docs/finishing-plan.md`](docs/finishing-plan.md) is the authority. In brief:

- **F0** file the backlog · **F1** integrity — *closed 2026-08-25 at `8ca55aa`*
- **F2** feel — board feedback, foresight, battlefield generation. *Art and audio are
  deliberately sequenced last within it (Brandon, 2026-08-26); visual **mechanics** are
  not art and proceed at normal priority.*
- **F3** the run becomes a game — route choice, loot decisions, stakes, the XP curve.
  Entry gate: the PlayMode modal refactor (#327). **A re-baselining checkpoint.**
- **F4** depth and variety — enemy casters, CR fill-ins, fog slice 2 (#545)
- **F5** confidence — client and console tests, content fixtures (#319), suite under
  ~3 minutes. Runs continuously alongside F2–F4
- **F6** ship — in-game attribution, packaging, a tagged release

**Sequencing rationale**: F1 first because everything builds on saves, accounting and
honest baselines. F2 before F3 because a run worth choosing must be a fight worth
feeling. F3 before F4 because new content lands better inside structures that give it
meaning. F5 throughout, with a dedicated push before F6.

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
- **One concern, one branch, one PR** (the standing law). A PR carries direct evidence
  that its own change is correct — focused tests, deterministic pins, the frozen
  transcript read rather than regenerated. `qc` reviews before Brandon sees anything.
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
for windowed runs and captures — but **`:1` was unreachable on 2026-08-27 and `:0` was**,
confirmed by `xdpyinfo` and independently by three agents, and the probe additionally
needed `--display-driver x11`. Use `:0`. Whether `:1` is gone for good or was simply not
up that night is unresolved, so this records what was measured rather than a new rule.

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

**`scripts/validate.sh` is the canonical gate — humans, agents and CI all call it**, so
the build and test invocation exists in one place instead of three that can drift
(2026-08-29). The whole merge gate is:

```bash
./scripts/validate.sh full
```

That is: SDK pin, restore, build **and** test in Debug **and** Release at 0 warnings,
then `git diff --check` — the same steps this file's "Gate before merge" convention has
always required, now executable. `fast` skips the tests for a quick pre-push check;
`sdk-pin` runs the #428 drift check alone. `.github/workflows/dotnet.yml` runs
`validate.sh ci Debug` / `ci Release`, one per matrix leg, and publishes what it
validated to the run summary — read that with `gh run view <id>` rather than re-running
the suite to find out whether a commit passed.

New machine: `mise install && ./scripts/doctor.sh` first (see Environment).

**The other two scripts generate rather than check.** `./scripts/status.sh` writes
[`docs/status.md`](docs/status.md) — test, content and line counts, measured not typed;
never hand-edit that file. `./scripts/agent-tokens.sh` reports what agent sessions on
this project actually cost in context, from Claude Code's own transcripts; it is the
instrument for the question "did that change make agents cheaper", the way
`tools/PacingMeasure` is the instrument for balance.

## Standing conventions

- **`git add` specific paths, never `-A` or `.`**
- **Agent worktrees go outside the repository**, in the session scratchpad. Eight of them
  under `.claude/worktrees/` reached 3.7 GB untracked and unignored, and the cost was
  agent tokens rather than disk: a repo-wide search returned nine copies of the tree, so
  every search paid ~9x and could read a stale checkout as if it were `src/`. They were
  removed on 2026-08-29 and the directory is gitignored (searches honour it — measured 9
  hits → 1), but the ignore is a backstop, not the convention: a worktree created outside
  the repo never lands in the working tree at all.
- **One narrowly-scoped branch per concern; branch → push → PR → wait for CI → stop.**
  Never push to `main`. **Merging is the agent's**, once CI is green — see "What stays
  human" in [The team](#the-team), which records Brandon's 2026-08-24 correction. This
  bullet claimed the opposite until 2026-08-27; the two passages contradicted each other
  for three days and agents got whichever rule they read first.
- **Confirm a merge really happened** before branching from `main`
  (`gh pr view <n> --json state,mergedAt` — the 504s lie), or a stale base silently
  drops the previous slice.
- **File found-but-deferred work as a GitHub issue**, not in this file and not in
  chat.
- **Gate before merge**: focused tests, then `./scripts/validate.sh full` — full suite,
  Debug **and** Release at 0 warnings, `git diff --check`, in one command.
- **Pacing is measured at checkpoints, not per PR** (2026-08-28, Brandon's direction).
  An ordinary gameplay-affecting PR does **not** run the canonical seed ranges and does
  **not** quote PacingMeasure in its body — re-tuning after every adjustment cost more
  than it bought. Comprehensive pacing and balance evaluation happens at an explicit
  **re-baselining checkpoint**: the phase exits that already name one (F3's curve, F4's
  variety re-run) and #542, which holds the deferred verdict on the current baseline
  and is where the checkpoint's scope is tracked. What a PR still owes is direct
  evidence the change itself is correct — the gate above, plus deterministic pins
  wherever behaviour is seeded. **If a change carries an obvious severe risk — a stall,
  an unwinnable encounter, broken progression — name that specific risk and show it
  does not occur**, aimed at the risk rather than swept for. The former spot-check
  waiver (`--seeds 1-20` for structurally CR-pool-inert changes, precedent #356–#358)
  is retired along with the universal gate it was an exception to.
- **There is no versioned DTO mirror and no generated schema, deliberately.** The
  guards are `UnmappedMemberHandling.Disallow`, required-member metadata on load-path
  records, and the serializer shape tests. Adding
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
