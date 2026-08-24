# Sylin.Koan.AI.Eval

Measure model results, enforce metric gates, compare candidates, and detect score drift through AI adapter capabilities.

```bash
dotnet add package Sylin.Koan.AI.Eval
```

## Meaningful use

Reference the package, retain `AddKoan()`, and call the facade from a running host:

```csharp
var result = await Eval.Measure(
    new ModelRef("support-model", Version: 4),
    new DatasetRef("support-regression", Hash: datasetHash),
    [Metric.Accuracy, Metric.F1],
    cancellationToken);

Console.WriteLine(result);
```

Make a failing gate explicit:

```csharp
await Eval.Gate(
    new ModelRef("support-model", 4),
    baseline: new ModelRef("support-model", 3),
    data: new DatasetRef("support-regression", datasetHash),
    require: gate => gate.Metric(Metric.Accuracy, min: 0.90).NoRegression(0.02),
    ct: cancellationToken);
```

These calls need an adapter that computes metrics (see Guarantees). Without one they fail with a
correction naming the missing piece - they do not return zero scores.

## Guarantees and limitations

- Reference plus `AddKoan()` registers `IEvalService` automatically.
- Measurement requires an active AI adapter that declares the `MetricCompute` capability **and** implements
  `IMetricAdapter`; computation is delegated to it. An adapter declaring the capability without the interface,
  or a host with no capable adapter at all, fails with a correction naming the remedy - measurement never
  answers placeholder values.
- `Gate` throws `GateFailedException` with violations. A standalone `NoRegression` condition, or one without a
  baseline to compare against, is refused instead of passing vacuously. `Regress` returns a failed `EvalResult`;
  `Compare` ranks by average requested score; `Drift` compares shared scores already present in two results.
- No connector shipped in-tree computes metrics yet; referencing this package alone means evaluation calls fail
  correctively until a metric-capable adapter is present.
- The package does not capture prompts/responses, create datasets, train models, deploy gates, schedule monitoring, or
  claim statistical significance. Applications own dataset provenance and the decision that follows a gate.

See [TECHNICAL.md](TECHNICAL.md) for capability resolution and result semantics.
