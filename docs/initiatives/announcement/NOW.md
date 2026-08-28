---
type: HANDOFF
domain: announcement
status: current
last_updated: 2026-08-27
framework_version: v1.0.12
---

# Announcement Initiative — current handoff

## Current state

A01's harness is built and its smoke acceptance is met: scenario S01 has one canonical run per arm
recorded with transcripts, wall clock, tokens, and 7/7 grader verdicts under
`evals/agent-race/`.

- Koan arm 321 s / 7/7; control arm 203 s / 7/7 — the control is faster on plain CRUD, so the
  campaign's crossover hypothesis now rests on S02–S06. Do not publish any S01-only claim.
- The unattended-run contract sentence is mandatory in every future prompt (the global `explore`
  skill stalls unattended sessions that lack it).
- The Koan-arm treatment is frozen as prompt v3: name Koan + point at
  `.agents/skills/koan/SKILL.md` ("read it and follow it"). Do not widen the pointer back to the
  repo root, and do not install the skill globally (control contamination). The skill itself is
  at **v4** (greenfield one-block opening); any further skill edit is a new treatment version and
  re-runs the paired S01 before more ladder data is collected.
- A02's full execution (≥5 runs per arm per scenario) has not started.

## Next session

1. Read `evals/agent-race/matrix/MATRIX.md` and the scoreboard in `LADDER.md`, then this ledger:
   [PROGRESS.md](PROGRESS.md).
2. **test01 (staged composite) cell state**: complete pairs for codex-sol-high (both 22/22;
   plain ~2.2× faster, ~4–5× cheaper) and agy-gemini (both 22/22; plain ~8× faster, koan S1
   cap-hit during skill reading); opencode-qwen35-9b pair both **0/9 — task-existence failure**
   at the local 9B tier (tool-engagement caveat recorded). claude-default koan 22/22 ($12.37);
   **claude-plain blocked on the operator's monthly spend cap** — rerun after the raise.
3. Priority experiments, in order: (a) the local-tier verdict is now **model-loop sustainment**:
   the codex↔Ollama pipe works (`wire_api="responses"`, per-run overrides only, global config
   untouched) but qwen38-27b ended both attempts mid-research without writing files — probe the
   `max` tuning or another local family next; the `local-feed/` fixture was deleted on operator
   order after attempt 1's confound (it was untracked and unreferenced); (b) A02 execution — ≥5
   runs per arm on the headline cells (codex-sol-high pair, claude pair); (c) then the
   MCP-enforcement and media-lifecycle tests as the next columns of the matrix.
4. Standing rules: identical prompts across arms; unattended sentence mandatory; skill pinned at
   v4 (a skill change re-baselines headline cells); no public claim from any cell with n<5.

## Validation

- Docs-only change so far; no framework code touched.
- If promoted public documents are edited along the way, run `pwsh scripts/public-docs-lint.ps1`
  before commit.
