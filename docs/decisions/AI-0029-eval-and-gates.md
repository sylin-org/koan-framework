---
id: AI-0029
slug: AI-0029-eval-and-gates
domain: AI
status: Accepted
date: 2026-03-20
implementation: "Implemented in src/Koan.AI.Eval/ with Eval.* facade"
---

# ADR: Eval — Model Evaluation, Quality Gates, and Drift Detection

**Contract**

- **Inputs:** Trained models (`ModelRef` from AI-0023), evaluation datasets (`Dataset.From<T>()` from AI-0028), metric specifications (`Metric.*` constants), baseline references (model version or explicit scores), judge model references (for LLM-as-evaluator), benchmark suite identifiers, gate requirement functions.
- **Outputs:** `EvalResult` (shared boundary model from AI-0022) containing per-metric scores, pass/fail determination, and diagnostic reason; `ComparisonResult` with ranked model table; `DriftResult` with distribution shift score, status, and top shifts; `GateFailedException` with full diagnostic when quality conditions are not met; `BenchmarkResult` with standard suite scores.
- **Error Modes:** Missing metric implementation degrades to `MetricNotAvailableException` with installation guidance (e.g., "BERTScore requires an adapter with AiCapability.MetricCompute — add Koan.AI.Connector.PythonSidecar or ensure a remote Worker has MetricCompute capability"). Dataset yielding zero examples returns diagnostic `EvalResult` with `Passed = false` and reason "Dataset contains 0 examples, minimum 1 required for evaluation". Judge model unreachable falls back to non-judge metrics with warning; if all requested metrics require a judge, throws `JudgeUnavailableException`. Baseline model not found in catalog throws `BaselineNotFoundException` with the model ID and available versions. Gate failure is **not an error** — it is the expected output of a functioning quality gate. Compute unavailable for metric computation (e.g., BERTScore needs GPU) degrades to CPU with performance warning, same as AI-0024 pattern.
- **Acceptance Criteria:** A developer can `Dataset.From<SupportTicket>(where: t => t.IsGoldenTestCase, input: t => t.Question, expected: t => t.GoldAnswer)`, run `Eval.Measure()` with text and RAG metrics, `Eval.Gate()` against a baseline with threshold requirements, `Eval.Drift()` between training-era and current-era entity queries, and `Eval.Compare()` across model versions — all with entity-native data, full lineage recording, and boot report integration.

**Edge Cases**

- `Eval.Gate()` with no baseline specified: Evaluates absolute thresholds only; `NoRegression()` clause without a baseline throws `ArgumentException("NoRegression requires a baseline model or explicit baseline scores")`.
- `Eval.Measure()` with a metric requiring expected outputs but dataset has none: Throws `DatasetSchemaException("Metric 'rouge_l' requires expected outputs but dataset provides input-only mapping")`.
- `Eval.Drift()` where baseline and current have zero overlap in embedding space: Returns `DriftScore = 1.0` with `Status = Critical` and recommendation "Distributions are entirely disjoint — input domain has changed fundamentally".
- `Eval.Compare()` with a single model: Degrades to `Eval.Measure()` semantics — returns results without ranking.
- `Eval.Judge()` where the judge model is weaker than the evaluated model: Emits a warning via boot report ("Judge model 'llama3.1:8b' has fewer parameters than evaluated model 'llama3.1:70b' — results may be unreliable") but proceeds. The developer decides.
- `Eval.Gate()` where no `MetricCompute`-capable adapter is available: Metrics computable in .NET (Accuracy, F1, Precision, Recall, Latency, ErrorRate) proceed. Metrics requiring `AiCapability.MetricCompute` (RougeL, BLEU, BERTScore) fail with `MetricNotAvailableException`. Gate fails if any required metric is unavailable — failing safe is the correct behavior for a quality gate.
- `Eval.Benchmark()` with a model format incompatible with lm-eval-harness: Returns `BenchmarkResult` with `Status = Unsupported` and guidance ("Model format GGUF requires conversion to SafeTensors or ONNX for benchmark evaluation. Use `Model.Convert()` first.").
- Concurrent `Eval.Gate()` calls for the same model: Each evaluation is independent and idempotent. No locking. Results are recorded independently in lineage — the most recent passing gate is authoritative.

## Context

Koan's AI pillar (AI-0001 through AI-0028) provides inference (`Client.*`), model lifecycle (`Model.*`), entity-native datasets (`Dataset.From<T>()`), and training (`Training.Train()`). A developer can train a model on production entity data and deploy it to a runtime — but nothing prevents a **bad** model from reaching production.

This is the gap `Eval.*` fills. Three problems exist today:

**1. No automated quality enforcement.** When `Training.Train()` completes, the resulting `ModelRef` can be passed directly to `Model.Deploy()`. There is no programmatic checkpoint that asks "is this model better than what's running?" The human must manually evaluate and decide. For continuous improvement loops (AI-0022, Part 14: The Closed Loop), manual evaluation is a bottleneck that breaks automation.

**2. No regression detection.** Model version v5 might score 0.92 on RougeL but regress on Faithfulness from 0.94 to 0.88. Without systematic comparison against the current production baseline, regressions in non-primary metrics go undetected until users notice degraded quality. This is the most common failure mode in production ML — the headline metric improves while secondary metrics silently decay.

**3. No drift monitoring.** The input distribution that a model was trained on changes over time. New product categories appear. Customer language shifts. Seasonal patterns emerge. Without drift detection, the model serves increasingly stale representations of the input space. By the time quality visibly degrades, the drift has compounded and retraining is urgent rather than planned.

### Why Entity-Native Evaluation Matters

Traditional ML evaluation requires a separate "golden test set" maintained outside the application. This creates a maintenance burden: the test set drifts from production reality, schema changes break export pipelines, and the test data lives in a different system than the production data.

Koan's `Dataset.From<T>()` (AI-0028) eliminates this. The same `Entity<T>` that stores production data, captures user feedback (AI-0030), and feeds training also provides evaluation data. When a domain expert marks a `SupportTicket` as a golden test case, that ticket is immediately available to `Eval.Measure()` without export, transformation, or synchronization.

This has a second-order benefit for drift detection. Because both the training-era baseline and the current production data are queryable through the same `Entity<T>`, `Eval.Drift()` can compare distributions without maintaining separate snapshots. The entity's `CreatedAt`, `UpdatedAt`, or any domain-specific timestamp becomes the temporal partition.

### Why Gate is the Central Verb

`Eval.Gate()` is the verb that transforms evaluation from an informational activity into an enforcement mechanism. Without Gate, evaluation is advisory — a developer runs `Eval.Measure()`, looks at scores, and decides. With Gate, evaluation is structural — the pipeline throws `GateFailedException` if conditions aren't met, and the model cannot proceed to deployment.

This is deliberately modeled as an exception rather than a boolean return. A gate failure is not a "false" result to be checked — it is a hard stop that must be explicitly handled. The exception carries full diagnostics: which metrics failed, by how much, the model and baseline details, the dataset used, and whether the failure was absolute (below threshold) or relative (regression from baseline).

Gate integrates with Pipeline (AI-0022, Part 14) as the control point between training and deployment stages. It also integrates with Model lineage (AI-0023) — when a gate passes, the `EvalResult` is recorded in the model's `Lineage`, creating an auditable quality history.

## Decision

### Part 1: Metric Taxonomy

Metrics are organized as string constants in a static `Metric` class, not an enum. This allows framework-provided metrics and user-defined metrics to coexist without extension methods or casting.

```csharp
public static class Metric
{
    // ── Text Quality ──
    public const string RougeL       = "rouge_l";
    public const string Bleu         = "bleu";
    public const string BERTScore    = "bert_score";

    // ── Faithfulness / RAG ──
    public const string Faithfulness     = "faithfulness";
    public const string ContextRelevancy = "context_relevancy";
    public const string AnswerRelevancy  = "answer_relevancy";
    public const string ContextRecall    = "context_recall";

    // ── Classification ──
    public const string Accuracy  = "accuracy";
    public const string F1        = "f1";
    public const string Precision = "precision";
    public const string Recall    = "recall";

    // ── Retrieval ──
    public const string RecallAtK = "recall_at_k";
    public const string NDCG      = "ndcg";
    public const string MRR       = "mrr";

    // ── Operational ──
    public const string Latency      = "latency";
    public const string CostPerQuery = "cost_per_query";
    public const string ErrorRate    = "error_rate";

    // ── Generative ──
    public const string Perplexity = "perplexity";
    public const string Coherence  = "coherence";
    public const string Toxicity   = "toxicity";
}
```

**Rationale for `const string` over `enum`:**

- User-defined metrics (e.g., `"domain_accuracy"`, `"brand_voice_score"`) are first-class without framework changes.
- `EvalScore.Metric` is already `string` in the shared boundary model (AI-0022). An enum would require serialization mapping.
- Metric implementations are resolved via adapter capabilities (`AiCapability.MetricCompute` for text metrics, `AiCapability.Chat` for LLM-as-judge) or registered as built-in .NET implementations via DI. New metrics are new implementations, not new cases.

**Metric categories by computation strategy:**

| Category | Computation | Runtime |
|----------|-------------|---------|
| Classification (Accuracy, F1, Precision, Recall) | Pure .NET — confusion matrix arithmetic | In-process |
| Text Quality (RougeL, BLEU) | Tokenization + n-gram overlap | In-process (.NET) or container (Python) |
| Embedding-based (BERTScore, Coherence) | Requires model inference | Container (`koan/eval:*`) |
| RAG (Faithfulness, Relevancy, ContextRecall) | LLM-as-judge internally | Delegates to `Client.Chat()` |
| Operational (Latency, CostPerQuery, ErrorRate) | Collected from live inference | In-process telemetry |
| Generative (Perplexity, Toxicity) | Requires model inference | Container or inference endpoint |

### Part 2: `Eval.Measure()` — Scoring a Model Against a Dataset

The foundational verb. All other verbs build on `Measure`.

```csharp
public static async Task<EvalResult> Measure(
    ModelRef model,
    DatasetRef data,
    IReadOnlyList<string> metrics,
    MeasureOptions? options = null,
    CancellationToken ct = default);
```

**Entity-native evaluation data:**

```csharp
var result = await Eval.Measure(
    model: "acme-support:v4",
    data: Dataset.From<SupportTicket>(
        where: t => t.IsGoldenTestCase,
        input: t => t.Question,
        expected: t => t.GoldAnswer),
    metrics: [Metric.RougeL, Metric.Faithfulness, Metric.Accuracy]);

// result.Model: "acme-support:v4"
// result.Scores: [{RougeL, 0.91}, {Faithfulness, 0.94}, {Accuracy, 0.88}]
// result.Passed: true (no gate applied, so always passes)
// result.Reason: null
```

The `Dataset.From<SupportTicket>()` query is **live** — if a domain expert adds a new golden test case between evaluations, the next `Eval.Measure()` includes it. No export step. No stale snapshots.

**Measure records its result in the model's lineage** when the model is a catalog entry (has a `ModelEntry`). This creates an audit trail: every evaluation run, with its dataset hash, metrics, and scores, is permanently associated with the model version.

**RAG evaluation** uses the same verb with RAG-specific metrics:

```csharp
var ragEval = await Eval.Measure(
    model: myRagChain,
    data: ragTestSet,
    metrics: [
        Metric.ContextRelevancy,
        Metric.Faithfulness,
        Metric.AnswerRelevancy,
        Metric.ContextRecall]);
```

Chains (AI-0026) are evaluable because they implement inference — `Eval.Measure()` sends dataset inputs through the chain and compares outputs against expected values. RAG metrics additionally inspect retrieved context (the chain's intermediate `{context}` variable) to assess retrieval quality independently from generation quality.

**`MeasureOptions`:**

```csharp
public sealed record MeasureOptions
{
    public int? BatchSize { get; init; }
    public int? MaxExamples { get; init; }
    public ComputeRequirement? Compute { get; init; }
    public IProgress<EvalProgress>? Progress { get; init; }
    public bool RecordLineage { get; init; } = true;
}
```

### Part 3: `Eval.Gate()` — The Deployment Blocker

The most important verb in this ADR. Gate is what prevents bad models from reaching production.

```csharp
public static async Task<EvalResult> Gate(
    ModelRef model,
    DatasetRef data,
    Action<IGateBuilder> require,
    ModelRef? baseline = null,
    GateOptions? options = null,
    CancellationToken ct = default);
```

**The gate requirement DSL:**

```csharp
public interface IGateBuilder
{
    /// <summary>
    /// Require a metric to meet an absolute threshold.
    /// </summary>
    IGateBuilder Metric(string metric, double? min = null, double? max = null);

    /// <summary>
    /// Require a metric to meet a threshold expressed as a TimeSpan (for latency-type metrics).
    /// </summary>
    IGateBuilder Metric(string metric, TimeSpan? max = null);

    /// <summary>
    /// Require no regression on any measured metric beyond the given tolerance.
    /// Requires a baseline model to be specified.
    /// </summary>
    IGateBuilder NoRegression(double tolerance = 0.0);

    /// <summary>
    /// Require no regression on a specific metric beyond the given tolerance.
    /// </summary>
    IGateBuilder NoRegression(string metric, double tolerance = 0.0);

    /// <summary>
    /// Add a custom gate condition with a name and predicate.
    /// </summary>
    IGateBuilder Custom(string name, Func<IReadOnlyList<EvalScore>, bool> predicate);
}
```

**Full example:**

```csharp
await Eval.Gate(
    model: trainedModel,
    baseline: "acme-support:current",
    data: goldenTestSet,
    require: gate => gate
        .Metric(Metric.RougeL, min: 0.85)
        .Metric(Metric.Faithfulness, min: 0.90)
        .Metric(Metric.Latency, max: TimeSpan.FromMilliseconds(100))
        .NoRegression(tolerance: 0.02));
```

**Execution flow:**

1. Gate evaluates the candidate model against the dataset using all metrics referenced in the `require` function.
2. If `baseline` is specified, Gate also evaluates the baseline model against the same dataset (or retrieves cached scores if the baseline+dataset pair was recently evaluated).
3. Gate checks each requirement:
   - `Metric(RougeL, min: 0.85)` — is the candidate's RougeL >= 0.85?
   - `Metric(Faithfulness, min: 0.90)` — is the candidate's Faithfulness >= 0.90?
   - `Metric(Latency, max: 100ms)` — is the candidate's p95 Latency <= 100ms?
   - `NoRegression(tolerance: 0.02)` — for every measured metric, is the candidate within 0.02 of the baseline?
4. If **all** requirements pass: returns `EvalResult` with `Passed = true`. The result is recorded in the model's lineage.
5. If **any** requirement fails: throws `GateFailedException`.

**`GateFailedException` — full diagnostic:**

```csharp
public sealed class GateFailedException : Exception
{
    public ModelRef Model { get; }
    public ModelRef? Baseline { get; }
    public DatasetRef Dataset { get; }
    public IReadOnlyList<GateViolation> Violations { get; }
    public EvalResult CandidateResult { get; }
    public EvalResult? BaselineResult { get; }
}

public sealed record GateViolation(
    string Gate,
    string Metric,
    double Actual,
    double? Required,
    double? BaselineValue,
    string Description);
```

**Example exception output:**

```
GateFailedException:
  Gate "RougeL" failed: 0.82 < 0.85 (min)
  Gate "NoRegression" failed: Faithfulness regressed by 0.03 (tolerance: 0.02)
    Candidate: 0.91, Baseline: 0.94, Delta: -0.03
  Model: acme-support:v5
  Baseline: acme-support:v4
  Dataset: golden-test-set (hash: a7f3c2...)
  2 of 4 gates failed
```

**Design rationale — exception over boolean:**

A boolean `Passed` property (as in `EvalResult`) is appropriate for `Eval.Measure()`, which is informational. Gate is not informational — it is an enforcement point. Returning a boolean invites `if (!result.Passed) { /* maybe handle, maybe not */ }`. An exception forces acknowledgment: the developer must `try/catch` or let it propagate. In a pipeline (AI-0022, Part 14), unhandled `GateFailedException` halts the pipeline — which is exactly correct behavior.

The exception still carries the full `EvalResult` for both candidate and baseline, enabling programmatic analysis in `catch` blocks (e.g., logging, alerting, conditional retry with relaxed thresholds).

**Baseline caching:** When the baseline model + dataset pair has been evaluated within a configurable window (default: 24 hours), Gate reuses the cached baseline scores rather than re-evaluating. This is correct because the baseline model's weights don't change — only the candidate is being evaluated. The cache is invalidated when the dataset's content hash changes (new golden test cases added).

**`GateOptions`:**

```csharp
public sealed record GateOptions
{
    public TimeSpan BaselineCacheTtl { get; init; } = TimeSpan.FromHours(24);
    public bool FailFast { get; init; } = false;
    public MeasureOptions? MeasureOptions { get; init; }
}
```

When `FailFast = true`, Gate throws on the first violation rather than evaluating all requirements. Useful when metrics are expensive and early termination is preferred.

### Part 4: `Eval.Compare()` — Side-by-Side Model Evaluation

Evaluates multiple models against the same dataset and returns a ranked comparison.

```csharp
public static async Task<ComparisonResult> Compare(
    IReadOnlyList<ModelRef> models,
    DatasetRef data,
    IReadOnlyList<string> metrics,
    CompareOptions? options = null,
    CancellationToken ct = default);
```

```csharp
var comparison = await Eval.Compare(
    models: ["acme-support:v3", "acme-support:v4", "acme-support:v5"],
    data: testSet,
    metrics: [Metric.RougeL, Metric.Latency, Metric.CostPerQuery]);
```

**`ComparisonResult`:**

```csharp
public sealed record ComparisonResult(
    IReadOnlyList<ComparisonEntry> Entries,
    DatasetRef Dataset,
    IReadOnlyList<string> Metrics);

public sealed record ComparisonEntry(
    ModelRef Model,
    IReadOnlyList<EvalScore> Scores,
    int Rank);
```

**Ranking strategy:** By default, entries are ranked by the first metric in the `metrics` list (higher is better, except for metrics in the "lower-is-better" set: Latency, CostPerQuery, ErrorRate, Perplexity, Toxicity). Custom ranking via `CompareOptions.RankBy`.

**Parallel evaluation:** When compute resources permit, models are evaluated concurrently. Each model's evaluation is independent. Compute routing (AI-0024) distributes workloads across available resources.

**Integration with `Training.Compare()`:** When `Training.Compare()` (AI-0028) trains N variations, it can pipe results directly to `Eval.Compare()` for systematic evaluation. The output is a single ranked table showing which training configuration produced the best model.

### Part 5: `Eval.Judge()` — LLM-as-Evaluator

For subjective quality metrics that cannot be computed algorithmically — helpfulness, tone, brand voice compliance, instructional clarity — a strong model evaluates a weaker model's outputs.

```csharp
public static async Task<EvalResult> Judge(
    ModelRef model,
    DatasetRef data,
    ModelRef? judge = null,
    IReadOnlyList<string>? criteria = null,
    JudgeOptions? options = null,
    CancellationToken ct = default);
```

```csharp
var result = await Eval.Judge(
    model: "acme-support:v4",
    data: testSet,
    judge: "claude-sonnet-4-6",
    criteria: ["helpfulness", "accuracy", "conciseness"]);
```

**How it works:**

1. For each example in `data`, the evaluated model generates a response.
2. The judge model receives the input, the evaluated model's response, and (if available) the expected output.
3. The judge scores each criterion on a 1-5 scale using a structured evaluation prompt.
4. Scores are normalized to 0.0-1.0 and aggregated across all examples.

**Judge defaults:** If `judge` is null, the framework uses the strongest available chat model (determined by `Client.Scope(AiCategory.Chat)` routing). If `criteria` is null, defaults to `["helpfulness", "accuracy", "coherence"]`.

**Judge prompt is a `PromptEntry`:** The evaluation prompt used by the judge is loaded via `Prompt.Load("koan:eval-judge")` (AI-0025). This means it is versionable, editable by domain experts, and A/B testable. The framework ships a default; teams can override it.

**`JudgeOptions`:**

```csharp
public sealed record JudgeOptions
{
    public int Rounds { get; init; } = 1;
    public bool IncludeReasoning { get; init; } = false;
    public MeasureOptions? MeasureOptions { get; init; }
}
```

When `Rounds > 1`, the judge evaluates each example multiple times and averages scores, reducing variance from non-deterministic judge responses. When `IncludeReasoning = true`, the judge's per-example reasoning is captured in the result for human review.

### Part 6: `Eval.Regress()` — Regression Detection

Focused comparison between exactly two model versions: current candidate and baseline. More targeted than `Compare()` — specifically designed for the "is this version worse?" question.

```csharp
public static async Task<RegressionResult> Regress(
    ModelRef current,
    ModelRef baseline,
    DatasetRef data,
    double threshold = 0.0,
    IReadOnlyList<string>? metrics = null,
    CancellationToken ct = default);
```

```csharp
var regression = await Eval.Regress(
    current: "acme-support:v5",
    baseline: "acme-support:v4",
    data: goldenTestSet,
    threshold: 0.02);

// regression.Passed: false
// regression.Regressions: [{Faithfulness, current: 0.91, baseline: 0.94, delta: -0.03}]
// regression.Improvements: [{RougeL, current: 0.93, baseline: 0.91, delta: +0.02}]
// regression.Unchanged: [{Accuracy, current: 0.88, baseline: 0.88, delta: 0.00}]
```

**`RegressionResult`:**

```csharp
public sealed record RegressionResult(
    ModelRef Current,
    ModelRef Baseline,
    bool Passed,
    IReadOnlyList<MetricDelta> Regressions,
    IReadOnlyList<MetricDelta> Improvements,
    IReadOnlyList<MetricDelta> Unchanged);

public sealed record MetricDelta(
    string Metric,
    double CurrentValue,
    double BaselineValue,
    double Delta);
```

**Relationship to `Eval.Gate()`:** `Regress()` is informational — it returns a result. `Gate()` with `NoRegression()` is enforcing — it throws. Internally, `Gate().NoRegression()` delegates to the same regression computation. Use `Regress()` when you want to inspect regressions programmatically without enforcement; use `Gate()` when you want to block deployment.

### Part 7: `Eval.Drift()` — Input Distribution Change Detection

Detects when the production input distribution has shifted from the training distribution. This is the early warning system: drift precedes quality degradation. By the time metrics drop, the drift has already been compounding.

```csharp
public static async Task<DriftResult> Drift(
    DatasetRef baseline,
    DatasetRef current,
    DriftOptions? options = null,
    CancellationToken ct = default);
```

**Entity-native drift detection — the Koan differentiator:**

```csharp
var drift = await Eval.Drift(
    baseline: Dataset.From<SupportTicket>(
        where: t => t.CreatedAt >= trainDate && t.CreatedAt < trainDate.AddMonths(1),
        input: t => t.Question),
    current: Dataset.From<SupportTicket>(
        where: t => t.CreatedAt >= DateTime.UtcNow.AddDays(-7),
        input: t => t.Question));
```

No export. No separate snapshot storage. The same entity that serves production, feeds training, and provides evaluation data also provides the temporal partitions for drift detection. The `where` clause is the temporal boundary.

**How drift is computed:**

1. Both datasets are embedded using an `Embed`-capable adapter resolved via `AdapterResolver.Resolve(registry, AiCapability.Embed)`. By default, the entity's configured embedding model is used (via `[Embedding]` attribute from AI-0020). An explicit model can be specified in `DriftOptions.EmbeddingModel`, which routes to the appropriate `Embed`-capable adapter.
2. The framework computes distribution statistics over the embedding vectors:
   - **Centroid shift:** Euclidean distance between the mean embedding vectors of baseline and current.
   - **Variance change:** Difference in embedding space dispersion.
   - **Cluster analysis:** New clusters in current that don't appear in baseline (indicating novel input categories).
3. These statistics are combined into a single `DriftScore` (0.0 = identical distributions, 1.0 = completely disjoint).

**`DriftResult`:**

```csharp
public sealed record DriftResult(
    double Score,
    DriftStatus Status,
    IReadOnlyList<string> TopShifts,
    string? Recommendation,
    DriftDetails Details);

public enum DriftStatus
{
    Stable,   // Score < 0.10
    Notice,   // Score 0.10 - 0.30
    Warning,  // Score 0.30 - 0.60
    Critical  // Score > 0.60
}

public sealed record DriftDetails(
    double CentroidShift,
    double VarianceChange,
    int NewClusterCount,
    int BaselineSampleCount,
    int CurrentSampleCount);
```

**`TopShifts`** provides human-readable descriptions of what changed, derived from cluster analysis: `"New cluster around 'SmartHome' topics not present in training data"`, `"Significant increase in Spanish-language queries"`, `"Decrease in 'Returns' category queries"`. These are generated by running representative examples from novel clusters through a summarization prompt.

**`DriftOptions`:**

```csharp
public sealed record DriftOptions
{
    public ModelRef? EmbeddingModel { get; init; }
    public int MaxSamples { get; init; } = 10_000;
    public double StableThreshold { get; init; } = 0.10;
    public double WarningThreshold { get; init; } = 0.30;
    public double CriticalThreshold { get; init; } = 0.60;
    public ComputeRequirement? Compute { get; init; }
}
```

**Scheduled drift monitoring:** `Eval.Drift()` is a point-in-time check. For continuous monitoring, it integrates with the framework's job scheduler:

```csharp
// Register a recurring drift check
Eval.MonitorDrift(
    name: "support-ticket-drift",
    baseline: Dataset.From<SupportTicket>(
        where: t => t.CreatedAt >= lastTrainDate && t.CreatedAt < lastTrainDate.AddMonths(1),
        input: t => t.Question),
    current: () => Dataset.From<SupportTicket>(
        where: t => t.CreatedAt >= DateTime.UtcNow.AddDays(-7),
        input: t => t.Question),
    schedule: TimeSpan.FromDays(1),
    onWarning: drift => Notify("support-team", $"Input drift detected: {drift.Score:P0}"),
    onCritical: drift => Pipeline.Trigger("retrain-support"));
```

The `current` parameter is a factory (`Func<DatasetRef>`) because the time window slides with each execution.

### Part 8: `Eval.Benchmark()` — Standard Benchmark Suites

Wraps lm-eval-harness for standard academic and industry benchmarks. This is a thin interop layer — Koan does not reimplement benchmark evaluation.

```csharp
public static async Task<BenchmarkResult> Benchmark(
    ModelRef model,
    IReadOnlyList<string> suites,
    BenchmarkOptions? options = null,
    CancellationToken ct = default);
```

```csharp
var result = await Eval.Benchmark(
    model: "acme-support:v4",
    suites: ["mmlu", "hellaswag", "gsm8k"]);

// result.Scores:
//   [{Suite: "mmlu", Score: 0.72, Subscores: [{stem: 0.68}, {humanities: 0.75}, ...]},
//    {Suite: "hellaswag", Score: 0.83},
//    {Suite: "gsm8k", Score: 0.61}]
```

**Implementation:** Benchmark runs in a container (`koan/eval:lm-eval`) that bundles lm-eval-harness. The model is made accessible to the container via the model runtime's inference endpoint. Results are parsed from lm-eval-harness JSON output and mapped to `BenchmarkResult`.

**`BenchmarkResult`:**

```csharp
public sealed record BenchmarkResult(
    ModelRef Model,
    IReadOnlyList<BenchmarkSuiteScore> Scores,
    TimeSpan Duration);

public sealed record BenchmarkSuiteScore(
    string Suite,
    double Score,
    IReadOnlyList<BenchmarkSubscore>? Subscores = null);

public sealed record BenchmarkSubscore(string Category, double Score);
```

### Part 9: Lineage Integration

Every `Eval.Measure()` and `Eval.Gate()` call records its result in the model's `Lineage` (AI-0023). This creates an auditable quality history.

```csharp
var history = await Model.History("acme-support");
// v3: Lineage { EvalScores: [{RougeL, 0.88}, {Faithfulness, 0.91}], GatePassed: true }
// v4: Lineage { EvalScores: [{RougeL, 0.91}, {Faithfulness, 0.94}], GatePassed: true }
// v5: Lineage { EvalScores: [{RougeL, 0.82}, {Faithfulness, 0.91}], GatePassed: false,
//              GateReason: "RougeL 0.82 < 0.85 (min)" }
```

When `Eval.Gate()` **fails**, the failure is still recorded in lineage. This is deliberate: the audit trail must show that v5 was evaluated and rejected, not just that v5 doesn't exist. Failed gates are not deleted — they are evidence.

**`EvalResult` in `Lineage` (extending AI-0023):**

The `Lineage` shared boundary model (AI-0022) already includes `IReadOnlyList<EvalScore>? EvalScores`. This ADR specifies how that field is populated:

- `Eval.Measure()` appends scores to `Lineage.EvalScores` when `MeasureOptions.RecordLineage` is true (default).
- `Eval.Gate()` always records: the `EvalResult`, the gate pass/fail status, and the gate violation details on failure.
- Multiple evaluations accumulate — a model can have evaluation scores from different datasets and different points in time.

### Part 10: Pipeline Integration

`Eval.Gate()` is designed as a pipeline stage (AI-0022, Part 14). Gates sit between training/shadow stages and deployment stages.

```csharp
var pipeline = Pipeline.Create("model-promotion")
    .Stage("eval", model => Eval.Measure(model, goldenSet, allMetrics))
    .Gate("quality", eval => eval
        .Metric(Metric.RougeL, min: 0.85)
        .Metric(Metric.Faithfulness, min: 0.90)
        .NoRegression(tolerance: 0.02))
    .Stage("shadow", model => Model.Shadow(model, against: "current", traffic: 0.1))
    .Gate("shadow-check", shadow => shadow
        .Metric(Metric.ErrorRate, max: 0.01)
        .Metric(Metric.Latency, max: TimeSpan.FromMilliseconds(200)))
    .Stage("promote", model => Model.Deploy(model, tag: "production"));
```

**Pipeline gate semantics:**

- A gate stage receives the `EvalResult` from the preceding `Eval.Measure()` or `Stage()`.
- If the gate passes, the pipeline proceeds to the next stage.
- If the gate fails, the pipeline halts and emits an `EvalGateFailed` domain event (AI-0022 bounded context communication).
- The `EvalGateFailed` event carries the full `GateFailedException` details, enabling downstream handlers (notifications, automatic rollback, conditional retry).

**Multi-gate pipelines:** A model promotion pipeline typically has two gates — quality (after offline evaluation) and shadow (after live traffic testing). This pattern ensures that a model passes both offline metrics and live behavior before full deployment.

### Part 11: Metric Implementation Architecture

Metric computation uses the **capability-driven adapter resolution** pattern (AI-0021). Instead of a separate `IMetricComputer` service interface, adapters with appropriate capabilities handle metric computation. The `EvalService` resolves the right adapter for each metric category:

| Metric Category | Resolution | Adapter Capability |
|----------------|------------|--------------------|
| Classification (Accuracy, F1, Precision, Recall) | In-process .NET — no adapter needed | N/A (built-in) |
| Operational (Latency, ErrorRate, CostPerQuery) | In-process .NET — no adapter needed | N/A (built-in) |
| Ranking (RecallAtK, NDCG, MRR) | In-process .NET — no adapter needed | N/A (built-in) |
| Text Quality (RougeL, BLEU, BERTScore) | `AdapterResolver.Resolve(registry, AiCapability.MetricCompute)` | `MetricCompute` |
| Generative (Perplexity, Toxicity) | `AdapterResolver.Resolve(registry, AiCapability.MetricCompute)` | `MetricCompute` |
| RAG / LLM-as-judge (Faithfulness, Relevancy) | `AdapterResolver.Resolve(registry, AiCapability.Chat)` | `Chat` |

```csharp
// EvalService internally resolves adapters per metric category:

// Text metrics → adapter with MetricCompute capability
// (e.g., PythonSidecar adapter or container adapter that has rouge-score, sacrebleu installed)
var metricAdapter = AdapterResolver.Resolve(registry, AiCapability.MetricCompute);

// LLM-as-judge metrics → adapter with Chat capability
// (e.g., Ollama adapter, OpenAI adapter, any Chat-capable adapter)
var judgeAdapter = AdapterResolver.Resolve(registry, AiCapability.Chat);
```

**Built-in .NET implementations (no adapter required):**

- `AccuracyComputer`, `F1Computer`, `PrecisionComputer`, `RecallComputer` — confusion matrix arithmetic.
- `LatencyComputer`, `ErrorRateComputer`, `CostPerQueryComputer` — telemetry aggregation from inference logs.
- `RecallAtKComputer`, `NDCGComputer`, `MRRComputer` — ranking metric arithmetic.

**`MetricCompute`-capable adapter implementations:**

A Python sidecar adapter (`Koan.AI.Connector.PythonSidecar`) or container adapter declaring `AiCapability.MetricCompute` handles:
- `RougeLComputer`, `BleuComputer` — wraps `rouge-score` and `sacrebleu` Python packages.
- `BERTScoreComputer` — wraps `bert-score` Python package, requires model inference.
- `PerplexityComputer`, `ToxicityComputer` — wraps `evaluate` Python package.

A remote `Koan.AI.Worker` with `MetricCompute` capability can also compute these metrics on GPU hardware when BERTScore or embedding-based metrics require it.

**`Chat`-capable adapter for LLM-as-judge:**

RAG metrics (Faithfulness, Relevancy, ContextRecall) and `Eval.Judge()` resolve to any adapter with `AiCapability.Chat`. The strongest available chat model is used by default (via `Client.Scope(AiCategory.Chat)` routing). This means judge evaluation works with any inference provider — Ollama, OpenAI, Anthropic, or a remote Worker with `Chat` capability.

- `FaithfulnessComputer`, `ContextRelevancyComputer`, `AnswerRelevancyComputer`, `ContextRecallComputer` — structured evaluation prompts sent to the `Chat`-capable adapter.
- `CoherenceComputer` — rates text coherence on a structured rubric.

**User-defined metrics:** Register a custom metric implementation via DI. It becomes available to `Eval.Measure()`, `Eval.Gate()`, and all other verbs by its `MetricName`. Custom metrics that need Python compute should delegate to a `MetricCompute`-capable adapter.

### Part 12: Boot Report Integration

`Koan.AI.Eval` reports its capabilities during boot via `IKoanAutoRegistrar.Describe()`:

```
┌─────────────────────────────────────────────┐
│ Koan.AI.Eval                                │
├─────────────────────────────────────────────┤
│ Metrics (built-in)  : accuracy, f1,         │
│                       precision, recall,     │
│                       latency, error_rate,   │
│                       recall_at_k, ndcg, mrr │
│ MetricCompute adapter: PythonSidecar         │
│   Metrics available : rouge_l, bleu,         │
│                       bert_score, perplexity  │
│ Judge model         : claude-sonnet-4-6     │
│ Benchmark harness   : Not installed          │
│ Registered custom   : domain_accuracy,       │
│                       brand_voice_score      │
└─────────────────────────────────────────────┘
```

The boot report makes it immediately clear which metrics are available, whether containers are running, and which custom metrics have been registered. A developer seeing "Container available: No" knows that RougeL and BERTScore metrics will fail — before writing any code.

### Part 13: Package Structure

```
Koan.AI.Eval                    ← Eval.* facade, gate DSL, built-in .NET metrics
                                   (classification, operational, ranking)
Koan.AI.Eval.Benchmark          ← lm-eval-harness integration, benchmark suite definitions
```

**Reference = Intent:** Adding `Koan.AI.Eval` enables measurement with built-in .NET metrics and gating. Text quality metrics (RougeL, BLEU, BERTScore) resolve to any adapter with `AiCapability.MetricCompute` — typically a `Koan.AI.Connector.PythonSidecar` or `Koan.AI.Connector.TrainerContainer` that has `MetricCompute` in its capabilities. LLM-as-judge metrics resolve to any adapter with `AiCapability.Chat`. Adding `Koan.AI.Eval.Benchmark` enables standard benchmark suites. No separate `Koan.AI.Eval.Python` package is needed — metric computation is an adapter capability, not a separate package.

## Consequences

### Positive

- **Quality enforcement is structural, not cultural.** `Eval.Gate()` throws `GateFailedException` — bad models cannot silently reach production. The gate is code, not a checklist.
- **Entity-native evaluation eliminates ETL.** `Dataset.From<SupportTicket>(where: t => t.IsGoldenTestCase)` uses the same entity type for evaluation, training, and production. No export pipelines. No stale test sets. New golden test cases are immediately available.
- **Drift detection uses temporal entity queries.** Comparing `t.CreatedAt >= trainDate` against `t.CreatedAt >= lastWeek` on the same `Entity<T>` requires no snapshot infrastructure. The entity store is the snapshot store.
- **Full audit trail via lineage.** Every evaluation — pass or fail — is recorded in the model's `Lineage`. Teams can answer "why was v5 not deployed?" months later.
- **Progressive disclosure maintained.** `Eval.Measure()` (informational) → `Eval.Gate()` (enforcing) → `Eval.Drift()` (monitoring) → `Eval.Benchmark()` (comprehensive). Each verb adds responsibility without requiring the previous ones.
- **Extensible metric system.** Adapter capabilities (`MetricCompute`, `Chat`) and custom DI-registered metrics allow domain-specific metrics (`brand_voice_score`, `medical_accuracy`) to participate in gates and pipelines as first-class citizens.
- **Pipeline integration enables automation.** Train → Gate → Shadow → Gate → Deploy as a single pipeline. Drift monitoring triggers retraining. The closed loop (AI-0022, Part 14) includes automated quality checkpoints.

### Negative / Trade-offs

- **`MetricCompute` adapter dependency for advanced metrics.** RougeL, BLEU, BERTScore, and generative metrics require an adapter with `AiCapability.MetricCompute` (e.g., `PythonSidecar` or `TrainerContainer`). Classification and operational metrics are built-in .NET and work without any adapter, but most text-focused evaluations need a `MetricCompute`-capable adapter.
- **LLM-as-judge costs.** RAG metrics (Faithfulness, Relevancy) and `Eval.Judge()` consume inference tokens. Evaluating 1,000 examples with 4 RAG metrics means approximately 4,000 LLM calls to the judge model. Cost scales linearly with dataset size.
- **Drift detection requires embeddings.** `Eval.Drift()` embeds both datasets via an `Embed`-capable adapter, which requires a functioning embedding model and compute for the embedding inference. This is a dependency on the AI-0020 `[Embedding]` infrastructure and the adapter resolution pattern (AI-0021).
- **Benchmark harness is heavyweight.** `lm-eval-harness` in a container requires significant disk space and download time for evaluation datasets. The `Koan.AI.Eval.Benchmark` package is deliberately separate to avoid imposing this cost on teams that don't need academic benchmarks.
- **Gate exception pattern may surprise.** Developers accustomed to result-returning APIs may find the exception-throwing gate pattern unfamiliar. This is a deliberate design choice documented in Part 3 — the exception forces acknowledgment, which is the correct behavior for a quality gate.

## References

- AI-0022: Unified AI Lifecycle Vision (shared boundary models: `EvalScore`, `EvalResult`, `Lineage`; bounded context map; pipeline integration; closed loop)
- AI-0023: Model Lifecycle (`ModelRef`, `ModelEntry`, `Lineage`, `Model.History()`, lineage recording)
- AI-0028: Training and Datasets (`Dataset.From<T>()`, entity-native data bridge, `Training.Compare()`)
- AI-0024: Compute Fabric (`ComputeRequirement`, container routing for metric computation)
- AI-0025: Prompt Primitive (`Prompt.Load("koan:eval-judge")` for judge evaluation prompts)
- AI-0026: Chain Composition (chains as evaluable inference targets for RAG evaluation)
- AI-0030: Review and Human Feedback (reviewed entities as golden test cases for evaluation)
- AI-0020: Entity-First AI (`[Embedding]` attribute, embedding lifecycle used by drift detection)
- AI-0021: Category-Driven AI (client routing for judge model selection)
- `src/Koan.AI/` — Current AI implementation
- `src/Koan.AI.Contracts/` — Current contracts (adapter interfaces)
