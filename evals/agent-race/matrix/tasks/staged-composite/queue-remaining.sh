#!/usr/bin/env bash
# Sequential queue for remaining staged-composite matrix cells (same-machine fairness:
# cells never run concurrently). usage: queue-remaining.sh  (run after claude-koan completes)
set -u
cd "$(dirname "$0")"
echo "QUEUE START $(date)"
bash run-claude.sh plain;  echo "cell claude-plain  exit=$?"
bash run-agy.sh koan;      echo "cell agy-koan      exit=$?"
bash run-agy.sh plain;     echo "cell agy-plain     exit=$?"
echo "QUEUE DONE $(date)"
