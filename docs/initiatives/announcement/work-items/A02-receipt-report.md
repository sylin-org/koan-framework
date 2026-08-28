---
type: GUIDE
domain: framework
title: "A02 - Benchmark execution and receipt report"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: benchmark execution work-item specification
---

# A02 — Benchmark execution and receipt report

- Tranche: `T0 — Receipts`
- Status: `draft`
- Depends on: A01
- Unlocks: A04 (copy drafting finalization), A09; gates publication of A03–A05
- Owner: maintainer

## Meaningful outcome

`evals/agent-race/REPORT.md` exists: at least five grader-judged runs per arm, medians for tokens,
turns, wall-clock, and lines of code touched, pass rates, and a named threats-to-validity section.
The measured number — whatever it is — becomes the only magnitude claim the initiative is allowed
to publish.

## Why now

This is the initiative's receipt. If the measured gap is large, the launch has an unattackable
headline; if it is small, the copy must lead with claims 1 and 2 (composition delight, one model
every surface) and drop magnitude language entirely. Either way the campaign copy is written from
this report, not from hope.

## Execution

- Run per A01's fairness rules: ≥5 runs per arm, fresh sessions, published transcripts.
- Do not iterate on prompts between runs; if a prompt defect invalidates the batch, discard the
  whole batch and record why.
- A failed control-arm run is data, not an accident: record retries-with-error counts as a metric.

## Report shape

- Headline table: median tokens, turns, minutes, LOC, grader pass rate — both arms.
- Per-run appendix with transcript references.
- Threats to validity: what could not be controlled (agent version drift, NuGet restore caching,
  Ollama cold start), each with its mitigation or honest disclosure.
- Reproduction instructions verbatim from A01's README.

## Evidence to read first

- [`../ACCEPTANCE.md`](../ACCEPTANCE.md) — §0 claim provenance and §2 reproducibility.
- [`../work-items/A01-agent-race-benchmark.md`](A01-agent-race-benchmark.md) — fairness rules.

## Acceptance criteria

- [ ] ≥5 valid runs per arm recorded with transcripts and grader output.
- [ ] `REPORT.md` contains medians only as summary statistics; no superlatives.
- [ ] Reproduction instructions executed once from a fresh checkout on the recording machine.
- [ ] The publication gate flips: `PROGRESS.md` records that T1 artifacts may publish.

## Proof

`REPORT.md` plus `runs/` records; linked from `PROGRESS.md` as the initiative's receipt.
