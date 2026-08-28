#!/usr/bin/env bash
# Staged composite runner — one Codex session, three stages, accumulating gates.
# usage: run.sh <koan|plain>
# Artifacts land in artifacts-<arm>/ ; the control's codex runs with cwd inside the neutral
# folder (-C alone is not trusted to pin the workspace root on Windows).
set -u
cd "$(dirname "$0")"
ARM="${1:?usage: run.sh <koan|plain>}"
REPO="$(cd ../../../../.. && pwd)"
G="$PWD"
MODEL="codex-sol-high"
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
  if [ -d "$CELL/code" ]; then echo "code/ exists — move it aside first." >&2; exit 1; fi
  mkdir -p "$CELL/code"
  WORK="$CELL/code"
else
  NEUTRAL="$(dirname "$REPO")/_agent-race-neutral/staged-composite"
  [ -d "$NEUTRAL" ] && { echo "neutral folder exists — move it aside first." >&2; exit 1; }
  mkdir -p "$NEUTRAL"
  WORK="$NEUTRAL"
fi
FLAGS=(--dangerously-bypass-approvals-and-sandbox --skip-git-repo-check)

stage_run() { # $1 stage no; $2 prompt file; $3 session id or --last
  local S E
  S=$(date +%s)
  if [ "$3" = "--last" ]; then
    (cd "$WORK" && timeout 1800 codex exec resume --last "${FLAGS[@]}" --json \
      -o "$ART/lastmsg-s$1.txt" - < "$2") > "$ART/events-s$1.jsonl" 2>&1
  else
    (cd "$WORK" && timeout 1800 codex exec resume "$3" "${FLAGS[@]}" --json \
      -o "$ART/lastmsg-s$1.txt" - < "$2") > "$ART/events-s$1.jsonl" 2>&1
  fi
  E=$(date +%s)
  echo $((E-S)) > "$ART/wallclock-stage$1.txt"
}

gate() { # $1 stage no -> nonzero if failed
  bash "$G/grade-staged.sh" "$WORK" "$1" 2>&1 | tee "$ART/grade-stage$1.txt"
  local s n t
  s=$(grep -o 'SCORE [0-9]*/[0-9]*' "$ART/grade-stage$1.txt" | tail -1)
  [ -z "$s" ] && return 1
  n=${s#SCORE }; n=${n%%/*}; t=${s##*/}
  [ "$n" = "$t" ]
}

echo "=== STAGE 1 ==="
if [ "$ARM" = "koan" ]; then
  S=$(date +%s)
  (cd "$WORK" && timeout 1800 codex exec "${FLAGS[@]}" --json \
    -o "$ART/lastmsg-s1.txt" - < <(cat "$G/arm-koan.txt" "$G/stage-1.txt")) > "$ART/events-s1.jsonl" 2>&1
  E=$(date +%s); echo $((E-S)) > "$ART/wallclock-stage1.txt"
else
  S=$(date +%s)
  (cd "$WORK" && timeout 1800 codex exec "${FLAGS[@]}" --json \
    -o "$ART/lastmsg-s1.txt" - < <(cat "$G/arm-plain.txt" "$G/stage-1.txt")) > "$ART/events-s1.jsonl" 2>&1
  E=$(date +%s); echo $((E-S)) > "$ART/wallclock-stage1.txt"
fi
SID=$(grep -o '"thread_id":"[^"]*"' "$ART/events-s1.jsonl" | head -1 | cut -d'"' -f4)
[ -z "$SID" ] && SID="--last"
echo "session: $SID"
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
