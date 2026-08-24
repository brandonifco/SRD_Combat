# Design — span-coverage accounting for the stat-block lane

**Date:** 2026-08-24. **Issue:** #382. **Mandate:** `docs/2026-08-24-span-accounting-brief.md`
(read it first; it is the argument, this is the plan). **Safety net:** #189's initial
scope, merged as PR #384 — `tests/SrdExtract.Tests`, 1,367 characterization tests.
**Author:** architect. **Executor:** engineer.

This document decides the shape of the refactor concretely enough to execute without
reopening judgement. Where it narrows the brief it says so and says why; where the code
contradicted the brief's assumptions it says that too, in [§13](#13-where-the-brief-met-the-code).

Nothing here relitigates the settled decisions: parsing stays regex, spells stay out of
the lane, `MatchesStructuredForm`/`IsAccountedFor` and the three lift-outs are deleted
rather than fixed, there is one regeneration and one re-baseline, and doubt always lands
in residue.

---

## 1. The problem in one paragraph of code

`LeftoverMechanicalSentences` splits an entry into sentences and drops every sentence
`IsAccountedFor` approves. `IsAccountedFor` approves a sentence when
`MatchesStructuredForm` recognises a *label* in it — `Contains("Attack Roll:")`,
`StartsWith("Failure")` — vetoed only where a condition rider is unimposable. So the unit
of accounting is the sentence and the test for crediting it is a substring. Any payload
sharing a sentence with a credited label is credited with it. That is the recurrence
mechanism, and the fix is to make the unit of accounting the **character** and the test
for crediting it **that some extraction actually consumed it**.

---

## 2. The span contract

### 2.1 Representation

Two types, both internal to `SrdExtract.Parsing`, both plain:

```csharp
/// A half-open character range [Start, End) into one entry's original text.
internal readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

/// The claims made against one entry's text, and the residue left over.
internal sealed class EntryCoverage
{
    public EntryCoverage(string text);

    public string Text { get; }

    /// Records a claim. `note` names the matcher, for the census — never serialized.
    public void Claim(TextSpan span, string note);

    /// A match's span minus the spans of the named groups the parse did not read.
    public void Claim(Match match, string note, params string[] unreadGroups);

    /// The whole entry, by a curated human decision. See §2.6.
    public void ClaimWholeEntry(string note);

    /// The text with every claimed range replaced by spaces of the same length.
    /// Offsets are preserved exactly. See §5.
    public string Masked { get; }

    /// Uncovered runs, glue-absorbed and chunked. See §6.
    public IReadOnlyList<string> Residue();

    /// Uncovered runs with their spans and neighbouring claim notes, for the census tool.
    public IReadOnlyList<(TextSpan Span, string Text, string? Before, string? After)> Uncovered();
}
```

Offsets are always into the entry's **original** `text` — the string that becomes
`MonsterEntry.Text`. There is exactly one coordinate space and nothing ever rewrites it
(§5). `EntryCoverage` is constructed once at the top of `Classify`/`ClassifyTrait` and
threaded to every matcher that participates.

### 2.2 What a claim asserts — the load-bearing reading

> **A claim says: the model expresses these characters.** It does not say a regex
> matched them, and it does not say a string was copied into a field.

Three consequences, each of which decides a real case in the corpus:

- **Matched is not read.** `AttackHeaderPattern` contains `[^.]*?` between the attack
  bonus and the reach. In the corpus that filler swallows nine printed clauses —
  *"(with Advantage if the target is Grappled by the bugbear)"*, *"(with Advantage if the
  target doesn't have all its Hit Points)"* and four more — none of which the engine
  applies. Crediting the whole match span would rebuild the goblin bug inside the very
  first pattern of the refactor. See §2.3.
- **Stored is not modelled.** `ReactionEffect` holds the Trigger and Response strings
  verbatim, and `Encounter` has no reaction resolver at all. Copying prose into a record
  field is storage, not expression, so a reaction's trigger and response text is
  **residue** (§7.5). `SaveEffect` is the contrast: `Encounter.ResolveSaveEffect` really
  runs it, so a save's structured parts really are claimed.
- **Grade is not coverage.** `EntryMechanics` says which *shape* an entry has;
  coverage says how much of its text the shape captures. A Trait-section entry graded
  `SavingThrow` that the engine never fires (#373's second half) is a **dispatch** gap
  and stays out of this refactor: the extraction did express the save, and `UseEntry`
  not reaching Trait-section entries is a different bug in a different file.

### 2.3 How a regex matcher composes its claim

Two rules, and both are mechanically checkable:

1. **Literal pattern text is claimed; wildcard text is not.** A literal the pattern
   required (`Attack Roll:`, `reach`, `ft.`, `Saving Throw: DC`) was read and verified by
   the matcher, so it is claimed. A permissive subexpression (`[^.]*?`, `.+?`, `.*`)
   matched characters nobody inspected, so it is not.
2. **Every permissive subexpression in a claiming pattern is a named group**, and the
   claim subtracts it unless the parse consumed its content into structure. So
   `AttackHeaderPattern` grows `(?<unread>[^.]*?)`, and `ParseAttack` claims
   `Claim(header, "attack.header", unreadGroups: ["unread"])`. The nine Advantage
   parentheticals then fall out as residue with no per-shape patch — which is the
   refactor working.

   A test enforces the convention: scan the `GeneratedRegex` pattern strings of every
   claiming matcher and fail on an *anonymous* `.*`, `.+`, `[^...]*` or `[^...]+`. This is
   the "write the validator that asserts the shape" rule pointed at the mechanism itself,
   and it is the tripwire that stops a future pattern from quietly widening a claim.

**A corollary the inventory in §7 turns on:** a claim may cover a printed clause's subject
and verb only when the pattern *anchored on them*. `NamedMultiattackPattern` matches
`two Rotting Fist attacks` and says nothing about `The mummy makes`, so under a naive
port every Multiattack entry in the bestiary would emit `The mummy makes` as residue.
The answer is not a glue entry for subjects (that is the keyword-filter bug, §4.4); it is
to **anchor the pattern over the clause it claims**. Patterns that participate in coverage
are anchored to printed clause boundaries; patterns that float on a field of interest
either get anchored or produce residue. Both directions are safe; only inventing glue is
not.

### 2.4 Overlapping claims: union, and that is deliberate

Claims are a **set of characters**, normalised by union. Two matchers claiming the same
characters is not an error and is not reported.

The reason is that coverage is monotone in the safe direction only for *under*-claiming:
an overlap cannot hide text, because a character is covered or it is not, and a second
claim over covered text changes nothing. Forbidding overlap would force artificial
bookkeeping between passes that legitimately read the same characters — the embedded
save's rider is read by `ParseEmbeddedSave` and its condition name is matched again by
`ConditionPattern` — and would buy nothing. **Over-claiming is the danger, and §2.3 is
what governs it; overlap is not.**

### 2.5 Which entries let a rider claim its clause

Only entries the engine actually resolves *with* riders: `EntryMechanics.Attack` and
`EntryMechanics.SavingThrow`. This is not new policy — it is the surviving half of
`IsAccountedFor`'s last line, promoted from an accounting quirk to a stated rule:

> An `AppliedCondition` claims its clause only when the entry's mechanics is one the
> engine imposes riders from. `Encounter.UseEntry` refuses `Multiattack` and `Unmodelled`
> entries outright, so a rider parsed on one is never imposed, and claiming its text
> would be a false claim.

An unimposable condition (`ConditionRules.CanBeImposed` false, or carrying an
`UnmodelledRequirement`) claims nothing at all — which is the other surviving half of
`IsAccountedFor`, and the reason its veto disappears rather than moving.

### 2.6 Coverage by fiat — `Passive` and `Narrative`

A curated human decision is a reading of the whole entry, so it covers the whole entry:

- `MonsterTraitRegistry.Implements(name)` → `ClaimWholeEntry("trait.registry")`.
- `KnownInertEntries.Contains(name)` → `ClaimWholeEntry("inert.curated")`.

Represented as a single claim of `[0, text.Length)` with its note, so the census can tell
a fiat cover from an earned one and the residue arithmetic needs no special case. The
honesty of these grades rests where it always did: on the curated lists, whose doc
comments already record where a registry reading is narrower than the printed sentence.
Nothing in this refactor audits them, and nothing in this refactor should.

### 2.7 An `Unmodelled` entry claims nothing

When classification falls through to `EntryMechanics.Unmodelled`, residue is the **whole
text**, chunked into sentences exactly as `MechanicalSentences` does today — not computed
by subtraction.

This is correct rather than merely convenient. `Encounter.UseEntry` refuses an
`Unmodelled` entry, so any conditions `ParseAppliedConditions` found on the way there are
never imposed by anything, and subtracting their clauses would credit the model with
mechanics it does not run. It also has a large free consequence: it is what keeps the
spell lane byte-identical (§8).

---

## 3. Residue is computed by subtraction

```
residue = chunk(absorbGlue(text \ claims))
```

`UnmodelledClauses` is no longer a filtered list of sentences. It is the uncovered
remainder. Every rule in §4 and §6 is a rule about that one expression.

---

## 4. The glue rule

This is the centrepiece and the one rot risk. It is written here in the
`AreaTargeting` style — the reading stated, the failure direction named — and the same
words go on the doc comment of the method that implements it.

### 4.1 The closed set

A **glue token** is one of, and nothing else:

| Kind | Members |
| --- | --- |
| Whitespace | any run of `\s` |
| Punctuation | `.` `,` `;` `:` |
| Connective words | `and` `or` `plus` — whole words, case-insensitive |

That is the entire set. Six characters and three words.

**Labels are deliberately not on it**, and this narrows the brief on purpose. The brief
allowed "labels the structure already implies"; the code says something stronger is
available. A label is *read* by the matcher that keys on it — `ParseAttack` finds
`Hit:` and parses damage from it, `ParseSave` finds `Failure:` and parses damage from it,
`ParseReaction` requires `Trigger:` and `Response:` — so the matcher claims its own label
under §2.3 and no glue entry is needed. The gain is not tidiness: `Failure or Success:`
is claimed by nobody, so #370's side clauses become residue for free, whereas a glue
entry for "labels" would have swallowed the exact text #370 is about. **When a label
could be either glue or a claim, it must be a claim.**

Parentheses are also not glue. In practice they sit *inside* claims already —
`the Grappled condition (escape DC 13)` and `5 (2d4) Piercing damage` are single matches
— so an unclaimed parenthesis means an unclaimed parenthetical, which is exactly the
Advantage clauses of §2.3, and they should be residue.

### 4.2 The boundedness rule, precisely

Take the uncovered runs — maximal runs of characters no claim covers. A run is
**absorbed** (treated as covered, contributing no residue) if and only if:

1. **Every token in the run is a glue token.** Tokenised on whitespace after separating
   the four punctuation characters; a run containing any other word, digit or symbol is
   not glue, whole, however much of it looks like glue.
2. **A run containing a connective word is bounded on both sides by claimed spans.**
   Not by the text's edges, not by another glue run (there are none — runs are maximal),
   not by a sentence boundary. Claim on the left, claim on the right, or it is residue.
3. **A run containing only whitespace and punctuation may be bounded by the text's edge
   on one side.** A trailing full stop after the last claim, or leading whitespace, is
   absorbed. A leading or trailing `and` is not — a connective with nothing on one side
   of it is a sentence fragment somebody lost, and the model should say so.

Worked, from the corpus:

| Uncovered run | Left | Right | Verdict |
| --- | --- | --- | --- |
| `, and ` between a claimed Hit clause and a claimed imposable rider | claim | claim | absorbed |
| ` plus ` between two claimed damage components | claim | claim | absorbed |
| `, or ` before `2 (1d4) Piercing damage if the swarm is Bloodied` | claim | **residue** | not absorbed → residue, and the whole alternative reads as one clause (#371) |
| `. ` between two claimed sentences | claim | claim | absorbed |
| `.` at the end of the entry | claim | edge | absorbed |
| ` and uses Dreadful Glare` | claim | edge | not glue (`uses`, `Dreadful`, `Glare`) → residue (#341's case, with no hand-back) |

### 4.3 The failure direction

**Anything not provably glue is residue.** Residue is cheap: a counted clause in a census
somebody reads once. A wrong absorption is expensive: it is a rule that vanished, which is
the single failure this whole model exists to prevent. When in doubt the run is residue,
and the way to fix a false residue is to make the *matcher* claim more — never to make
glue absorb more.

### 4.4 Why the set cannot be grown casually

A glue whitelist that grows to make the census shorter is CLAUDE.md's bug 2 rebuilt
inside the mechanism meant to prevent it. The keyword filter that let Flyby, Nimble Escape
and Shape-Shift through as inert was also, at the time, a small set of obviously-harmless
words. Two guards, both mechanical:

1. **The glue census is pinned.** A test in `SrdExtract.Tests` enumerates every distinct
   absorbed run across all 1,318 entries — normalised (whitespace collapsed) and sorted —
   and asserts it against a checked-in golden list. The corpus is closed, so this
   vocabulary is finite and small (expect on the order of ten distinct runs). Any change
   to the glue set, any widening of a claim that changes what glue has to bridge, shows
   up as a reviewable diff in that file. This is the same instrument as
   `SpellValidator.ExpectedSpellCount`: a count fixed by the source gets an exact
   assertion, not a floor.
2. **Three strikes applies to the glue set explicitly.** Per the 2026-08-24 protocol rule,
   the **third** proposal to add a token to the glue set auto-files a mechanism issue —
   "is the closed-set answer still right, or is this a claim that should be earned by a
   matcher?" The patch may still ship; the question may not be deferred silently. Record
   each addition with its date and reason in the doc comment, so the count is visible
   rather than reconstructed from git.

---

## 5. Rewrite tracking: mask, never mutate — and annex, never join

Three places mutate text before parsing. The decision is **no offset-mapping layer and no
rewrites**, by two different mechanisms.

An offset-mapping layer was considered and rejected. It is permanent machinery whose only
job is to undo a mutation that need not happen, and it leaves every future matcher having
to know which coordinate space it is in — a bug class worse than the one being fixed, and
one that would be silent in exactly this project's documented way.

### 5.1 The two deletions become same-length masks

`Classify`'s embedded-save lift (`text.Replace(embedded.MatchedSpan, string.Empty)`) and
`ParseAppliedConditions`' petrifying-tier lift (`text.Replace(PetrifyingTierSentences,
string.Empty)`) both exist to stop a later pass re-reading text an earlier pass consumed.

Both become: **claim the span, then read `coverage.Masked`** — the original string with
every claimed range replaced by spaces of the same length. Offsets are preserved exactly,
the later pass cannot match across a masked region (which is the whole point), and
sentence splitting over masked text yields the same sentences deletion yielded, because a
masked sentence collapses to whitespace and `SplitSentences` already drops empties.

Two incidental gains worth naming: `Replace` replaced *every* occurrence and searched from
the string's start, so a span printed twice would have deleted both — masking is
positional and cannot; and `Classify`'s `Build(..., riderText, ...) with { Text = text }`
dance disappears, because there is only ever one text.

### 5.2 The repeat-save join becomes a span-aware annex

`RepeatSaveJoinPattern().Replace(...)` is a genuine rewrite: it replaces a sentence
boundary and a following sentence with different words, so no mask can express it. It is
**restructured out** instead.

Its purpose is to let one template serve both printings of the same rule — the
Doppelganger's single sentence and the Quasit's two. Under the span contract the two
printings are two spans rather than one rewritten string:

> **The annex rule.** When a rider's own trailing text carries no duration, and the
> **next** sentence is exactly the printed repeat-save sentence ("At the end of each of
> its turns, the target repeats the save, ending the effect on itself on a success."),
> and the entry contains `AutomaticSuccessSentence`, the rider takes
> `ConditionDuration.RepeatSaveUpToOneMinute` and its claim **annexes that next
> sentence's span**.

Two supporting decisions:

- `SplitSentences` grows a span-aware sibling returning `(string Text, TextSpan Span)`,
  which the rider loop iterates. This is needed anyway — every rider claim is expressed in
  offsets into the whole entry.
- **The rider also claims the `AutomaticSuccessSentence` occurrence**, wherever it sits in
  the entry. Stated reading: `RepeatSaveUpToOneMinute`'s engine meaning *is* the ten-turn
  cap that sentence prints, so the duration expresses it, and it is not a second rule left
  over. This preserves what `IsAccountedFor`'s repeat-save special case does today, which
  is why that special case can be deleted with the rest of the method rather than
  migrated.

Behaviour must not move: the join added no `Failure:` and no failure tier, so the
`sentence`-scoped checks in `ReadRider` see the same thing either way. The three tests in
the characterization suite's `Repeat saves` region are the proof and must stay green
unchanged through this stage.

---

## 6. Residue granularity and the serialized shape

### 6.1 Chunking

1. Compute uncovered runs over the original text.
2. Absorb glue runs (§4).
3. **Split every surviving run at sentence boundaries** — the existing
   `SentenceBoundary` regex, applied to the run — so a run spanning two sentences yields
   two clauses. Sentence-level chunking is what keeps the census readable, and it is the
   granularity `MechanicalSentences` already produces for a wholly-unclaimed entry, so an
   `Unmodelled` entry's residue is unchanged (§2.7).
4. Trim each chunk of leading and trailing whitespace and glue punctuation.
5. Drop a chunk that is empty after trimming. **No length threshold** — a minimum length
   is a keyword filter measured in characters, and it would be the same mistake in a new
   unit.

Sub-sentence residue therefore renders as the clause itself: the Swarm of Rats' Bites
yields `or 2 (1d4) Piercing damage if the swarm is Bloodied` sitting beside a fully
claimed `Hit:` clause. That is the brief's whole point made visible — payload inside a
credited sentence, printed as its own line in the diff.

### 6.2 Verbatim, and testably so

> **A residue string is a verbatim substring of the entry's own text.**

No capitalisation, no synthesised trailing period, no reassembly. `CapitalizeFirst` and
the `"And {clause}."` formatting in `BundledMultiattackUseClauses` both die with the
methods that own them. The invariant is asserted directly — for every entry in the corpus,
every residue string is `Text.Contains(clause)` — which makes any residue line greppable
back to the page it came from, and makes a synthesised clause impossible to reintroduce.

Expect small cosmetic churn in the regeneration diff from this alone: `And uses Dreadful
Glare.` becomes `and uses Dreadful Glare`.

### 6.3 The serialized shape does not change

`MonsterEntry.UnmodelledClauses` and `TraitEntry.UnmodelledClauses` stay
`IReadOnlyList<string>`, serialized as an array of strings under `unmodelledClauses`.

Reasons, in order of weight: `ContentSerializer` runs `UnmappedMemberHandling.Disallow`,
so any schema change ripples immediately into `ContentLoader` and every committed JSON
file, turning a parser refactor into a content-format migration; a plain string list is
the reviewable-in-a-diff form the census depends on; and spans in the data would churn the
diff on every unrelated text touch while adding nothing a reader wants. **Offsets belong
in the census tool's output, not in `data/srd`.**

---

## 7. The matcher inventory

What each participating matcher claims. This is the work list for stage 3; the exact
regex text is the engineer's, the claim's extent is not.

### 7.1 `StatBlockLineGrammar.ParseAttack`

- `AttackHeaderPattern` match, **minus a new named `unread` group** wrapping the `[^.]*?`
  filler. Nine entries lose their Advantage parentheticals to residue — correctly.
- The literal `Hit:` (the parse keys damage on it).
- Each `DamagePattern` match `ParseDamage` accepted into the returned list. A component
  the loop **broke on** is not claimed — that break is exactly the `or`-alternative of
  #371.
- The qualifier span when `ReadCondition` returned an `AttackDamageCondition`
  (`if the attack roll had Advantage`, structured). When it returned null, the text it
  scanned is not claimed.

### 7.2 `EntryMechanicsParser.ParseSave`

- `SaveHeaderPattern` match.
- **A new anchored target-clause matcher** — see §7.6, the largest single piece of new
  work in this refactor.
- The literal `Failure:` that `failureIndex` keys on, and each `SaveDamagePattern` match
  `ParseDamageList` returned.
- `AreaPattern`'s match, when an `EffectArea` was produced.
- `Success: Half damage` when it decided `SaveSuccessOutcome.HalfDamage`; the whole
  outcome sentence otherwise unclaimed. **`Failure or Success:` is claimed by nobody**
  (§4.1).

### 7.3 `ParseAppliedConditions` / `ReadRider`

For a rider that is imposed (§2.5), the claim runs from the start of the
`RiderLeadInPattern` gate match through the end of the duration text — covering
`If the target is a Large or smaller creature, it has the Grappled condition (escape DC
13)` whole, because every one of those words was read and required. Plus the annexed
spans of §5.2 where a repeat-save duration was taken.

A rider that comes back with an `UnmodelledRequirement`, or whose condition
`ConditionRules` cannot impose, claims nothing.

The petrifying-tier template claims its two sentences (it is an exact-constant match that
the parse fully expresses as one escalating rider).

**A duplicate is not a claim.** `ParseAppliedConditions` skips a condition already
recorded; the second printing's text is then unclaimed and becomes residue. That is
honest — the engine imposes it once — and it is a shape to expect in the census.

### 7.4 `ParseMultiattack`

- The composition clause **including its subject and verb**: an anchored pattern over
  `The <creature> makes` at the sentence start, plus each `NamedMultiattackPattern` match
  that contributed to `AttackCount`, plus an adjacent `makes` on a later clause (the
  Roper's `, and makes two Bite attacks`). Adjacency is judged modulo glue.
- `CombinationMultiattackPattern`'s match whole, when that branch fired.
- **`AlternativeCompositionPattern`'s match is deliberately not claimed.** The model does
  not express the second branch — that is the standing designer reading recorded on
  `ParseMultiattack` — so it falls out as residue, and the `alternativeClause` out
  parameter is deleted.
- Nothing claims `uses`/`can use` clauses, so `BundledMultiattackUseClauses` is deleted
  and #341's fifteen bundled uses land in residue by subtraction.

Safe direction if the subject anchor fails to match a printing: the subject becomes
residue and somebody reads about it in the census. That is a false positive costing a
line, not a lost rule.

### 7.5 `ParseReaction`

Claims the literals `Trigger:` and `Response:` and nothing else. The captured trigger and
response prose is **residue**, because `ReactionEffect` stores strings and no resolver
exists (§2.2).

Twenty entries are affected. Six monsters currently graded `Complete` carry a Reaction
entry and demote to `Playable`; **pool admission is unaffected**, because `Playable`
reads only Action-section entries and reactions are not one. The Bandit Captain's Parry
reads as fully modelled today while the engine has no reactions at all — this is the
refactor telling the truth cheaply, and it should ship rather than be carved out.

### 7.6 New: the save entry's target clause

Between `Saving Throw: DC 10` and `Failure:` the SRD prints who rolls:
`, each creature in a 15-foot Cone.` Nothing reads it today — `ParseArea` finds the area
anywhere in the entry, and the selector words are never inspected. Under coverage, with no
matcher, **every one of the 183 saving-throw entries emits its target clause as residue**,
which would demote most of them and gut the pool for a reason that is not a real gap.

So: an **anchored target-clause matcher**, claim-only (it feeds no new structure), over
the shapes the engine's `ResolveSaveEffect` really honours. Measured over the committed
corpus, 88 distinct clauses reduce to a small number of shapes, of which the top five
cover roughly two-thirds:

| Printed shape | Count | Claim? |
| --- | --- | --- |
| `each creature in a <N>-foot Cone` | 47 | yes — the area targeting the engine runs |
| `each creature in a <N>-foot-long, <N>-foot-wide Line` | 24 | yes |
| `one creature the <creature> can see within <N> feet` | ~22 | yes — a single target within range |
| `each creature in a <N>-foot-radius Sphere centered on a point …` | 6 | yes |
| `each creature in a <N>-foot Emanation originating from the <creature>` | ~7 | yes |
| `each creature that isn't currently affected by this breath in a <N>-foot Cone` | 4 | **no** — a gate the model does not express |
| `one creature within <N> feet that has the Prone condition` | 3 | **no** |
| `one Large or smaller creature Grappled by the behir (…)` | 1 | **no** |

The rule, stated: **the target clause is claimed only when the selector is the whole of
what the engine targets** — an area the engine builds, or one creature within a distance.
Every added gate, exclusion or state requirement is a rule of its own and stays residue.
Will-o'-Wisp's `one living creature the wisp can see within 5 feet that has 0 Hit Points`
must therefore be residue: that gate is half of why #373 filed it.

Anchor the pattern at both ends — from the comma after the DC to the sentence's end — so
a selector carrying anything more fails to match rather than matching the part that looks
familiar. That is `RiderLeadInPattern`'s discipline applied to a new clause, and it is the
project's standing answer to this exact temptation.

### 7.7 `ParseEmbeddedSave`

Claims its `MatchedSpan` (unchanged in extent, now recorded rather than deleted). The
`(EmbeddedAttackSave, string)` tuple return collapses to `(EmbeddedAttackSave, TextSpan)`.

---

## 8. The `ClassifyTrait` lane joins now

Confirmed, per the brief's default, and with a stronger argument than "it shares the
credit flaw": the join is provably free on the spell side.

`ClassifyTrait` has three callers — `OriginParser` (species traits), `ClassParser` (class
features), and `SpellParser` (with `consultInertList: false`). `SpellParser` reads
`classified.UnmodelledClauses` **only** when the spell's final mechanics is `Unmodelled`,
which requires `classified.Mechanics` to be `Unmodelled` too. By §2.7 an `Unmodelled`
entry claims nothing and its residue is every sentence — exactly today's output.

Therefore:

> **`data/srd/spells.json` must be byte-identical after the regeneration.**

That is the acceptance criterion for the scope boundary, and it is worth more than a
promise in a doc: it is checked by `git diff --exit-code data/srd/spells.json`. Twenty-one
spells currently classify `Unmodelled` while carrying a parsed condition — those are the
cases that would move if §2.7 were decided the other way, and they are the reason it is
decided this way.

Species traits and class features do move: the `SavingThrow` branch of `ClassifyTrait`
gets honest residue like any other save entry, so `data/srd/species.json` and
`data/srd/classes.json` will show changes and `OriginContentTests`/`ClassContentTests`
expectations move with them (§9.3).

---

## 9. Test evolution

### 9.1 The characterization suite (`EntryMechanicsCharacterizationTests`, 45 fixtures)

Most fixtures assert structured output — durations, sizes, attack fields, multiattack
compositions — and must stay green **unchanged** through every stage. Any structural
assertion that moves is a regression until proven otherwise; that is what the suite is
for.

The fixtures that assert *residue* are the ones expected to change, and they change in
exactly one stage (stage 4). Anticipated, from reading them:

- `ARiderSentenceWhoseConditionIsImposableIsAccountedFor` — stays `Empty`, and is now the
  positive proof that a rider claim covers its clause end to end rather than that a
  sentence was credited.
- `AnUnmodelledEntryCountsEverySentenceUnfiltered` — stays exactly as-is (§2.7).
- `AmphibiousClassifiesAsNarrativeWithNoClauses`, `PackTacticsInTraitSectionClassifiesAsPassive`
  — stay `Empty` by fiat (§2.6).
- `SentenceSplittingDoesNotBreakOnFtAbbreviation` — stays `Empty`; it is now also a
  coverage test, since the header claim must reach through `reach 5 ft.`.
- The `Multiattack` region's residue expectations change wording where a hand-back was
  synthesised: `And uses Dreadful Glare.` → `and uses Dreadful Glare` (§6.2).

Every changed expectation is reviewed as part of the PR. **A fixture whose expected
residue changes must gain a comment saying which claim's absence produced it** — the
census made visible, per the brief's step 3.

### 9.2 `KnownGapPinsTests` — the four flips

These pin current *buggy* behaviour with issue references, and the file's own doc comment
says the refactor is expected to make them false. Under this design:

| Pin | Under #382 | Then what |
| --- | --- | --- |
| #371 (`or 2 (1d4)…` dropped) | **flips** — `UnmodelledClauses` gains the alternative | rewrite the test to assert the residue; #371's execution half (structuring the tier) stays open |
| #372 (plural conditions vanish) | **flips on the accounting half only** — `ConditionPattern` still matches nothing, so `AppliedConditions` stays empty, but the sentence is now unclaimed and lands in residue | rewrite to assert non-empty residue and *keep* the empty-conditions assertion with #372's reference |
| #373 (death rider behind `Failure:`) | **flips** — the rider text is residue | rewrite to assert the residue |
| #370 (`Failure or Success:` forces `SameAsFailure`) | **does not flip** — the misattribution is in `ParseSave`'s outcome check, untouched here — but the side clause now appears in residue | keep the `SameAsFailure` assertion with its #370 reference, **add** an assertion that the side clause is residue |

The #370 row is the brief's warning made concrete: coverage ends the omission class and
does nothing for misattribution. Do not let the appearance of #370's side clause in
residue read as #370 being fixed.

### 9.3 `CorpusRoundTripTests` — the ordering that matters

It re-parses all 1,318 entries and compares against the committed JSON. It is green today
and it must be green at the end. In between:

- **Stages 0–3 leave it green**, because they change no output. That is the point of
  splitting them out: three quarters of the code lands under a whole-corpus proof that
  nothing moved.
- **Stage 4 breaks it by construction.** The parser changes, `data/srd` does not, and no
  amount of care makes those agree. It **must not be skipped, weakened, or given a
  tolerance**: it is the instrument that makes "change the parser without regenerating"
  impossible, and that guarantee is worth more than a green intermediate commit.
- Therefore **stage 4 and stage 5 are one PR**, as two commits: the code, then the
  regenerated `data/srd` plus updated fixtures. The tree is green at the PR's head and at
  no point in its middle, which is the honest description and should be said in the PR
  body.

Two additions to this file at stage 4:

1. The verbatim invariant of §6.2 — every residue string is a substring of its entry's
   text — asserted across the whole corpus.
2. The glue census golden-file test of §4.4.

### 9.4 Content-side tests that move

Outside `SrdExtract.Tests`, in `SRDCombat.Content.Tests`:

- `EntryMechanicsTests.TierOneCoverageDoesNotRegress` — the `Floor = 320` **will** fall
  and must be lowered with a comment in the style the file already uses. Its own history
  is the precedent and should be cited: the floor went 340 → 330 → 320 twice before, each
  time because correctness went up while the metric went down. This is the third and
  largest such move.
- `EntryMechanicsTests.MultiattackReplaceClauseAccountingIsExact` and
  `ABundledUseInsideTheCompositionSentenceIsCountedNotDropped` — exact counts (170
  multiattacks, 62 with residue, 64 tier-one, 11 with residue) move; re-derive from the
  regenerated corpus and keep them exact.
- `MonsterPoolTests` — the per-CR floors are the one place the engineer **stops and asks**
  (§11.2).
- `MonsterValidator` — checks that a structured entry with no residue is consistent; no
  change expected, but re-read it against the new residue source.
- `OriginContentTests` / `ClassContentTests` — species and class trait residue moves (§8).

---

## 10. Staged execution plan

Each stage is a PR unless stated. "Green" means the full suite plus Debug and Release
builds at 0 warnings, the project's standing gate.

### Stage 0 — the coverage type

`TextSpan`, `EntryCoverage`, glue absorption, chunking, `Masked`. Unit tests over the type
itself (bounded-both-sides absorption, edge runs, overlap union, chunking at sentence
boundaries, trimming). Nothing calls it yet.

*Acceptance:* new tests pass; the rest of the suite is untouched and green; corpus
round-trip green.

### Stage 1 — deletions become masks

`Classify`'s embedded-save lift and `ParseAppliedConditions`' petrifying lift read
`coverage.Masked` instead of a mutated string. `ParseEmbeddedSave` returns a `TextSpan`.
The `with { Text = text }` dance goes.

*Acceptance:* **corpus round-trip green** — 1,318 entries reproduce byte-for-byte. That is
the proof, and it is a strong one. All 1,367 characterization tests unchanged and green.

### Stage 2 — the join becomes an annex

Span-aware `SplitSentences`; the annex rule of §5.2; `RepeatSaveJoinPattern` deleted.

*Acceptance:* corpus round-trip green; the `Repeat saves` fixtures unchanged and green.
No output moves.

### Stage 3 — matchers report claims (plumbing only)

Every matcher in §7 threads `EntryCoverage` and records its claims. The new anchored
patterns land here: the `unread` group on the attack header, the multiattack subject
anchor, the target-clause matcher. Coverage is computed and **not yet used** —
`LeftoverMechanicalSentences` still produces `UnmodelledClauses`.

Also here: `tools/SrdExtract --census <path>`, dumping every uncovered run with monster,
entry, section, span, text and neighbouring claim notes, plus a normalised frequency
table. The census exists before the switch so the switch can be reviewed against it.

*Acceptance:* corpus round-trip green (nothing moved); the anonymous-wildcard test from
§2.3 passes; `--census` produces a file the engineer has read end to end and summarised in
the PR body — **including a count of how many currently-zero-residue entries would gain
residue**, which is the number the review of stage 4 turns on. As a bar to check against:
558 entries currently carry a structured grade and empty residue (342 Attack, 108
Multiattack, 96 SavingThrow, 12 Reaction) — that is the population at risk, and a crude
probe suggests on the order of 90 Attack/SavingThrow entries carry substantial
unclaimed trailing text.

### Stage 4 + 5 — the switch, and the regeneration (one PR, two commits)

**Commit A, the switch.** `UnmodelledClauses` becomes `coverage.Residue()`.
`MatchesStructuredForm`, `IsAccountedFor`, `LeftoverMechanicalSentences`,
`MechanicalSentences`'s filtering role, `CapitalizeFirst`, `BundledMultiattackUseClauses`,
`DescribesTheComposition` and `ParseMultiattack`'s `alternativeClause` out-parameter are
deleted. `ConditionsIn` goes with them if nothing else uses it.

**Commit B, the regeneration.** `dotnet run --project tools/SrdExtract -- --out data/srd`,
the updated `data/srd`, the updated characterization and content-test expectations, the
census, and the doc updates (`CLAUDE.md`'s rule section and the counts it quotes, the
plan's F1 row, the new glue-set doc comment).

*Acceptance:*
- Extractor reports 330 monsters, 339 spells, 258 magic items, 0 errors and the expected
  warning count — the shape of a clean run is unchanged.
- `git diff --exit-code data/srd/spells.json` is clean (§8).
- Corpus round-trip green against the **new** committed JSON.
- The four `KnownGapPinsTests` updated per §9.2, each keeping its issue reference.
- Every residue-expectation change carries a comment naming the absent claim.
- The verbatim invariant and the glue census test are in place and green.
- The census is committed or attached, and the PR body summarises: entries gaining
  residue, grade movements by monster, pool admissions lost, and the three or four largest
  residue shapes with a print check on one example each.

### Stage 6 — the re-baseline

`dotnet run --project tools/PacingMeasure -- --seeds 1-120` and `--seeds 200-320`, against
a same-build baseline taken immediately before the merge of stage 4+5. Both ranges, loot
on, figures written into `CLAUDE.md`'s pacing row with the date and the command.

*Acceptance:* both ranges reported with median, clears, level-4 runs, died-by-fight-4,
and the per-band hit-point line. **The expected direction is easier or unchanged** — a
thinner pool means fewer distinct creatures, and the creatures that leave are the ones
carrying the most unmodelled text, which the engine was not executing anyway. A large
move in either direction is a finding to write down, not to tune against.

### After — the small fixes on top

#371's execution half, #372's plural-condition parsing, #373's grade question, #370's
clause scoping. Each is now an ordinary small fix against a corpus that already counts
what it is missing, and each lands on the fixtures rather than before them.

---

## 11. Expected census movement, and the one place to stop

### 11.1 What the regeneration should show

Measured on the committed corpus at `debb5d7` with throwaway scripts (these are bars to
check the run against, not predictions):

- **1,318 entries**, currently: 423 Attack, 418 Unmodelled, 183 SavingThrow, 170
  Multiattack, 59 Passive, 45 Narrative, 20 Reaction. 656 already carry residue.
- **558 entries carry a structured grade and empty residue** — the population where
  residue can appear.
- **Grades today**: 87 Complete, 42 Playable, 199 Diminished, 2 Unusable — 129 admitted at
  `Playable` or better, before the plausibility and genre cuts take the pool to 81.
- **Known shapes that must appear**: the ten #371 alternatives, the thirteen entries
  printing plural `conditions` (#372), #373's death-and-heal riders and push/Speed
  clauses, #341's fifteen bundled uses, the nine attack-header Advantage parentheticals,
  the twenty reaction bodies, and the un-claimable target selectors of §7.6.

Grade demotions are the mechanism: an Action-section entry gaining residue takes its
monster from `Playable` to `Diminished`, which removes it from the pool. Expect the pool
to thin materially. That is the accepted consequence, recorded by Brandon on 2026-08-24,
and F4's expansion fill-ins are the repair.

### 11.2 The stop-and-ask trigger

`MonsterPoolTests.EveryChallengeRatingInTheBandHasSomethingToDrawFrom` asserts per-CR
floors (3 at CR 0 and CR 4, 4 elsewhere). If the regeneration breaks a floor:

**Do not lower the floor to make the build green.** A broken floor has exactly two causes,
and they need different answers: a **missing claim** (a matcher that should have covered
text and did not — a parser bug, fix it), or a **real loss of playable creatures** (a
design consequence, Brandon's call, and possibly a reason to sequence an F4 fill-in
earlier). The engineer stops at that fork and asks; nobody edits the floor without the
answer.

---

## 12. Risks, non-goals, rot

### 12.1 Non-goals — what this does not fix

- **The misattribution class.** #370's wrong-scope tier and #375's or-as-and both claim
  every span, one wrongly. Coverage is blind to a wrong claim by construction. The
  fixtures, print verification and audits own that class. Say so in the PR body; the one
  way this refactor can be oversold is by pointing at #370's side clause appearing in
  residue and calling #370 closed.
- **Spells.** Untouched, and provably so (§8). `PreparableSpells` remains the authority
  and #292's argument stands.
- **Engine dispatch gaps.** #373's twelve Trait-section `SavingThrow` entries the engine
  never fires are a `UseEntry` question, not an extraction one (§2.2).
- **Grammar/AST parsing.** Declined at adjudication and not revisited here. The one thing
  that would reopen it is span-consuming regexes fighting the structure — the honest place
  to watch for it is §7.4's subject anchoring and §7.6's target clause, and if either
  needs a third or fourth escape hatch, that is the signal to say so rather than to keep
  widening.
- **Pool repair.** Thinning the pool is this refactor's consequence; repopulating it is
  F4's work.

### 12.2 Risks

| Risk | Guard |
| --- | --- |
| Over-claiming through a permissive pattern (the goblin bug rebuilt) | §2.3's named-`unread` convention plus the anonymous-wildcard test |
| Glue set rot | §4.4's pinned glue census plus the three-strikes rule |
| Residue flood demoting the pool below usable | §11.2's stop-and-ask; census reviewed before the switch is merged |
| A stage landing with parser and data disagreeing | The corpus round-trip test, deliberately un-skipped (§9.3) |
| Cosmetic residue churn read as substantive | §6.2's verbatim invariant makes every residue line traceable to the page |
| Behaviour drift hidden by expectation edits | Stages 0–3 move no output at all; only stage 4 may change a fixture, and each change carries a comment naming the claim it came from |

### 12.3 Rot monitoring, stated for the next reader

The glue set is the mechanism's soft spot, so it is the one thing instrumented rather than
trusted. Three signals, in escalating order: a diff in the pinned glue census (every PR);
a proposed addition to the closed set (record date and reason inline); the third such
addition (auto-files a mechanism issue under the 2026-08-24 three-strikes rule). The
corpus is closed, so a glue set that keeps needing to grow is not meeting a new printing —
it is being asked to cover for a matcher that should have claimed.

---

## 13. Where the brief met the code

Four places the code said something the brief did not anticipate. All four are decided
above; they are collected here so the next reader does not have to reconstruct them.

1. **The target clause.** The brief's model is "extractions claim what they consumed."
   Applied literally, 183 saving-throw entries would emit `each creature in a 15-foot
   Cone` as residue, because nothing reads the selector today. This is the largest single
   piece of new work the refactor needs and the brief does not mention it (§7.6). It is
   genuinely new coverage, not bookkeeping — and it has a real dividend, since the gated
   selectors it refuses are honest gaps the model never saw.
2. **Subjects and verbs.** `The mummy makes`, `The roper makes` — printed clause heads
   that no matcher reads. The tempting fix is a glue category for subjects, which is the
   keyword-filter bug in a new costume. The design's answer is to anchor the matcher over
   the clause instead (§2.3, §7.4).
3. **Matched is not read.** `AttackHeaderPattern`'s `[^.]*?` is silently swallowing nine
   printed conditional-Advantage clauses right now. A whole-match claim rule would have
   preserved that bug inside the mechanism built to end it — so the claim rule is
   group-aware and the convention is enforced by a test (§2.3).
4. **The spell boundary is provable, not merely respected.** `SpellParser` calls the
   shared `ClassifyTrait`, so "spells stay out of scope" needed an argument rather than an
   assertion. §2.7's rule — an `Unmodelled` entry claims nothing — makes
   `spells.json` byte-identical by construction, which turns the scope boundary into a
   checkable acceptance criterion.

Two more, smaller: `text.Replace` in the two lift-outs replaced *every* occurrence and
would have deleted a span printed twice (masking cannot); and `Encounter.UseEntry`
refusing `Multiattack` and `Unmodelled` entries is what makes §2.5's "only Attack and
SavingThrow let a rider claim" correct rather than conservative.

---

## 14. Deliberately left to the engineer

- The exact regex text for the three new anchored patterns (§7.4, §7.6) and the
  `unread` group placement in `AttackHeaderPattern`. The claims' *extent* is decided
  above; the pattern is craft, and the corpus is the test.
- Whether `EntryCoverage.Claim`'s `note` survives past the census tool. It costs a string
  per claim and buys triage; keeping it is fine, dropping it after stage 5 is fine, and
  the choice needs no design.
- The initial contents of the glue census golden file — it is produced by the first run,
  read once, and committed.
- The census triage itself: which residue shapes deserve issues, which are permanent
  honest gaps. That is a reading of the book, and it belongs to whoever holds the PDF.
- Whether stage 0 through 2 land as one PR or three. Three is cleaner to review; one is
  defensible because none of them moves output. Not a design question.
