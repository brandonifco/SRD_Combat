---
name: file-issue
description: File a GitHub issue in this project's house form (hazard, evidence, phase label, acceptance criteria, reasoned refs). Use for anything found but deferred, a 'file as issue' from qc, a design question to route, a stale doc claim, or a three-strikes mechanism issue.
---

# file-issue

`gh issue list` is the work queue. Not chat, not CLAUDE.md, not a TODO in code. So the
moment you find something you will not fix in the current slice, it becomes an issue —
and the issue has to let a *fresh* agent, with none of your context, pick it up and
finish it. That is the whole standard: **could someone resume this from the issue
alone?**

## Before writing

1. **Search first.** `gh issue list --search "<two or three distinctive words>"` and
   `gh issue list --label <phase>`. The queue has over a hundred open items; the thing
   you found may be one of them, or a follow-up to one. If it is, comment there or
   widen it instead of filing a duplicate — and if you widen it, say so in the comment.
2. **Count before you write a number.** A count asserted from one reading and then
   repeated until it looked established is a bug shape this project has named (PR #564:
   "nine worktrees" — there were eight). Run the command, paste the result, cite the
   command.
3. **Decide what kind of issue this is**, because the body differs:
   - a **correctness or silence bug** — something wrong, silent, or misleading;
   - a **scoped implementation slice** — behaviour that should become true;
   - a **design or judgement question** — a reading of print, a run-shape decision, a
     seam; routed to `designer` or `architect`, never decided inside the issue;
   - a **mechanism issue** under the three-strikes rule — the third occurrence of a bug
     shape forces "is the abstraction under this still right?" (see the reference).

## The body

Prose headers, in this order, dropping any that genuinely does not apply. The issue
forms in `.github/ISSUE_TEMPLATE/` render only in the browser; none of the open issues
was filed through one, and `gh issue create` bypasses them. These headers are what the
forms would have asked for, written by hand.

```markdown
## The hazard            (or: ## What is wrong / ## Outcome, for a slice)
One paragraph. What happens, why it matters, and the failure mode it produces — not
the fix. For a slice: what exact behaviour or confidence property should become true.

## Evidence
file:line citations. The command you ran and what it printed. For gameplay: seed and
fight number, or the --spawn roster, or the .scenario.json — a seed is a complete bug
report here. For a parser: the printed source text, page, and the entry's name.

## Why it belongs in <phase>
One or two sentences tying it to the finishing plan (docs/finishing-plan.md).

## Acceptance criteria
- [ ] Observable conditions, each checkable by a test, a command, or a capture.
- [ ] Include the refusal path: what must refuse, with which named code.
- [ ] Say what the frozen transcript is allowed to do (unchanged / churn expected).

## Scope
In: … Out: … (bounded to one branch and one PR; name the follow-ups if you see them)

## Open judgements
Anything that needs designer or architect before an engineer can start. If this
section is non-empty, the issue is routed there, not to engineer.

## Refs
#NNN — why this link exists (the reasoning is the point; a bare number is a puzzle
for the next reader). PR #NNN — the change that surfaced it.
```

Filed-from context matters: if this came out of a PR review or a session, say which,
in one line, so the trail closes in both directions.

## Title

What is wrong or what should be true, specific enough to distinguish it from its
neighbours in a list of a hundred. Look at the queue for the house style:

- `A move does not stop when fog reveals an enemy mid-path: the party member walks on into the ambush`
- `RosterParser hardening: trip-wire the name-grammar invariant; normalise whitespace the same with and without a count`
- `Design: route choice — pick the next rung from two or three revealed options`
- `Three strikes (four, actually): this project keeps building verification instruments nobody verifies`

Prefix design questions with `Design:` and mechanism issues with `Three strikes:` or
`Mechanism question:`. Do not prefix with `[bug]` — that is what labels are for.

## Labels

Every issue carries exactly one phase label; that is how the board is read
(`gh issue list --label phase:F3-run-game`). Pick from `gh label list`:

| Label | For |
| --- | --- |
| `phase:F2-feel` | board feedback, foresight, battlefield generation, art pipeline, audio |
| `phase:F3-run-game` | route, loot, shop, stakes, XP curve, the PlayMode modal work |
| `phase:F4-depth` | pool depth, casters, CR fill-ins, fog slice 2, parser residue, class roster |
| `phase:F5-confidence` | tests, suite speed, seams, verification instruments, process |
| `phase:F6-ship` | attribution, licensing, packaging, release |
| `phase:FI-instrument` | the battle builder, scenario batch, other human instruments |

Add `paused:balance-design` to anything about pacing, balance, pool composition or
tactics — those are paused until the re-baselining checkpoint (#542). Add `bug`,
`enhancement`, `documentation` or `question` as the kind warrants.

## Filing

Write the body to a file in your scratchpad, then:

```bash
gh issue create --title "<title>" --body-file <path> --label "phase:F5-confidence" --label bug
```

`gh` prints the URL. Then close the loop: mention the new number where it came from
(a comment on the parent issue, the PR body, or the commit that deferred it), so the
reference points both ways.

## Do not

- Decide a design question inside the issue. State the question, the readings you can
  see, and route it. Deciding it yourself is how partly-structured bugs ship.
- File pacing numbers as evidence for an ordinary slice; pacing is measured at
  checkpoints, and a PR is not required to carry it.
- Write "should be easy" or estimate effort. Write what must be true when it is done.
- Leave the reason off a cross-reference.

For a worked mechanism issue and a worked bug issue, read `references/examples.md`.
