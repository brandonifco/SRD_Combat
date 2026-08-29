# Contributing to SRD_Combat

Read [`CLAUDE.md`](CLAUDE.md) before proposing or changing anything. It is the
governing project document; this file is only the short contribution path.

1. Start with a GitHub issue. Confirm the problem, scope, acceptance criteria, and
   phase before writing code. Found-but-deferred work belongs in another issue.
2. Keep one concern on one branch and in one pull request. Link the issue and exclude
   unrelated cleanup, even when it is nearby.
3. State the exact behavioral claim the change makes. Supply direct evidence for that
   claim: focused regression tests, deterministic pins, relevant measurements, and
   the full project gate prescribed by `CLAUDE.md`.
4. For parser or extraction work, identify the source text and ownership of every
   extracted claim. Include a reproducer, a bounded fixture, and trip-wires for
   misattribution or silent loss; verify generated output separately from parser
   behavior.
5. Preserve the honesty rule: an unsupported printed rule must remain visible and
   refuse with a named code. Never make an entry look complete by dropping or merely
   storing mechanics the engine does not execute.
6. In the pull request, disclose known limitations, gameplay or design divergences,
   and the model or agent provenance of material judgments. Resolve substantive review
   findings before merge.

## The canonical gate

One script, called by humans, agents, and CI alike, so the invocation exists in exactly
one place:

```bash
./scripts/validate.sh full   # the merge gate: Debug + Release at 0 warnings, whole suite
./scripts/validate.sh fast   # builds only, for a quick check before pushing
```

CI runs the same script (`ci Debug` / `ci Release`). A green run publishes what it
validated to the job summary, readable with `gh run view <id>` — prefer reading that over
re-running the suite to learn whether a commit passed.

## Issues and branches

Issue forms (`.github/ISSUE_TEMPLATE/`) render only in the web "new issue" picker. As of
2026-08-29 none of the 111 open issues was filed through one: `gh issue create --body`
and the API bypass them, and that is how this project actually files work. So the real
convention is a **blank issue with prose headers** carrying the failure mode, acceptance
criteria, and `#NNN` cross-references with the reasoning for each link — that prose is
what lets a fresh agent resume without reconstructing history, and it is worth writing
carefully. Use the forms when filing from the browser; do not treat them as required.

Branch prefixes in use: `fix/`, `feature/`, `refactor/`, `docs/`, `test/`, `art/`,
`chore/`, `codex/`. Name the issue number where there is one (`fix/510-quit-confirm`).

Dependabot pull requests are triaged like any other: merge once the required checks are
green; skim the changelog first for a major-version bump.
