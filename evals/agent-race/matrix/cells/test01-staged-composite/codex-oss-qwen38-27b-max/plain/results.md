# Results — test01 staged composite · codex-oss-qwen38-27b-max · plain arm (skill v5)

- Harness: codex-cli 0.150.0 over Ollama (`qwen38-27b-q4-max`, 100% GPU); cap 45 min/stage
- Treatment: plain arm line (no `Sylin.*`); stage bodies byte-identical to the koan arm's

| Stage | Battery | Wall clock | Outcome |
|---|---|---|---|
| 1 | 0/1 (no csproj produced; code/ empty) | 2701 s (cap hit) | active writing, patches failing on shell parsing |

## Findings

- The plain arm **was writing** — its transcript ends mid-apply-patch authoring SQL DDL
  (`CREATE TABLE IF NOT EXISTS recipes …`) that repeatedly failed with a PowerShell parser error
  (`Missing expression after unary operator '+'` — diff-formatted lines pasted into the shell).
  The failure is **codex-on-Windows tooling friction under a 27B local model**, not task
  misunderstanding: the agent's approach (hand-rolled SQLite DDL + minimal API) was sound.
- Pair verdict for the v5 A/B at the local tier: **neither arm completes stage 1 within 45
  minutes** — koan research-bound, plain tooling-bound. The tier's binding constraint is the
  budget/apply-loop, not framework knowledge.
- Next levers, in order: (1) tier cap 90–120 min recorded as a harness parameter for
  success-rate-only comparisons; (2) an apply-retry/auto-continue harness loop; (3) a smaller
  stage-1 task for the local tier (S1-lite) — a benchmark design change, to be decided by the
  operator, not silently substituted.
