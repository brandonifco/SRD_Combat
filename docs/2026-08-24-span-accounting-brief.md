# Architect brief — span-coverage accounting for the stat-block lane

**Date:** 2026-08-24. **For:** `architect` (design), then `engineer` (execution).
**Issues:** #382 (the refactor), #189 (its safety net, pulled to F1), #371/#372/#373
(whose accounting halves it subsumes). **Origin:** an outside critique of `main` at
`a90f7aa`, adjudicated with Brandon the same day. This brief is the argument; the
architect's design document is the next artifact, and nothing here is code yet.

## The decision, already made

Three layers, settled with Brandon:

1. **Characterization fixtures first** (#189, initial scope): entry text in, structured
   output and residue out, pinning every doc-commented reading in
   `EntryMechanicsParser` *before* anything changes. No-regret under any answer.
2. **Coverage-by-consumption replaces credit-by-label** in the stat-block lane (#382).
   This document's subject.
3. **No grammar/AST parser.** Declined deliberately. The invariant is the valuable
   part; the parsing technology stays regex, which already knows its match positions.
   Revisit only if span-consuming regexes themselves start fighting the structure.

## Diagnosis

The extractor already has a residue system — `MechanicalSentences` is deliberately
unfiltered, `LeftoverMechanicalSentences` computes per-sentence residue — so the
founding rule ("nothing may hold unimplemented rules silently") is enforced at
**sentence granularity with a flawed credit test**. The flaw is one method:
`MatchesStructuredForm` credits a whole sentence on a *label prefix* (contains
"Attack Roll:", starts with "Failure"), and `IsAccountedFor` patches it with a veto
that covers only condition riders. Any other payload behind a credited label vanishes
ungraded: "Failure: The target dies, and the wisp regains 10 (3d6) Hit Points." is
accounted for by its first word.

That credit rule is the recurrence mechanism behind the goblin conditional-damage
shape — fourteen occurrences and counting, each patched at the instance while the
mechanism survived. The five F1-exit audit bugs sort cleanly against it:

- **#371, #372, #373** — payload hiding behind a credited label. Pure omission class.
  Coverage catches all three structurally; the unconsumed spans simply fall out as
  residue.
- **#370** — half omission (dropped side clauses; coverage catches), half
  *misattribution* (a tier attached to the wrong scope; coverage cannot catch — every
  span was claimed, one wrongly).
- **#375** — pure misattribution, in the spell lane besides. Out of scope here.

Coverage ends the omission class and does nothing for the misattribution class. The
fixtures, print verification and audits own that class; do not oversell this refactor
as ending it.

## The code already wants this

Three hand-rolled span-consumption mechanisms exist, each a one-off answer to "I need
to know what text I consumed":

1. `ParseEmbeddedSave` returns a `MatchedSpan` the caller lifts out of the text.
2. The `PetrifyingTierSentences` template is lifted by exact string replacement.
3. `ParseMultiattack` hands back `alternativeClause`, and
   `BundledMultiattackUseClauses` exists at all, because sentence-splitting cannot see
   inside a credited composition sentence (#341's whole story).

Under the new contract all three become ordinary consumption and the special cases
disappear.

## The decisive strategic fact

**The corpus is closed.** SRD 5.2.1 prints 330 monsters and will never print more.
Coverage-by-consumption over a closed corpus yields a finite, exhaustive, one-time
census of every span in the bestiary that nothing claimed. Triage that list once and
the omission class is over *by construction* — there is no unexamined text left for
the fifteenth occurrence to hide in. This is what neither more tripwires nor more
tests can deliver: a test encodes its author's assumptions and catches regressions,
not blind spots; #370–#373 would have passed any suite written before the audit that
found them.

## The contract

- Every structured extraction reports the character spans it consumed. Imposable
  riders consume their clauses. Whole-entry grades made by human decision —
  `Passive` (registry traits), `Narrative` (the curated inert list) — cover their
  entry by fiat, which is fine: a curated decision is a reading of the whole text.
- `UnmodelledClauses` becomes the **uncovered residue, computed by subtraction**,
  chunked at clause/sentence granularity for reporting so the census stays readable.
- `MatchesStructuredForm` and `IsAccountedFor` are **deleted, not fixed**, along with
  the three lift-out mechanisms.

## The glue rule — the design's centerpiece and its one rot risk

Some spans are legitimately consumable by rule rather than by extraction: labels the
structure already implies, punctuation, conjunctions *between* claimed spans. The
discipline, stated in advance and to be written down with `AreaTargeting`-style care:

- Glue is a **tiny closed set** — punctuation and conjunctions **bounded on both
  sides by claimed spans** — and nothing more.
- Any doubt lands in residue. Residue is cheap (a counted clause); a lazy glue match
  is the keyword-filter bug (CLAUDE.md bug 2) rebuilt inside the mechanism meant to
  prevent it, and it is the exact failure shape that would make this whole refactor
  worse than what it replaces.

The design document must enumerate the glue set and the boundedness rule explicitly.

## Known technical snag

The preprocessing rewrites mutate text before parsing —
`RepeatSaveJoinPattern().Replace(...)` and the petrifying lift — so spans in rewritten
text do not map back to the source. Either the joins become span-aware or the rewrites
are tracked through. This is the fiddliest part of the job; budget design attention
here.

## Scope boundaries

- **Stat-block lane only.** Spells stay under the curated `PreparableSpells` answer
  (#292); the project already measured that completeness accounting over spell prose
  is anti-correlated with truth. #375 resolves by designer reading plus a pinning
  test, per its own criteria.
- The `TraitEntry` path (`ClassifyTrait` — species/class traits) shares the parser;
  the design should say whether it joins the span contract now or follows. Default:
  join now, since it shares the same credit flaw through `ParseSave`.

## Consequences, accepted eyes-open (Brandon, 2026-08-24)

Regeneration under honest coverage surfaces every hidden gap at once: unmodelled
counts jump, grades demote (the Lion-shaped cases), the pool thins before F4's
fill-ins repair variety, pacing churns. #370–#373's acceptance criteria already
commit to regeneration + census + both-range PacingMeasure apiece; this consolidates
four installments into **one regeneration, one census, one re-baseline** — and F1's
exit demands a re-baseline anyway. Grade and pool changes are reviewed, never
suppressed.

## Sequencing

1. #189 initial scope: characterization fixtures pinning current readings. Land and
   merge first.
2. Architect design doc for #382: the span contract, the glue set, the
   rewrite-tracking answer, the residue reporting granularity.
3. Engineer executes #382 against the fixtures; the fixtures' *expected residue*
   updates are part of the review (they are the census made visible).
4. The execution halves of #371/#372/#373 and the scoping fix of #370 land on top as
   ordinary small fixes.
5. One regeneration, census update, both-range PacingMeasure, F1 exit re-baseline.
