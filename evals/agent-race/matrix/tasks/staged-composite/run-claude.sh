#!/usr/bin/env bash
# Staged composite — Claude Code harness. usage: run-claude.sh <koan|plain>
# Mirrors run.sh; identical stage bodies, gates, and graders — only the harness differs.
set -u
cd "$(dirname "$0")"
ARM="${1:?usage: run-claude.sh <koan|plain>}"
REPO="$(cd ../../../../.. && pwd)"
G="$PWD"
MODEL="claude-default"
CELL="$REPO/evals/agent-race/matrix/cells/test01-staged-composite/$MODEL/$ARM"
ART="$CELL/transcripts"
mkdir -p "$ART"

snapshot() {
  [ "$ARM" = "plain" ] || return 0
  cp "$NEUTRAL"/events-s*.jsonl "$NEUTRAL"/ABORTED.md "$ART"/ 2>/dev/null
  mkdir -p "$CELL/code" && cp -r "$NEUTRAL/." "$CELL/code/" 2>/dev/null || true
  rm -rf "$NEUTRAL"
}

if [ "$ARM" = "koan" ]; then
  [ -d "$CELL/code" ] && { echo "code/ exists — move it aside first." >&2; exit 1; }
  mkdir -p "$CELL/code"
  WORK="$CELL/code"
else
  NEUTRAL="$(dirname "$REPO")/_agent-race-neutral/staged-claude"
  [ -d "$NEUTRAL" ] && { echo "neutral folder exists — move it aside first." >&2; exit 1; }
  mkdir -p "$NEUTRAL"
  WORK="$NEUTRAL"
fi
CFLAGS=(--dangerously-skip-permissions --verbose --output-format stream-json)

run_stage() { # $1 stage no; $2 prompt file; $3 session id ("" for fresh)
  local S E
  S=$(date +%s)
  if [ -n "$3" ]; then
    (cd "$WORK" && timeout 1800 claude -p --resume "$3" "${CFLAGS[@]}" < "$2") > "$ART/events-s$1.jsonl" 2>&1
  else
    (cd "$WORK" && timeout 1800 claude -p "${CFLAGS[@]}" < "$2") > "$ART/events-s$1.jsonl" 2>&1
  fi
  E=$(date +%s)
  echo $((E-S)) > "$ART/wallclock-stage$1.txt"
}

gate() {
  bash "$G/grade-staged.sh" "$WORK" "$1" 2>&1 | tee "$ART/grade-stage$1.txt"
  local s n t
  s=$(grep -o 'SCORE [0-9]*/[0-9]*' "$ART/grade-stage$1.txt" | tail -1)
  [ -z "$s" ] && return 1
  n=${s#SCORE }; n=${n%%/*}; t=${s##*/}
  [ "$n" = "$t" ]
}

echo "=== STAGE 1 ==="
if [ "$ARM" = "koan" ]; then P1="$G/arm-koan.txt"; else P1="$G/arm-plain.txt"; fi
run_stage 1 <(cat "$P1" "$G/stage-1.txt") ""
SID=$(grep -o '"session_id":"[^"]*"' "$ART/events-s1.jsonl" | head -1 | cut -d'"' -f4)
echo "session: ${SID:-<none>}"
if ! gate 1; then echo "STAGE 1 GATE FAILED — aborting."; snapshot; exit 1; fi

echo "=== STAGE 2 ==="
run_stage 2 "$G/stage-2.txt" "$SID"
if ! gate 2; then echo "STAGE 2 GATE FAILED — aborting."; snapshot; exit 1; fi

echo "=== STAGE 3 ==="
run_stage 3 "$G/stage-3.txt" "$SID"
if ! gate 3; then echo "STAGE 3 GATE FAILED — aborting."; snapshot; exit 1; fi

for n in 1 2 3; do
  echo "stage$n wallclock: $(cat "$ART/wallclock-stage$n.txt")s  usage: $(grep -o '"usage":{[^}]*}' "$ART/events-s$n.jsonl" | tail -1)"
done
echo "ALL STAGES PASSED"
snapshot
