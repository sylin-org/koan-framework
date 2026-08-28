#!/usr/bin/env bash
# S01 control arm — codex runs OUTSIDE the repo so its AGENTS.md cannot leak, then artifacts are
# copied back and the same grader is applied.
set -u
cd "$(dirname "$0")"
REPO="$(cd ../../../../&& pwd)"
NEUTRAL="$(dirname "$REPO")/_agent-race-neutral/scenario01"
if [ -d "$NEUTRAL" ]; then echo "neutral folder already exists — refusing to overwrite a run." >&2; exit 1; fi
mkdir -p "$NEUTRAL"

START=$(date +%s)
timeout 1800 codex exec \
  --dangerously-bypass-approvals-and-sandbox \
  --skip-git-repo-check \
  -C "$(cygpath -w "$NEUTRAL")" \
  --json -o "$NEUTRAL/last-message.txt" - \
  < prompt-plain.txt > "$NEUTRAL/codex-events.jsonl" 2>&1
EXIT=$?
END=$(date +%s)
echo $((END-START)) > wallclock-seconds.txt
echo "$EXIT" > codex-exit-code.txt

bash "$REPO/evals/agent-race/graders/grade-s01.sh" "$NEUTRAL" 2>&1 | tee grade-output.txt

# pull artifacts back for the record (snapshot the whole neutral folder; the project may sit at
# the root rather than in a project/ subfolder)
cp "$NEUTRAL/codex-events.jsonl" "$NEUTRAL/last-message.txt" "$NEUTRAL/ABORTED.md" . 2>/dev/null
mkdir -p project-snapshot
cp -r "$NEUTRAL/." ./project-snapshot/ 2>/dev/null || true
rm -rf "$NEUTRAL"
echo "wallclock: $((END-START))s  codex-exit: $EXIT"
