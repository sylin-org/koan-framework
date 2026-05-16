---
id: AI-0022
slug: AI-0022-unified-ai-lifecycle-vision
domain: AI
status: Accepted
date: 2026-03-20
implementation: "All eight lifecycle modules implemented in src/Koan.AI.*/"
---

# ADR: Unified AI Lifecycle — Vision and Capability Expansion

**Contract**

- **Inputs:** Existing Koan.AI inference surface (AI-0021 `Client.*`), existing entity system (`Entity<T>`), existing storage/media pillars (`StorageEntity<T>`, `MediaEntity<T>`), existing ZenGarden topology, HuggingFace/PyTorch/LangChain ecosystem capabilities.
- **Outputs:** Eight new facades (`Model.*`, `Prompt()`, `Compute.*`, `Chain.*`, `Training.*`, `Dataset.*`, `Eval.*`, `Review.*`) covering the full AI lifecycle; lean shared boundary models; six persona-aligned entry points; entity-native closed-loop learning pipeline.
- **Error Modes:** Each facade degrades independently — missing compute doesn't block inference; missing training runtime doesn't block model catalog; missing vector provider doesn't block chains. Each capability reports readiness in the boot report.
- **Acceptance Criteria:** A developer can `Model.Pull()` a HuggingFace model, `Model.Convert()` it to GGUF, `Model.Deploy()` it to Ollama, `Training.Train()` a LoRA adapter from `Dataset.From<Entity>()`, `Eval.Gate()` quality, and serve via `Client.Chat()` — all within the Koan type system, with full lineage, across heterogeneous compute.

**Edge Cases**

- No GPU available anywhere: Training falls back to CPU with performance warning; inference routes to CPU-capable runtimes (Ollama CPU, ONNX CPU provider).
- Network compute unreachable mid-job: Job checkpoints locally; resumes when compute returns; user notified via progress callback.
- `Dataset.From<Entity>()` with zero matching entities: Returns empty dataset with diagnostic; `Training.Train()` rejects with clear message ("Dataset contains 0 examples, minimum 10 required").
- `Model.Convert()` requested but conversion toolchain not installed: Framework checks for container runtime first; if unavailable, checks local Python/llama.cpp; if neither, fails with installation guidance per platform.
- `Model.Convert()` requested but no adapter has the `Convert` capability: Fails with clear error — `"No adapter with 'Convert' capability. Install Koan.AI.Training.Python (Python Sidecar) to enable format conversion."` No silent fallback; no guessing.
- `Prompt.Load()` with no matching `PromptEntry`: Falls back to inline prompt string; logs dev-mode guidance.

## Context

Koan.AI (AI-0001 through AI-0021) provides a mature inference surface: `Client.Chat()`, `Client.Embed()`, `Client.Ocr()`, `Client.Stream()`, `Client.Scope()`, with entity-first embedding via `[Embedding]`, multi-provider routing via source-member architecture (AI-0015), and convention-driven defaults (AI-0021). The framework excels at **consuming AI** — sending prompts, receiving responses, storing vectors.

However, the broader AI lifecycle remains unaddressed:

1. **Model management is fragmented.** Models are referenced as opaque strings (`"llama3.1:8b"`). No catalog, no versioning, no lineage. Converting between formats (SafeTensors → GGUF → ONNX) requires manual tooling. Deploying a model to a runtime is a multi-step manual process.

2. **Training is out of scope.** AI-0001 explicitly scoped out training. Production data in Koan entities cannot become training data without manual ETL. The most valuable data — the data already modeled in `Entity<T>` — is inaccessible to ML workflows.

3. **Compute is inference-only.** The source-member architecture routes inference requests. Training, conversion, and evaluation workloads have no compute abstraction. Heterogeneous hardware (CUDA, ROCm, Metal, DirectML) is not addressed.

4. **Composition is ad-hoc.** RAG patterns, multi-step chains, and structured extraction are implemented per-application (S6.SnapVault's `PhotoProcessingService`, S7.Meridian's `SchemaGuidedExtractor`). No reusable composition primitives exist.

5. **Quality enforcement is manual.** No gates between training and deployment. No regression detection. No drift monitoring. Models reach production without automated evaluation.

6. **The feedback loop is open.** Users interact with AI outputs, but their feedback (ratings, corrections, behavior) doesn't flow back into training without custom ETL pipelines.

Three external ecosystems offer capabilities Koan needs but doesn't have: **HuggingFace** (model hub, training, tokenization, inference infrastructure), **PyTorch** (training loops, quantization, format conversion), and **LangChain/LangGraph** (composition, agents, retrieval strategies, workflows). Integrating these as first-class capabilities — while maintaining Koan's premium ergonomics — is the objective.

### The Differentiator

Koan's unique advantage is not in training (PyTorch), model hosting (HuggingFace), or orchestration (LangChain). It is in the **entity-native data bridge**:

- `Entity<T>` is simultaneously the production data model AND the training data source.
- `Dataset.From<Entity>()` eliminates ETL between application and ML.
- `[Embedding]` owns the embedding lifecycle (versioning, staleness, re-indexing).
- `MediaEntity<T>` + `[MediaAnalysis]` extends this to images, audio, and documents via the existing storage pillar.
- The closed loop — model serves → users interact → entities capture feedback → feedback trains → model improves — is possible because all participants share the same type system.

No other framework provides this. The design principle: **build deep where entity-awareness is the value; build thin or interop where orchestration maturity is the value.**

## Decision

### Part 1: Bounded Contexts and Shared Models

The expansion is organized as **eight bounded contexts**, each with a dedicated facade, communicating through **lean shared boundary models**. Proper SoC/DDD: no context imports another's internals; cross-boundary references use shared models only.

#### Shared Boundary Models

These models are semantically meaningful, lean, and reused across all contexts:

```csharp
// ── Model identity (shared by: Model, Training, Eval, Chain, Client) ──

public sealed record ModelRef(string Id, int? Version = null)
{
    // "meta-llama/Llama-3.1-8B-Instruct"
    // "acme-support:v3"
    // "BAAI/bge-large-en-v1.5"
    public static implicit operator ModelRef(string id) => new(id);
}

// ── Dataset identity (shared by: Dataset, Training, Eval) ──

public sealed record DatasetRef(string Id, string? Hash = null);

// ── Compute requirement (shared by: Model, Training, Eval) ──

public sealed record ComputeRequirement(
    Accelerator Accelerator = Accelerator.Any,
    long? MinVramBytes = null,
    ComputeLocation? Location = null,
    string? PreferredNode = null);

// ── Job lifecycle (shared by: Training, Model.Convert, Eval) ──

public sealed record JobRef(string Id, JobStatus Status);

public enum JobStatus { Queued, Running, Completed, Failed, Cancelled }

// ── Eval scores (shared by: Eval, Model lineage, Pipeline gates) ──

public sealed record EvalScore(string Metric, double Value, double? Baseline = null);

public sealed record EvalResult(
    ModelRef Model,
    IReadOnlyList<EvalScore> Scores,
    bool Passed,
    string? Reason = null);

// ── Lineage (shared by: Model, Training, Eval) ──

public sealed record Lineage(
    ModelRef? Base = null,
    string? Method = null,
    DatasetRef? Data = null,
    IReadOnlyList<EvalScore>? EvalScores = null,
    string? TrainedBy = null,
    DateTimeOffset? TrainedAt = null,
    string? Notes = null);

// ── Accelerator (shared by: Compute, Model, Training) ──

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

// ── Model format (shared by: Model, Training) ──

public enum ModelFormat
{
    SafeTensors,
    GGUF,
    ONNX,
    PyTorch,
    CoreML,
    OpenVINO
}
```

#### Bounded Context Map

```
┌─────────────┐    ModelRef     ┌─────────────┐    ModelRef     ┌─────────────┐
│  Training    │ ──────────────►│   Model      │◄────────────── │   Eval       │
│  Dataset     │    JobRef      │   Catalog    │    EvalResult  │   Gate       │
│              │◄──────────────│              │ ──────────────►│              │
└──────┬───────┘                └──────┬───────┘                └──────────────┘
       │ DatasetRef                    │ ModelRef                       ▲
       ▼                               ▼                               │ EvalResult
┌─────────────┐                ┌─────────────┐                ┌──────────────┐
│  Review      │──entity──────►│  Compute     │◄──Compute─────│  Pipeline    │
│  Queues      │  updates      │  Fabric      │  Requirement  │  (composes   │
└─────────────┘                └─────────────┘                │  the above)  │
       ▲                               │                       └──────────────┘
       │ entity                        │ routes to
       │ feedback                      ▼
┌─────────────┐                ┌─────────────┐
│  Chain       │──ModelRef────►│  Client      │   (existing, AI-0021)
│  Prompt      │               │  Inference   │
└─────────────┘                └─────────────┘
```

Cross-boundary communication uses **domain events** on the Koan event bus:

- `TrainingCompleted` → Model context registers new `ModelEntry`
- `ModelDeployed` → Client context updates routing tables
- `ReviewCompleted` → Entity updated; next `Dataset.From<T>()` includes feedback
- `EvalGateFailed` → Pipeline halts; notification emitted

### Part 2: Capability-Driven Adapter Resolution

All lifecycle verbs — `Model.Pull()`, `Model.Convert()`, `Model.Deploy()`, `Training.Train()`, `Eval.Measure()` — resolve to the right adapter through a single, unified pattern: adapters declare capabilities via string flags, and the framework queries those capabilities at invocation time. There are no separate abstractions for sourcing, deployment, conversion, or training runtimes — the adapter IS the provider.

#### Expanded `AiCapability` Constants

The existing `AiCapability` string constants (AI-0021: `Chat`, `Embed`, `Ocr`, `Vision`, etc.) are extended to cover the full lifecycle:

```csharp
// In Koan.Core.AI
public static class AiCapability
{
    // ── Inference (existing, AI-0021) ──
    public const string Chat = "Chat";
    public const string Embed = "Embed";
    public const string Ocr = "Ocr";
    public const string Vision = "Vision";
    public const string Transcribe = "Transcribe";
    public const string Tools = "Tools";
    public const string Streaming = "Streaming";
    public const string JsonMode = "JsonMode";
    public const string BatchEmbed = "BatchEmbed";

    // ── Model Lifecycle (new) ──
    public const string Pull = "Pull";
    public const string Push = "Push";
    public const string Remove = "Remove";
    public const string ModelList = "ModelList";

    // ── Format Support (new) ──
    public const string ServeGGUF = "Serve.GGUF";
    public const string ServeSafeTensors = "Serve.SafeTensors";
    public const string ServeONNX = "Serve.ONNX";
    public const string Convert = "Convert";
    public const string Quantize = "Quantize";

    // ── Training (new) ──
    public const string Train = "Train";
    public const string Align = "Align";

    // ── Evaluation (new) ──
    public const string MetricCompute = "MetricCompute";
}
```

**Why strings, not an enum:** Matches the existing `AiSourceDefinition.Capabilities` pattern from AI-0015. Strings are extensible — custom adapters can declare capabilities (`"MyCompany.CustomInference"`) without framework changes. Enums would require a framework release for every new capability.

#### `IAiAdapter.Capabilities` Surface

Every adapter declares the set of capabilities it supports. This is the **single surface** that all resolution flows query:

```csharp
public interface IAiAdapter
{
    string Id { get; }
    string Name { get; }
    string Type { get; }
    IReadOnlySet<string> Capabilities { get; }  // NEW
    bool HasCapability(string capability) => Capabilities.Contains(capability);
    IAiModelManager? ModelManager => null;
    Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken ct = default);
}
```

#### `AdapterResolver` — Universal Resolution

One resolver, one pattern, used by every facade verb:

```csharp
internal static class AdapterResolver
{
    public static IAiAdapter Resolve(
        IAiAdapterRegistry registry,
        string capability,
        string? target = null)
    {
        if (target is not null)
            return registry.Get(target) ?? throw new UnknownAdapterException(target);

        var candidates = registry.All
            .Where(a => a.HasCapability(capability))
            .ToList();

        return candidates.Count switch
        {
            0 => throw new InvalidOperationException(
                $"No adapter with '{capability}' capability. " +
                $"Install a connector package that provides it."),
            1 => candidates[0],
            _ => throw new AmbiguousAdapterException(capability,
                candidates.Select(a => a.Id).ToList())
        };
    }
}
```

**Resolution rules (identical for ALL verbs):**

1. **Explicit target** (`to:` parameter) → look up adapter by ID directly.
2. **One adapter** with the capability → use it (unambiguous, zero-config).
3. **Multiple adapters** → if only one can handle the specific model ID → use it.
4. **Still ambiguous** → `AmbiguousAdapterException` with candidate list; developer adds `to:` parameter.
5. **Zero adapters** → clear error naming the missing capability and suggesting a package.

#### Adapter Capability Matrix

| Adapter | Package | Capabilities |
|---------|---------|-------------|
| Ollama | `Koan.AI.Connector.Ollama` | `Chat`, `Embed`, `Vision`, `Pull`, `Remove`, `ModelList`, `Serve.GGUF`, `Streaming`, `Tools` |
| HuggingFace | `Koan.AI.Connector.HuggingFace` | `Chat`, `Embed`, `Pull`, `Push`, `ModelList`, `Serve.SafeTensors`, `Streaming`, `Tools` |
| LM Studio | `Koan.AI.Connector.LMStudio` | `Chat`, `Embed`, `ModelList`, `Serve.GGUF`, `Streaming` |
| ONNX Runtime | `Koan.AI.Models.Onnx` | `Embed`, `Serve.ONNX`, `BatchEmbed` |
| Python Sidecar | `Koan.AI.Training.Python` | `Train`, `Align`, `Convert`, `Quantize`, `Serve.SafeTensors` |
| TGI | `Koan.AI.Connector.TGI` | `Chat`, `Streaming`, `Tools`, `Serve.SafeTensors` |
| TEI | `Koan.AI.Connector.TEI` | `Embed`, `BatchEmbed`, `Serve.SafeTensors` |

#### How Facade Verbs Use Resolution

Every facade verb delegates to `AdapterResolver.Resolve()` with the appropriate capability:

```csharp
// Model.Pull("meta-llama/Llama-3.1-8B-Instruct")
//   → AdapterResolver.Resolve(registry, AiCapability.Pull)
//   → Only HuggingFace adapter has Pull for HF IDs → unambiguous

// Model.Pull("llama3.1:8b")
//   → AdapterResolver.Resolve(registry, AiCapability.Pull)
//   → Both Ollama and HF have Pull → model ID format disambiguates (Ollama tag syntax)

// Model.Convert(model, to: ModelFormat.GGUF)
//   → AdapterResolver.Resolve(registry, AiCapability.Convert)
//   → Only Python Sidecar has Convert → unambiguous

// Model.Deploy(model)
//   → AdapterResolver.Resolve(registry, AiCapability.ServeGGUF)  // based on model format
//   → Ollama and LM Studio both have Serve.GGUF → developer adds to: "ollama-local"

// Training.Train(options)
//   → AdapterResolver.Resolve(registry, AiCapability.Train)
//   → Only Python Sidecar has Train → unambiguous

// Eval.Measure(model, data, metrics)
//   → AdapterResolver.Resolve(registry, AiCapability.MetricCompute)
//   → Only Python Sidecar has MetricCompute → unambiguous
```

#### `AmbiguousAdapterException`

When multiple adapters match and the model ID does not disambiguate, the exception provides actionable guidance:

```csharp
public class AmbiguousAdapterException : InvalidOperationException
{
    public string Capability { get; }
    public IReadOnlyList<string> CandidateAdapterIds { get; }

    // Message: "Multiple adapters support 'Serve.GGUF': [ollama-local, lmstudio-local].
    //           Specify the target adapter: Model.Deploy(model, to: \"ollama-local\")"
}
```

This pattern eliminates `IModelSourceProvider`, `IModelRuntime`, `IFormatConverter`, `ITrainingRuntime`, and `IMetricComputer` as separate abstractions. The adapter IS the provider — it declares what it can do, and the resolver finds the right one.

### Part 3: Personas and Facade Alignment

Six personas use the system. Each has a **home facade** where they spend 80% of their time. All facades share the same entities, catalog, and compute fabric.

| Persona | Role | Home Facades | Entry Point |
|---------|------|-------------|-------------|
| **Software Engineer** | Builds features that use AI | `Client.*`, `Chain.*` | `[Embedding]`, `Client.Chat()` |
| **AI Scientist** | Experiments, trains, evaluates | `Training.*`, `Dataset.*`, `Eval.*` | `Dataset.From<T>()`, `Training.Train()` |
| **Model Specialist** | Manages model lifecycle, fleet | `Model.*` | `Model.Pull()`, `Model.Deploy()` |
| **AI Integration Specialist** | Guards production quality | `Eval.*`, `Pipeline.*` | `Eval.Gate()`, `Model.History()` |
| **Domain Expert** | Reviews AI outputs, edits prompts | `Review.*`, `Prompt` (no code) | Review queue UI, prompt editing |
| **Platform Engineer** | Manages compute infrastructure | `Compute.*` | `Compute.Fleet()`, `Model.Health()` |

Every facade follows the **progressive disclosure ladder**:

| Tier | Pattern | Cognitive Load | Example |
|------|---------|---------------|---------|
| 0 | Convention | Zero | `[Embedding]` → search works |
| 1 | One-liner | One decision | `await Client.Chat(question)` |
| 2 | Options | Few decisions | `new TrainOptions { Method, Rank }` |
| 3 | Rich result | Read metadata | `job.Output.Lineage` |
| 4 | Escape hatch | Full control | `Training.Run(script: "custom.py")` |

### Part 4: `Model.*` — Centralized Model Lifecycle (AI-0023)

Models are **first-class entities** with lifecycle, lineage, and version history. A model is not "an Ollama model" — it is a Koan model that can be sourced, converted, and deployed to any compatible runtime.

**Facade verbs:** `Search`, `Pull`, `Inspect`, `Convert`, `Quantize`, `Deploy`, `Merge`, `Register`, `Publish`, `History`, `Rollback`, `Routes`, `Plan`, `Inventory`, `Health`, `Prune`, `Advisor`.

**Entity:**

```csharp
public class ModelEntry : Entity<ModelEntry>
{
    public string HubId { get; set; }
    public int Version { get; set; }
    public ModelRef? Base { get; set; }
    public ModelFormat Format { get; set; }
    public long Parameters { get; set; }
    public int? ContextWindow { get; set; }
    public int? EmbeddingDim { get; set; }
    public Quantization? Quantization { get; set; }
    public ModelCapability[] Capabilities { get; set; }
    public Lineage? Lineage { get; set; }
    public string? LocalPath { get; set; }
    public long DiskSizeBytes { get; set; }
    public string[] DeployedTo { get; set; }
    public string[] Tags { get; set; }
    public DateTime? LastUsed { get; set; }
    public ModelOrigin Origin { get; set; }
}
```

**Key patterns:**

```csharp
// Platform-agnostic verbs — runtime resolved, not specified
await Model.Pull("BAAI/bge-large-en-v1.5");             // HF Hub
await Model.Pull("llama3.1:8b");                         // Ollama library
await Model.Convert(model, to: ModelFormat.GGUF);        // Toolchain resolved
await Model.Deploy(model);                               // Runtime auto-selected
await Model.Rollback("acme-support", to: "v3");          // Instant version swap

// Model.Routes() — shows viable paths for a model
var routes = await Model.Routes("BAAI/bge-large-en-v1.5");
// → [{Format: ONNX, Runtime: OnnxLocal, EstTime: "12min"}, ...]

// Model.Plan() — optimal fleet-wide placement
var plan = await Model.Plan(fleetSpec);
await plan.Apply();

// Escape hatch: register externally-trained models
await Model.Register(path: "/results/my-model/", lineage: ...);
```

**Runtime resolution:** Adapters declare `Serve.*` capabilities (`Serve.GGUF`, `Serve.SafeTensors`, `Serve.ONNX`) and the `AdapterResolver` (Part 2) matches models to adapters by format. No separate `IModelRuntime` interface — the adapter IS the runtime.

**Format conversion:** Resolved via `AdapterResolver.Resolve(registry, AiCapability.Convert)`. Adapters with the `Convert` capability (e.g., Python Sidecar) handle conversion. Submitted as jobs. Conversion graph: SafeTensors ↔ GGUF, SafeTensors → ONNX, ONNX → CoreML, etc.

Full specification in **AI-0023**.

### Part 5: `Prompt()` — Uri-Inspired Prompt Primitive (AI-0025)

Prompts are values with structure, like `Uri`. A string goes in; a rich, inspectable, immutable object comes out. Variables are extracted. The prompt round-trips losslessly.

**Construction:**

```csharp
// From string (shallow parse: extract {variables}, store raw text)
var prompt = Prompt("You are a {role}. Answer questions about {product}. Be concise.");
prompt.Variables   // ["role", "product"]
prompt.Raw         // Original string

// From builder (structured parts)
var prompt = Prompt(p => p
    .System("You are a {role}")
    .Instruct("Answer questions about {product}")
    .Constrain("Be concise", "Max 3 sentences")
    .OutputAs<SupportResponse>());

// From catalog (entity-backed, versionable)
var prompt = await Prompt.Load("support-response");
```

**Resolution:**

```csharp
// Variables resolved from anonymous object, entity, or dictionary
var text = prompt.Resolve(new { role = "support agent", product = "Widget" });

// Direct use with Client
await Client.Chat(prompt, new { role = "analyst", product = "Koan" });
await Client.Chat<Summary>(prompt, article);  // Typed response + variable resolution
```

**`PromptEntry` entity** enables non-technical editing, versioning, and A/B testing without code deploys. Domain experts (Marta persona) edit prompts; scientists measure which version performs better via `Eval.Compare()`.

Full specification in **AI-0025**.

### Part 6: `Compute.*` — Hardware-Agnostic Fabric (AI-0024)

Compute extends ZenGarden's topology discovery to include hardware capabilities. Work (training, conversion, evaluation) is described as **intent**; the fabric resolves **where** it runs.

**Discovery:**

```csharp
var resources = await Compute.Available();
// → [{Id: "local", Accelerator: DirectML, VRAM: 8GB},
//    {Id: "gpu-server", Accelerator: CUDA, VRAM: 80GB, Location: Network}]
```

**Routing:**

```csharp
// Transparent — verbs route automatically
await Training.Train(options);
// → Compute resolves: 8B model + QLoRA needs ~18GB. Local has 8GB. Route to gpu-server.

// Explicit targeting when needed
await Training.Train(options, compute: "gpu-server");
await Training.Train(options, compute: Compute.Require(minVram: GiB(48)));
```

**`Accelerator` enum** abstracts GPU vendors. The public API never says "CUDA" — it says `Accelerator.Any`, and the runtime resolves to CUDA, ROCm, Metal, or DirectML based on what's available.

**`Koan.AI.Worker`** — a lightweight service that runs on compute-capable machines, registers with ZenGarden, and accepts delegated work. Work delegation uses the **filesystem contract**:

```
.koan/jobs/{job-id}/
├── input/         ← Framework stages: data, model weights, recipe
├── output/        ← Script writes: results, metrics, checkpoints
├── progress.jsonl ← Script appends: progress lines (optional)
└── status         ← Framework reads: running, completed, failed
```

Full specification in **AI-0024**.

### Part 7: `Chain.*` — Composition Primitives (AI-0026)

Chains compose AI operations into typed, immutable pipelines. Inspired by LangChain's LCEL but native to Koan's type system and entity model.

**Core verbs:** `.Chat()`, `.Retrieve<T>()`, `.Parse<T>()`, `.Classify()`, `.Branch()`, `.Parallel()`, `.Rerank()`, `.Compress()`, `.Moderate()`.

```csharp
// RAG chain with entity-aware retrieval
var rag = Chain.Create()
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5, rerank: true)
    .Compress()
    .System("Answer from context only. Cite sources.")
    .Chat("{question}\n\nContext:\n{context}")
    .Parse<AnswerWithCitations>();

var answer = await rag.Run(new { question = "What is the refund policy?" });

// Web endpoint in one line
app.MapChain("/api/ask", rag);
```

Chains are **immutable blueprints** — `.Run()` creates an execution. This enables reuse, testing, and serialization. Chains integrate with `Prompt.Load()` for named, versionable prompts.

**Scope:** Core RAG, branching, structured output, streaming. Agent orchestration and stateful workflows **deferred** to future ADRs — the principle: build where entity-awareness is the value; interop where orchestration maturity is the value.

Full specification in **AI-0026**.

### Part 8: `[MediaAnalysis]` — AI for Storage/Media Entities (AI-0027)

Extends the existing `MediaEntity<T>` (MEDIA-0001) and `StorageEntity<T>` (STOR-0001) with automatic AI processing, mirroring how `[Embedding]` (AI-0020) auto-embeds on save.

```csharp
[StorageBinding(Profile = "cold", Container = "photos")]
[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr, Async = true)]
[Embedding]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string? AiDescription { get; set; }   // Auto-populated by Describe
    public string? OcrText { get; set; }         // Auto-populated by Ocr
    public float[]? Embedding { get; set; }      // Auto-populated by [Embedding]
}
```

**Pipeline:** `MediaEntity.Upload()` → bytes stored via `IStorageProvider` → `[MediaAnalysis]` reads bytes via `OpenRead()` (cache-aware via `ReplicatedStorageProvider`) → vision/OCR/transcription → results written to entity properties → `[Embedding]` feeds on analysis text → entity + vector saved atomically.

**`[MediaAnalysis]` feeds `[Embedding]`:** The vision description becomes embedding text. Cross-modal search works because the image is represented as text in the vector space.

**Supported operations:** `Describe` (vision), `Ocr` (text extraction), `Transcribe` (audio/video), `Classify` (categorization), `Extract` (structured extraction via named `Prompt`).

**Custom extraction** via Prompt integration: `[MediaAnalysis(Prompt = "receipt-extractor")]` loads a `PromptEntry` entity. Domain experts edit the extraction prompt without code deploys.

Full specification in **AI-0027**.

### Part 9: `Training.*` and `Dataset.*` — The Entity-Native Bridge (AI-0028)

The core differentiator. Production entities become training data without ETL.

**`Dataset.From<Entity>()`:**

```csharp
var dataset = Dataset.From<SupportTicket>(
    where: t => t.Resolved && t.Rating >= 4,
    input: t => t.Question,
    output: t => t.Resolution);
```

- The query is **live** — tomorrow's run includes today's new tickets.
- The schema is **compiler-enforced** — renamed properties are caught at build time.
- Lineage is **automatic** — entity type, query, field mapping recorded.

**Training verbs with escape hatches:**

| Level | Verb | Who Controls the Loop |
|-------|------|-----------------------|
| 1 | `Training.Train()` | Framework (built-in recipe) |
| 2 | `Training.Train()` with overrides | Framework + user scripts for specific components |
| 3 | `Training.Run()` | User's Python script; framework handles infra |
| 4 | `Model.Register()` | External process; user registers result only |

```csharp
// Level 1: Framework controls training
var job = await Training.Train(
    "meta-llama/Llama-3.1-8B-Instruct",
    dataset,
    method: TrainMethod.LoRA);

// Level 3: User controls training, framework controls infra
var job = await Training.Run(
    script: "./training/custom_dpo.py",
    base: "meta-llama/Llama-3.1-8B-Instruct",
    data: dataset,
    compute: Compute.Require(minVram: GiB(48)));

// Level 4: Register externally-trained model
await Model.Register(path: "/results/custom/", lineage: ...);
```

**All levels produce the same artifacts:** `ModelRef` in the catalog, lineage chain, eval-ready output. The escape hatch doesn't abandon the framework — it replaces the inner loop while keeping the outer infrastructure (compute routing, progress streaming, artifact collection, lineage recording).

**Training runtime:** Container-first (`koan/trainer:cuda`, `koan/trainer:rocm`, `koan/trainer:cpu`). Local Python as opt-in fallback. The container provides hermetic, reproducible environments and eliminates dependency conflicts.

**`Training.Compare()`:** Run N variations against the same data and eval set, get a ranked table. Compute auto-distributes across available GPUs.

Full specification in **AI-0028**.

### Part 10: `Eval.*` — Quality Enforcement (AI-0029)

**Core verbs:** `Measure`, `Compare`, `Regress`, `Gate`, `Drift`, `Judge`.

```csharp
// Measure: score a model against a dataset
var scores = await Eval.Measure(model, data: testSet,
    metrics: [Metric.RougeL, Metric.Faithfulness]);

// Gate: block deployment if thresholds not met (throws on failure)
await Eval.Gate(model, baseline: "acme-support:current",
    data: goldenSet,
    require: g => g
        .Metric(Metric.RougeL, min: 0.85)
        .NoRegression(tolerance: 0.02));

// Drift: detect input distribution changes using entity queries
var drift = await Eval.Drift(
    baseline: Dataset.From<SupportTicket>(where: t => t.CreatedAt > trainDate),
    current: Dataset.From<SupportTicket>(where: t => t.CreatedAt > lastWeek));

// Compare: side-by-side model evaluation
var comparison = await Eval.Compare(
    models: ["acme-support:v3", "acme-support:v4"],
    data: testSet,
    metrics: [Metric.RougeL, Metric.Latency]);
```

**`Eval.Gate()`** is the deployment blocker. It throws `GateFailedException` if conditions aren't met. `EvalResult` (shared boundary model) flows into `ModelEntry.Lineage` — quality scores are permanently recorded.

**Entity-native evaluation:** `Dataset.From<Entity>()` as evaluation data. The same entity type provides training data, evaluation data, and drift baselines.

Full specification in **AI-0029**.

### Part 11: `Review.*` — Human Feedback Loop (AI-0030)

Closes the loop between AI output and training data. Domain experts review, approve, correct, and label AI outputs — directly on entities.

```csharp
var queue = Review.Create<SupportTicket>(
    name: "AI Response Quality",
    where: t => t.AiResponse != null && t.ReviewStatus == ReviewStatus.Pending,
    display: t => new { t.Question, t.AiResponse, t.Category },
    actions: [
        Review.Approve(),
        Review.Reject(requireReason: true),
        Review.Edit(field: t => t.AiResponse),
        Review.Label(field: t => t.Quality, options: [1, 2, 3, 4, 5])
    ]);
```

**API-only surface.** Generates review-specific endpoints. UI is the consumer's responsibility.

When Marta approves or corrects a response, the entity is updated. Riku's `Dataset.From<SupportTicket>(where: t => t.ReviewStatus == Approved)` automatically includes Marta's reviewed data. The feedback loop closes without ETL.

**Corrections as alignment data:**

```csharp
var alignment = Dataset.From<SupportTicket>(
    where: t => t.ReviewStatus == ReviewStatus.Edited,
    prompt: t => t.Question,
    chosen: t => t.EditedResponse,
    rejected: t => t.OriginalAiResponse);
// → DPO training pairs from human corrections
```

Full specification in **AI-0030**.

### Part 12: Strategic Opportunities (Future ADRs)

Identified during analysis, deferred to dedicated ADRs:

| Opportunity | Concept | Strategic Value |
|-------------|---------|-----------------|
| **`Signal.From<T,U>()`** | Implicit behavioral signals as training data | The Google/Amazon advantage — train on user behavior, not explicit labels |
| **`Model.Advisor()`** | Cost optimization from traffic analysis | "Use a fine-tuned 8B instead of cloud API — save $14K/year" |
| **`Chain.Gaps()`** | RAG blind spot detection | "47 queries about Shopify integration — no docs found" |
| **`Dataset.Generate()`** | Synthetic data seeded from entity examples | Solve cold-start with strong-model-generated, human-validated data |
| **`Agent.*`** | Entity-aware autonomous agents | `agent.WithEntities<Product, Order>()` auto-generates CRUD tools |
| **`Graph.*`** | Stateful multi-actor workflows | HITL, persistence, checkpointing for ML pipelines |
| **`Client.Chat<T>()`** | Typed inference with JSON schema constraint | `await Client.Chat<Sentiment>(text)` — compiler-enforced structured output |

**Principle for scope:** Build where entity-awareness is the value. Interop where orchestration maturity is the value. Agent and Graph are deferred pending evaluation of whether Koan-native implementations outperform LangGraph interop.

### Part 13: Package Structure

```
Koan.AI.Contracts.Shared       ← Shared boundary models (ModelRef, JobRef, EvalScore, etc.)
Koan.AI                        ← Client facade, routing, pipeline (exists)
Koan.AI.Contracts              ← Adapter interfaces, options (exists)
Koan.AI.Models                 ← Model.* facade, ModelEntry entity, catalog
Koan.AI.Models.Onnx            ← ONNX Runtime adapter, in-process inference
Koan.AI.Prompt                 ← Prompt() type, PromptEntry entity, builder
Koan.AI.Compute                ← Compute.* facade, discovery, routing
Koan.AI.Compute.Worker         ← Koan.AI.Worker service (runs on GPU machines)
Koan.AI.Orchestration          ← Chain.* facade, retrieval strategies
Koan.AI.Training               ← Training.*, Dataset.* facades, job engine
Koan.AI.Training.Container     ← Container runtime for training jobs
Koan.AI.Training.Python        ← Local Python sidecar fallback
Koan.AI.Eval                   ← Eval.* facade, metrics, gates
Koan.AI.Review                 ← Review.* facade, queue endpoints
Koan.AI.Web                    ← HTTP controllers (exists, extended)
Koan.AI.Connector.Ollama       ← Extended with model management (exists)
Koan.AI.Connector.LMStudio     ← Extended with model management (exists)
Koan.AI.Connector.HuggingFace  ← HF Hub client, Inference API, model search/download (Connector pattern)
Koan.AI.Connector.TGI          ← Text Generation Inference adapter
Koan.AI.Connector.TEI          ← Text Embeddings Inference adapter
Koan.AI.Connector.TorchServe   ← TorchServe REST adapter
```

Extension packages (user-installed, detected at runtime):

```
Koan.AI.Convert.GGUF           ← llama.cpp-based GGUF conversion
Koan.AI.Convert.ONNX           ← optimum-based ONNX conversion
Koan.AI.Convert.CoreML         ← coremltools-based CoreML conversion
```

### Part 14: Phasing

| Phase | ADR | Deliverables | Dependency |
|-------|-----|-------------|------------|
| P1 | AI-0023 | `Model.*` (Catalog, Pull, Inspect, Deploy, History) | None |
| P2 | AI-0025 | `Prompt()` type, `PromptEntry` entity, builder | None |
| P3 | AI-0024 | `Compute.*` (Discovery, Fleet, Routing) | AI-0023 |
| P4 | AI-0026 | `Chain.*` (Compose, Retrieve, Parse, Branch) | AI-0025 |
| P5 | AI-0027 | `[MediaAnalysis]` attribute | AI-0023, MEDIA-0001 |
| P6 | AI-0028 | `Dataset.From<T>()`, `Training.Train/Run()` | AI-0023, AI-0024 |
| P7 | AI-0029 | `Eval.*` (Measure, Gate, Regress, Drift) | AI-0023, AI-0028 |
| P8 | AI-0030 | `Review.*` (Queues, Actions) | None |
| P9 | Future | `Signal.From<T,U>()`, `Model.Advisor()`, `Chain.Gaps()` | AI-0028, AI-0029 |
| P10 | Future | `Agent.*` / `Graph.*` (build vs interop decision) | AI-0026 |

### Part 15: The Closed Loop

The complete cycle, touching every bounded context:

```
1. Priya builds app with Entity<SupportTicket>        [Client, Chain]
2. Users interact, rate responses                      [entities updated]
3. Marta reviews AI outputs, corrects errors           [Review]
4. Riku trains on reviewed + rated entities            [Dataset, Training]
5. Eval gates ensure quality                           [Eval]
6. Jun deploys to optimal runtime                      [Model]
7. Dana monitors, detects drift, triggers retrain      [Eval, Pipeline]
8. Loop back to step 1 — model serves better           [Client]

All steps share: Entity<T>, ModelRef, Prompt, Compute, Lineage.
No ETL. No export. No separate systems. One type system.
```

## Consequences

### Positive

- **Entity-native ML bridge** — the unique differentiator no other framework provides.
- **Premium DX maintained** — progressive disclosure from one-liner to escape hatch.
- **Six personas served** — each with a natural entry point, all sharing the same system.
- **Hardware-agnostic** — `Accelerator` abstraction enables CUDA, ROCm, Metal, DirectML parity.
- **Network-native compute** — ZenGarden extension enables GPU sharing across an organization.
- **Proper SoC/DDD** — bounded contexts communicate through lean shared models.
- **Incremental adoption** — teams can start at Level 4 (register external models) and graduate to Level 1 (managed training).
- **Escape hatches preserve infrastructure value** — custom scripts still get compute routing, progress streaming, artifact collection, and lineage recording.

### Negative / Trade-offs

- **Scope is large.** Ten phases spanning 9 ADRs. Requires disciplined phasing.
- **Python dependency** for training and conversion. Container-first mitigates environment issues but adds Docker/Podman as a soft dependency.
- **Format conversion relies on external tools** (llama.cpp, optimum, coremltools). Extension package model means some capabilities require additional installation.
- **Compute routing estimates are imperfect.** VRAM requirements depend on batch size, sequence length, and framework version. Advisory by default, automatic as opt-in.
- **Review surface is API-only.** Requires frontend implementation for domain expert usage. Reference sample planned.
- **Agent/Graph deferred.** Teams needing agentic workflows must use LangChain/LangGraph directly until a future ADR addresses interop or native implementation.

## References

- AI-0001: Native AI Baseline (scope definition)
- AI-0015: Canonical Source-Member Architecture (routing model extended by Compute)
- AI-0019: Koan.AI Zero-Config on Microsoft.Extensions.AI (pipeline foundation)
- AI-0020: Entity-First AI and Transaction Coordination (`[Embedding]` lifecycle pattern)
- AI-0021: Category-Driven AI with Convention Defaults (current Client surface)
- MEDIA-0001: Media Pillar Baseline and Storage Integration (`MediaEntity<T>`)
- STOR-0001: Storage Module and Contracts (`StorageEntity<T>`, `IStorageProvider`)
- STOR-0010: Replicated Storage with Local Cache Tier (`ReplicatedStorageProvider`)
- DX-0047: Fluent Media Transform API
- `src/Koan.AI/` — Current AI implementation
- `src/Koan.AI.Contracts/` — Current contracts
- `src/Koan.Storage/` — Storage abstractions
- `src/Koan.Media.Abstractions/` — Media entity hierarchy
- `samples/S6.SnapVault/` — Reference implementation (Media + AI + Storage integration)
- `samples/S7.Meridian/` — Reference implementation (Fact extraction)
