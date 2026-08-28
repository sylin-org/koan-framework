#!/usr/bin/env bash
# Staged composite — codex harness over a local Ollama model (OSS provider, per-run overrides
# only; the global codex config is never modified). usage: run-codex-oss.sh <koan|plain>
set -u
cd "$(dirname "$0")"
ARM="${1:?usage: run-codex-oss.sh <koan|plain>}"
REPO="$(cd ../../../../.. && pwd)"
G="$PWD"
MODEL="${COX_MODEL:-codex-oss-qwen38-27b}"
CELL="$REPO/evals/agent-race/matrix/cells/test01-staged-composite/$MODEL/$ARM"
ART="$CELL/transcripts"
mkdir -p "$ART"

snapshot() {
  [ "$ARM" = "plain" ] || return 0
  cp "$NEUTRAL"/events-s*.jsonl "$NEUTRAL"/lastmsg-s*.txt "$NEUTRAL"/ABORTED.md "$ART"/ 2>/dev/null
  mkdir -p "$CELL/code" && cp -r "$NEUTRAL/." "$CELL/code/" 2>/dev/null || true
  rm -rf "$NEUTRAL"
}

if [ "$ARM" = "koan" ]; then
  [ -d "$CELL/code" ] && { echo "code/ exists — move it aside first." >&2; exit 1; }
  mkdir -p "$CELL/code"
  WORK="$CELL/code"
else
  NEUTRAL="$(dirname "$REPO")/_agent-race-neutral/staged-codex-oss"
  [ -d "$NEUTRAL" ] && { echo "neutral folder exists — move it aside first." >&2; exit 1; }
  mkdir -p "$NEUTRAL"
  WORK="$NEUTRAL"
fi
FLAGS=(--dangerously-bypass-approvals-and-sandbox --skip-git-repo-check)
MREF="${COX_MREF:-local-code-candidate:qwen38-27b-q4-daily}"
COV=(-c model_provider=ollama-local \
     -c 'model_providers.ollama-local.name="Ollama local"' \
     -c 'model_providers.ollama-local.base_url="http://localhost:11434/v1"' \
     -c 'model_providers.ollama-local.wire_api="responses"' \
     -c "model=\"$MREF\"")

stage_run() { # $1 stage no; $2 prompt file; $3 session id ("" for fresh)
  local S E
  S=$(date +%s)
  if [ -n "$3" ]; then
    (cd "$WORK" && timeout 2700 codex exec resume "$3" "${FLAGS[@]}" "${COV[@]}" --json \
      -o "$ART/lastmsg-s$1.txt" - < "$2") > "$ART/events-s$1.jsonl" 2>&1
  else
    (cd "$WORK" && timeout 2700 codex exec "${FLAGS[@]}" "${COV[@]}" --json \
      -o "$ART/lastmsg-s$1.txt" - < "$2") > "$ART/events-s$1.jsonl" 2>&1
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
stage_run 1 <(cat "$P1" "$G/stage-1.txt") ""
SID=$(grep -o '"thread_id":"[^"]*"' "$ART/events-s1.jsonl" | head -1 | cut -d'"' -f4)
echo "session: ${SID:-<none>}"
if ! gate 1; then echo "STAGE 1 GATE FAILED — aborting."; snapshot; exit 1; fi

echo "=== STAGE 2 ==="
stage_run 2 "$G/stage-2.txt" "$SID"
if ! gate 2; then echo "STAGE 2 GATE FAILED — aborting."; snapshot; exit 1; fi

echo "=== STAGE 3 ==="
stage_run 3 "$G/stage-3.txt" "$SID"
if ! gate 3; then echo "STAGE 3 GATE FAILED — aborting."; snapshot; exit 1; fi

for n in 1 2 3; do
  echo "stage$n wallclock: $(cat "$ART/wallclock-stage$n.txt")s  tokens: $(grep -o '"input_tokens":[0-9]*' "$ART/events-s$n.jsonl" | tail -1) $(grep -o '"output_tokens":[0-9]*' "$ART/events-s$n.jsonl" | tail -1)"
done
echo "ALL STAGES PASSED"
snapshot
