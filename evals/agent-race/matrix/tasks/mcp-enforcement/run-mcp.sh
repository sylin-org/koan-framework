#!/usr/bin/env bash
# MCP-enforcement cell runner — single task, one stage. usage: run-mcp.sh <koan|plain>
# Mirrors the staged-composite runners: identical task body, arm line differs only.
# No Ollama anywhere in this task (pure web + MCP), so it is GPU-free by design.
set -u
cd "$(dirname "$0")"
ARM="${1:?usage: run-mcp.sh <koan|plain>}"
REPO="$(cd ../../../../.. && pwd)"
G="$PWD"
MODEL="${MCP_HARNESS:-codex-sol-high}"
CELL="$REPO/evals/agent-race/matrix/cells/test02-mcp-enforcement/$MODEL/$ARM"
ART="$CELL/transcripts"
mkdir -p "$ART"

snapshot() {
  [ "$ARM" = "plain" ] || return 0
  cp "$NEUTRAL"/events-s1.jsonl "$NEUTRAL"/lastmsg-s1.txt "$NEUTRAL"/ABORTED.md "$ART"/ 2>/dev/null
  mkdir -p "$CELL/code" && cp -r "$NEUTRAL/." "$CELL/code/" 2>/dev/null || true
  rm -rf "$NEUTRAL"
}

if [ "$ARM" = "koan" ]; then
  [ -d "$CELL/code" ] && { echo "code/ exists — move it aside first." >&2; exit 1; }
  mkdir -p "$CELL/code"
  WORK="$CELL/code"
else
  NEUTRAL="$(dirname "$REPO")/_agent-race-neutral/test02-mcp"
  [ -d "$NEUTRAL" ] && { echo "neutral folder exists — move it aside first." >&2; exit 1; }
  mkdir -p "$NEUTRAL"
  WORK="$NEUTRAL"
fi
FLAGS=(--dangerously-bypass-approvals-and-sandbox --skip-git-repo-check)

gate() {
  bash "$G/grade-mcp.sh" "$WORK" 2>&1 | tee "$ART/grade-stage1.txt"
  local s n t
  s=$(grep -o 'SCORE [0-9]*/[0-9]*' "$ART/grade-stage1.txt" | tail -1)
  [ -z "$s" ] && return 1
  n=${s#SCORE }; n=${n%%/*}; t=${s##*/}
  [ "$n" = "$t" ]
}

echo "=== STAGE 1 ==="
S=$(date +%s)
if [ "$ARM" = "koan" ]; then P1="$G/arm-koan.txt"; else P1="$G/arm-plain.txt"; fi
(cd "$WORK" && timeout 2700 codex exec "${FLAGS[@]}" --json \
  -o "$ART/lastmsg-s1.txt" - < <(cat "$P1" "$G/task-mcp.txt")) > "$ART/events-s1.jsonl" 2>&1
E=$(date +%s)
echo $((E-S)) > "$ART/wallclock-stage1.txt"
SID=$(grep -o '"thread_id":"[^"]*"' "$ART/events-s1.jsonl" | head -1 | cut -d'"' -f4)
echo "session: ${SID:-<none>}"

if ! gate 1; then echo "GATE FAILED — aborting."; snapshot; exit 1; fi

echo "stage1 wallclock: $(cat "$ART/wallclock-stage1.txt")s  tokens: $(grep -o '"input_tokens":[0-9]*' "$ART/events-s1.jsonl" | tail -1) $(grep -o '"output_tokens":[0-9]*' "$ART/events-s1.jsonl" | tail -1)"
echo "CELL COMPLETE"
snapshot
