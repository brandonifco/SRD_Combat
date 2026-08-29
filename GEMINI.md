# SRD_Combat — for Gemini

**[`CLAUDE.md`](CLAUDE.md) is the governing document for this project. Read it first, in
full.** The finishing plan, the rule this project runs on, the three founding bugs, the
extraction traps, and the standing conventions live there and only there.

This file exists because the Gemini CLI looks for `GEMINI.md` by convention. Like
[`AGENTS.md`](AGENTS.md) (Codex's entry point), it is deliberately a pointer and not a
copy — see AGENTS.md's "Why a pointer rather than a mirror" for the reasoning, and
`.codex/agents/`'s 2026-08-29 repair for what happens when a mirror is made instead.

## Gemini-specific invocation notes

- Headless runs need `--skip-trust` (or `GEMINI_CLI_TRUST_WORKSPACE=true`) and
  `GEMINI_API_KEY` from the shell profile.
- There is no `.gemini/` directory and no Gemini mirror of the seven agent charters.
  Point Gemini at [`.claude/agents/`](.claude/agents/) — those are the originals.
- Gemini has no assigned standing role on this project. Claude executes; Codex takes the
  judgement roles (`designer`, `qc`, `steward`). Use Gemini as an outside opinion, and
  record which model made which judgement, per AGENTS.md's provenance rule.
