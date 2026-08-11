# SRD_Combat

A turn-based tactical combat game on **SRD 5.2.1** (2024 rules, CC-BY-4.0). Party of
four, levels 1–5, full tactical grid, persistent gauntlet ladder of escalating fights
with XP, levelling, and loot. Combat only — no exploration, dialogue, or travel.

**Read [`docs/2026-08-11-design-and-development-plan.md`](docs/2026-08-11-design-and-development-plan.md) first.**
It is the governing design document: the kickoff decisions, the architecture and why
it diverges from `5eGoldBox`, the content pipeline, the phase plan, and the open
questions. Everything below is operational detail that doc doesn't carry.

## Current state — read this first

**As of 2026-08-11.** All numbers here are verified, not estimated.

| | |
| --- | --- |
| Branch | `main` at PR #17 (condition gates). Nothing open. |
| Tests | **343 passing**, 1 skipped by design (the transcript fixture writer) |
| Build | Debug and Release, **0 warnings** (`TreatWarningsAsErrors`) |
| Content | 330 monsters · 300 spells · 12 classes · 9 species · 4 backgrounds · 38 weapons · 13 armor |
| Work remaining | **8 open GitHub issues.** Not in this file, not in chat. |

**What works today.** A fight runs end to end, headless. Grid movement, initiative, the
action economy, attacks, damage, death saves and opportunity attacks. Characters resolve
from real content — species, class, background, levels 1–5 — and fight alongside
monsters, with nine implemented class features and working spellcasting (attack spells,
save spells with areas, slots, Concentration). A wolf's bite knocks a Medium creature
Prone and a Huge one not, from the stat block's own words. A frozen transcript pins one
whole eight-round fight byte-for-byte.

**What does not exist yet.** No client of any kind — nothing is playable by a person.
No gauntlet, no XP awards, no levelling in play, no loot, no save files, no pregenerated
characters. Monster tactics are a placeholder (`SimpleTacticsPolicy`) that closes to
melee and swings.

**Picking up cold:** `gh issue list` is the work queue, and the order below is not the
order the issues were filed in. Take the top of it.

### The order to do the open work in

Ordered by what each piece rests on, not by how valuable it looks on its own. The
governing plan doc carries the same list with the reasoning; this is the short form.

1. **#15 condition durations — first, and wider than the issue asks.** Give the condition
   record **both an expiry and the combatant who imposed it** in one pass. Expiry is all
   #15 needs; the source is what #16 needs for "the grapple ends with its grappler", and
   it is the same field on the same type. Split them and `Combatant`, the condition
   collection and every call site get reopened twice.
2. **#16 Grappled and Restrained.** Straight after, while that model is fresh. It is the
   smallest real consumer of it and therefore the best proof it is shaped right:
   Restrained exercises the expiry, the grapple exercises the source, and Escape
   exercises removal by something that is not a timer.
3. **#19 a way for a monster to use a stat-block entry, together with #8 recharge.** The
   prerequisite nothing had filed. `UsageLimit` is never read in `Core`,
   `MonsterEntry.Save` is never read in `Core`, and every `Encounter` action is either
   hardcoded (`Dodge`, `Dash`) or gated on `Stats.Character` (`CastSpell`, `Rage`) — so
   **there is no path for a monster to use an entry at all**, and `SimpleTacticsPolicy`
   has no concept of choosing between attacking and doing something else. Build the
   "can I use this?" and "should I use it now?" branches together or write them twice.
4. **#6 saving-throw effects.** Only now does it land with nothing left to invent: the
   area geometry already existed, durations come from step 1, recharge gates the breath
   weapons, and step 3 is what invokes it. `AreaTargeting` and `Encounter.ResolveSpellSave`
   are the working reference for the geometry and the roll-and-halve loop.
5. **#9 passive monster traits.** Several reference machinery that has to exist first —
   Magic Resistance is Advantage on saves and is worth nothing before #6. Best
   repetition in the queue once unblocked: Pack Tactics ×18, Spider Climb ×10, Magic
   Resistance ×7, Swarm ×7, Flyby ×7 across the tier-1 band.
6. **#10 class features.** Same argument, weaker — Danger Sense wants saves executed,
   Cunning Strike imposes conditions. The least architectural item here and on a
   different subsystem from #9, which makes it the safest work to interleave.
7. **#11 curate the monster pool — last, deliberately.** Weighting the pool by mechanical
   coverage means nothing until coverage stops moving, and every step above moves it.

**Conditions are the most-reopened type in that list** — #6 imposes them on a failed
save, #9 has passives referencing them, #10 has Cunning Strike applying them. That is why
steps 1 and 2 come before anything else, and why they are worth doing as one design.

Steps 1, 3 and 4 all touch the turn loop, so **the frozen transcript may churn**. It uses
hand-authored combatants carrying no riders, so it may well survive — but if it diffs,
read the diff before regenerating.

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
model express it?* — exactly one qualifier is modelled, the size gate, and anything else
printed with the condition (a duration, a charge requirement, a pull, a chained second
condition) goes to `AppliedCondition.UnmodelledRequirement` and makes the rider unusable
rather than approximate. *Does the engine execute it?* — `ConditionRules.Executable` is a
curated allowlist, exactly like `ClassFeatureRegistry`, and holds Prone, Incapacitated
and Unconscious. **Add a condition there only alongside the code that gives it effects.**
Grappled is the instructive absence: its riders are fully modelled and it still must not
be imposed, because a Grappled creature would walk away at full speed while its sheet
said otherwise. Twenty attacks satisfy both checks today and all twenty are Prone.

Finding this cost coverage, and the drop is worth understanding before you read the
number: 342 tier-1 entries down to 322. Thirteen attacks had read as fully modelled
because the whole entry is one sentence containing `Attack Roll:`, so the accounting
matched on that and `and the target has the Poisoned condition until the start of its
next turn` was invisible. That is bug 1's exact shape, third occurrence.

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
  visible.
- **Casting works.** Attack spells roll a spell attack against AC; save spells make
  every creature in the area roll against the caster's DC, halving on a success. Slots
  are spent (cantrips are free), Concentration is tracked and broken by damage, and a
  spell whose effect is not modelled is **refused with a reason** rather than silently
  doing nothing.
- **Area geometry is a stated interpretation, not a derivation.** The SRD describes
  areas for a table with a ruler; `AreaTargeting` documents how each becomes squares.
  Cylinder is not modelled and a spell using one is refused.
- **`SpellcastingRules.AbilityFor` is a curated map, not Primary Ability.** A Paladin's
  primary abilities are Strength *and* Charisma and it casts on Charisma — reading it
  from the Core Traits table would be right for six classes and quietly wrong for two.
- **Spells need their own effect grammar, not the stat block one** — see bug 3 above.
  `SpellEffectParser`, not `EntryMechanicsParser`.
- **Extra Attack and Multiattack are the same rule to the engine**: the Attack action
  buys several attacks rather than several actions. `CombatantStats.AttacksPerAction`
  resolves both. Modelling them as extra actions would also wrongly allow a second Dodge
  or Dash.
- **A Multiattack constrains which attacks it is made of.** `AllowsInMultiattack` refuses
  a swing the stat block does not license, and a Multiattack naming an attack the
  creature does not have is **dropped entirely** rather than granting phantom swings.

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

Decided at kickoff and no longer open: **six launch classes** (Fighter, Rogue, Cleric,
Wizard, Barbarian, Ranger — they cover every mechanical shape the engine must handle)
and **no code licence for now** (public repo, no `LICENSE`, all rights reserved by
default — deliberate).

## Working on the combat engine

- **The frozen transcript is the most valuable test here.** It pins the exact narrated
  sequence of a whole fight, so it catches interaction bugs no unit test reaches. When
  it fails, **read the diff before touching the fixture** — a change to the transcript
  is a change to how the game plays. Regenerate only once the new behaviour is intended:
  un-skip `TranscriptWriter`, run it, re-skip it, review.
- **It uses hand-authored combatants, not SRD monsters, on purpose** — so it fails when
  the *engine* changes, not when content is re-extracted. `RealMonsterCombatTests` in
  `SRDCombat.Content.Tests` covers the other direction, including a smoke test that
  every CR 0–4 monster can take a turn without throwing.
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

Three things a Phase 2 author should know before starting:

- **There is no versioned DTO mirror, deliberately.** Content serializes straight from
  the `Core` definitions. The design doc explains why this diverges from 5eGoldBox, and
  what guards replace the mirror. Don't "restore" it without reading that section.
- **Monster prose is not yet mechanics.** Attacks are structured; saving-throw effects,
  recharge abilities and riders ("the target has the Prone condition") are still text
  on `MonsterEntry.Text`. Turning those into executable effects is an open scoping
  question, flagged in the design doc.
- **`ChallengeRatingRules` already exists** in `Core.Rules` with the full XP and
  proficiency-bonus tables, and the SRD's per-character encounter XP budget is on
  printed page 202 — the encounter builder implements a published table, not a guess.

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

- **.NET is snap-confined.** Use `DOTNET_ROLL_FORWARD=LatestMajor /snap/bin/dotnet`
  rather than bare `dotnet` if the bare command misbehaves. SDKs present: 8.0.129 and
  10.0.110; `global.json` pins 8 with `latestMajor` roll-forward, which in practice
  means **SDK 10 is what actually runs locally** while CI installs 8.0.x. Both build
  the `net8.0` targets fine, but the version gap is real — see the next point.
- **`dotnet new sln` under SDK 10 produces a `.slnx`, which .NET 8 cannot read.** Hit
  during setup: the solution has to be `SRDCombat.sln` in the classic format, or CI
  (pinned to 8.0.x) fails to find a project file at all. `dotnet new sln --format sln`
  forces it. The same version gap means **templates default to `net10.0`** and write
  `TargetFramework`/`Nullable`/`ImplicitUsings` into each new `.csproj`, silently
  overriding `Directory.Build.props` — strip those three lines from any project
  created by a template.
- **Godot 4.7 stable mono** at `~/.local/bin/godot`. Not used until Phase 7.
- **`pdftotext`** (poppler) is installed and is the extraction workhorse.
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
reports 330 monsters, 38 weapons, 13 armor, 0 errors, and **10 warnings, all expected**:
the Archmage's XP, which is a real SRD inconsistency, and nine spells whose component
line is truncated at a column break in the source.

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
  then stop.** The user reviews the diff and merges. Do not merge your own PR, and do
  not push to `main`.
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

## Attribution obligation

SRD 5.2.1 is CC-BY-4.0, so derived content **can** be shipped — but the attribution
in [`NOTICE.md`](NOTICE.md) is required and must stay accurate. Per the SRD's terms,
do not add any other attribution to Wizards of the Coast beyond that statement.
