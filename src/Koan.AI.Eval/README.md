# Koan.AI.Eval

Model evaluation, quality gates, regression detection, and distribution drift monitoring for Koan. Plug in metrics adapters and gate deployments automatically.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Eval
```

## Quick Start

```csharp
// Measure quality of a model response
var result = await Eval.Measure("llama3", new[]
{
    new EvalCase { Input = "What is 2+2?", Expected = "4", Actual = await Client.ChatAsync("What is 2+2?", ct) }
}, metrics: [Metric.Accuracy, Metric.Faithfulness]);

Console.WriteLine($"Accuracy: {result.Scores[Metric.Accuracy]:P1}");
Console.WriteLine($"Pass: {result.Passed}");
```

## Core API

```csharp
Eval.Measure(model, cases, metrics)      // → EvalResult   — run metrics on output samples
Eval.Gate(model, cases)                  // → GateBuilder   — define pass/fail conditions
Eval.Compare(modelA, modelB, cases)      // → ComparisonResult — A/B metric comparison
Eval.Regress(model, baseline, cases)     // → bool           — true if regression detected
Eval.Drift(currentMetrics, baseline)     // → DriftResult    — distribution drift analysis
Eval.Benchmark(model, suite)             // → BenchmarkResult — standard benchmark run
```

## Quality Gates

Gates block deployment when conditions fail:

```csharp
var gate = await Eval.Gate("llama3", testCases)
    .Metric(Metric.RougeL,    min: 0.85)
    .Metric(Metric.Accuracy,  min: 0.90)
    .NoRegression(baselineResult)
    .EvaluateAsync(ct);

// Throws GateFailedException if any condition fails
// gate.Violations — list of GateViolation for failed conditions
```

## Well-Known Metrics (`Metric` constants)

| Constant | What it measures |
|----------|-----------------|
| `Metric.RougeL` | Longest common subsequence overlap |
| `Metric.Bleu` | N-gram precision (translation quality) |
| `Metric.F1` | Token-level F1 |
| `Metric.Accuracy` | Exact-match accuracy |
| `Metric.Faithfulness` | Grounded response vs. context |

## Drift Detection

```csharp
var drift = await Eval.Drift(currentMetrics, baselineMetrics);
// DriftStatus: OK | Notice | Warning
// drift.Shifts — metric deltas that moved significantly
// drift.Recommendation — action string
```

## Reference

- **ADR**: `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md`
- **Related**: `Koan.AI.Review` (HITL gates), `Koan.AI.Models` (deployment lifecycle)
