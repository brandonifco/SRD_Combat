#!/usr/bin/env bash
#
# Measures what agent sessions on this project actually cost, from Claude Code's own
# session transcripts. This exists because the 2026-08-29 workflow audit named its own
# blind spot: every recommendation in it rested on byte counts and settings, and nobody
# had measured which context agents actually pay for. PacingMeasure is the instrument
# for balance; this is the instrument for context.
#
# The headline number is the **context floor**: the cache_creation_input_tokens on a
# session's first assistant message. That is the prompt an agent pays before it has read
# a line of code — system prompt, tool schemas, and every instruction file the harness
# injects, CLAUDE.md among them. It is the number a smaller CLAUDE.md is supposed to
# move, so it is the number to compare across a change.
#
#   ./scripts/agent-tokens.sh              # last 10 sessions
#   ./scripts/agent-tokens.sh --all
#   ./scripts/agent-tokens.sh --since 2026-08-29
#
# Transcripts are local and private; nothing here leaves the machine.
set -euo pipefail

DIR="$HOME/.claude/projects/-home-brandon-SRD-Combat"
LIMIT=10; SINCE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --all) LIMIT=9999; shift ;;
    --since) SINCE="$2"; shift 2 ;;
    *) echo "usage: $0 [--all] [--since YYYY-MM-DD]" >&2; exit 2 ;;
  esac
done

[[ -d "$DIR" ]] || { echo "No transcripts at $DIR" >&2; exit 1; }

printf '%-11s %-19s %9s %10s %10s %7s  %s\n' DATE SESSION FLOOR CACHE_RD OUTPUT TOOLS "TOP TOOLS"
printf '%s\n' "---------------------------------------------------------------------------------------------"

find "$DIR" -name '*.jsonl' -newermt "${SINCE:-1970-01-01}" -printf '%T@ %p\n' \
  | sort -rn | head -n "$LIMIT" | cut -d' ' -f2- | while read -r f; do
  jq -rs --arg id "$(basename "$f" .jsonl)" '
    # Context floor: cache_creation on the first assistant message that has usage.
    ( [ .[] | select(.type=="assistant" and .message.usage != null) ] ) as $a
    | ( $a | length ) as $n
    | if $n == 0 then empty else
      ( $a[0].message.usage.cache_creation_input_tokens // 0 ) as $floor
      | ( [ $a[].message.usage.cache_read_input_tokens // 0 ] | add ) as $rd
      | ( [ $a[].message.usage.output_tokens // 0 ] | add ) as $out
      | ( [ .[] | select(.message.content != null)
              | .message.content[]? | select(.type=="tool_use") | .name ] ) as $tools
      | ( $tools | group_by(.) | sort_by(-length) | .[0:3]
          | map("\(.[0]) \(length)") | join(", ") ) as $top
      | ( $a[0].timestamp // "" | .[0:10] ) as $date
      | [ $date, ($id|.[0:8]), $floor, $rd, $out, ($tools|length), $top ] | @tsv
    end' "$f" 2>/dev/null \
  | awk -F'\t' '{printf "%-11s %-19s %9s %10s %10s %7s  %s\n",$1,$2,$3,$4,$5,$6,$7}'
done

cat <<'NOTE'

FLOOR    context paid before the first token of work (system + tools + instruction files)
CACHE_RD cumulative cached input re-read across the session — cheap, but it scales with FLOOR
OUTPUT   generated tokens
TOOLS    tool calls in the session

Read FLOOR across sessions, not within one. A single session's floor also carries the
harness's own system prompt and whatever MCP servers were connected that day, so the
absolute number is not "the size of CLAUDE.md" — the *movement* in it across a change is
the signal.
NOTE
