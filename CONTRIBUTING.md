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

Use the repository's issue forms for correctness reports and scoped implementation
slices. Blank issues remain available for design and stewardship work that does not fit
those forms.
