#!/usr/bin/env bash
#
# The knockout table for .claude/hooks/guard-primary-checkout.py.
#
# The hook is a PreToolUse guard with no test project of its own — #528's point ("an
# instrument that has not been knocked out is not evidence") applies to it as much as
# to any C# guard, and #589 found the ad-hoc harness qc used to review it was a ten-line
# loop nobody committed. This is that harness, committed, so the hook's accident-class
# coverage is reproducible rather than re-derived per review.
#
# Scope: the ACCIDENT class only — the shapes the hook's header documents as refused,
# plus the ff-only exemption and ordinary worktree use it must still allow. The EVASION
# class (eval, subprocess, PATH games, cross-call $VAR, an unresolvable directory) is
# out of scope BY DESIGN (#589 decision (a)): those rows are included here labelled
# RESIDUE and expected ALLOW, so the table demonstrates the boundary instead of hiding
# it. The hook's own "What is not a defence" section is the prose record; this is the
# reproducible one.
#
#   hook-cases.sh
#
# Builds two disposable git checkouts under a temp dir — a fake "primary" (git-dir ==
# git-common-dir, SRDCombat.sln at top, clean, on main) and a linked "worktree" off it
# (git-dir != git-common-dir) — then feeds the hook the same PreToolUse JSON shape
# Claude Code sends (`{"tool_input": {"command": ...}, "cwd": ...}` on stdin, per
# .claude/settings.json's hook command) for each case below, and compares its verdict
# against the expected one. The hook only ever *judges* — it does not execute the
# command — so no case here can actually commit, push or touch the real repository.
#
# Exit 0 all rows matched; 1 any row's actual != expected.
set -uo pipefail

here="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
HOOK="$(cd -- "$here/../../../hooks" && pwd)/guard-primary-checkout.py"
[[ -f "$HOOK" ]] || { echo "hook-cases.sh: hook not found at $HOOK" >&2; exit 2; }

FIXTURES="$(mktemp -d)"
trap 'rm -rf "$FIXTURES"' EXIT

PRIMARY="$FIXTURES/primary"
WORKTREE="$FIXTURES/worktree"

git init -q "$PRIMARY"
git -C "$PRIMARY" config user.email "hook-cases@example.invalid"
git -C "$PRIMARY" config user.name "hook-cases"
touch "$PRIMARY/SRDCombat.sln"
git -C "$PRIMARY" add SRDCombat.sln
git -C "$PRIMARY" commit -q -m "fixture: initial"
git -C "$PRIMARY" branch -M main
git -C "$PRIMARY" worktree add -q -b feature "$WORKTREE" main

# Sanity: the fixtures must actually present the shape the hook keys off, or every
# case below would be testing nothing.
[[ "$(git -C "$PRIMARY" rev-parse --path-format=absolute --git-dir)" \
   == "$(git -C "$PRIMARY" rev-parse --path-format=absolute --git-common-dir)" ]] \
  || { echo "hook-cases.sh: fixture primary is not primary (git-dir != git-common-dir)" >&2; exit 2; }
[[ "$(git -C "$WORKTREE" rev-parse --path-format=absolute --git-dir)" \
   != "$(git -C "$WORKTREE" rev-parse --path-format=absolute --git-common-dir)" ]] \
  || { echo "hook-cases.sh: fixture worktree is not a linked worktree" >&2; exit 2; }
[[ -z "$(git -C "$PRIMARY" status --porcelain --untracked-files=no)" ]] \
  || { echo "hook-cases.sh: fixture primary is not clean" >&2; exit 2; }

# verdict CWD COMMAND
# Feeds the hook the PreToolUse JSON and prints DENY or ALLOW.
verdict() {
    local cwd="$1" command="$2" out decision
    out="$(jq -n --arg cwd "$cwd" --arg command "$command" \
        '{tool_input: {command: $command}, cwd: $cwd}' | python3 "$HOOK" 2>/dev/null)"
    decision="$(printf '%s' "$out" | jq -r '.hookSpecificOutput.permissionDecision // empty' 2>/dev/null)"
    if [[ "$decision" == "deny" ]]; then echo "DENY"; else echo "ALLOW"; fi
}

# Case table: label|class|cwd|command|expected
# class is ACCIDENT, ALLOWED-IN-WORKTREE, or RESIDUE (documented-out-of-scope evasion).
cases=(
    # --- ACCIDENT class: expected DENY when cwd is the primary checkout ---
    "git commit|ACCIDENT|$PRIMARY|git commit -m x|DENY"
    "git checkout (non-main)|ACCIDENT|$PRIMARY|git checkout feature|DENY"
    "git switch (non-main)|ACCIDENT|$PRIMARY|git switch feature|DENY"
    "git restore|ACCIDENT|$PRIMARY|git restore some-file.txt|DENY"
    "git merge (non-ff)|ACCIDENT|$PRIMARY|git merge feature|DENY"
    "git rebase|ACCIDENT|$PRIMARY|git rebase main|DENY"
    "git reset|ACCIDENT|$PRIMARY|git reset --hard|DENY"
    "git stash|ACCIDENT|$PRIMARY|git stash|DENY"
    "git pull (non-ff-only)|ACCIDENT|$PRIMARY|git pull|DENY"
    "git -C <primary> commit|ACCIDENT|$WORKTREE|git -C $PRIMARY commit -m x|DENY"
    "cd <primary> && git commit|ACCIDENT|$WORKTREE|cd $PRIMARY && git commit -m x|DENY"
    "bash -c 'git commit'|ACCIDENT|$PRIMARY|bash -c 'git commit -m x'|DENY"
    "GIT_DIR=<primary>/.git git commit|ACCIDENT|$WORKTREE|GIT_DIR=$PRIMARY/.git git commit -m x|DENY"
    "git --work-tree=<primary> commit|ACCIDENT|$WORKTREE|git --work-tree=$PRIMARY --git-dir=$PRIMARY/.git commit -m x|DENY"
    "same-command D=<primary>; cd \$D && git commit|ACCIDENT|$WORKTREE|D=$PRIMARY; cd \$D && git commit -m x|DENY"
    "git push origin +main|ACCIDENT|$WORKTREE|git push origin +main|DENY"
    "git push origin HEAD:main|ACCIDENT|$WORKTREE|git push origin HEAD:main|DENY"
    "git worktree remove --force|ACCIDENT|$WORKTREE|git worktree remove --force $WORKTREE|DENY"

    # --- Allowed in a worktree (not the primary) ---
    "git commit (in worktree)|ALLOWED-IN-WORKTREE|$WORKTREE|git commit -m x|ALLOW"
    "git push -u origin <branch>|ALLOWED-IN-WORKTREE|$WORKTREE|git push -u origin feature|ALLOW"
    "git checkout main (ff-only, clean primary)|ALLOWED-IN-WORKTREE|$PRIMARY|git checkout main|ALLOW"
    "git merge --ff-only origin/main (clean primary)|ALLOWED-IN-WORKTREE|$PRIMARY|git merge --ff-only origin/main|ALLOW"

    # --- RESIDUE: known evasions, accepted out of scope (#589 decision a) ---
    "eval \"git commit\"|RESIDUE|$PRIMARY|eval \"git commit -m x\"|ALLOW"
    "python3 -c subprocess git|RESIDUE|$PRIMARY|python3 -c \"import subprocess; subprocess.run(['git','commit','-m','x'])\"|ALLOW"
    "cross-call \$VAR (unset this command)|RESIDUE|$WORKTREE|cd \$D_NOT_SET_HERE && git commit -m x|ALLOW"
    "cd \"\$UNSET\" && git commit|RESIDUE|$WORKTREE|cd \"\$UNSET\" && git commit -m x|ALLOW"
)

pass=0
fail=0
printf '%-55s %-9s %-9s %-9s %s\n' "CASE" "CLASS" "EXPECTED" "ACTUAL" "RESULT"
for row in "${cases[@]}"; do
    IFS='|' read -r label class cwd command expected <<<"$row"
    actual="$(verdict "$cwd" "$command")"
    if [[ "$actual" == "$expected" ]]; then
        result="PASS"; ((pass++))
    else
        result="FAIL"; ((fail++))
    fi
    printf '%-55s %-9s %-9s %-9s %s\n' "$label" "$class" "$expected" "$actual" "$result"
done

echo
echo "hook-cases.sh: $pass passed, $fail failed, $((pass + fail)) total"
(( fail == 0 )) || { echo "hook-cases.sh: FAILED — the hook's verdict moved for at least one case above" >&2; exit 1; }
exit 0
