# SRD_Combat — for Codex

**[`CLAUDE.md`](CLAUDE.md) is the governing document for this project. Read it first, in
full.** It carries what is true of every task — the rule this project runs on, the three
founding bugs, the invariants, the standing conventions — plus a routing table naming the
one document to read for the subsystem you are touching. As of 2026-08-29 the roadmap
([`docs/finishing-plan.md`](docs/finishing-plan.md)), the measured status
([`docs/status.md`](docs/status.md), generated), and the subsystem guides
([`docs/guides/`](docs/guides/)) live outside it, so they no longer enter every agent's
context uninvited. **Follow the routing table rather than searching for what moved.**

This file exists because Codex looks for `AGENTS.md` by convention. It is deliberately a
pointer and not a copy.

## Why a pointer rather than a mirror

An earlier `AGENTS.md` was a full duplicate of `CLAUDE.md`, produced by a find-and-replace
of "claude" with "Codex". It drifted immediately and broke in the process: it linked
`docs/history/2026-08-21-Codex-md-archive.md` (which does not exist) and pointed at
`.Codex/agents/` (wrong case, and the wrong directory besides). It was also untracked, so
nothing reviewed it and nothing could catch either fault.

Two 45 KB documents that must agree are exactly the duplication this project refuses
everywhere else — the same reasoning that gives the repo one `RepositoryPaths` instead of
three (#318), and one row list behind three menus (#505). A pointer cannot drift.

**So: do not copy `CLAUDE.md` into this file.** If something is true of the project, it
belongs in `CLAUDE.md`. Only what is specifically true of *Codex* belongs here.

## What differs for Codex

- **Agent charters.** The team's seven charters are mirrored for Codex in
  [`.codex/agents/`](.codex/agents/) as `.toml`. The originals, and the authority, are
  [`.claude/agents/`](.claude/agents/) as `.md`. A charter changed in one must be changed
  in the other in the same commit — they are small enough that duplication is cheaper
  than indirection, unlike this file.
- **Codex's role on this project** (Brandon's decision, 2026-08-27): Codex takes the
  judgement work `CLAUDE.md`'s team table assigns to Fable — `designer`, `qc` and
  `steward`. Claude takes execution, measurement, and the tree. The reasoning is the same
  one behind the twice-per-project outside review: internal adversarial review
  approximates epistemic independence, and a genuinely different model is the real thing.
  This has already paid — Codex corrected two verdicts in the 2026-08-27 issue-queue cut
  analysis, rejected a `#534` design that would have built a new misattribution surface,
  and found nine of the fourteen stale claims that this file's own truth pass then fixed.
- **Provenance is recorded, per the precedent in
  [`docs/2026-08-27-enemy-ai-audit.md`](docs/2026-08-27-enemy-ai-audit.md):** a PR body or
  design doc says which model made which judgement, so a later reader knows whose
  reasoning they are reading.

## The one thing to read before touching a parser

`CLAUDE.md`'s **"The rule this project runs on"** and the three founding bugs beneath it.
The first bug's shape recurred fourteen times before the mechanism under it was put on
trial, and the trial came from outside the project. Assume the next parser bug is a
misattribution, and write its trip-wire.
