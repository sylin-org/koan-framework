# Results — test01 staged composite · claude-default · plain arm

- Harness: Claude Code 2.1.250, account-default model, unattended; one session resumed per stage
- Outcome: **22/22** — stage 1: 9/9 (232 s), stage 2: 16/16 (250 s), stage 3: 22/22 (727 s);
  ≈20.2 min total; harness-reported cost **$6.21** ($1.42 / $1.19 / $3.60)

## Pair verdict (claude-default, single runs)

| Arm | Battery | Wall | Cost |
|---|---|---|---|
| koan (skill v5) | 22/22 | 1,854 s | $12.37 |
| plain | 22/22 | 1,209 s | $6.21 |

Both arms passed everything including the keyword-disjoint semantic probes. Plain ~1.5× faster and
~half the cost on this tier — consistent with the codex-sol-high pair's direction. The claude pair
is complete and is the strongest current candidate for A02's n≥5 repeats (both arms demonstrated,
harness cost is directly measurable, and the semantic probes are the repeated-interest checks).

Transcripts: `transcripts/events-s{1,2,3}.jsonl`.
