#!/usr/bin/env bash
# S01 Koan arm — unattended codex run in the isolated project folder, then grade.
set -u
cd "$(dirname "$0")"
if [ -d project ]; then echo "project/ already exists — refusing to overwrite a run. Move it aside first." >&2; exit 1; fi
mkdir -p project
REPO="$(cd ../../../../&& pwd)"

START=$(date +%s)
timeout 1800 codex exec \
  --dangerously-bypass-approvals-and-sandbox \
  --skip-git-repo-check \
  -C "$(cygpath -w "$PWD/project")" \
  --json -o last-message.txt - \
  < prompt-koan.txt > codex-events.jsonl 2>&1
EXIT=$?
END=$(date +%s)
echo $((END-START)) > wallclock-seconds.txt
echo "$EXIT" > codex-exit-code.txt

bash "$REPO/evals/agent-race/graders/grade-s01.sh" project 2>&1 | tee grade-output.txt
echo "wallclock: $((END-START))s  codex-exit: $EXIT"
