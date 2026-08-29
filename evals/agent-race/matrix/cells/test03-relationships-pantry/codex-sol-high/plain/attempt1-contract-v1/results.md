# Results — test03 relationships-pantry · codex-sol-high · plain arm (attempt 1)

- Harness: codex-cli 0.150.0, `gpt-5.6-sol` @ high, unattended; treatment: plain arm line
- Outcome: **8/12** — failed: create-join, pantry-full-match-first, pantry-rank-last
  (missing parsed null), semantic-probe-2; passed: build, start, seed, usage-count, conversion
  filter, stat-over10, restart-persistence, semantic-probe-1

## Reading

Same failure signature as the koan arm on create-with-embedded-lines and the pantry-match shape —
across two independent implementations, which redirects suspicion to the **task contract's
create-shape clarity and the grader's match-response parsing** (see the koan arm receipt).
Notably the control's conversion filter, usage count, and stat all passed, and semantic-probe-1
passed while probe-2 missed — the one candidate genuine quality difference, re-checked after the
grader fix.

Status: attempt not counted as a model verdict; grader/contract investigation queued before
re-grade. Transcripts: `transcripts/events-s1.jsonl`.
