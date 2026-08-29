# Repository development framework — 2026-08-28

Issue: [#556](https://github.com/brandonifco/SRD_Combat/issues/556)

This is the before/after ledger for the repository-development work begun on
2026-08-28. `CLAUDE.md` remains the governing document and GitHub issues remain the
work queue; this record captures live configuration facts and why controls changed.

## Audit basis

- Audit time: 2026-08-28, America/New_York.
- Fetched `origin/main`: `4bc468ef5ffbd8c632c9d4eb879a43554506a8b6`
  (merge of PR #554).
- Local checkout: `main` matched `origin/main`. The pre-existing untracked
  `.claude/worktrees/` tree and all registered worktrees were preserved.
- GitHub CLI account: `brandonifco`; authenticated scopes were `gist`, `read:org`,
  `read:project`, `repo`, and `workflow`. The token value was not recorded.
- Live queue: 111 open issues and one open PR (#555). Inaccessible endpoints are
  recorded as unknown, never inferred absent.

## Before and Phase 1 after

| Surface | Verified before | Phase 1 after |
| --- | --- | --- |
| Contribution and security policy | No `CONTRIBUTING.md` or `SECURITY.md` | Concise pointers and an operational private-reporting policy added |
| Intake | No PR template or issue forms | One evidence/provenance PR template; correctness and scoped-implementation forms; blank design/stewardship issues retained |
| Dependency updates | No Dependabot configuration | Weekly grouped minor/patch NuGet and GitHub Actions version updates; major and security updates stay individually visible; no auto-merge |
| Workflow permissions | No explicit `permissions:` | Repository workflow explicitly uses `contents: read` |
| Action references | `actions/checkout@v7`, `actions/setup-dotnet@v6`, and `actions/upload-artifact@v7` | Official release tags resolved through the GitHub API and pinned to full commit SHAs, with release comments |
| Workflow concurrency | PR supersession cancelled safely; `main` runs never cancelled | Preserved unchanged |

Pinned Action provenance:

- `actions/checkout` v7.0.1 → `3d3c42e5aac5ba805825da76410c181273ba90b1`
- `actions/setup-dotnet` v6.0.0 → `a98b56852c35b8e3190ac28c8c2271da59106c68`
- `actions/upload-artifact` v7.0.1 → `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a`

## Live GitHub baseline before settings changes

- Merge methods: merge commit, squash, and rebase all allowed; auto-merge disabled;
  automatic head-branch deletion enabled.
- `main`: legacy branch protection only; administrators included; force pushes,
  deletion, signed commits, and linear history disabled. Debug and Release were
  required, non-strict checks. Pull requests and conversation resolution were not
  required. No repository rulesets existed.
- Actions: enabled for the full marketplace; full-SHA enforcement disabled. Default
  `GITHUB_TOKEN` permission was read-only and Actions could not approve pull requests.
- Security: secret scanning and push protection enabled, with zero open secret alerts.
  Dependabot vulnerability alerts and security updates disabled; private vulnerability
  reporting was disabled at audit time, then enabled on 2026-08-29 after PR #557's
  pinned workflow passed all three jobs; CodeQL default setup `not-configured`
  (detected languages: Actions, C#, Python). The SBOM endpoint returned HTTP 404. The
  public repository's dependency graph is documented by GitHub as always enabled, but
  SBOM availability remains unverified until the remaining security controls are enabled
  and the endpoint is retried.
- Planning: no milestones and no Projects. Existing phase labels and
  `paused:balance-design` were present; no priority label existed. The token had only
  `read:project`, so Project creation requires a later `project` scope authorization.
- Repository surfaces: Wiki enabled, Discussions disabled, no releases or tags.
- Remote branches not merged into `origin/main`: `art/ship-bugbear-warrior-441`,
  `design/327-playmode-refactor`, `fix/510-quit-confirm-holds-turn-open`, and
  `fix/save-tier-attachment`. Their issue and PR histories and unique commits were
  audited on 2026-08-29:

  | Branch | Classification | Evidence |
  | --- | --- | --- |
  | `art/ship-bugbear-warrior-441` | Intentionally retained | PR #446 was closed unmerged after Brandon rejected the visual change; #441 records that the unshipped master stays deliberately |
  | `design/327-playmode-refactor` | Patch-equivalent to `main`; safe cleanup candidate | `git cherry origin/main origin/design/327-playmode-refactor` reports its only commit with `-`; PR #498 merged the design |
  | `fix/510-quit-confirm-holds-turn-open` | Active | Open PR #555, linked to open issue #510 |
  | `fix/save-tier-attachment` | Superseded, containing unique commits | Its unmerged parser/content commit is not patch-equivalent; #370 was independently closed by PR #395 |

  No branch was deleted. Even the patch-equivalent cleanup candidate requires Brandon's
  explicit confirmation before deletion.

## Ordered settings work and safety gates

1. Prove Phase 1's workflow succeeds with the pinned Actions, then make the documented
   private-reporting route live before merge. Both gates passed on PR #557.
2. After merge, enable dependency alerts/updates and CodeQL; add dependency review in a
   separate PR; inspect findings before requiring any new check.
3. Restrict Actions and enable repository-wide SHA enforcement only after every
   committed workflow complies.
4. Strengthen the existing legacy `main` protection rather than layering a duplicate
   ruleset. Require a check only after it reports successfully and consistently.
5. Defer strict/up-to-date protection until #319 lands and its latency is measured.

The Wiki will be disabled only after checking its separate Git repository for unique
content. Discussions, Pages, CODEOWNERS, signed commits, linear history, merge queue,
and ceremonial environments remain deferred because no concrete need currently
outweighs their cost.

## Verification commands

```text
./scripts/doctor.sh
dotnet test SRDCombat.sln -c Debug
dotnet build SRDCombat.sln -c Debug
dotnet build SRDCombat.sln -c Release
git diff --check
```

Phase 1 results: the environment check passed; Debug tests passed 4,814 with the two
intentional fixture writers skipped (`Game.Tests` 388 passed in 9m03s); Debug and
Release builds both completed with zero warnings and zero errors; all committed YAML
parsed successfully; GitHub-specific form review removed empty optional `title` keys
that its validator rejects; both issue forms then passed a schema-shape check for
required keys, non-empty string fields, unique ids and labels, and checkbox structure;
`git diff --check` was clean.

GitHub state was read with `gh api`, `gh issue list`, `gh pr list`, and
`gh project list`. Action release tags were resolved from the official Action
repositories, not copied from third-party examples.

## Provenance

Audit, framework design, and implementation: OpenAI Codex, GPT-5.6 Sol with `max`
reasoning, in the 2026-08-28 implementation task. A separate fresh-context Codex QC
pass is required on the final diff and evidence before merge; it is adversarial model
review, not independent human approval.

Primary behavior references:

- [GitHub Actions repository policy](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository)
- [Secure use of full-SHA Action references](https://docs.github.com/en/actions/reference/security/secure-use)
- [Dependabot configuration options](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)
- [GitHub supply-chain feature availability](https://docs.github.com/en/code-security/concepts/supply-chain-security/supply-chain-security)
