# Agent × framework matrix — spec (v0)

The full experiment: every major agent harness/model tier × both arms × the test battery,
coalesced into one graph. This file is the frozen spec; cells fill incrementally.

## Cells

Cell = (harness, model, arm, test). Arms: `koan` (skill pointer, byte-identical stage bodies) and
`plain` (no Sylin.*). Tests: `staged-composite` (9→16→22 checks), `mcp-enforcement`,
`media-lifecycle`, `honesty-probe` (the latter three specified in their own folders when built).

| Harness | Version | Models | Status |
|---|---|---|---|
| codex-cli | 0.150.0 | `gpt-5.6-sol` @ high (workspace default); lower OpenAI tiers TBD | running |
| Claude Code | 2.1.250 | account default (unpinned for smoke cells); tier ladder TBD | starting |
| Gemini CLI | 0.57.0 | — | **blocked**: `IneligibleTierError` — Google retired the CLI for individual accounts. |
| Antigravity | 1.107.0 | Gemini tiers | **GUI-bound**: `antigravity chat` drives an IDE window — no headless transcript/JSON. Cells need either a Google API key in opencode (preferred) or future headless support. Operator decision. |
| opencode | 1.18.21 | local models via its provider config | candidate for the ≤12GB tier |

Local tier (≤12GB VRAM), via codex OSS provider config or opencode: qwen3.5-9b, llama-4-small
class, gemma-4-small class, mistral-small class — pinned at execution.

## Metrics per stage (all cells)

- battery pass rate (the gate), wall clock, tokens (input/cached/output), and test-specific
  defect audits (orphans, enforcement leaks, hallucinated identifiers, honest refusals).
- For low tiers, **pass rate is the primary axis**; time is secondary. Hypothesis under test:
  the framework's advantage grows as model capability falls (crossover curve per pillar).

## n-policy

- Headline cells (crossover tiers, MCP enforcement, Sol-High anchor): n≥5 per arm.
- Long-tail cells: n=1–2, drawn in the graph with uncertainty, labeled exploratory.
- No public claim from any cell with n<5. Koan treatment pinned at skill v4; a skill change
  re-baselines every headline cell.

## Layout

```
matrix/
  MATRIX.md  results.jsonl  .gitignore
  tasks/staged-composite/     prompts (byte-identical across arms/harnesses), graders, runners
  cells/<test>/<model-harness>/<arm>/
      results.md              committed run record (the receipt)
      transcripts/            gitignored: events jsonl, grades, wallclocks
      code/                   gitignored: the agent's project
  archive/                    gitignored: discarded attempts and superseded scenarios
```

- Cells may run in parallel lanes; each lane exports `GRADE_PORT` (grader default 5099) to avoid
  app-port collisions. Wall clock under parallel load is noise — pass rate, tokens, cost, and
  defect audits are the primary axes; headline timing cells re-run on a quiet machine.
- Arms compared are those launched as a concurrent pair (same load conditions).

## Results

One JSONL row per stage in `results.jsonl` (schema: cell id, harness+version, model, arm, test,
stage, wallclock_s, tokens, checks passed/total, defect counts, record path). `coalesce.ps1`
(builds the graph) reads only that file.
