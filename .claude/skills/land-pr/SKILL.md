---
name: land-pr
description: Branch, commit, gate, PR, CI wait and merge on SRD_Combat, each step scripted and confirmed. Use before creating a branch, committing, pushing, opening a PR, waiting for CI, merging, starting on an issue, or when the guard hook refuses a git command.
---

# land-pr

The standing law is one concern, one branch, one PR, with direct evidence that the
change is correct. This skill is the *procedure* behind that law, with a script for
each step that has gone wrong before. The charters say who does the work; this says how
the work reaches `main` without losing anything on the way.

## Why each step is a script

Every step below was once a sentence in CLAUDE.md, and each sentence was disobeyed at
least once, silently:

- **Four sessions collided in the primary checkout on 2026-08-30** (#582): a commit
  landed on another session's branch, a review's `FETCH_HEAD` moved mid-review, a
  worktree was deleted under a running agent, an orchestrator carried someone's
  uncommitted edit onto `main`. So the checkout root is now *enforced* read-only for
  git write operations: `.claude/hooks/guard-primary-checkout.py` refuses `commit`,
  `checkout`, `switch`, `restore`, `merge`, `rebase`, `reset`, `stash`, `pull` and
  friends when the target is the primary checkout, refuses any push naming `main` from
  anywhere, and refuses `git worktree remove --force`. If you see that refusal, you are in
  the wrong directory; do not look for a way around it. **The primary checkout lives on
  `main`, clean, fast-forward only** — it is where every session loads its hooks, skills
  and CLAUDE.md from, so nothing you merge reaches the next session until the primary is
  on it. The hook allows exactly that move (`git checkout main`, `git merge --ff-only
  origin/main`) while the tree is clean, and `merge.sh` performs it.
- **`gh pr merge` has answered 504 on a merge that succeeded**, and a slice then
  branched from a local `main` that did not have the previous slice. `worktree.sh
  --after-pr` and `merge.sh` both confirm against `origin/main`, not against the
  command's exit code.
- **A PR with zero check runs reports `mergeStateStatus: CLEAN`** (#513). `pr-ready.sh`
  requires the two `build-and-test` legs to exist and be `SUCCESS` before it says ready.

## The lifecycle

All scripts live in `.claude/skills/land-pr/scripts/`. Paths below are relative to the
repository root of *whichever checkout you are in*.

### 1. Start in a worktree, outside the repository

```bash
bash .claude/skills/land-pr/scripts/worktree.sh <your-scratchpad-dir> <branch> [--after-pr N]
```

Your scratchpad directory is named in your system prompt; pass it. The script prunes
stale registrations, fetches `origin/main`, refuses a parent inside the repo, refuses to
start on top of a PR that is not really merged, and prints the worktree path last. `cd`
there — and note that this harness resets the shell's directory between Bash calls, so
prefix every git command with `cd <worktree> &&` rather than relying on an earlier `cd`.
A bare `git commit` after the reset runs in the primary and is refused, which is the hook
doing its job. Branch names carry the issue number: `fix/510-quit-confirm`,
`feature/298-damage-numbers`, `refactor/508-playmode-partials`, `docs/…`, `test/…`,
`chore/…`.

A subagent that was handed a worktree path by its orchestrator uses that one and creates
no other.

### 2. Work, and commit by path

- `git add <specific paths>`. Never `-A`, never `.` — the tree carries untracked local
  art under a licence that forbids redistribution, and a stray save file or capture
  directory has ridden along before.
- Docs are part of the diff: a doc-comment, a guide, a CLAUDE.md claim or a plan row that
  your change invalidates is fixed in the same commit. Grep for the symbol you renamed or
  the number you changed.
- The commit title is the PR title: what is now true, not what you did. `Closes #N` in
  the body when the slice closes the issue; `Refs #N` when it does not, and say what
  remains.
- One concern. If you found something else, the `file-issue` skill takes it.

### 3. Gate, in the worktree

Focused tests first — the ones that pin your change, including its refusal paths — then
the whole gate:

```bash
./scripts/validate.sh full
```

That is the SDK pin, restore, Debug and Release builds at 0 warnings, the full suite in
both, and `git diff --check`, in one command. It takes about fifteen minutes.

**Who may background it — read this, it has stranded three PRs in one session (#594).**
Only the **orchestrator** (the main/interactive session) may run this in the background
and keep writing the PR body meanwhile: the harness re-invokes the main session when a
background command finishes. A **subagent is _not_ woken by its own background command** —
if you are a subagent, run the gate in the **foreground** (do not pass a background flag)
and stay in the turn until it returns. A subagent that backgrounds the gate and then ends
its turn *looks done and is not*: the harness marks it "completed", nothing wakes it, and
the code you wrote is silently abandoned before it is ever committed or pushed. **The
tell: if your turn is about to end while the gate is "still running in the background",
you have stalled — wait for it in-turn instead.**

Two things the gate does not do for you:

- **Any new test, guard, refusal, validator or instrument gets knockout-verified**
  (`knockout-verify` skill). A green suite proves nothing about a test that cannot go
  red.
- **If `FrozenTranscriptTests` moved, stop and use the `transcript-churn` skill** before
  regenerating anything. Twice the churn was a shipped bug no unit test saw.

Do not run `tools/PacingMeasure` and do not quote pacing figures. Pacing moves at
re-baselining checkpoints, not per PR (2026-08-28). If your change carries an obvious
severe risk — a stall, an unwinnable encounter, broken progression — name that risk in
the PR and show it does not occur with a deterministic pin aimed at it.

### 4. Push and open the PR

```bash
git push -u origin <branch>
gh pr create --title "<title>" --body-file <body.md>
```

Write the body to a file in your scratchpad first, following
`.github/pull_request_template.md` section for section. The sections that carry weight:

- **Exact behavioral claim** — one or two sentences saying precisely what is now true and
  which failure mode it prevents. A reviewer should be able to falsify it.
- **Tests and evidence** — the commands and their results, the knockout table, the
  transcript account if it churned, before/after captures for anything visual. Numbers
  you quote must be numbers you measured in this run, not copied from an earlier PR:
  PR #564 had to correct two counts that were asserted from one reading and then repeated
  until they looked established.
- **Known limitations** and **divergence** — what this deliberately does not solve, and
  any departure from the printed SRD or accepted design. "None" is an answer; silence is
  not.
- **Model and agent provenance** — which agent did the implementation and which made the
  material judgements. Another Claude session under the same GitHub identity is not
  independent review and must not be described as one.

CI starts when the PR opens (pushes before that get no run, by design — #485). Then hand
the PR number to `qc` for adversarial review. Fix what must be fixed before merge; file
what qc says to file; push the fixes to the same branch.

### 5. Wait for CI, from evidence

```bash
bash .claude/skills/land-pr/scripts/pr-ready.sh <pr> --wait
```

It polls once a minute and returns when nothing is in progress, printing one line per
check and ending with `READY` or `NOT READY`. The same backgrounding rule as the gate
applies (#594): the **orchestrator** runs this in the background and is re-invoked on
completion; a **subagent** runs it in the **foreground** and stays in the turn. Ending
your turn with `--wait` "running in the background" strands the PR — nothing wakes a
subagent to act on the result. On a failure, read the run rather than re-running the
suite locally:

```bash
gh run view <run-id> --log-failed
```

A `BEHIND` warning means `main` moved under you. If what moved touches your slice,
rebase onto `origin/main` and let CI run again; otherwise proceed — but say in the PR
that you saw it.

### 6. Merge, then confirm

```bash
bash .claude/skills/land-pr/scripts/merge.sh <pr>
```

It re-runs the readiness check, merges with a merge commit (the only method enabled on
this repository), polls until GitHub reports `MERGED`, fetches, and asserts the merge
commit is in `origin/main`. Its last line is either `MERGED: … present in origin/main`
or an instruction to stop. The head branch is deleted by the repository setting; your
worktree is yours to remove:

```bash
git worktree remove <path>
```

Only remove worktrees you created, and never with `--force` (the hook refuses it): plain
`git worktree remove` declines a dirty tree on its own, which is the protection. Removing
another session's worktree was collision three of #582. The script's last step
fast-forwards the primary checkout when it is on `main` and clean; if it reports that the
primary is elsewhere, say so in your report rather than moving it yourself.

Merging is the agent's job once CI is green (Brandon corrected this explicitly on
2026-08-24). The one exception is the `engineer` charter, which stops at step 5 and
reports the PR number to whoever spawned it; the orchestrator runs step 6.

## Do not

- Work in `/home/brandon/SRD_Combat` itself. The hook will refuse, and if it somehow does
  not, the collision it prevents is on you.
- Trust `gh pr merge`'s exit code, a green badge, or `mergeStateStatus` on its own.
- Regenerate a fixture to make a red test green. Read the diff first.
- Batch two concerns because they are near each other. Two branches cost less than one
  review that cannot tell which change broke what.
- Report a number you did not measure in this session.
