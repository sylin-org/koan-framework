# Run record — staged composite, control arm (attempt 1; first valid paired run)

- Date: 2026-08-28
- Agent: codex-cli 0.150.0, `gpt-5.6-sol` @ `high`, unattended; one session (thread in
  `artifacts-plain/events-s1.jsonl`), ran inside the neutral folder outside the framework repo
- Prompt: arm line (plain ASP.NET Core, no `Sylin.*`) + byte-identical stage bodies

## Results — all stages passed, faster than the Koan arm at every stage

| Stage | Battery | Wall clock | Cumulative tokens (last event) |
|---|---|---|---|
| 1 — CRUD + health + persistence | 9/9 | 226 s | input 381,527 · output 6,887 |
| 2 — query every field | 16/16 | 246 s | input 553,887 · output 8,168 |
| 3 — semantic search (hand-rolled Ollama + ranking) | 22/22 | 330 s | input 849,365 · output 9,273 |

Total ≈ 13.4 min vs the Koan arm's ≈ 29.3 min; cumulative input tokens ≈ 0.85 M vs ≈ 3–4.6 M.
All three keyword-disjoint semantic probes passed: the control's hand-rolled Ollama integration
and ranking is fully competitive on this model tier.

## Finding (honest, uncomfortable, load-bearing)

**The crossover hypothesis failed on a frontier model.** Stage 3 assumed the control would have
to hand-roll what it "does not know cold" — gpt-5.6-sol knows Ollama's API and vector ranking
well enough to add semantic search in 5.5 minutes. Combined with the Koan arm's ecosystem-reading
and self-verification overhead, plain ASP.NET Core won every stage. The naive "Koan is orders of
magnitude faster for agents" claim is measured and rejected for this model/task pair — which is
precisely why the receipt exists before the announcement does.

## Discarded prior attempt

A first control attempt aborted at its stage-1 gate when codex resolved its workspace into the
shared repo folder despite `-C` (built in `staged-composite/project`; archived at
`attempts/control-stray-stage1/`). The runner now invokes codex with cwd inside the neutral
folder and keeps per-arm artifact directories. Its partially-overwritten transcript was discarded.

## Next question this run poses

Frontier models know the plain stack cold; the open question is whether Koan's advantage appears
at lower model tiers (the models users actually run locally for cost). Same composite, smaller
model, both arms — that experiment decides whether charter claim 3 stands, narrows, or retires.
