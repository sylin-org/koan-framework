---
type: HANDOFF
domain: announcement
status: current
last_updated: 2026-08-28
framework_version: v1.0.30
---

# Announcement Initiative — current handoff

## Current state

WEB-0073 (governed PUT + the entity verb map) is **implemented, proven, committed, and published**
(Sylin.Koan.Web 1.0.30 / Sylin.Koan.Mcp 1.0.24 via the SignPath release pipeline on push to dev).
The matrix (test01 staged composite) has receipts for five harness/model rows; the skill is at
**v5** (verb surface + draft-before-verify sequencing).

- Frontier/mid verdict (measured, robust): the plain control wins every timed stage — codex-sol
  22/22 both arms (plain 2.2× faster, 4–5× cheaper in context); agy-gemini 22/22 both arms (plain
  ~8× faster); claude-default koan 22/22 ($12.37), plain blocked on the operator's monthly spend
  cap.
- Local-tier verdict: qwen35-9b (opencode) and qwen38-27b-max (codex-OSS, both tunings) **fail at
  task existence** — 0/1/0/9 at the caps. Binding constraint is budget + apply-loop friction
  (PowerShell diff parsing), not framework knowledge. `local-feed/` fixture deleted (it trapped
  the v4 attempt).
- The full story, including the concurrent-session collision and its recovery, is in
  [PROGRESS.md](PROGRESS.md).

## Next session

1. Read `evals/agent-race/matrix/MATRIX.md`, the scoreboard + verdicts in `LADDER.md`, then
   [PROGRESS.md](PROGRESS.md).
2. Priority experiments: (a) **local-tier second lap** — operator decision between a 90–120-min
   tier cap (success-rate only), an apply-retry/auto-continue harness loop, or an S1-lite local
   task; (b) **MCP-enforcement column** — task, README, and the two-session adversarial grader
   are ready in `matrix/tasks/mcp-enforcement/`; materialize its runner (port 5097) and execute;
   (c) **claude-plain** after the spend-cap raise; (d) **A02** — ≥5 runs per arm on headline
   cells, re-baselined under skill v5.
3. Then: the crossover graph (`coalesce` from `results.jsonl`), and the announcement written from
   the chart — wins and losses both.

## Standing rules

- Identical task bodies across arms; only the arm line differs. Unattended-run sentence mandatory
  in every prompt.
- Skill is at **v5**; any skill edit is a new treatment version and re-baselines headline cells.
- No public claim from any cell with n<5; verify means full unfiltered suites + solution-wide
  build; every `MakeGenericType` on a changed contract gets swept repo-wide (the MCP translator
  arity break was found exactly there).

## Validation

- Framework change (WEB-0073): build 0 errors; PatchOps 14/14; Web AdapterSurface InMemory 81/81
  and Sqlite 59/59 full suites (incl. six new verb-surface specs); Mcp conformance 84/84 +
  Operations 5/5 (the MCP translator arity regression was found and fixed in this pass).
- Two pre-existing docs-lint errors await launch hygiene: README's missing `1.0` public-experience
  anchor, and the `embedded-analytics-duckdb` promoted claim resolving to zero capability homes.
