# Run record — staged composite, Koan arm (attempt 1, clean)

- Date: 2026-08-28 (run window crossed midnight UTC)
- Agent: codex-cli 0.150.0, `gpt-5.6-sol` @ `high`, unattended; one session across all three
  stages (thread `01a045c3-b662-7760-b8cc-dadf187e9fa2`, resumed per stage)
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4; stage bodies byte-identical
  to the control arm's
- Project: `project/`

## Results — all stages passed

| Stage | Battery | Wall clock | Cumulative session tokens (last event of invocation) |
|---|---|---|---|
| 1 — CRUD + health + persistence | 9/9 | 787 s | input 3,028,382 · output 15,087 |
| 2 — query every field | 16/16 | 501 s | input 4,596,893 · output 12,294 |
| 3 — semantic search (Ollama) | 22/22 | 471 s | input 4,058,955 · output 10,993 |

Stage-3 probe detail: all three keyword-disjoint probes passed both disjointness and rank
(target in top 3): "fancy french dinner for guests" → Coq au Vin; "my kid refuses vegetables" →
Hidden Veg Pasta Sauce; "warming breakfast on a cold morning" → Overnight Oats. Total wall clock
≈ 29.3 minutes across stages, inside the 30-min-per-stage cap throughout.

Token caveat: per-invocation cumulative counters are reported as recorded; the stage-3 figure
reads lower than stage-2's due to event ordering in the transcript. Deltas should be recomputed
from the JSONL events during A02 aggregation, not from these tails.

## Marginal-cost story (single run, n=1)

Adding query-every-field cost the agent ~8 minutes; adding true semantic search cost ~8 minutes —
each while keeping the entire previous battery green. The rework signal (edits to pre-existing
code) is measurable from the transcripts and will be extracted during A02.

## Run-history note

Two earlier attempts were discarded before this canonical run, both due to harness defects (a
grader `set -u` crash, then a broken session handoff via `resume --last` picking a ghost thread
from a double-backgrounded launch); artifacts preserved under `attempts/staged-koan-graderbug/`
and `attempts/staged-koan-sessionbug/`. No Koan-arm result in this file is affected by them: this
attempt ran in a clean environment with explicit thread resumption.
