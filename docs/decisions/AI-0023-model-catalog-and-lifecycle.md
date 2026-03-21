---
id: AI-0023
slug: AI-0023-model-catalog-and-lifecycle
domain: AI
status: Proposed
date: 2026-03-20
---

# ADR: Model Catalog and Lifecycle — `Model.*` Facade

**Contract**

- **Inputs:** Model identifiers (HF Hub IDs, Ollama library tags, local paths, URLs), existing `IAiAdapter` / `IAiModelManager` infrastructure (AI-0015), `AiModelDescriptor` from adapters, format conversion requests, deployment targets, fleet specifications, version references, compute availability from ZenGarden topology.
- **Outputs:** `ModelEntry` entities (first-class Koan entities with lineage, versioning, and queryability), `JobRef` for async operations (conversion, quantization, merge), capability-driven adapter resolution for deployment targets (adapters with `Serve.*` capabilities), unified model inventory across all sources, format conversion via adapters with `Convert` capability, version history with rollback capability, fleet placement plans, health and cost telemetry.
- **Error Modes:** Model pull from unreachable hub degrades to local catalog lookup; format conversion requested but toolchain not installed returns guidance per platform (container → local Python → manual install); deployment to unavailable runtime falls back to next viable runtime from `Model.Routes()` ranking; `Model.Remove()` on a deployed model fails with dependency list; `Model.Rollback()` to a version whose weights were pruned fails with restore guidance; air-gapped environment with no database falls back to `.Koan/models/catalog.json` file-based catalog.
- **Acceptance Criteria:** A developer can `Model.Pull("meta-llama/Llama-3.1-8B-Instruct")` from HuggingFace Hub, `Model.Convert(model, to: ModelFormat.GGUF, quantization: Quantization.Q4_K_M)` to a quantized GGUF, `Model.Deploy(gguf)` to an auto-detected Ollama runtime, query `Model.History("meta-llama/Llama-3.1-8B-Instruct")` for the full version chain with lineage, and `Model.Rollback("meta-llama/Llama-3.1-8B-Instruct", to: 2)` to swap back — all through a single static facade, with `ModelEntry` persisted as a queryable Koan entity, across heterogeneous runtimes.

**Edge Cases**

- `Model.Pull()` for a model that already exists locally at the same version: Returns existing `ModelEntry` without re-downloading. If `force: true` is specified, re-downloads and increments version.
- `Model.Convert()` requested but no adapter has the `Convert` capability: Fails with clear error — `"No adapter with 'Convert' capability. Install Koan.AI.Training.Python (Python Sidecar) to enable format conversion."` If a converter adapter exists but does not support the specific source→target format pair, the error lists the supported conversion graph and which additional packages would enable the path.
- `Model.Deploy()` with no adapter having a matching `Serve.*` capability: `Model.Routes()` returns empty; deploy fails with `"No adapter with 'Serve.GGUF' capability."` and lists connector packages that provide it. If an adapter exists but the model format is incompatible, suggests conversion path via `Convert` capability.
- `Model.Remove()` on a model with active deployments: Fails with `ModelInUseException` listing deployment targets. Developer must `Model.Undeploy()` first or use `force: true` to cascade.
- `Model.Prune(keep: 3)` when fewer than 3 models exist: No-op, returns empty removal list.
- `Model.Register()` with a path that does not exist: Fails with `FileNotFoundException`. No catalog entry created.
- `Model.Rollback()` to a version that was pruned: Fails with `ModelVersionPrunedException` containing the version's lineage metadata and guidance to re-pull or re-convert.
- `Model.Search()` with no hub connectivity: Returns local catalog results only; logs warning about unreachable hubs with connectivity diagnostic.
- `Model.Deploy()` to Ollama when the model is SafeTensors format: Auto-converts to GGUF if an adapter with `Convert` capability is available; otherwise fails with conversion guidance. The conversion is submitted as a job; deployment queues behind it.
- `Model.Plan()` with heterogeneous compute (CUDA + Metal): Plans respect accelerator compatibility; GGUF models can target either; ONNX models prefer the node with matching execution provider; PyTorch models require CUDA nodes.
- `Model.Health()` when a deployed model's runtime is unreachable: Returns `RuntimeModelStatus.Unreachable` with last-known metrics and unreachable duration.
- Air-gapped deployment with no database provider configured: Catalog falls back to `.Koan/models/catalog.json` with file-based CRUD. `ModelEntry.Query()` supports basic predicate filtering over the JSON catalog.

## Context

Koan.AI (AI-0001 through AI-0021) treats models as **opaque strings**. A developer writes `Client.Chat("llama3.1:8b")` and the routing engine resolves a source and member (AI-0015), but the framework knows nothing about the model itself — its format, parameter count, context window, lineage, or deployment history. The `AiModelDescriptor` record in `Koan.AI.Contracts` captures `Name`, `Family`, `ContextWindow`, `EmbeddingDim`, `AdapterId`, and `AdapterType` — a thin metadata slice surfaced by adapter `ListModelsAsync()` calls. The `IAiModelManager` interface adds `EnsureInstalledAsync`, `RefreshAsync`, `FlushAsync`, and `ListManagedModelsAsync` for adapters that can provision models (Ollama implements this today).

This infrastructure is necessary but insufficient for the lifecycle demands identified in AI-0022:

1. **No unified catalog.** Each adapter maintains its own model list. There is no cross-adapter inventory. A model pulled via Ollama and the same model available via LM Studio appear as unrelated entries. Developers cannot answer "what models do I have?" without querying each adapter individually.

2. **No versioning or lineage.** When a model is fine-tuned, quantized, or converted, the relationship to the base model is lost. There is no version chain. Rollback means manual file management. Audit trails do not exist.

3. **No format awareness.** The framework does not know that `llama3.1:8b` is GGUF, that `meta-llama/Llama-3.1-8B-Instruct` is SafeTensors, or that converting between them is possible. Format-specific runtimes (Ollama for GGUF, ONNX Runtime for ONNX) cannot be matched to models automatically.

4. **No deployment abstraction.** Deploying a model to Ollama means `ollama pull`. Deploying to ONNX Runtime means copying files and configuring a session. Deploying to TGI means launching a container. Each path is manual and adapter-specific.

5. **No lifecycle operations.** No pruning of unused models, no health monitoring per model, no cost tracking, no usage statistics. Storage fills silently. Degraded models serve traffic without alerting.

AI-0022 designates `Model.*` as Phase 1 (no dependencies) — the foundation that Training, Eval, Compute, and Pipeline contexts reference through `ModelRef`. This ADR specifies the full `Model.*` bounded context.

### Design Constraints

- **Entity-first.** `ModelEntry` must be `Entity<ModelEntry>` — queryable, versionable, persistable through the standard Koan data pipeline. This is non-negotiable; it enables `ModelEntry.Query(m => m.Format == ModelFormat.GGUF)` and integrates with the entity event system for cross-context communication.
- **Runtime-agnostic facade.** The `Model.*` verbs never mention a specific runtime. `Model.Deploy()` auto-selects. `Model.Routes()` shows options. The developer thinks in models, not runtimes.
- **Async operations are jobs.** Conversion, quantization, and merge are long-running. They return `JobRef` immediately and report progress via callbacks. The job infrastructure is shared with Training (AI-0028) and Compute (AI-0024).
- **Capability-driven conversion.** Format conversion requires external toolchains (llama.cpp, optimum, coremltools). Adapters with the `Convert` capability (e.g., Python Sidecar) handle conversion. The core provides the conversion graph and job orchestration; adapter packages provide the actual conversion logic. No separate `IFormatConverter` interface — the adapter IS the converter (see AI-0022 Part 2).
- **Air-gapped fallback.** Not every deployment has a database. The catalog must work with a local JSON file for disconnected or edge scenarios.

## Decision

### Part 1: Shared Boundary Models

The following types are defined in `Koan.AI.Contracts.Shared` and used across bounded contexts. They were introduced in AI-0022 and are fully specified here for the Model context's consumption.

```csharp
// ── Model identity ──────────────────────────────────────────────────
// Shared by: Model, Training, Eval, Chain, Client

public sealed record ModelRef(string Id, int? Version = null)
{
    /// <summary>
    /// Accepts HF Hub IDs ("meta-llama/Llama-3.1-8B-Instruct"),
    /// Ollama tags ("llama3.1:8b"), or catalog names ("acme-support:v3").
    /// </summary>
    public static implicit operator ModelRef(string id) => new(id);
}

// ── Model format ────────────────────────────────────────────────────

public enum ModelFormat
{
    SafeTensors,
    GGUF,
    ONNX,
    PyTorch,
    CoreML,
    OpenVINO
}

// ── Quantization ────────────────────────────────────────────────────

public enum Quantization
{
    None,
    Q4_K_M,
    Q5_K_M,
    Q8_0,
    GPTQ_4bit,
    AWQ_4bit,
    GPTQ_8bit,
    AWQ_8bit,
    FP16,
    BF16,
    INT8,
    INT4
}

// ── Lineage ─────────────────────────────────────────────────────────

public sealed record Lineage(
    ModelRef? Base = null,
    string? Method = null,            // "LoRA", "QLoRA", "DPO", "Quantization", "Merge"
    DatasetRef? Data = null,
    IReadOnlyList<EvalScore>? EvalScores = null,
    string? TrainedBy = null,
    DateTimeOffset? TrainedAt = null,
    string? Notes = null);

// ── Job lifecycle ───────────────────────────────────────────────────
// Shared by: Model.Convert, Model.Quantize, Model.Merge, Training.Train

public sealed record JobRef(string Id, JobStatus Status);

public enum JobStatus { Queued, Running, Completed, Failed, Cancelled }

// ── Accelerator ─────────────────────────────────────────────────────

public enum Accelerator
{
    None,       // CPU only
    Any,        // Framework selects best available
    CUDA,       // NVIDIA
    ROCm,       // AMD
    Metal,      // Apple Silicon
    DirectML,   // Windows universal GPU
    OneAPI      // Intel
}
```

### Part 2: `ModelEntry` — The Model Entity

`ModelEntry` extends `Entity<ModelEntry>`, making models first-class Koan entities with automatic GUID v7 generation, queryability, and persistence through the configured data provider.

```csharp
namespace Koan.AI.Models;

/// <summary>
/// A model in the Koan catalog. Persisted as a Koan entity with full
/// query, version, and lineage support. Each format/quantization variant
/// of a base model is a separate <see cref="ModelEntry"/>.
/// </summary>
public class ModelEntry : Entity<ModelEntry>
{
    /// <summary>
    /// Canonical identifier from the origin hub. HuggingFace:
    /// "meta-llama/Llama-3.1-8B-Instruct". Ollama: "llama3.1:8b".
    /// Local/Custom: user-assigned name.
    /// </summary>
    public string HubId { get; set; } = string.Empty;

    /// <summary>
    /// Monotonically increasing version within this catalog entry.
    /// Incremented on re-pull, conversion, quantization, or fine-tune.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Reference to the base model this entry was derived from.
    /// Null for original/base models pulled directly from a hub.
    /// </summary>
    public ModelRef? Base { get; set; }

    /// <summary>Storage format of the model weights.</summary>
    public ModelFormat Format { get; set; }

    /// <summary>Total parameter count (e.g., 8_000_000_000 for an 8B model).</summary>
    public long Parameters { get; set; }

    /// <summary>Maximum context window in tokens. Null if unknown.</summary>
    public int? ContextWindow { get; set; }

    /// <summary>Embedding output dimension. Null for non-embedding models.</summary>
    public int? EmbeddingDim { get; set; }

    /// <summary>Quantization applied to this variant. Null for full-precision.</summary>
    public Quantization? Quantization { get; set; }

    /// <summary>AI capabilities this model supports.</summary>
    public ModelCapability[] Capabilities { get; set; } = [];

    /// <summary>Full provenance chain linking this model to its origins.</summary>
    public Lineage? Lineage { get; set; }

    /// <summary>
    /// Local filesystem path to cached weights. Null if not downloaded.
    /// Follows HF Hub cache convention under <c>.Koan/models/</c>.
    /// </summary>
    public string? LocalPath { get; set; }

    /// <summary>Size on disk in bytes. Zero if not yet downloaded.</summary>
    public long DiskSizeBytes { get; set; }

    /// <summary>
    /// Runtime IDs where this model is currently deployed
    /// (e.g., ["ollama-local", "tgi-gpu-server"]).
    /// </summary>
    public string[] DeployedTo { get; set; } = [];

    /// <summary>User-defined tags for organization and filtering.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Timestamp of last inference or management operation.</summary>
    public DateTime? LastUsed { get; set; }

    /// <summary>Where this model was acquired from.</summary>
    public ModelOrigin Origin { get; set; }
}

/// <summary>How the model entered the catalog.</summary>
public enum ModelOrigin
{
    HuggingFace,
    Ollama,
    LMStudio,
    Local,
    Custom,
    Training,
    Conversion
}

/// <summary>What the model can do.</summary>
[Flags]
public enum ModelCapability
{
    None      = 0,
    Chat      = 1 << 0,
    Embed     = 1 << 1,
    Vision    = 1 << 2,
    Code      = 1 << 3,
    Ocr       = 1 << 4,
    Rerank    = 1 << 5,
    Classify  = 1 << 6,
    Generate  = 1 << 7
}
```

**Entity identity:** `ModelEntry` inherits `Entity<ModelEntry>.Id` (GUID v7). The `HubId` + `Version` pair forms the natural key. A unique index on `(HubId, Version)` prevents duplicate entries. Multiple format/quantization variants of the same base model are separate `ModelEntry` rows, linked through `Base`.

**Queryability:**

```csharp
// All GGUF models in the catalog
var ggufModels = await ModelEntry.Query(m => m.Format == ModelFormat.GGUF);

// Models with embedding capability, sorted by parameters
var embedders = await ModelEntry.Query(
    m => m.Capabilities.HasFlag(ModelCapability.Embed),
    orderBy: m => m.Parameters);

// Models deployed to a specific runtime
var deployed = await ModelEntry.Query(m => m.DeployedTo.Contains("ollama-local"));
```

### Part 3: `Model.*` Static Facade

The `Model` static class is the single entry point for all model lifecycle operations. It follows the same static facade pattern as `Client` (AI-0021), backed by `AsyncLocal` service resolution.

#### Discovery and Acquisition

```csharp
/// <summary>
/// Search across all registered hubs and the local catalog simultaneously.
/// Results are ranked by relevance and deduplicated across sources.
/// </summary>
public static async Task<IReadOnlyList<ModelSearchResult>> Search(
    string query,
    ModelSearchFilters? filters = null,
    CancellationToken ct = default);

/// <summary>
/// Download a model from any source. Auto-detects origin from identifier format.
/// HF Hub IDs ("org/model"), Ollama tags ("model:tag"), URLs, local paths.
/// Creates a <see cref="ModelEntry"/> in the catalog upon completion.
/// </summary>
public static async Task<ModelEntry> Pull(
    string id,
    PullOptions? options = null,
    IProgress<ModelPullProgress>? progress = null,
    CancellationToken ct = default);

/// <summary>
/// Rich metadata inspection. Resolves from catalog first, then from hub API.
/// Returns parameters, format, capabilities, license, context window,
/// embedding dim, disk size, and deployment list.
/// </summary>
public static async Task<ModelInspection> Inspect(
    string id,
    CancellationToken ct = default);
```

**Pull auto-detection logic:**

```csharp
// HuggingFace Hub — contains "/" and no "://"
await Model.Pull("meta-llama/Llama-3.1-8B-Instruct");
// → HuggingFaceHubClient.DownloadAsync()

// Ollama library — contains ":" but no "/"
await Model.Pull("llama3.1:8b");
// → OllamaAdapter.EnsureInstalledAsync() via IAiModelManager

// URL — starts with "http://" or "https://"
await Model.Pull("https://example.com/models/custom.gguf");
// → HttpClient download to .Koan/models/

// Local path — absolute path
await Model.Pull("/models/my-fine-tune/");
// → Copy/symlink to .Koan/models/, register in catalog
```

**Search with filters:**

```csharp
var results = await Model.Search("embedding english", new ModelSearchFilters
{
    Capabilities = ModelCapability.Embed,
    Formats = [ModelFormat.GGUF, ModelFormat.ONNX],
    MaxParameters = 1_000_000_000,  // < 1B for local deployment
    Sources = [ModelSearchSource.HuggingFace, ModelSearchSource.Local]
});

foreach (var r in results)
    Console.WriteLine($"{r.Id} — {r.Parameters/1e9:F1}B — {r.Format} — {r.Source}");
```

#### Transformation (Job-Based)

All transformation operations are long-running and return `JobRef` immediately. Progress is reported via callbacks. Jobs are persisted and resumable.

```csharp
/// <summary>
/// Convert a model between formats. Resolves the conversion toolchain
/// automatically. Returns a <see cref="JobRef"/> for tracking.
/// The converted model is registered as a new <see cref="ModelEntry"/>
/// with lineage pointing to the source.
/// </summary>
public static async Task<JobRef> Convert(
    ModelRef source,
    ModelFormat to,
    ConvertOptions? options = null,
    IProgress<JobProgress>? progress = null,
    CancellationToken ct = default);

/// <summary>
/// Standalone quantization. For combined conversion + quantization,
/// use <see cref="Convert"/> with <see cref="ConvertOptions.Quantization"/>.
/// </summary>
public static async Task<JobRef> Quantize(
    ModelRef source,
    Quantization method,
    QuantizeOptions? options = null,
    IProgress<JobProgress>? progress = null,
    CancellationToken ct = default);

/// <summary>
/// Merge a LoRA adapter into a base model, or average multiple models.
/// Produces a new standalone <see cref="ModelEntry"/> with merged weights.
/// </summary>
public static async Task<JobRef> Merge(
    ModelRef @base,
    ModelRef adapter,
    MergeOptions? options = null,
    CancellationToken ct = default);
```

**Conversion with options:**

```csharp
// SafeTensors → GGUF with Q4_K_M quantization
var job = await Model.Convert(
    "meta-llama/Llama-3.1-8B-Instruct",
    to: ModelFormat.GGUF,
    new ConvertOptions
    {
        Quantization = Quantization.Q4_K_M,
        OutputName = "llama3.1-8b-q4km"
    },
    progress: new Progress<JobProgress>(p =>
        Console.WriteLine($"Converting: {p.Percent}% — {p.Stage}")));

// Poll or await completion
var completed = await Model.AwaitJob(job.Id);
// completed.Output is the new ModelEntry
```

**Job lifecycle:**

```csharp
// Check job status
var status = await Model.Job(job.Id);
// → JobRef { Id, Status: Running }

// Cancel a running job
await Model.CancelJob(job.Id);

// List all jobs
var jobs = await Model.Jobs(status: JobStatus.Running);
```

#### Deployment

```csharp
/// <summary>
/// Deploy a model to a runtime. Auto-selects the best runtime based on
/// model format, available compute, and runtime capabilities. Registers
/// the model as a Koan AI source for <c>Client.Chat()</c> / <c>Client.Embed()</c>.
/// </summary>
public static async Task<DeployResult> Deploy(
    ModelRef model,
    DeployOptions? options = null,
    CancellationToken ct = default);

/// <summary>
/// Remove a model from a runtime. The <see cref="ModelEntry"/> remains
/// in the catalog; only the runtime deployment is removed.
/// </summary>
public static async Task Undeploy(
    ModelRef model,
    string? runtimeId = null,
    CancellationToken ct = default);

/// <summary>
/// Show all viable format → runtime → compute paths for a model,
/// ranked by estimated performance. Includes conversion steps if the
/// model's current format doesn't match a runtime natively.
/// </summary>
public static async Task<IReadOnlyList<DeployRoute>> Routes(
    ModelRef model,
    CancellationToken ct = default);

/// <summary>
/// Compute optimal placement of N models across available runtimes
/// and compute nodes. Returns a plan that can be inspected and applied.
/// </summary>
public static async Task<FleetPlan> Plan(
    FleetSpec spec,
    CancellationToken ct = default);
```

**Auto-deployment flow:**

```csharp
// Framework resolves: GGUF model → AdapterResolver finds adapter with Serve.GGUF → deploy there
var result = await Model.Deploy("llama3.1:8b-q4km");
// result.Runtime: "ollama-local"
// result.Endpoint: "http://localhost:11434"
// result.Source: "ollama-local" (registered as AI source for Client.Chat)

// Explicit target when multiple adapters have Serve.GGUF (e.g., Ollama + LM Studio)
var result = await Model.Deploy("llama3.1:8b-q4km", new DeployOptions { RuntimeId = "ollama-local" });
// Or equivalently via to: parameter in the facade shorthand

// Model is now usable through the standard Client facade
var answer = await Client.Chat("Hello", new ChatOptions
{
    Model = "llama3.1:8b-q4km"  // Routes through deployed runtime
});
```

**Route inspection:**

```csharp
var routes = await Model.Routes("BAAI/bge-large-en-v1.5");
// [
//   { Format: ONNX,  Runtime: "onnx-local",  Compute: "local-cpu",  ConvertSteps: ["SafeTensors→ONNX"], EstTime: "~8min" },
//   { Format: GGUF,  Runtime: "ollama-local", Compute: "local-gpu",  ConvertSteps: ["SafeTensors→GGUF"], EstTime: "~12min" },
//   { Format: SafeTensors, Runtime: "tei-remote", Compute: "gpu-server", ConvertSteps: [], EstTime: "~1min" }
// ]
```

**Fleet planning:**

```csharp
var plan = await Model.Plan(new FleetSpec
{
    Models =
    [
        new FleetModel("llama3.1:70b", Priority: 1, MinInstances: 1),
        new FleetModel("BAAI/bge-large-en-v1.5", Priority: 2, MinInstances: 2),
        new FleetModel("llama3.1:8b", Priority: 3, MinInstances: 1)
    ],
    Strategy = PlacementStrategy.BinPack  // Minimize node count
});

// Inspect before applying
foreach (var placement in plan.Placements)
    Console.WriteLine($"{placement.Model} → {placement.Runtime} on {placement.Node}");

// Apply the plan
await plan.Apply();
```

#### Versioning and Rollback

```csharp
/// <summary>
/// Version history for a model name. Returns all versions with
/// lineage, eval scores, and deployment dates.
/// </summary>
public static async Task<IReadOnlyList<ModelVersion>> History(
    string name,
    CancellationToken ct = default);

/// <summary>
/// Instant swap to a previous version. The current version enters
/// standby; the target version becomes active. No re-download needed
/// if weights are still cached.
/// </summary>
public static async Task<ModelEntry> Rollback(
    string name,
    int to,
    CancellationToken ct = default);

/// <summary>
/// Full provenance audit at a point in time. Returns the model state,
/// lineage chain, eval results, and deployment history as of the
/// specified date.
/// </summary>
public static async Task<ModelAudit> Audit(
    string name,
    DateTimeOffset at,
    CancellationToken ct = default);
```

**Version chain example:**

```csharp
var history = await Model.History("acme-support");
// [
//   { Version: 1, HubId: "meta-llama/Llama-3.1-8B-Instruct", Lineage: null, DeployedAt: 2026-01-15 },
//   { Version: 2, Lineage: { Base: v1, Method: "LoRA", Data: "support-tickets-q1" }, DeployedAt: 2026-02-20 },
//   { Version: 3, Lineage: { Base: v2, Method: "DPO", Data: "human-corrections-feb" }, DeployedAt: 2026-03-10 },
// ]

// Rollback to v2 (v3 enters standby)
var restored = await Model.Rollback("acme-support", to: 2);
// Deployment updated atomically — next Client.Chat() routes to v2
```

#### Registration (Escape Hatch)

```csharp
/// <summary>
/// Register an externally-managed model in the catalog.
/// No download. No format conversion. Creates a catalog entry with
/// optional lineage for provenance tracking.
/// </summary>
public static async Task<ModelEntry> Register(
    string path,
    RegisterOptions? options = null,
    CancellationToken ct = default);
```

**Usage:**

```csharp
// Externally trained model — just register for catalog + lineage
var entry = await Model.Register("/results/my-fine-tune/", new RegisterOptions
{
    Name = "acme-support:v4",
    Format = ModelFormat.SafeTensors,
    Lineage = new Lineage(
        Base: "meta-llama/Llama-3.1-8B-Instruct",
        Method: "Full fine-tune",
        Data: new DatasetRef("support-tickets-2026-q1"),
        TrainedBy: "riku@acme.com",
        TrainedAt: DateTimeOffset.UtcNow,
        Notes: "Trained on external cluster with custom recipe")
});

// Now deployable through standard Model.Deploy()
await Model.Deploy(entry);
```

#### Lifecycle Management

```csharp
/// <summary>
/// List models by status. Default returns all models in the catalog.
/// </summary>
public static async Task<IReadOnlyList<ModelEntry>> List(
    ModelStatus? status = null,
    CancellationToken ct = default);

/// <summary>
/// Rich inventory table with deployment, usage, and size information.
/// Designed for CLI/dashboard display.
/// </summary>
public static async Task<ModelInventory> Inventory(
    CancellationToken ct = default);

/// <summary>
/// Delete a model from the cache. Fails if the model has active deployments
/// unless <paramref name="force"/> is true.
/// </summary>
public static async Task<RemoveResult> Remove(
    ModelRef id,
    bool force = false,
    CancellationToken ct = default);

/// <summary>
/// Remove least-recently-used models beyond the top N. Deployed models
/// are never pruned. Standby models are pruned before unused models.
/// </summary>
public static async Task<PruneResult> Prune(
    int keep,
    CancellationToken ct = default);

/// <summary>
/// Per-model runtime health: latency p50/p95/p99, error rate,
/// request count, uptime, current load.
/// </summary>
public static async Task<IReadOnlyList<ModelHealthReport>> Health(
    CancellationToken ct = default);

/// <summary>
/// Usage statistics over a period: request count, token throughput,
/// estimated cost (when pricing is configured), per-model breakdown.
/// </summary>
public static async Task<ModelCostReport> Costs(
    TimeSpan period,
    CancellationToken ct = default);
```

**Inventory output (designed for CLI rendering):**

```csharp
var inventory = await Model.Inventory();
// ┌─────────────────────────────────────┬────────────┬───────┬────────┬──────────────┬────────────┐
// │ Model                               │ Format     │ Quant │ Size   │ Deployed To  │ Last Used  │
// ├─────────────────────────────────────┼────────────┼───────┼────────┼──────────────┼────────────┤
// │ llama3.1:8b                         │ GGUF       │ Q4_K_M│ 4.7 GB │ ollama-local │ 2 min ago  │
// │ BAAI/bge-large-en-v1.5              │ ONNX       │ None  │ 1.3 GB │ onnx-local   │ 5 min ago  │
// │ acme-support:v3                     │ SafeTensors│ None  │ 16 GB  │ tgi-gpu      │ 1 hr ago   │
// │ acme-support:v2 (standby)           │ SafeTensors│ None  │ 16 GB  │ —            │ 3 days ago │
// └─────────────────────────────────────┴────────────┴───────┴────────┴──────────────┴────────────┘
```

#### Advisory

```csharp
/// <summary>
/// Cost optimization recommendations based on actual traffic patterns,
/// model utilization, and available alternatives. Analyzes usage telemetry
/// to suggest model consolidation, quantization, or right-sizing.
/// </summary>
public static async Task<IReadOnlyList<ModelAdvisorRecommendation>> Advisor(
    CancellationToken ct = default);
```

**Recommendation example:**

```csharp
var advice = await Model.Advisor();
// [
//   { Type: Downsize, Model: "llama3.1:70b", Suggestion: "llama3.1:8b-q4km",
//     Reason: "95% of requests are < 200 tokens. 8B model handles these with identical quality.",
//     EstSavings: "12 GB VRAM, ~3x throughput increase" },
//   { Type: Prune, Model: "acme-support:v1", Suggestion: "Remove",
//     Reason: "No requests in 30 days. Superseded by v3.", EstSavings: "16 GB disk" }
// ]
```

### Part 4: Capability-Driven Deployment Resolution

Deployment targets are **not** a separate abstraction. Adapters declare `Serve.*` capabilities (`Serve.GGUF`, `Serve.SafeTensors`, `Serve.ONNX`) and the `AdapterResolver` (AI-0022 Part 2) matches models to the right adapter based on the model's format. The adapter IS the runtime.

**Capability mapping (deployment-relevant subset):**

| Adapter | Package | Serve Capabilities | Additional |
|---------|---------|-------------------|------------|
| Ollama | `Koan.AI.Connector.Ollama` | `Serve.GGUF` | `Pull`, `Remove`, `ModelList` |
| LM Studio | `Koan.AI.Connector.LMStudio` | `Serve.GGUF` | `ModelList` |
| ONNX Runtime | `Koan.AI.Models.Onnx` | `Serve.ONNX` | `BatchEmbed` |
| TGI | `Koan.AI.Connector.TGI` | `Serve.SafeTensors` | `Streaming`, `Tools` |
| TEI | `Koan.AI.Connector.TEI` | `Serve.SafeTensors` | `BatchEmbed` |
| HuggingFace | `Koan.AI.Connector.HuggingFace` | `Serve.SafeTensors` | `Pull`, `Push`, `ModelList` |
| Python Sidecar | `Koan.AI.Training.Python` | `Serve.SafeTensors` | `Train`, `Align`, `Convert`, `Quantize` |

**Deployment resolution algorithm:**

```
1. Determine the model's format → map to Serve.{Format} capability
2. AdapterResolver.Resolve(registry, AiCapability.Serve{Format}, to)
   a. If `to:` parameter specified → look up adapter by ID directly
   b. One adapter with Serve.{Format} → use it (unambiguous)
   c. Multiple adapters → AmbiguousAdapterException with candidate list
3. The resolved adapter's IAiModelManager.EnsureInstalledAsync() handles deployment
4. If no adapter has the matching Serve.{Format} capability:
   a. Check if an adapter has Convert capability
   b. Suggest conversion path: "Model is SafeTensors. Install Koan.AI.Connector.TGI
      to serve directly, or convert to GGUF for Ollama."
```

**Supporting types for deployment status and health remain unchanged:**

```csharp
public enum RuntimeLocation
{
    Local,      // Same machine as the Koan application
    Remote,     // Network-accessible (ZenGarden node, cloud endpoint)
    Container   // Docker/Podman container (local or remote)
}

public record RuntimeModelStatus
{
    public required string ModelId { get; init; }
    public required RuntimeModelState State { get; init; }
    public long? VramUsedBytes { get; init; }
    public TimeSpan? Uptime { get; init; }
    public double? LatencyP95Ms { get; init; }
    public long? RequestCount { get; init; }
    public double? ErrorRate { get; init; }
}

public enum RuntimeModelState
{
    NotLoaded,
    Loading,
    Ready,
    Unloading,
    Error,
    Unreachable
}
```

**`Model.Routes()` — queries all adapters for `Serve.*` capabilities:**

```csharp
// Model.Routes() builds the route table by querying all registered adapters:
// 1. For each adapter with any Serve.* capability:
//    a. Check if the model's format matches the adapter's Serve capability
//    b. If not, check if a Convert-capable adapter can bridge the format gap
//    c. Build DeployRoute with conversion steps, estimated time, and compute info
// 2. Rank routes by: format match (native > conversion), location, accelerator, VRAM
```

### Part 5: Runtime-to-Source Bridge

When `Model.Deploy()` loads a model into a runtime, the framework must register it as a Koan AI source so that `Client.Chat()` and `Client.Embed()` can route to it through the existing source-member architecture (AI-0015). This bridge connects the Model context to the Client context without either importing the other's internals.

```csharp
/// <summary>
/// Emitted when a model deployment completes. The AI source registry
/// subscribes to create/update source entries for the deployed model.
/// </summary>
public sealed record ModelDeployedEvent(
    ModelEntry Model,
    string RuntimeId,
    string Endpoint,
    ModelCapability[] Capabilities);

/// <summary>
/// Emitted when a model is undeployed. The AI source registry
/// subscribes to remove the corresponding source entry.
/// </summary>
public sealed record ModelUndeployedEvent(
    string ModelId,
    string RuntimeId);
```

The `AiSourceRegistry` (AI-0015) listens for `ModelDeployedEvent` and creates a source definition with members matching the runtime endpoint. The source name follows the convention `model:{hubId}` (e.g., `model:llama3.1:8b-q4km`). Category routing (AI-0021) can reference these sources explicitly or discover them through capability matching.

```csharp
// After Model.Deploy("llama3.1:8b-q4km") completes:
//
// AiSourceRegistry sees ModelDeployedEvent and creates:
//   Source: "model:llama3.1:8b-q4km"
//   Members: [{ Name: "ollama-local::llama3.1:8b-q4km", Endpoint: "http://localhost:11434" }]
//   Capabilities: ["Chat"]
//
// Client.Chat() can now route to it:
await Client.Chat("Hello", new ChatOptions { Model = "llama3.1:8b-q4km" });
```

### Part 6: Format Conversion via Capability Resolution

Conversion is orchestrated by the `Model.*` facade and executed by adapters that declare the `Convert` and/or `Quantize` capabilities. There is no separate `IFormatConverter` interface — the adapter IS the converter (see AI-0022 Part 2).

**Resolution flow:**

```csharp
// Model.Convert(model, to: ModelFormat.GGUF)
//   → AdapterResolver.Resolve(registry, AiCapability.Convert)
//   → Python Sidecar has Convert capability → delegates to sidecar
//   → Sidecar invokes llama.cpp / optimum / coremltools in container or locally

// Model.Convert(model, to: ModelFormat.GGUF, target: "my-custom-converter")
//   → AdapterResolver.Resolve(registry, AiCapability.Convert, target: "my-custom-converter")
//   → Explicit adapter lookup by ID
```

**Conversion graph (supported by Python Sidecar and extension packages):**

```
SafeTensors ←→ GGUF         (llama.cpp)
SafeTensors  → ONNX         (optimum)
ONNX         → CoreML       (coremltools)
ONNX         → OpenVINO     (openvino toolkit)
SafeTensors ←→ PyTorch      (torch.save / safetensors.torch)
```

**Two execution modes (within the adapter):**

1. **Container converter** (preferred): The adapter launches a container image (e.g., `koan/convert-gguf:latest`). The conversion runs in an isolated container with all dependencies pre-installed. Requires Docker/Podman.

2. **Local converter** (fallback): The adapter detects local toolchain installation (e.g., `llama-quantize` binary, Python with `optimum` package). Uses the local toolchain directly. Requires manual tool installation.

**Supporting types for conversion requests and results:**

```csharp
public record ConversionRequest(
    ModelEntry Source,
    ModelFormat TargetFormat,
    Quantization? Quantization = null,
    string? OutputPath = null,
    ConvertOptimization? Optimization = null);

public record ConversionResult(
    string OutputPath,
    ModelFormat Format,
    Quantization? Quantization,
    long OutputSizeBytes,
    TimeSpan Duration);

public record ConverterAvailability(
    bool IsAvailable,
    string? ToolchainVersion = null,
    string? InstallGuidance = null);
```

**Custom converters** can be added by implementing a new adapter that declares `Convert` capability and registering it via `KoanAutoRegistrar`. The extension packages (`Koan.AI.Convert.GGUF`, etc.) each contribute an adapter with `Convert` capability scoped to their format pairs — Reference = Intent.

### Part 7: Storage and Catalog

**Local storage convention:**

```
.Koan/models/
├── hub/                              ← HF Hub cache (follows HF convention)
│   └── models--meta-llama--Llama-3.1-8B-Instruct/
│       ├── snapshots/
│       │   └── abc123def/            ← Commit hash
│       │       ├── model-00001-of-00004.safetensors
│       │       ├── model-00002-of-00004.safetensors
│       │       └── config.json
│       └── refs/
│           └── main                  ← Points to current snapshot
├── converted/                        ← Conversion outputs
│   └── llama3.1-8b-q4km.gguf
├── registered/                       ← Externally registered (symlinks)
│   └── acme-support-v4/
└── catalog.json                      ← Air-gapped fallback catalog
```

**Dual catalog persistence:**

The `ModelEntry` entity is persisted through the standard Koan data pipeline — whatever database provider is configured (Postgres, MongoDB, SQLite, etc.) holds the catalog. For air-gapped environments without a database, a `JsonModelCatalog` fallback stores entries in `.Koan/models/catalog.json`.

```csharp
namespace Koan.AI.Models.Catalog;

/// <summary>
/// Internal catalog abstraction. Default implementation uses
/// <c>Entity&lt;ModelEntry&gt;</c> persistence. Falls back to
/// JSON file when no data provider is configured.
/// </summary>
internal interface IModelCatalog
{
    Task<ModelEntry?> GetAsync(string hubId, int? version = null, CancellationToken ct = default);
    Task<IReadOnlyList<ModelEntry>> QueryAsync(Expression<Func<ModelEntry, bool>> predicate, CancellationToken ct = default);
    Task SaveAsync(ModelEntry entry, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Entity-backed catalog. Standard path for all database-connected deployments.
/// </summary>
internal sealed class EntityModelCatalog : IModelCatalog
{
    public Task<ModelEntry?> GetAsync(string hubId, int? version = null, CancellationToken ct = default)
    {
        if (version.HasValue)
            return ModelEntry.Query(m => m.HubId == hubId && m.Version == version.Value)
                .ContinueWith(t => t.Result.FirstOrDefault(), ct);

        // Latest version
        return ModelEntry.Query(m => m.HubId == hubId)
            .ContinueWith(t => t.Result.OrderByDescending(m => m.Version).FirstOrDefault(), ct);
    }

    // ... other operations delegate to Entity<ModelEntry> static methods
}

/// <summary>
/// JSON-file catalog for air-gapped deployments without a database provider.
/// Stores entries in <c>.Koan/models/catalog.json</c>.
/// </summary>
internal sealed class JsonModelCatalog : IModelCatalog
{
    private readonly string _catalogPath;

    public JsonModelCatalog(string basePath)
    {
        _catalogPath = Path.Combine(basePath, ".Koan", "models", "catalog.json");
    }

    // Thread-safe file I/O with optimistic concurrency via file lock
    // Supports basic predicate filtering via in-memory LINQ over deserialized entries
}
```

**Catalog selection at boot:**

```csharp
// In KoanAutoRegistrar for Koan.AI.Models
if (dataProviderAvailable)
    services.AddSingleton<IModelCatalog, EntityModelCatalog>();
else
    services.AddSingleton<IModelCatalog>(sp =>
        new JsonModelCatalog(sp.GetRequiredService<IKoanPathResolver>().BasePath));
```

### Part 8: How Existing Adapters Change

The `Model.*` facade unifies model management that currently lives in individual adapters. Existing adapters declare their capabilities via `IAiAdapter.Capabilities` and the `AdapterResolver` (AI-0022 Part 2) routes lifecycle verbs to the right adapter. No separate `IModelRuntime` implementations needed — the adapter IS the runtime.

**OllamaAdapter evolution:**

```csharp
// Before (current): IAiAdapter + IAiModelManager
// After: IAiAdapter with expanded Capabilities set

internal sealed class OllamaAdapter : IAiAdapter
{
    public string Id => "ollama-local";
    public string Name => "Ollama";
    public string Type => "Ollama";

    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>
    {
        AiCapability.Chat,
        AiCapability.Embed,
        AiCapability.Vision,
        AiCapability.Pull,
        AiCapability.Remove,
        AiCapability.ModelList,
        AiCapability.ServeGGUF,
        AiCapability.Streaming,
        AiCapability.Tools
    };

    public IAiModelManager? ModelManager => _modelManager;

    // Model.Deploy() resolves to this adapter via Serve.GGUF capability,
    // then delegates to ModelManager.EnsureInstalledAsync()
    // Model.Pull("llama3.1:8b") resolves via Pull capability + Ollama tag format
}
```

**LMStudioAdapter declares capabilities:**

```csharp
internal sealed class LMStudioAdapter : IAiAdapter
{
    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>
    {
        AiCapability.Chat,
        AiCapability.Embed,
        AiCapability.ModelList,
        AiCapability.ServeGGUF,
        AiCapability.Streaming
    };
    // No Pull capability — LM Studio model management is external
    // Serve.GGUF — file-based deployment: copy GGUF to models directory
    // Status checks via /v1/models endpoint
}
```

**New HuggingFaceAdapter (Connector pattern):**

The `Koan.AI.Connector.HuggingFace` package follows the same Connector directory pattern as Ollama and LMStudio:

```csharp
internal sealed class HuggingFaceAdapter : IAiAdapter
{
    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>
    {
        AiCapability.Chat,
        AiCapability.Embed,
        AiCapability.Pull,
        AiCapability.Push,
        AiCapability.ModelList,
        AiCapability.ServeSafeTensors,
        AiCapability.Streaming,
        AiCapability.Tools
    };

    // Pull capability: HuggingFaceHubClient for model search and download
    // Push capability: upload models/adapters to HF Hub
    // Serve.SafeTensors: HF Inference API as serving target
}
```

- `HuggingFaceHubClient` for model search and download (used by `Model.Pull()` and `Model.Search()`)
- HF Inference API for `Chat` and `Embed` capabilities
- `Pull` and `Push` capabilities for hub operations
- Located in `src/Connectors/AI/HuggingFace/` alongside Ollama and LMStudio

### Part 9: Configuration

```json
{
  "Koan": {
    "Ai": {
      "Models": {
        "CachePath": ".Koan/models",
        "HuggingFace": {
          "Token": "${HF_TOKEN}",
          "CacheDir": null
        },
        "AutoRegisterDeployed": true,
        "Prune": {
          "Enabled": false,
          "KeepCount": 10,
          "ExcludeTags": ["production", "pinned"]
        }
      }
    }
  }
}
```

**Options class:**

```csharp
namespace Koan.AI.Models;

public sealed class ModelCatalogOptions
{
    public const string SectionName = "Koan:Ai:Models";

    /// <summary>Base path for model cache. Default: ".Koan/models".</summary>
    public string CachePath { get; set; } = ".Koan/models";

    /// <summary>HuggingFace Hub settings.</summary>
    public HuggingFaceOptions HuggingFace { get; set; } = new();

    /// <summary>
    /// When true, models deployed via <c>Model.Deploy()</c> are automatically
    /// registered as AI sources for <c>Client.Chat()</c>/<c>Client.Embed()</c>.
    /// Default: true.
    /// </summary>
    public bool AutoRegisterDeployed { get; set; } = true;

    /// <summary>Automatic pruning settings.</summary>
    public PruneOptions Prune { get; set; } = new();
}

public sealed class HuggingFaceOptions
{
    /// <summary>HuggingFace API token for gated model access. Read from HF_TOKEN env var as fallback.</summary>
    public string? Token { get; set; }

    /// <summary>Override the HF cache directory. Default: uses framework cache path.</summary>
    public string? CacheDir { get; set; }
}

public sealed class PruneOptions
{
    /// <summary>Enable automatic pruning of unused models. Default: false.</summary>
    public bool Enabled { get; set; }

    /// <summary>Number of models to keep (by last-used date). Default: 10.</summary>
    public int KeepCount { get; set; } = 10;

    /// <summary>Tags that prevent a model from being pruned.</summary>
    public string[] ExcludeTags { get; set; } = ["production", "pinned"];
}
```

### Part 10: Boot Report

The `Koan.AI.Models` module reports its status through the standard `IKoanAutoRegistrar.Describe()` pattern:

```
╔══ Module: Koan.AI.Models ═══════════════════════════════════════════════╗
║                                                                         ║
║ Catalog                                                                 ║
║   Storage   → Entity<ModelEntry> (postgres)                            ║
║   Cache     → .Koan/models/ (42.3 GB, 7 models)                       ║
║   HF Hub    → authenticated (token from env:HF_TOKEN)                  ║
║                                                                         ║
║ Adapters (Serve Capabilities)                                           ║
║   ollama-local     │ Serve.GGUF       │ Local  │ CUDA (8 GB) │ healthy ║
║   onnx-local       │ Serve.ONNX       │ Local  │ DirectML    │ healthy ║
║   tgi-gpu-server   │ Serve.SafeTensors│ Remote │ CUDA (80 GB)│ healthy ║
║                                                                         ║
║ Convert Capability                                                      ║
║   SafeTensors → GGUF   │ python-sidecar  │ llama.cpp v3892           ║
║   SafeTensors → ONNX   │ python-sidecar  │ optimum 1.19             ║
║                                                                         ║
║ Auto-prune  → disabled                                                  ║
║                                                                         ║
╚═════════════════════════════════════════════════════════════════════════╝
```

### Part 11: Supporting Types

```csharp
// ── Search ──────────────────────────────────────────────────────────

public record ModelSearchResult
{
    public required string Id { get; init; }
    public required ModelSearchSource Source { get; init; }
    public ModelFormat? Format { get; init; }
    public long? Parameters { get; init; }
    public ModelCapability Capabilities { get; init; }
    public string? Description { get; init; }
    public long? Downloads { get; init; }
    public string? License { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public bool IsLocal { get; init; }
}

public enum ModelSearchSource { Local, HuggingFace, Ollama }

public record ModelSearchFilters
{
    public ModelCapability? Capabilities { get; init; }
    public ModelFormat[]? Formats { get; init; }
    public long? MaxParameters { get; init; }
    public long? MinParameters { get; init; }
    public ModelSearchSource[]? Sources { get; init; }
    public string[]? Tags { get; init; }
    public string? License { get; init; }
}

// ── Pull ────────────────────────────────────────────────────────────

public record PullOptions
{
    /// <summary>Force re-download even if the model exists locally.</summary>
    public bool Force { get; init; }

    /// <summary>Specific revision/commit hash to download.</summary>
    public string? Revision { get; init; }

    /// <summary>Tags to apply to the catalog entry.</summary>
    public string[]? Tags { get; init; }
}

public record ModelPullProgress
{
    public required string Stage { get; init; }     // "downloading", "verifying", "registering"
    public double Percent { get; init; }
    public long? BytesDownloaded { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
}

// ── Inspect ─────────────────────────────────────────────────────────

public record ModelInspection
{
    public required ModelEntry Entry { get; init; }
    public string? License { get; init; }
    public string? Description { get; init; }
    public string? Architecture { get; init; }
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
    public IReadOnlyList<string> DeployedTo { get; init; } = [];
    public IReadOnlyList<DeployRoute> ViableRoutes { get; init; } = [];
}

// ── Deploy ──────────────────────────────────────────────────────────

public record DeployOptions
{
    /// <summary>Target a specific runtime by ID. Default: auto-select.</summary>
    public string? RuntimeId { get; init; }

    /// <summary>Accelerator preference. Default: Any.</summary>
    public Accelerator Accelerator { get; init; } = Accelerator.Any;

    /// <summary>Register as AI source for Client routing. Default: uses ModelCatalogOptions.AutoRegisterDeployed.</summary>
    public bool? RegisterAsSource { get; init; }

    /// <summary>Maximum VRAM budget in bytes. Null: no limit.</summary>
    public long? MaxVramBytes { get; init; }

    /// <summary>Number of concurrent request slots (runtime-dependent).</summary>
    public int? Concurrency { get; init; }
}

public record DeployResult
{
    public required ModelEntry Model { get; init; }
    public required string RuntimeId { get; init; }
    public required string Endpoint { get; init; }
    public string? SourceName { get; init; }    // AI source name if registered
    public TimeSpan LoadTime { get; init; }
}

public record DeployRoute
{
    public required ModelFormat Format { get; init; }
    public required string RuntimeId { get; init; }
    public required string ComputeNode { get; init; }
    public Accelerator Accelerator { get; init; }
    public long? EstimatedVramBytes { get; init; }
    public string[] ConversionSteps { get; init; } = [];
    public string? EstimatedTime { get; init; }
    public int Rank { get; init; }
}

// ── Convert ─────────────────────────────────────────────────────────

public record ConvertOptions
{
    public Quantization? Quantization { get; init; }
    public string? OutputName { get; init; }
    public ConvertOptimization? Optimization { get; init; }
}

public enum ConvertOptimization
{
    None,
    Speed,      // Optimize for inference speed (e.g., operator fusion, graph optimization)
    Size        // Optimize for smallest output
}

public record QuantizeOptions
{
    public string? CalibrationDataset { get; init; }
    public string? OutputName { get; init; }
    public int? GroupSize { get; init; }
}

public record MergeOptions
{
    public MergeStrategy Strategy { get; init; } = MergeStrategy.LoRA;
    public string? OutputName { get; init; }
}

public enum MergeStrategy
{
    LoRA,           // Merge LoRA adapter into base
    Average,        // Weight averaging
    TIES,           // Task-specific interference elimination
    DARE            // Drop and rescale
}

// ── Versioning ──────────────────────────────────────────────────────

public record ModelVersion
{
    public required int Version { get; init; }
    public required string HubId { get; init; }
    public required ModelFormat Format { get; init; }
    public Quantization? Quantization { get; init; }
    public Lineage? Lineage { get; init; }
    public IReadOnlyList<EvalScore>? EvalScores { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DeployedAt { get; init; }
    public DateTimeOffset? RetiredAt { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsStandby { get; init; }
    public bool IsPruned { get; init; }
}

public record ModelAudit
{
    public required ModelVersion Version { get; init; }
    public required IReadOnlyList<ModelVersion> FullChain { get; init; }
    public IReadOnlyList<EvalScore>? EvalScoresAtDate { get; init; }
    public IReadOnlyList<string>? DeployedToAtDate { get; init; }
    public ModelEntry? EntrySnapshot { get; init; }
}

// ── Fleet ───────────────────────────────────────────────────────────

public record FleetSpec
{
    public required IReadOnlyList<FleetModel> Models { get; init; }
    public PlacementStrategy Strategy { get; init; } = PlacementStrategy.BinPack;
}

public record FleetModel(string Id, int Priority = 1, int MinInstances = 1);

public enum PlacementStrategy
{
    BinPack,        // Minimize node count (cost-optimized)
    Spread,         // Maximize redundancy (availability-optimized)
    Affinity        // Co-locate models that are frequently used together
}

public record FleetPlan
{
    public required IReadOnlyList<FleetPlacement> Placements { get; init; }
    public long TotalVramRequired { get; init; }
    public int NodesUsed { get; init; }
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>Apply the plan: deploy/undeploy models to match target state.</summary>
    public Task ApplyAsync(CancellationToken ct = default) => throw new NotImplementedException();
}

public record FleetPlacement
{
    public required string Model { get; init; }
    public required string RuntimeId { get; init; }
    public required string Node { get; init; }
    public long EstimatedVramBytes { get; init; }
    public PlacementAction Action { get; init; }
}

public enum PlacementAction { Deploy, Keep, Undeploy, Migrate }

// ── Lifecycle ───────────────────────────────────────────────────────

public enum ModelStatus
{
    Cached,         // Downloaded, not deployed
    Deployed,       // Actively serving
    Standby,        // Previous version, ready for rollback
    Loading,        // Deployment in progress
    Converting      // Format conversion in progress
}

public record ModelInventory
{
    public required IReadOnlyList<ModelInventoryEntry> Entries { get; init; }
    public long TotalDiskBytes { get; init; }
    public int TotalModels { get; init; }
    public int DeployedCount { get; init; }
}

public record ModelInventoryEntry
{
    public required ModelEntry Model { get; init; }
    public required ModelStatus Status { get; init; }
    public string? RuntimeId { get; init; }
    public TimeSpan? TimeSinceLastUsed { get; init; }
}

public record RemoveResult(string ModelId, long FreedBytes, bool WasDeployed);
public record PruneResult(IReadOnlyList<RemoveResult> Removed, long TotalFreedBytes);

// ── Health & Cost ───────────────────────────────────────────────────

public record ModelHealthReport
{
    public required string ModelId { get; init; }
    public required string RuntimeId { get; init; }
    public required RuntimeModelState State { get; init; }
    public double? LatencyP50Ms { get; init; }
    public double? LatencyP95Ms { get; init; }
    public double? LatencyP99Ms { get; init; }
    public double? ErrorRate { get; init; }
    public long RequestCount { get; init; }
    public TimeSpan Uptime { get; init; }
}

public record ModelCostReport
{
    public required TimeSpan Period { get; init; }
    public required IReadOnlyList<ModelCostEntry> Models { get; init; }
    public long TotalRequests { get; init; }
    public long TotalTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
}

public record ModelCostEntry
{
    public required string ModelId { get; init; }
    public long Requests { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
}

// ── Advisory ────────────────────────────────────────────────────────

public record ModelAdvisorRecommendation
{
    public required AdvisorActionType Type { get; init; }
    public required string ModelId { get; init; }
    public string? SuggestedModel { get; init; }
    public required string Reason { get; init; }
    public string? EstimatedSavings { get; init; }
}

public enum AdvisorActionType
{
    Downsize,       // Replace with smaller model
    Quantize,       // Apply quantization
    Prune,          // Remove unused model
    Consolidate,    // Merge similar deployments
    Upgrade         // Newer version available with better performance
}

// ── Jobs ────────────────────────────────────────────────────────────

public record JobProgress
{
    public required string Stage { get; init; }
    public double Percent { get; init; }
    public string? Detail { get; init; }
    public TimeSpan? Elapsed { get; init; }
    public TimeSpan? EstimatedRemaining { get; init; }
}
```

### Part 12: Packages

| Package | Contents | Dependencies |
|---------|----------|-------------|
| `Koan.AI.Contracts.Shared` | `ModelRef`, `ModelFormat`, `Quantization`, `Lineage`, `JobRef`, `Accelerator`, `EvalScore`, `AiCapability` constants | None |
| `Koan.AI.Models` | `Model.*` facade, `ModelEntry`, `IModelCatalog`, `AdapterResolver` | `Koan.AI.Contracts.Shared`, `Koan.Core` |
| `Koan.AI.Connector.HuggingFace` | `HuggingFaceAdapter` (inference, hub client, search, download, push), Connector pattern | `Koan.AI.Contracts.Shared`, `Koan.Core` |
| `Koan.AI.Models.Onnx` | ONNX Runtime adapter with `Serve.ONNX` + `BatchEmbed` capabilities | `Koan.AI.Models`, `Microsoft.ML.OnnxRuntime` |
| `Koan.AI.Convert.GGUF` | Adapter with `Convert` capability for SafeTensors ↔ GGUF | `Koan.AI.Models` |
| `Koan.AI.Convert.ONNX` | Adapter with `Convert` capability for SafeTensors → ONNX | `Koan.AI.Models` |
| `Koan.AI.Convert.CoreML` | Adapter with `Convert` capability for ONNX → CoreML | `Koan.AI.Models` |
| `Koan.AI.Convert.OpenVINO` | Adapter with `Convert` capability for ONNX → OpenVINO | `Koan.AI.Models` |

All packages follow the **Reference = Intent** principle: adding a package reference to a project activates its functionality through `KoanAutoRegistrar` without explicit registration code.

### Part 13: Integration with Existing Infrastructure

**`IAiModelManager` bridge:**

The existing `IAiModelManager` (implemented by `OllamaAdapter`) is not deprecated. It continues to serve adapters that can provision models. When `AdapterResolver` routes a `Pull` or `Deploy` verb to an adapter, the facade delegates to `IAiModelManager.EnsureInstalledAsync()` on that adapter:

```csharp
// AdapterResolver.Resolve(registry, AiCapability.Pull) → OllamaAdapter
// Model facade calls adapter.ModelManager!.EnsureInstalledAsync()
// AdapterResolver.Resolve(registry, AiCapability.ServeGGUF) → OllamaAdapter
// Model facade calls adapter.ModelManager!.EnsureInstalledAsync() for deployment
// Undeploy delegates to adapter.ModelManager!.FlushAsync()
```

**`AiModelDescriptor` enrichment:**

When an adapter returns `AiModelDescriptor` from `ListModelsAsync()`, the Model catalog cross-references with `ModelEntry` to provide enriched metadata. If no `ModelEntry` exists, one is created lazily with `Origin = ModelOrigin.Ollama` (or the relevant adapter origin).

**ZenGarden integration:**

`ZenGardenModelAdvisor` (existing) provides model recommendations from the orchestrator proxy. The `Model.*` facade consumes these recommendations in `Model.Deploy()` auto-selection and `Model.Advisor()` output. When ZenGarden topology includes compute nodes with GPU capabilities, `Model.Plan()` incorporates them as deployment targets.

**Category routing (AI-0021):**

`Model.Deploy()` creates AI sources that are immediately routable through category configuration:

```json
{
  "Koan": {
    "Ai": {
      "Chat": {
        "Source": "model:llama3.1:8b-q4km"
      }
    }
  }
}
```

Or dynamically via `Client.Scope()`:

```csharp
using (Client.Scope(chat: "model:acme-support:v3"))
{
    var answer = await Client.Chat("How do I return an item?");
}
```

## Consequences

### Positive

- **Models become queryable entities.** `ModelEntry.Query(m => m.Format == ModelFormat.GGUF)` works with any Koan data provider. Version history and lineage are first-class, not afterthoughts.
- **Runtime-agnostic deployment.** Developers think in models, not in "how to configure Ollama" or "how to launch TGI." `Model.Deploy()` resolves the optimal adapter via `Serve.*` capability matching automatically.
- **Format conversion is discoverable.** `Model.Routes()` shows all viable paths. The developer sees that SafeTensors can become GGUF and what tools are needed. No guessing.
- **Lineage enables audit.** `Model.Audit("acme-support", at: lastMonth)` answers "what model was serving production on March 1st, where did it come from, and what eval scores did it achieve?"
- **Capability-driven converters preserve core size.** `Koan.AI.Models` stays lean. Heavy toolchain dependencies (llama.cpp, optimum, coremltools) live in adapter packages that declare `Convert` capability.
- **Air-gapped fallback.** The JSON catalog ensures the Model facade works in disconnected environments, edge deployments, and CI pipelines without a database.
- **Backward compatible.** Existing `IAiModelManager` and `AiModelDescriptor` continue to function. The capability-driven resolution layers on top without breaking existing adapter contracts. Adapters simply declare their `Capabilities` set.
- **Foundation for Training and Eval.** `ModelRef`, `Lineage`, and `ModelEntry` are the shared boundary models that AI-0028 (Training) and AI-0029 (Eval) reference. Building Model first enables clean cross-context communication.

### Negative / Trade-offs

- **Adapter authors must declare `Capabilities` and may need to implement `IAiModelManager`.** Existing adapters (Ollama, LM Studio) need to populate their `Capabilities` set. Adapters that support model lifecycle operations (Pull, Deploy) should implement `IAiModelManager`. Mitigated by clear capability matrix documentation and delegation patterns.
- **HuggingFace Hub dependency.** `Model.Search()` and `Model.Pull()` for HF models require network access and optionally an API token. Mitigated by graceful degradation to local-only results.
- **Container dependency for conversion.** The preferred conversion path uses container images. Environments without Docker/Podman fall back to local toolchains, which require manual installation. Mitigated by clear availability guidance from `Convert`-capable adapters.
- **VRAM estimation is approximate.** `Model.Plan()` and `DeployRoute.EstimatedVramBytes` are heuristics based on parameter count, format, and quantization. Actual VRAM depends on batch size, sequence length, and runtime implementation. The `Warnings` field on `FleetPlan` communicates uncertainty.
- **Catalog divergence risk.** If models are managed outside the `Model.*` facade (e.g., `ollama pull` from CLI), the catalog becomes stale. Mitigated by periodic reconciliation: adapters with `ModelList` capability are queried to cross-check catalog state on health probe cycles.
- **JSON catalog limitations.** The air-gapped `JsonModelCatalog` does not support concurrent writes safely across processes, complex queries, or transactions. It is a single-process fallback, not a production catalog.

## References

- AI-0022: Unified AI Lifecycle Vision (parent vision, bounded context map, phasing)
- AI-0021: Category-Driven AI with Convention Defaults (Client facade, category routing, `IChatAdapter`/`IEmbedAdapter` split)
- AI-0015: Canonical Source-Member Architecture (source/member routing, `AiSourceDefinition`, `AiSourceRegistry`)
- AI-0020: Entity-First AI and Transaction Coordination (`Entity<T>` lifecycle, `[Embedding]` attribute)
- AI-0019: Koan.AI Zero-Config on Microsoft.Extensions.AI (auto-registrar, pipeline foundation)
- AI-0022 Part 2: Capability-Driven Adapter Resolution (`AdapterResolver`, `AiCapability` expansion, capability matrix)
- `src/Koan.AI.Contracts/Adapters/IAiAdapter.cs` — Base adapter identity (extended with `Capabilities` surface)
- `src/Koan.AI.Contracts/Adapters/IAiModelManager.cs` — Existing model provisioning contract (delegates from capability-resolved adapters)
- `src/Koan.AI.Contracts/Models/AiModelDescriptor.cs` — Existing model metadata (enriched by `ModelEntry`)
- `src/Koan.ZenGarden/AI/ZenGardenModelAdvisor.cs` — Existing model advisory (consumed by `Model.Advisor()`)
- `src/Connectors/AI/Ollama/OllamaAdapter.cs` — Reference adapter implementing `IAiModelManager`
- `src/Connectors/AI/LMStudio/LMStudioAdapter.cs` — Reference adapter with `Serve.GGUF` capability
- `src/Connectors/AI/HuggingFace/HuggingFaceAdapter.cs` — Connector pattern adapter with hub operations
