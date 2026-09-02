#!/usr/bin/env python3
"""PreToolUse hook on Bash: keeps the SHARED PRIMARY CHECKOUT of SRD_Combat on `main`,
clean, and fast-forward-only, and keeps `main` itself off the push path.

Why a hook and not a sentence: the sentence already existed ("agent worktrees go
outside the repository") and on 2026-08-30 four concurrent sessions still collided in
the checkout root — a commit on another session's branch, a review's FETCH_HEAD moved
mid-review, a worktree deleted under a running agent, an orchestrator carrying someone's
uncommitted edit onto main (#582). Advice does not stop that; a refusal does.

The rule this enforces
----------------------
Claude Code loads `.claude/settings.json`, `.claude/skills/`, `.claude/hooks/` and
`CLAUDE.md` from the directory a session is launched in, and every session here is
launched in the primary checkout. So the primary must sit on `main`, carry no
uncommitted work, and move only by fast-forward — otherwise the hooks and skills that
land on `main` never reach the sessions that need them. Work happens in linked
worktrees outside the repository (`.claude/skills/land-pr/scripts/worktree.sh`).

What is refused
---------------
Each git statement in the command is judged on its own, against the directory it will
actually run in (`cd`/`pushd` tracked left to right; `git -C`, `--git-dir=`,
`--work-tree=`, `GIT_DIR=`/`GIT_WORK_TREE=` honoured; `bash -c '…'`/`sh -c '…'` bodies
parsed recursively; heredoc bodies ignored).

  In the primary checkout (git-dir == git-common-dir, SRDCombat.sln at the top):
    commit checkout switch restore merge rebase reset cherry-pick revert am pull stash
    clean add rm mv apply bisect update-ref symbolic-ref, and `branch` with -f/-D/-M/-m
    — everything that moves HEAD, a ref, the index or the working tree.
    EXCEPT the fast-forward path, allowed only while the tree is clean:
      git checkout main | git switch main | git merge --ff-only <ref> | git pull --ff-only
  From any checkout:
    a push that names main (main, HEAD:main, +main, refs/heads/main, --all, --mirror),
    or a bare `git push` with main checked out;
    `git worktree remove --force` and `git worktree move` — the shapes that take a
    worktree away from a session that may still be using it (#582, collision 3).

Read-only git, `git worktree add`, plain `git worktree remove` (which refuses a dirty
tree on its own) and every non-git command pass, so the primary stays the place
worktrees are created from and returned to.

What is not a defence
---------------------
This stops accidents, not evasion. A `$VAR` assigned outside the command, `eval`,
`python -c`, a `cd` inside a function, or PATH games can get past it, and an unresolvable
directory or a parse failure falls open (the command runs). If `$CLAUDE_PROJECT_DIR`
is unset or python3 is missing the hook errors and Claude Code proceeds. An agent that
routes around this is making a choice, and the choice is visible in the transcript.

Escape hatch: SRD_COMBAT_ALLOW_PRIMARY_GIT=1 in the environment — under "env" in
`.claude/settings.local.json` for a human-driven session. It is session-wide: every
subagent of that session inherits it.

Output contract (Claude Code PreToolUse): a JSON permissionDecision on stdout, exit 0.
"""
import json
import os
import re
import shlex
import subprocess
import sys

PRIMARY_WRITE = {
    "commit", "checkout", "switch", "restore", "merge", "rebase", "reset", "cherry-pick",
    "revert", "am", "pull", "stash", "clean", "add", "rm", "mv", "apply", "bisect",
    "update-ref", "symbolic-ref",
}
BRANCH_MOVE_FLAGS = {"-f", "--force", "-D", "-M", "-m", "--move", "--delete"}
SPLIT = re.compile(r"\s*(?:&&|\|\||;|\||\n)\s*")
HEREDOC = re.compile(r"<<-?\s*['\"]?(\w+)['\"]?[^\n]*\n(?:.*?\n)*?\s*\1\s*(?=\n|$)", re.S)
SKIP_WRAPPERS = {"command", "exec", "nohup", "time", "xargs", "sudo", "builtin"}
MAIN_NAMES = {"main", "refs/heads/main"}


def deny(reason):
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny",
        "permissionDecisionReason": reason,
    }}))
    sys.exit(0)


def git(dir_, *args):
    try:
        out = subprocess.run(["git", "-C", dir_, *args], capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return None
    return out.stdout.strip() if out.returncode == 0 else None


VARS = {}


def expand(path):
    """Substitute $VAR / ${VAR} assigned earlier in the same command; unknown → None."""
    def sub(m):
        name = m.group(1) or m.group(2)
        if name in VARS:
            return VARS[name]
        raise KeyError(name)
    try:
        return re.sub(r"\$\{(\w+)\}|\$(\w+)", sub, path)
    except KeyError:
        return None


def resolve(base, path):
    """A concrete directory, or None when the path cannot be known here (unassigned $VAR, `cd -`, backticks)."""
    if path is None or path == "-" or "`" in path:
        return None
    if "$" in path:
        path = expand(path)
        if path is None:
            return None
    path = os.path.expanduser(path)
    if not os.path.isabs(path):
        if base is None:
            return None
        path = os.path.join(base, path)
    return os.path.normpath(path)


class Checkout:
    def __init__(self, dir_):
        self.ok = False
        if dir_ is None or not os.path.isdir(dir_):
            return
        git_dir = git(dir_, "rev-parse", "--path-format=absolute", "--git-dir")
        common = git(dir_, "rev-parse", "--path-format=absolute", "--git-common-dir")
        top = git(dir_, "rev-parse", "--show-toplevel")
        if not (git_dir and common and top):
            return
        if not os.path.isfile(os.path.join(top, "SRDCombat.sln")):
            return
        self.ok = True
        self.dir = dir_
        self.top = top
        self.primary = os.path.realpath(git_dir) == os.path.realpath(common)

    def branch(self):
        return git(self.dir, "symbolic-ref", "--short", "HEAD")

    def clean(self):
        status = git(self.dir, "status", "--porcelain", "--untracked-files=no")
        return status == ""


def judge_push(args, co):
    positional = [a for a in args if not a.startswith("-")]
    flags = [a for a in args if a.startswith("-")]
    if "--all" in flags or "--mirror" in flags:
        deny("Refused: 'git push --all/--mirror' would push main. Push one feature branch and open a PR.")
    for a in positional:
        name = a.lstrip("+")
        target = name.split(":", 1)[1] if ":" in name else name
        if target in MAIN_NAMES:
            deny("Refused: this push names main. Nothing is pushed to main on SRD_Combat; open a PR and merge it once CI is green (CLAUDE.md, Standing conventions).")
    if len(positional) <= 1 and co.ok and co.branch() == "main":
        deny("Refused: a bare 'git push' with main checked out would push main. Work on a branch in a worktree and open a PR.")


def judge_worktree(args):
    if args and args[0] == "move":
        deny("Refused: 'git worktree move' relocates a tree another session may be running in (#582). Leave worktrees where their sessions made them.")
    if args and args[0] == "remove" and any(a in ("--force", "-f") for a in args[1:]):
        deny("Refused: 'git worktree remove --force' discards a worktree's uncommitted work — that was collision three of #582. Plain 'git worktree remove' refuses a dirty tree on its own; use it, and only on worktrees you created.")


def ff_exempt(sub, args, co):
    """The one sanctioned way the primary moves: onto main, or forward along it, clean."""
    positional = [a for a in args if not a.startswith("-")]
    flags = [a for a in args if a.startswith("-")]
    if sub in ("checkout", "switch") and positional == ["main"] and not flags:
        return True
    if sub == "merge" and "--ff-only" in flags and len(positional) == 1:
        return True
    if sub == "pull" and "--ff-only" in flags:
        return True
    return False


def judge_git(toks, cwd):
    """toks[0] is git. Returns nothing; denies by exiting."""
    dir_ = cwd
    i = 1
    sub = None
    while i < len(toks):
        t = toks[i]
        if t == "-C" and i + 1 < len(toks):
            dir_ = resolve(dir_, toks[i + 1]); i += 2; continue
        if t.startswith("-C") and len(t) > 2:
            dir_ = resolve(dir_, t[2:]); i += 1; continue
        if t == "-c" and i + 1 < len(toks):
            i += 2; continue
        if t.startswith("--work-tree="):
            dir_ = resolve(dir_, t.split("=", 1)[1]); i += 1; continue
        if t.startswith("--git-dir="):
            g = resolve(dir_, t.split("=", 1)[1])
            dir_ = os.path.dirname(g) if g else None; i += 1; continue
        if t in ("--git-dir", "--work-tree") and i + 1 < len(toks):
            p = resolve(dir_, toks[i + 1])
            dir_ = (os.path.dirname(p) if t == "--git-dir" else p) if p else None; i += 2; continue
        if t.startswith("-"):
            i += 1; continue
        sub = t
        args = toks[i + 1:]
        break
    if sub is None:
        return
    co = Checkout(dir_)
    if sub == "push":
        judge_push(args, co)
        return
    if sub == "worktree":
        judge_worktree(args)
        return
    if not (co.ok and co.primary):
        return
    if sub in PRIMARY_WRITE:
        if ff_exempt(sub, args, co):
            if co.clean():
                return
            deny(f"Refused: the primary checkout at {co.top} has uncommitted changes, so it cannot move onto or along main. Someone left work here; commit or stash it from a session that owns it, or move it to a worktree.")
        deny(f"Refused: {co.top} is the shared primary checkout of SRD_Combat, and 'git {sub}' would move its HEAD, index or working tree. It stays on main, clean, fast-forward only (#582). Create a worktree outside the repository and work there: bash .claude/skills/land-pr/scripts/worktree.sh <scratchpad-dir> <branch>. Read-only git, 'git worktree add', and 'git merge --ff-only origin/main' on a clean main still work here.")
    if sub == "branch" and any(a in BRANCH_MOVE_FLAGS for a in args):
        deny(f"Refused: 'git branch' with a move/force/delete flag rewrites a ref in the shared primary checkout ({co.top}). Do it from your worktree, or leave the ref alone.")


def walk(command, cwd, depth=0):
    if depth > 3:
        return
    text = HEREDOC.sub("", command)
    for raw in SPLIT.split(text):
        st = raw.strip().lstrip("({").strip()
        if not st:
            continue
        try:
            toks = shlex.split(st)
        except ValueError:
            toks = st.split()
        # VAR=value prefixes; GIT_DIR / GIT_WORK_TREE redirect the target.
        env_dir = None
        if toks and toks[0] in ("export", "local", "declare", "readonly"):
            toks.pop(0)
        while toks and re.match(r"^[A-Za-z_][A-Za-z0-9_]*=", toks[0]):
            k, v = toks.pop(0).split("=", 1)
            expanded = expand(v) if "$" in v else v
            if expanded is not None:
                VARS[k] = os.path.expanduser(expanded)
            if k == "GIT_WORK_TREE":
                env_dir = resolve(cwd, v)
            elif k == "GIT_DIR" and env_dir is None:
                g = resolve(cwd, v)
                env_dir = os.path.dirname(g) if g else None
        while toks and toks[0] in SKIP_WRAPPERS:
            toks.pop(0)
            while toks and toks[0].startswith("-"):
                toks.pop(0)
        if not toks:
            continue
        head = toks[0].lstrip("\\")
        base = os.path.basename(head)
        if base in ("cd", "pushd"):
            cwd = resolve(cwd, toks[1] if len(toks) > 1 else "~")
            continue
        if base == "env":
            j = 1; env_cwd = cwd
            while j < len(toks) and (toks[j].startswith("-") or "=" in toks[j]):
                if toks[j] == "-C" and j + 1 < len(toks):
                    env_cwd = resolve(cwd, toks[j + 1]); j += 2; continue
                j += 1
            if j < len(toks):
                walk(" ".join(shlex.quote(t) for t in toks[j:]), env_cwd, depth + 1)
            continue
        if base in ("bash", "sh", "zsh", "dash"):
            if "-c" in toks:
                k = toks.index("-c")
                if k + 1 < len(toks):
                    walk(toks[k + 1], cwd, depth + 1)
            continue
        if base == "git":
            judge_git(["git"] + toks[1:], env_dir or cwd)


def main():
    try:
        data = json.load(sys.stdin)
    except ValueError:
        return
    command = (data.get("tool_input") or {}).get("command") or ""
    if not command or "git" not in command:
        return
    if os.environ.get("SRD_COMBAT_ALLOW_PRIMARY_GIT") == "1":
        return
    cwd = data.get("cwd") or os.getcwd()
    walk(command, cwd)


if __name__ == "__main__":
    main()
