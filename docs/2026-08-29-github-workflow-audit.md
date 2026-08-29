# GitHub workflow and agent-efficiency audit — 2026-08-29

A follow-on to [#556](https://github.com/brandonifco/SRD_Combat/issues/556) and PR #557,
which landed the contribution/security/intake surface on 2026-08-28. That work is not
re-litigated here. This pass asked a different question: **where do this repository's
GitHub configuration and agent-context architecture make an AI agent spend tokens
rediscovering something a file, script, or CI result could establish once?**

Model provenance, per AGENTS.md's rule: fact base and synthesis by Claude Opus 5;
solo-developer process review by Claude Sonnet 5; mechanical config audit by Codex
gpt-5.6-luna; CI architecture by Codex gpt-5.6-terra; adversarial efficiency critique by
Codex gpt-5.6-sol. **The critique was specified for Gemini 3.7 Flash and was not run on
it**: the Gemini CLI's free tier returned a daily-quota error, and its own error text
metered the request as `gemini-3.5-flash` rather than the requested `gemini-3.7-flash`.
Sol was substituted rather than parking the work. No Gemini judgement is in this record.

## The finding that mattered most

`.claude/worktrees/` held **3.7 GB across eight stale agent worktrees, untracked and not
gitignored**, inside the working tree. (This section first said *nine*; the directory
held eight, corrected on cleanup — see the correction note at the end.) The cost was never disk. Every repo-wide agent
search saw ten copies of the source tree:

| Search | Before | After |
| --- | --- | --- |
| `rg -l EndBrokenGrapples --glob '*.cs'` | 9 files | 1 file |
| `rg --files --glob 'Encounter.cs'` | 10 hits | 1 hit |
| `git status` on a clean checkout | 2 untracked entries | clean |

Every agent search paid roughly ten times its necessary tokens, and — worse than the
cost — could read a stale checkout as though it were `src/`. The search tools honour
`.gitignore`, so ignoring the directory is what actually restores single-copy results.
The eight worktrees were **not deleted in this pass**; deletion was Brandon's call, and
he approved it later the same day — see "Cleanup performed" below.

## Changes made

| Change | Why |
| --- | --- |
| `.gitignore`: `.claude/worktrees/`, `output/`, `tmp/` | The table above |
| `scripts/validate.sh` (new) | The build/test gate existed in three places — CLAUDE.md, CONTRIBUTING.md, `dotnet.yml` — that could drift with nothing failing. Now one script, called by humans, agents and CI |
| `.github/workflows/dotnet.yml` | Calls `validate.sh`; deletes a byte-identical 10-line SDK-pin block duplicated across two jobs; uploads the trx only on failure; publishes a commit-keyed validation summary to `$GITHUB_STEP_SUMMARY`. **Job names are unchanged** (`build-and-test (Debug)`/`(Release)`) because they are the required checks on `main` |
| `.codex/agents/*.toml` | See below |
| `GEMINI.md` (new, 20 lines) | Pointer only, matching AGENTS.md's precedent. No `.gemini/`, no charter mirror |
| `.claude/settings.json` | Permission allowlist for read-only discovery and the routine build/test/gh commands. Approval round-trips are pure waste |
| `CONTRIBUTING.md` | Corrects a false claim (below); adds the canonical gate, branch prefixes, Dependabot triage |
| `CLAUDE.md` | "Build and test" and "Gate before merge" now name `validate.sh`; a standing convention records the worktree rule |

### The Codex charters had drifted, in the exact way AGENTS.md warns about

`.codex/agents/*.toml` mirror the seven `.claude/agents/*.md` charters. They had been
produced by a find-and-replace of `CLAUDE.md` → `AGENTS.md`, so **eleven references
across six of the seven charters pointed Codex at the wrong document** — `engineer.toml`
instructed "Read `AGENTS.md` — at minimum 'The rule this project runs on'", a section
that exists only in CLAUDE.md. AGENTS.md's own "Why a pointer rather than a mirror"
section describes precisely this failure and says it was fixed; it was fixed in
AGENTS.md and reintroduced in the charters.

One drift was behavioural rather than cosmetic: `analyst.toml`'s description still read
"Use before/after any gameplay-affecting change" — the per-PR pacing gate **retired on
2026-08-28**. A Codex-run analyst was being told to do the thing that convention change
abolished. All eleven pointers and the analyst description are corrected.

No CI check enforces the pair sync, deliberately: seven small cross-format files do not
justify a normalization comparator, and a false sense of enforcement is worse than the
accepted duplication AGENTS.md already documents.

### The issue forms are not used, and the docs said otherwise

Of the 111 open issues, **zero** were filed through `bug.yml` or `implementation.yml`.
Forms render only in the web "new issue" picker; `gh issue create --body` and the API
bypass them, and that is how every issue here is actually filed. CONTRIBUTING.md
nonetheless prescribed the forms as the path. The real convention — a blank issue with
prose headers and `#NNN` cross-references that carry the *reasoning* for each link — is
also the thing that makes issues work as resumable state, so the doc now describes it
rather than the fiction.

## Considered and rejected

Recorded so they are not re-proposed without new evidence.

- **Splitting the `[Debug, Release]` test matrix** (test in Debug, build-only in Release).
  Rejected: the legs run in parallel and Debug (~7 min in Game.Tests alone) dominates
  Release (~4m25s), so the change saves **zero wall-clock** and loses all coverage
  against optimized output. A search for `#if DEBUG` / `#if RELEASE` / `Debug.Assert` /
  `Conditional("DEBUG")` across `src/`, `tests/`, `tools/` and `client/` returned nothing,
  so no test is symbol-gated either way.
- **NuGet caching / `packages.lock.json`.** Restore is tens of seconds against a ~19 min
  critical path dominated by tests. Lockfile churn for ~0 wall-clock.
- **Moving `vulnerable-packages` to a weekly schedule.** It runs in parallel on a public
  repo, so it costs no wall-clock and no money; making it weekly would only widen
  detection latency while Dependabot alerts are still disabled.
- **`paths-ignore` for docs-only branches.** The required checks are named
  `build-and-test (Debug)`/`(Release)`; a skipped workflow leaves them pending forever
  and blocks merge. The safe form (same-named no-op jobs) is more machinery than a
  latency-only saving on a free runner deserves.
- **A CI check that the 7 charter pairs stay in sync.** See above.
- **Deleting the five unused default labels** (`good first issue`, `help wanted`,
  `duplicate`, `invalid`, `wontfix`). Labels cost no agent context unless queried and no
  maintenance; deletion saves nothing real.
- **GitHub Projects, Milestones, Wiki, CODEOWNERS, merge queue, required reviews.** One
  developer. The `phase:F1`–`F6` labels already cover all 111 open issues and
  `gh issue list --label phase:F4` is the board. A milestone's date/percent UI does not
  fit dependency-ordered phases. Required review has no second human to require.
- **Native sub-issues / issue dependencies.** ~1,830 informal `#NNN` references already
  carry the reasoning for each link; a dependency graph would show *that* #495 needs #493
  but not *why*, which is the part an agent actually needs.
- **Releases/tags now.** F6's scope, gated behind F1–F5. Nothing is distributed yet.
- **Wholesale extraction of CLAUDE.md.** See below — a targeted split is proposed, not a
  teardown, and it is Brandon's call.

## Requires approval — not done

1. ~~**Delete the `.claude/worktrees/` checkouts** and prune stale registrations.~~
   **Done 2026-08-29** — see "Cleanup performed". The claim that several held unmerged
   branches was wrong; all eight were fully merged.
2. **Enable Dependabot vulnerability alerts and security updates** (both currently off;
   `automated-security-fixes.enabled=false`, `GET vulnerability-alerts` → 404). This is
   the real fix for the dependency-latency gap and is free on a public repo.
   `gh api -X PUT repos/:owner/:repo/vulnerability-alerts` and
   `gh api -X PUT repos/:owner/:repo/automated-security-fixes`.
3. **Disable squash and rebase merging.** All of the last 15+ merges are merge commits
   and CLAUDE.md hardcodes `gh pr merge <n> --merge`; the other two methods are a
   misclick that would collapse the per-round `qc round N` commit trail.
4. **Disable Projects and Wiki** — both enabled, both unused, each a surface a future
   agent could populate outside PR review.
5. **CodeQL default setup** for C#. Free on public repos; a security decision, not an
   efficiency one, so it is listed rather than assumed.

## Cleanup performed — 2026-08-29, and two corrections to this record

Brandon approved the approval list the same day. What was applied:

| Change | Result |
| --- | --- |
| Dependabot vulnerability alerts | Enabled (`204`) |
| Dependabot security updates | Enabled (`204`) |
| Squash and rebase merging | Disabled; merge commits only |
| Projects and Wiki | Disabled |
| CodeQL | **Not enabled** — this record graded it DEFER, and nothing new argues otherwise |
| `.claude/worktrees/` | 8 worktrees removed, **3.7 GB reclaimed** |

Every branch was verified before removal: all eight were **0 commits ahead of `main`**
with no uncommitted files. `git worktree remove` does not delete branches, and the local
branch count was 61 before and after — no work was lost, and none could have been.

**Two claims in this document were wrong, and are corrected rather than quietly fixed:**

1. **"Nine" worktrees. There were eight.** The count was written from a directory listing
   read once and never re-counted. The finding is unaffected — the search pollution was
   measured directly, not inferred from the count — but the number was repeated into two
   commit messages and a pull request body before anyone checked it.
2. **"~13 stale worktree registrations pointing at deleted `/tmp` session directories."**
   None were stale. `git worktree prune` removed **zero** entries, and every one of the 18
   remaining registrations points at a live directory. The `/tmp` scratchpad worktrees
   from earlier sessions still exist and still hold roughly 5 GB — but they are *outside*
   the repository, so they never caused the search pollution this document is about, and
   removing them is disk hygiene rather than agent-token work. They are left alone: they
   belong to other sessions' scratchpads.

Both errors share a shape worth naming, because it is this project's own: **a number
asserted from a single reading, then repeated until it looked established.** Neither was
load-bearing, which is exactly why neither got checked. The audit's measured claim (9
search hits → 1) was reproducible in one command and held up; the counted claims did not.

## Proposed, not implemented: the CLAUDE.md context tax

CLAUDE.md is 667 lines / 48.7 KB — roughly **12k tokens injected into every Claude agent
context**, before the agent has read a line of code. The largest sections are "The
finishing plan" (12.2 KB) and "Current state — read this first" (6.7 KB): roadmap and
volatile measurements, the two kinds of content that go stale fastest and that most
agents do not need for the task in front of them.

The counter-argument is real and was pressed: an agent that cannot find the roadmap
inline may spend more tokens hunting for it than the injection cost, and may act on a
stale assumption instead. The resolution both the critique and this synthesis reached is
a **targeted split with an inline routing index** — move "The finishing plan", "Current
state", "Working on characters and spells", the two extraction sections, and "Running the
game" into named documents, and leave behind 10–15 lines naming the active phase, the
current gate, and the *exact* link, so a specialist pays one explicit read and no agent
ever greps for it. Estimated payload: ~49 KB → ~20–23 KB.

**This is not done here.** CLAUDE.md is the project's governing document and
restructuring it is a judgement call that belongs to Brandon, not to an audit branch.

## Follow-up: the blind spot, instrumented

The section below closed by saying nobody had measured per-task token cost. That gap is
now closed with an instrument rather than an assurance: `scripts/agent-tokens.sh` reads
Claude Code's own session transcripts for this project and reports, per session, the
**context floor** — the `cache_creation_input_tokens` on the first assistant message,
which is the prompt an agent pays before it has read a line of code.

Baseline measured 2026-08-29, immediately before the CLAUDE.md split, over the ten most
recent sessions:

| Session kind | Context floor |
| --- | --- |
| Main interactive sessions | 61k–75k tokens |
| Subagents | 52k–53k tokens |

CLAUDE.md at ~12k tokens was therefore **roughly 16–23% of everything an agent paid
before starting work**. That is the number the split is aimed at, and it is now
measurable rather than argued. Re-run the script after a few post-split sessions to see
whether the floor actually moved; the absolute figure also carries the harness's own
system prompt and whatever MCP servers were connected that day, so read the *movement*,
not the level.

One thing the baseline shows that the audit did not anticipate: cumulative
`cache_read_input_tokens` reached 103–177M in the longest sessions. The floor is not
paid once — it is re-read every turn. A reduction in it compounds across a session's
length rather than being a single saving.

## The honest limit of this audit

Every estimate above rests on byte counts, settings, and measured search results — not
on per-task token telemetry. Nobody measured which CLAUDE.md sections agents actually
reuse, how often a fresh agent pays the full injection, or which commands flood context
worst. The worktree finding is measured (9 hits → 1). The CLAUDE.md split is reasoned.
Those are different grades of evidence and are not presented as the same thing.
