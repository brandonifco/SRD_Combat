# Worked examples

Two issues from the queue that a fresh agent could resume from cold. Read them for the
shape, not to copy the words.

## A correctness issue — #582 (filed 2026-08-31 about the 2026-08-30 collisions, phase:F5-confidence)

Title: `Concurrent sessions share the primary checkout: enforce isolated worktrees so agents stop colliding`

What made it resumable:

- **The hazard** opened with the observed behaviour and the convention it violated,
  quoting the CLAUDE.md sentence so nobody had to search for it.
- **Evidence** was four numbered collisions, each with what happened, how it was
  recovered, and which PR it hit. One of them poisoned a timing measurement — named as
  such, because that is the project's failure mode of record.
- **Why it belongs in F5** was one paragraph: a shared checkout undermines the
  instrument, not just convenience.
- **Direction** was explicitly "for steward/architect to scope" — four candidate
  moves, none decided. The issue did not pick the fix; it routed the judgement.
- **Not a code bug** closed it: filed under the found-but-deferred convention rather
  than acted on inline, with the related PRs named.

## A mechanism issue — #528 (filed 2026-08-27, phase:F5-confidence)

Title: `Three strikes (four, actually): this project keeps building verification instruments nobody verifies`

The three-strikes rule (CLAUDE.md, The team): the third occurrence of the same bug shape
auto-files an issue asking whether the abstraction under it is still right. The steward
must triage it even if the answer is "keep patching". The patch still ships; the
question can no longer be deferred silently.

What the body carried:

1. **The rule it was filed under**, quoted.
2. **A table of the occurrences** — issue, instrument, what it claimed, what was true.
   Five rows, each already fixed or filed. The point of the table is that the reader
   can see the shape without re-deriving it.
3. **The shape**, named in one sentence and tied back to CLAUDE.md's rule 1 ("a
   partly-structured entry is more dangerous than an unstructured one") — applied to
   the things that check content rather than to content.
4. **The question for the steward**, with candidate readings labelled as candidates
   and not recommendations: knockout testing as the discipline; verification code
   being exempt from production discipline; or a sampling artifact.
5. **Not to be resolved by patching** — explicit, because the temptation is to close
   a mechanism issue with a fifth patch.
6. **Doc consequence** — which section of CLAUDE.md would have to change under each
   answer.

That issue produced the `knockout-verify` skill. A mechanism issue that names its doc
consequence is one the steward can actually close.

## The template for a mechanism issue

```markdown
Filed under CLAUDE.md's three-strikes rule. Occurrences are patched; this issue is the
question, not the fix.

## The occurrences
| # | Where | What it claimed | What was true |
| --- | --- | --- | --- |

## The shape
One sentence. Which founding bug or case-law entry it rhymes with.

## The question for the steward
Is the mechanism under this still right? Candidate readings (not a recommendation): …

## Not to be resolved by patching

## Doc consequence
Which doc section changes under each answer.

Refs: the occurrences, their PRs, the rule.
```
