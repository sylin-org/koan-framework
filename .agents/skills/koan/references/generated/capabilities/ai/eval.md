---
type: REFERENCE
domain: ai
title: "Evaluation gates and drift"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/ai/eval.md - cold-executed on the Ollama path against published packages
    (feed probe): measurement without a metric-capable adapter refused correctively through a real
    host for both Measure and Gate, drift math (status bands, shift listing) verified over results.
    Metric delegation via IMetricAdapter and the vacuous-gate refusals verified against a source pin -
    packages published before it predate that seam (pending next package release).
---

# Evaluation gates and drift

Measure model quality, enforce quality gates before a candidate ships, and detect score drift between
results - through one static facade backed by metric-capable adapters.

## You need

| Piece | Package | Note |
|---|---|---|
| Facade, gates, drift math | `Sylin.Koan.AI.Eval` | registers `IEvalService` through `AddKoan()` |
| A metric-computing adapter | one implementing `IMetricAdapter` | **none ships in-tree today** - see the correction box |

Verified against: `Sylin.Koan.AI.Eval` 1.0.6 or newer (patch releases compatible).

> **Measurement never answers placeholders.** Every measurement resolves an adapter that declares the
> `MetricCompute` capability *and* implements `IMetricAdapter`; with none present, `Measure`, `Gate`,
> `Compare`, and `Regress` fail correctively naming exactly that - they do not return zero scores.
> Drift is different: it compares two results you already hold, so it works with no adapters at all.

## The constraint box

> Two gate shapes are refused instead of passing vacuously:
>
> - a standalone `NoRegression(...)` with no `Metric(...)` condition - it would measure nothing;
> - any `NoRegression(...)` without a baseline model - there would be nothing to compare against.
>
> Both throw at call time explaining which half is missing. A passing gate has always measured real
> values.

## Assembly

```csharp
using Koan.AI.Contracts.Shared;
using Koan.AI.Eval;
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
```

From a running host:

```csharp
using Koan.AI.Contracts.Shared;
using Koan.AI.Eval;

// Compare two held-back results - works everywhere, no adapters involved.
// Results are built from EvalScore(metric, value [, baseline]) records:
var baselineResult = new EvalResult(
    new ModelRef("support-model", Version: 3),
    [new EvalScore(Metric.Accuracy, 0.95), new EvalScore(Metric.F1, 0.90)],
    Passed: true);
var currentResult = new EvalResult(
    new ModelRef("support-model", Version: 4),
    [new EvalScore(Metric.Accuracy, 0.80), new EvalScore(Metric.F1, 0.72)],
    Passed: true);

var drift = await Eval.Drift(baselineResult, currentResult);
Console.WriteLine($"{drift.Status}: {drift.Score:F3}");   // Status: OK | Notice | Warning
foreach (var shift in drift.TopShifts)                    // per-metric lines for movement above 0.05
    Console.WriteLine(shift);
Console.WriteLine(drift.Recommendation);                  // non-null when drift warrants action

// Measurement and gates need a metric-capable adapter (see correction box):
var datasetHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("support-regression.json")));
var result = await Eval.Measure(
    new ModelRef("support-model", Version: 4),
    new DatasetRef("support-regression", Hash: datasetHash),
    [Metric.Accuracy, Metric.F1]);

await Eval.Gate(
    new ModelRef("support-model", 4),
    baseline: new ModelRef("support-model", 3),
    data: new DatasetRef("support-regression"),
    require: gate => gate.Metric(Metric.Accuracy, min: 0.90).NoRegression(0.02));
```

A failing gate throws `GateFailedException` carrying typed violations (`Metric`, `Actual`,
`Required`, `BelowMinimum` / `AboveMaximum` / `Regression`). `Regress` is the non-throwing form: it
returns `Passed: false` with the violations as scores. `Compare` ranks models by average requested
score.

## Correction box

- No capable adapter anywhere: "No adapter with MetricCompute capability registered. Add an adapter
  that declares AiCapability.MetricCompute to enable evaluation."
- An adapter declaring the capability without implementing `IMetricAdapter`: names the adapter and the
  missing interface - capability is structural, flags alone are lies.
- Vacuous gate shapes: "A NoRegression gate requires at least one Metric(...) condition..." /
  "...requires a baseline model...".

## Do not, at this level

- Do not treat `DriftStatus.OK` as model health - it compares the two results you handed it.
- Do not build datasets through this package - dataset storage and provenance are yours; refs are identities.
- Do not swallow `GateFailedException` to let a build pass; the violation list is the deliverable.

## Leaves

- **Deep contract:** [AI.Eval TECHNICAL](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.AI.Eval/TECHNICAL.md)
