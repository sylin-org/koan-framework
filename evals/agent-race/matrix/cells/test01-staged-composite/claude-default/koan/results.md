# Results — test01 staged composite · claude-default · koan arm

- Harness: Claude Code 2.1.250, account-default model (unpinned for smoke cell), unattended `-p`
- Session: `16e07522-6379-4bae-8efa-e553d72f4412` (resumed per stage)
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4; stage bodies byte-identical
  across arms and harnesses

| Stage | Battery | Wall clock | Cost (harness-reported) |
|---|---|---|---|
| 1 — CRUD + health + persistence | 9/9 | 629 s | $3.26 |
| 2 — query every field | 16/16 | 738 s | $3.61 |
| 3 — semantic search | 22/22 | 487 s | $5.50 |

**Cell complete: 22/22 cumulative, ≈30.6 min, ≈$12.37 total.**

## Notes

- Stage-3's first resume died in 2 s (session `init` only, no work — a resume flake); the retry
  on the same session passed the full accumulated battery including all three keyword-disjoint
  semantic probes. The 2 s failure is recorded as a harness reliability datapoint, not an agent
  result.
- The stage-1 runner stalled after the agent completed its turn (harness defect, worked around by
  driving gates manually with identical commands); fixed invocation lives in
  `tasks/staged-composite/run-claude.sh`.
- Cost is harness-reported and directly comparable per cell — a metric codex does not expose.
- Comparison context: codex-sol-high koan cell passed 22/22 in ≈29.3 min; codex plain cell in
  ≈13.4 min. Claude-default (Opus-class, unpinned) is the first cross-harness Koan cell; its
  plain pair is queued.
