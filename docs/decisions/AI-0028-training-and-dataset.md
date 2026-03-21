---
id: AI-0028
slug: AI-0028-training-and-dataset
domain: AI
status: Proposed
date: 2026-03-20
---

# ADR: Training and Dataset — Entity-Native Bridge to ML Training

**Contract**

- **Inputs:** Existing entity system (`Entity<T>`), existing storage/media pillars (`StorageEntity<T>`, `MediaEntity<T>`), Model Catalog (AI-0023 `Model.*`), Compute Fabric (AI-0024 `Compute.*`), shared boundary models (AI-0022 `ModelRef`, `DatasetRef`, `ComputeRequirement`, `JobRef`, `Lineage`), external data sources (JSONL, CSV, Parquet, HuggingFace Hub), user Python scripts.
- **Outputs:** `Dataset.*` facade for entity-native and external dataset construction, transformation, chunking, analysis, and export; `Training.*` facade for four-level training with built-in recipes, custom scripts, and external registration; filesystem contract for training jobs; container and local-Python runtimes; progress streaming; automatic lineage recording; `ModelRef` output registered in Model Catalog.
- **Error Modes:** `Dataset.From<T>()` with zero matching entities returns empty dataset with diagnostic; `Training.Train()` rejects datasets below minimum threshold (10 examples) with clear message; compute requirement exceeds available resources — job queued with advisory, fails after configurable timeout; container runtime unavailable — falls back to local Python if configured, otherwise fails with installation guidance; training script crashes — job marked `Failed`, last checkpoint preserved, `progress.jsonl` and container logs retained for debugging; network interruption during HuggingFace download — resume with range headers; disk space exhaustion during training — job fails gracefully with checkpoint preservation; `Dataset.From(hubId)` with invalid hub ID — fails with HuggingFace API error message forwarded.
- **Acceptance Criteria:** A developer can `Dataset.From<SupportTicket>(where: t => t.Rating >= 4, input: t => t.Question, output: t => t.Resolution)` to create a live, compiler-enforced dataset from production entities; `Training.Train("meta-llama/Llama-3.1-8B-Instruct", dataset, method: TrainMethod.LoRA)` to train an adapter; monitor progress via callback; receive a `ModelRef` registered in the catalog with full lineage; and the entire chain — from entity query through training to catalog registration — requires zero ETL, zero Python, and zero manual file management.

**Edge Cases**

- Entity property renamed after dataset definition compiled: Compiler catches the break — `Dataset.From<T>()` uses expression trees, not strings. No silent schema drift.
- Entity query returns different results on each run: By design. The dataset is a live query, not a snapshot. Use `dataset.Save(path)` to pin a snapshot for reproducibility. `DatasetRef.Hash` changes when query results change.
- Training interrupted by process crash: Container preserves checkpoints in `output/checkpoints/`. `Training.Resume(jobId)` continues from last checkpoint. Status file reads `failed`; progress.jsonl retains all logged steps.
- Multiple `Training.Train()` calls with identical parameters: Each produces a separate job with unique ID. Framework does not deduplicate — idempotency is the caller's responsibility. `DatasetRef.Hash` enables caller-side dedup if desired.
- `Dataset.From<T>()` on entity type with no provider configured: Framework uses the entity's default provider (per entity-first conventions). If no provider is configured at all, the standard entity resolution error surfaces — not a training-specific error.
- Training on CPU when GPU was expected: `Training.Estimate()` warns about projected time. Training proceeds if compute requirement allows `Accelerator.Any`. Explicit `Accelerator.CUDA` with no CUDA available fails fast.
- `Dataset.From(hubId)` with gated model requiring authentication: Framework reads HuggingFace token from `Koan:Ai:HuggingFace:Token` configuration. Missing token produces actionable error with setup instructions.
- Export to Parquet with schema mismatch: `dataset.Save()` infers Parquet schema from data format. Mixed types within a column fail with descriptive error identifying the offending rows.
- `Training.Compare()` with heterogeneous compute: Variations are distributed across available GPUs. If fewer GPUs than variations, jobs queue and execute as resources free. Results collected when all variations complete.

## Context

Koan.AI (AI-0001 through AI-0027) provides a mature inference surface and is expanding toward full AI lifecycle management (AI-0022). The Model Catalog (AI-0023) gives models identity, lineage, and versioning. The Compute Fabric (AI-0024) abstracts hardware and routes work. The missing piece is the **bridge between production data and ML training** — the mechanism that turns entities into datasets and datasets into trained models.

This bridge is Koan's core differentiator. No other framework can do this:

```csharp
var dataset = Dataset.From<SupportTicket>(
    where: t => t.Resolved && t.Rating >= 4,
    input: t => t.Question,
    output: t => t.Resolution);
```

The query is **live** — tomorrow's run includes today's resolved tickets. The schema is **compiler-enforced** — renaming `Question` to `UserQuery` breaks the build, not the training pipeline at 3 AM. Lineage is **automatic** — the entity type, filter predicate, and field mapping are recorded without manual annotation.

Every ML platform requires an ETL step: export data from the application database, transform it into training format (JSONL, CSV, Parquet), upload it to the training environment, and hope the schema hasn't drifted since the last run. This ETL step is where data quality degrades, lineage breaks, and feedback loops stall.

Koan eliminates this step entirely because `Entity<T>` is simultaneously the production data model and the training data source. The same type system that enforces application logic enforces training data schema. The same query engine that powers the application powers dataset construction. The same entity that stores user feedback feeds back into training — no export, no transformation, no separate system.

### Why Four Training Levels

ML teams operate at different maturity levels and have different control requirements:

1. **Getting started:** "I want to fine-tune a model on my data. I don't know PyTorch." → Level 1 (built-in recipe).
2. **Customizing:** "The default evaluation doesn't capture my domain. I want to plug in my own eval script." → Level 2 (modified recipe).
3. **Full control:** "I have a custom RLHF training loop that I've been developing for months. I just need infrastructure." → Level 3 (custom script).
4. **External training:** "I trained on a DGX cluster. I just want to register the result and get lineage tracking." → Level 4 (external registration).

The key design insight: **every level produces the same artifacts**. A `ModelRef` in the catalog, a lineage chain, an eval-ready output. The escape hatch doesn't abandon the framework — it replaces the inner loop while keeping the outer infrastructure (compute routing, progress streaming, artifact collection, lineage recording).

### Existing Ecosystem Gaps

- **HuggingFace `datasets`** library is Python-only. No C# equivalent exists. Entity-to-dataset conversion requires custom ETL.
- **PyTorch/transformers training** requires Python environment management, dependency resolution, and GPU driver compatibility. Container-first execution solves this.
- **MLflow/Weights & Biases** provide experiment tracking but not data sourcing. They track what you trained; Koan also handles where the data comes from.
- **LangChain** provides document loaders but not entity-native dataset construction. Its loaders are retrieval-oriented, not training-oriented.

## Decision

### Part 1: `Dataset.*` Facade — Entity-Native Data Construction

The `Dataset.*` facade constructs, transforms, analyzes, and exports training data. Its differentiator is `Dataset.From<T>()` — live, typed, compiler-enforced dataset construction from production entities.

#### From Entities (The Differentiator)

```csharp
// Instruction format — the most common fine-tuning format
var dataset = Dataset.From<SupportTicket>(
    where: t => t.Resolved && t.Rating >= 4,
    input: t => t.Question,
    output: t => t.Resolution);

// Contrastive pairs — for embedding model training
var triplets = Dataset.From<SupportTicket>(
    format: DataFormat.Triplet,
    anchor: t => t.Question,
    positive: t => t.Resolution,
    negative: t => t.WrongDepartmentResponse);

// Classification — for category prediction
var labels = Dataset.From<SupportTicket>(
    format: DataFormat.Classification,
    text: t => t.Question,
    label: t => t.Category);

// Preference — for DPO alignment (from human corrections via Review.*)
var alignment = Dataset.From<SupportTicket>(
    format: DataFormat.Preference,
    prompt: t => t.Question,
    chosen: t => t.EditedResponse,
    rejected: t => t.OriginalAiResponse);

// Conversation — for multi-turn chat training
var conversations = Dataset.From<ChatSession>(
    format: DataFormat.Conversation,
    messages: t => t.Messages.Select(m => new { m.Role, m.Content }));

// Raw — for continued pretraining
var corpus = Dataset.From<KnowledgeArticle>(
    format: DataFormat.Raw,
    text: t => t.Title + "\n\n" + t.Body);
```

**How it works internally:**

1. Expression trees capture the field mappings at compile time. `input: t => t.Question` is not a string — it is a `Expression<Func<T, string>>` that the compiler validates.
2. At runtime, the expression is translated to the entity's query provider (SQL, MongoDB, etc.) to materialize matching entities.
3. Each entity is projected through the field mapping expressions to produce a training sample in the specified `DataFormat`.
4. The query expression, entity type, field mappings, and result hash are recorded as `DatasetRef` for lineage.

**`DatasetRef` — shared boundary model (defined in AI-0022):**

```csharp
public sealed record DatasetRef(string Id, string? Hash = null);
```

The `Hash` is computed from the combination of: entity type name, serialized filter expression, field mapping expressions, and a content hash of the materialized data. This enables:
- **Change detection:** If the hash differs between runs, the underlying data changed.
- **Lineage tracking:** The trained model's `Lineage.Data` records exactly which dataset version was used.
- **Caller-side deduplication:** If `Hash` matches a previous run, the caller can skip retraining.

#### DataFormat Enum

```csharp
public enum DataFormat
{
    Instruction,      // { instruction, input, output }
    Triplet,          // { anchor, positive, negative } — contrastive learning
    Classification,   // { text, label }
    Preference,       // { prompt, chosen, rejected } — DPO/RLHF alignment
    Conversation,     // { messages: [{role, content}] } — multi-turn chat
    Raw               // { text } — continued pretraining
}
```

Each format maps to a well-defined JSONL schema understood by standard training libraries (transformers, trl, sentence-transformers). The framework handles the projection — users specify intent via field mappings, not JSON structure.

#### From External Sources

Entity-native construction is the differentiator, but real-world training often combines entity data with external data:

```csharp
// Load from file — JSONL, CSV, Parquet auto-detected by extension
var external = Dataset.From("./data/public-qa-pairs.jsonl");
var csv = Dataset.From("./data/labeled-samples.csv");
var parquet = Dataset.From("./data/training-v2.parquet");

// Load from HuggingFace Hub
var squad = Dataset.From("rajpurkar/squad", split: "train");
var alpaca = Dataset.From("tatsu-lab/alpaca");

// Combine entity data with external data
var combined = dataset.Concat(external);
```

File format detection uses extension + content sniffing. JSONL requires one JSON object per line. CSV requires a header row. Parquet is self-describing. Unsupported formats fail with a clear error listing supported formats.

HuggingFace Hub access uses the `datasets` library via the training container or local Python sidecar. Authentication is read from `Koan:Ai:HuggingFace:Token`.

#### Document Loading (For RAG Pipelines)

```csharp
// Load documents from multiple sources for RAG
var docs = Dataset.Load(
    DocumentSource.Directory("./docs/", pattern: "*.md"),
    DocumentSource.Pdf("./manuals/product-guide.pdf"),
    DocumentSource.Web("https://docs.example.com/api"),
    DocumentSource.Entities<KnowledgeArticle>(
        where: a => a.Published,
        text: a => a.Title + "\n\n" + a.Body));

// Chunk for vector ingestion
var chunks = docs.Chunk(new ChunkOptions
{
    Strategy = ChunkStrategy.Semantic,
    Size = 512,        // tokens
    Overlap = 64,      // tokens
    Tokenizer = "cl100k_base"
});
```

`DocumentSource.Entities<T>()` reuses the same expression-tree mechanism as `Dataset.From<T>()`. Document loading is distinct from dataset construction: it produces text chunks for retrieval, not structured training samples.

#### Transformation Pipeline

Datasets are immutable. Every transformation returns a new dataset:

```csharp
var prepared = dataset
    .Filter(sample => sample.Input.Length > 20)       // Remove very short inputs
    .Map(sample => sample with                         // Transform samples
    {
        Input = sample.Input.Trim(),
        Output = sample.Output.Trim()
    })
    .Shuffle(seed: 42)                                 // Reproducible shuffle
    .Split(train: 0.8, val: 0.1, test: 0.1);          // Three-way split

// Access splits
var trainSet = prepared.Train;
var valSet = prepared.Validation;
var testSet = prepared.Test;

// Subsetting
var quick = dataset.Take(100);          // First 100 samples
var batched = dataset.Batch(32);        // Iterate in batches of 32
```

**Immutability is non-negotiable.** `Filter()` returns a new dataset; the original is unchanged. This enables safe sharing, reproducibility, and debugging.

#### Chunking

```csharp
var chunks = dataset.Chunk(new ChunkOptions
{
    Strategy = ChunkStrategy.Semantic,    // or Recursive, Sentence, Fixed
    Size = 512,                           // target size in tokens
    Overlap = 64,                         // overlap between chunks in tokens
    Tokenizer = "cl100k_base"             // tokenizer for token counting
});
```

**`ChunkStrategy` enum:**

```csharp
public enum ChunkStrategy
{
    Fixed,        // Fixed token count, hard split
    Sentence,     // Split on sentence boundaries
    Recursive,    // Split on paragraphs → sentences → words (LangChain-style)
    Semantic      // Split on topic boundaries using embedding similarity
}
```

`Semantic` chunking uses the configured embedding provider to detect topic boundaries. It requires a deployed embedding model. If no embedding model is available, it falls back to `Recursive` with a warning.

#### Analysis

```csharp
var stats = await dataset.Analyze();
// stats.TotalSamples        → 12,847
// stats.TotalTokens          → 4,231,090
// stats.AvgTokensPerSample   → 329
// stats.MaxTokensPerSample   → 2,048
// stats.MinTokensPerSample   → 12
// stats.Distribution         → {<128: 1204, 128-256: 3891, 256-512: 5102, ...}
// stats.EstimatedTrainTime   → TimeSpan (based on sample count + method heuristics)
// stats.EstimatedCost        → CostEstimate (based on compute rates if configured)
```

Analysis runs the configured tokenizer over the dataset to produce accurate token counts. This is essential for:
- Choosing `MaxSequenceLength` (should cover 95th percentile)
- Estimating training cost before committing compute
- Detecting data quality issues (extremely short or long samples)

#### Export

```csharp
// Save to local file — format inferred from extension
await dataset.Save("./output/training-data.parquet");
await dataset.Save("./output/training-data.jsonl");

// Publish to HuggingFace Hub
await dataset.Publish("acme/support-qa-v3", to: "huggingface");
```

Export produces files in standard formats consumable by any ML toolchain. This is the escape hatch for teams that want to use Koan for dataset construction but train elsewhere.

### Part 2: `Training.*` Facade — Four-Level Escape Hatch

Training is organized as four levels of control. Each level adds user control while preserving framework infrastructure. All levels produce identical artifacts: a `ModelRef` in the catalog with full lineage.

#### Level 1: Built-In Recipe (Framework Controls Everything)

```csharp
var job = await Training.Train(
    "meta-llama/Llama-3.1-8B-Instruct",
    dataset,
    method: TrainMethod.LoRA);
```

**What the framework does:**

1. Resolves `ModelRef` via Model Catalog (AI-0023) — downloads if needed.
2. Exports dataset to `.koan/jobs/{id}/input/data/` in the format required by the training method.
3. Generates `recipe.json` — a complete job specification with sensible defaults for the model size and method.
4. Selects compute via Compute Fabric (AI-0024) — routes to appropriate GPU based on VRAM requirements.
5. Launches training container (`koan/trainer:cuda`, `koan/trainer:rocm`, or `koan/trainer:cpu` based on detected accelerator).
6. Streams progress from `progress.jsonl` back to the caller via callback.
7. Collects output model from `output/model/`, metrics from `output/metrics.json`.
8. Registers output as new `ModelEntry` in the catalog with full `Lineage` (base model, method, dataset ref, eval scores, timestamp).
9. Returns `JobRef` with `ModelRef` pointing to the registered output.

**Who this is for:** Priya (Software Engineer) who wants to fine-tune a model on her entity data without learning PyTorch. She writes the `Dataset.From<T>()` query, calls `Training.Train()`, and gets a `ModelRef` she can deploy.

#### Level 2: Modified Recipe (Framework + User Overrides)

```csharp
var job = await Training.Train(new TrainOptions
{
    Base = "meta-llama/Llama-3.1-8B-Instruct",
    Data = dataset,
    Method = TrainMethod.QLoRA,
    Rank = 32,
    LoraAlpha = 64,
    Epochs = 3,
    LearningRate = 2e-4,
    BatchSize = 4,
    GradientAccumulation = 8,
    MaxSequenceLength = 2048,
    // Escape hatches: override specific components
    EvalScript = "./scripts/custom_eval.py",
    DataCollator = "./scripts/my_collator.py",
    Dependencies = ["rouge-score==0.1.2"]
});
```

**What changes from Level 1:**

- User controls hyperparameters directly instead of accepting defaults.
- User can replace specific components (evaluation, data collation) with custom Python scripts.
- Framework still handles everything else: compute routing, container lifecycle, progress streaming, artifact collection, catalog registration.

**`TrainOptions` record:**

```csharp
public sealed record TrainOptions
{
    public required ModelRef Base { get; init; }
    public required DatasetRef Data { get; init; }
    public TrainMethod Method { get; init; } = TrainMethod.LoRA;

    // LoRA/QLoRA hyperparameters
    public int Rank { get; init; } = 16;
    public int LoraAlpha { get; init; } = 32;
    public string[]? TargetModules { get; init; }

    // Training hyperparameters
    public int Epochs { get; init; } = 3;
    public double LearningRate { get; init; } = 2e-4;
    public int BatchSize { get; init; } = 4;
    public int GradientAccumulation { get; init; } = 4;
    public int MaxSequenceLength { get; init; } = 2048;
    public string? OutputName { get; init; }
    public ComputeRequirement? Compute { get; init; }

    // Level 2 escape hatches — override specific pipeline components
    public string? EvalScript { get; init; }
    public string? DataCollator { get; init; }
    public string[]? Dependencies { get; init; }
}
```

**Who this is for:** Riku (AI Scientist) who understands hyperparameters and wants to tune them, or who has a custom evaluation metric that the built-in recipe doesn't cover.

#### Level 3: Custom Script (User Controls Training, Framework Controls Infra)

```csharp
var job = await Training.Run(new RunOptions
{
    Script = "./training/custom_rl_training.py",
    Base = "meta-llama/Llama-3.1-8B-Instruct",
    Data = dataset,
    Compute = Compute.Require(minVram: GiB(48)),
    Image = "koan/trainer:cuda-2.5",
    Dependencies = ["trl==0.12", "deepspeed==0.15"],
    Timeout = TimeSpan.FromHours(12)
});
```

**The contract between user script and framework:**

The user's script reads from `input/` and writes to `output/`. The framework handles everything else. The script receives the filesystem contract:

```
.koan/jobs/{job-id}/
├── input/
│   ├── recipe.json      ← Full job specification (model ref, data path, options)
│   ├── data/             ← Training data (exported from Dataset)
│   └── base-model/       ← Model weights (or symlink to cached download)
├── output/
│   ├── model/            ← Script writes: output weights (adapter or full)
│   ├── metrics.json      ← Script writes: final metrics {loss, eval_loss, ...}
│   ├── checkpoints/      ← Script writes: resumable checkpoints
│   └── artifacts/        ← Script writes: plots, logs, tensorboard, etc.
├── progress.jsonl        ← Script appends: progress lines (see format below)
└── status                ← Framework manages: running | completed | failed
```

**`recipe.json` — the input contract:**

```json
{
  "job_id": "train-2026-03-20-abc123",
  "base_model": "meta-llama/Llama-3.1-8B-Instruct",
  "data_path": "input/data/",
  "data_format": "instruction",
  "output_path": "output/model/",
  "metrics_path": "output/metrics.json",
  "checkpoints_path": "output/checkpoints/",
  "options": {
    "method": "lora",
    "rank": 16,
    "epochs": 3,
    "learning_rate": 2e-4,
    "batch_size": 4,
    "max_sequence_length": 2048
  }
}
```

The script can read `recipe.json` to discover paths and options, or ignore it entirely and use its own configuration. The only hard requirement: write output to `output/model/` and final metrics to `output/metrics.json`.

**`progress.jsonl` — the progress contract:**

```jsonl
{"step": 1, "total": 1000, "loss": 2.45, "lr": 0.0002, "epoch": 0.01, "gpu_mem_gb": 15.2}
{"step": 2, "total": 1000, "loss": 2.41, "lr": 0.0002, "epoch": 0.02, "gpu_mem_gb": 15.3}
{"step": 100, "total": 1000, "loss": 1.83, "lr": 0.00019, "epoch": 1.0, "gpu_mem_gb": 15.1}
```

The framework reads `progress.jsonl` using a tail-follow mechanism and streams progress back to the caller via callback. Fields are advisory — the framework renders whatever fields are present. The minimum useful set is `step`, `total`, and `loss`.

**Progress streaming in C#:**

```csharp
var job = await Training.Train(options, progress: p =>
    Console.WriteLine($"Step {p.Step}/{p.Total} Loss: {p.Loss:F4} ETA: {p.Eta}"));
```

**Who this is for:** Riku (AI Scientist) who has an existing training script — perhaps a custom RLHF loop, a distillation pipeline, or an experimental training method. He doesn't want to rewrite it as a Koan recipe. He wants infrastructure: compute routing, container management, progress streaming, and catalog registration.

#### Level 4: External Registration (In `Model.*` Facade)

```csharp
await Model.Register(path: "/results/my-model/", lineage: new Lineage
{
    Base = "meta-llama/Llama-3.1-8B-Instruct",
    Method = "Custom RLHF",
    Data = new DatasetRef("internal-feedback-v7"),
    TrainedBy = "riku@acme.com",
    TrainedAt = DateTimeOffset.UtcNow,
    Notes = "Trained on DGX cluster, 8xA100, 48 hours"
});
```

**What the framework does:** Registers the model in the catalog with whatever lineage the user provides. The model participates in versioning, deployment, evaluation, and routing — the same as any framework-trained model.

**What the framework does not do:** Validate that the lineage is accurate, verify the model weights, or reproduce the training. Lineage at Level 4 is self-reported.

**Who this is for:** Teams that train on external infrastructure (cloud GPU clusters, university HPC, vendor-managed training) but want catalog management, versioning, and deployment through Koan.

### Part 3: Training Methods and Alignment

#### TrainMethod Enum

```csharp
public enum TrainMethod
{
    LoRA,                  // Low-Rank Adaptation — most common, parameter-efficient
    QLoRA,                 // Quantized LoRA — lower VRAM, slight quality trade-off
    Full,                  // Full fine-tuning — requires significant VRAM
    SentenceTransformer,   // Embedding model training via sentence-transformers
    Contrastive,           // Contrastive learning for custom similarity
    Adapter                // Generic adapter training (future extensibility)
}
```

**Default selection logic:** If `Method` is not specified, the framework selects based on model size and available compute:
- < 1B parameters + sufficient VRAM → `Full`
- 1B–13B parameters → `LoRA`
- > 13B parameters or limited VRAM → `QLoRA`

#### Alignment Methods

```csharp
var job = await Training.Align(
    base: "acme-support:v3",
    data: preferenceDataset,
    method: AlignMethod.DPO);
```

**AlignMethod Enum:**

```csharp
public enum AlignMethod
{
    DPO,     // Direct Preference Optimization — simple, stable
    RLHF,    // Reinforcement Learning from Human Feedback — requires reward model
    KTO,     // Kahneman-Tversky Optimization — works with binary feedback
    ORPO     // Odds Ratio Preference Optimization — no reference model needed
}
```

`Training.Align()` is syntactic sugar over `Training.Train()` with `DataFormat.Preference` data and alignment-specific defaults. The separation exists for discoverability: developers searching for "alignment" or "DPO" find `Training.Align()` directly.

### Part 4: Experiment Comparison

```csharp
var results = await Training.Compare(
    base: "meta-llama/Llama-3.1-8B-Instruct",
    data: dataset,
    variations: [
        new { Method = TrainMethod.LoRA, Rank = 8 },
        new { Method = TrainMethod.LoRA, Rank = 16 },
        new { Method = TrainMethod.LoRA, Rank = 32 },
        new { Method = TrainMethod.QLoRA, Rank = 16 }
    ],
    eval: testSet,
    metrics: [Metric.RougeL, Metric.Faithfulness, Metric.Latency]);
```

**How it works:**

1. Each variation becomes a separate `TrainOptions` merged with the shared `base` and `data`.
2. Jobs are submitted to the Compute Fabric. If multiple GPUs are available, jobs run in parallel. If not, they queue.
3. After training, each output model is evaluated against `eval` using the specified `metrics` (via `Eval.Measure()` from AI-0029).
4. Results are returned as a ranked table, sorted by the first metric.

**Return type:**

```csharp
public sealed record CompareResult(
    IReadOnlyList<CompareEntry> Entries,
    CompareEntry Best);

public sealed record CompareEntry(
    int VariationIndex,
    TrainOptions Options,
    ModelRef Output,
    IReadOnlyList<EvalScore> Scores,
    TimeSpan TrainTime,
    TrainResourceUsage Resources);
```

**Who this is for:** Riku (AI Scientist) running hyperparameter sweeps. Instead of manually launching N training runs, comparing results, and tracking which configuration produced which model, `Training.Compare()` automates the entire grid.

### Part 5: Cost Estimation

```csharp
var estimate = await Training.Estimate(new TrainOptions
{
    Base = "meta-llama/Llama-3.1-8B-Instruct",
    Data = dataset,
    Method = TrainMethod.LoRA,
    Epochs = 3
});

// estimate.TotalTokens       → 12,693,270
// estimate.EstimatedGpuHours → 2.4
// estimate.MinVramRequired   → 18 GiB
// estimate.RecommendedCompute → ComputeRequirement(Accelerator.CUDA, MinVram: 24 GiB)
// estimate.CanRunLocally     → false (local GPU: 8 GiB)
// estimate.Advisory          → "Model requires ~18 GiB VRAM with LoRA rank 16.
//                                Local GPU has 8 GiB. Route to network compute
//                                or use QLoRA (est. ~10 GiB)."
```

Estimation uses heuristics based on:
- Model parameter count (from Model Catalog metadata)
- Training method (LoRA/QLoRA VRAM multipliers)
- Dataset token count (from `dataset.Analyze()`)
- Batch size and gradient accumulation
- Historical training times from previous jobs (when available)

Estimates are **advisory, not contractual**. Actual resource usage depends on framework versions, driver versions, and model architecture details.

### Part 6: Job Lifecycle Management

```csharp
// Check status
var status = await Training.Status(jobId);
// status.JobId, status.Status, status.Progress, status.Metrics, status.Eta

// Cancel gracefully (saves checkpoint)
await Training.Cancel(jobId);

// Resume from checkpoint
var resumed = await Training.Resume(jobId, fromCheckpoint: "checkpoint-500");

// List all jobs
var jobs = await Training.List();
// → [{Id, Status, Base, Method, StartedAt, CompletedAt, Metrics}, ...]
```

**Job states:**

```
Queued → Running → Completed
                 → Failed (checkpoint preserved)
                 → Cancelled (checkpoint preserved)
```

`Training.Resume()` creates a **new job** that continues from the specified checkpoint. The original job remains in its terminal state. This preserves history and avoids mutating completed job records.

### Part 7: Training Runtime as Adapter

Training runtimes are **adapters** in the capability-driven resolution system (AI-0021). There is no separate `ITrainingRuntime` interface — the adapter IS the provider. Each runtime registers as an `IAiAdapter` with capabilities that reflect what it can do.

#### Adapter Registration

| Adapter | Capabilities | Runtime |
|---------|-------------|---------|
| `Koan.AI.Connector.TrainerContainer` | `Train, Align, Convert, Quantize, Serve.SafeTensors` | Docker/Podman container |
| `Koan.AI.Connector.PythonSidecar` | `Train, Align, Convert, Quantize, MetricCompute` | Local Python venv |
| `Koan.AI.Worker` (remote) | `Train, Align, Convert, Quantize, Serve.GGUF, ...` | Remote GPU machine |

`Training.Train()` resolves via `AdapterResolver.Resolve(registry, AiCapability.Train)`. If only one adapter declares `Train`, it handles the job unambiguously. If multiple adapters declare `Train`, the caller can disambiguate with the `to:` parameter or let the compute fabric's resolution rules (VRAM, locality, utilization) select the best target. Ambiguity without disambiguation throws `AmbiguousAdapterException`.

`Training.Align()` resolves via `AdapterResolver.Resolve(registry, AiCapability.Align)`. Same pattern.

`Training.Run()` (Level 3 escape hatch) also routes to the `Train`-capable adapter — the custom script executes within the adapter's runtime environment.

#### Container Adapter (Default)

The container adapter runs training in Docker/Podman containers. Three base images cover the accelerator matrix:

| Image | Accelerator | Pre-installed |
|-------|------------|---------------|
| `koan/trainer:cuda` | NVIDIA CUDA | PyTorch, transformers, peft, trl, datasets, sentence-transformers, bitsandbytes |
| `koan/trainer:rocm` | AMD ROCm | PyTorch (ROCm), transformers, peft, trl, datasets, sentence-transformers |
| `koan/trainer:cpu` | CPU only | PyTorch (CPU), transformers, peft, trl, datasets, sentence-transformers |

**Container selection:** The framework detects the available accelerator (via Compute Fabric) and selects the appropriate image. User can override with `Image` in `RunOptions`.

**User dependencies:** Additional Python packages specified in `Dependencies` are installed at container startup via `pip install`. This adds startup latency but ensures reproducibility — the base image is immutable, and user dependencies are layered on top.

**Volume mounts:**

```
.koan/jobs/{job-id}/ → /job/          (read-write)
.koan/cache/models/  → /models/       (read-only, shared model cache)
```

#### Python Sidecar Adapter (Opt-In Fallback)

Configured via `Koan:Ai:Training:Runtime = "local-python"`.

The Python sidecar adapter manages a virtual environment in `.koan/training-venv/` or uses a user-specified Python interpreter via `Koan:Ai:Training:PythonPath`. It registers with capabilities `Train, Align, Convert, Quantize, MetricCompute`.

**Trade-offs acknowledged:**
- No isolation between jobs
- Dependency conflicts possible
- Not reproducible across machines
- Useful for development and debugging where container overhead is unwanted

### Part 8: The Closed-Loop Learning Scenario

This is the complete cycle — the reason this ADR exists. Every bounded context participates:

```csharp
// ═══════════════════════════════════════════════════════════════════
// Step 1: Application serves customers using AI         [Client.*]
// ═══════════════════════════════════════════════════════════════════

var ticket = await SupportTicket.Get(ticketId);
var answer = await Client.Chat(ticket.Question);
ticket = ticket with { AiResponse = answer };
await ticket.Save();

// ═══════════════════════════════════════════════════════════════════
// Step 2: Customers rate responses                      [Entity<T>]
// ═══════════════════════════════════════════════════════════════════

ticket = ticket with { Rating = customerRating };
await ticket.Save();

// ═══════════════════════════════════════════════════════════════════
// Step 3: Domain experts review AI outputs              [Review.*]
// ═══════════════════════════════════════════════════════════════════

// Marta reviews in the review queue UI (API-driven, see AI-0030)
// She approves good responses, corrects bad ones, labels quality.
// Entity updated: ticket.ReviewStatus = Approved | Edited
// If edited: ticket.EditedResponse contains her correction.

// ═══════════════════════════════════════════════════════════════════
// Step 4: Reviewed entities become training data        [Dataset.*]
// ═══════════════════════════════════════════════════════════════════

// Instruction data from high-quality responses
var instructionData = Dataset.From<SupportTicket>(
    where: t => t.Rating >= 4 && t.ReviewStatus == ReviewStatus.Approved,
    input: t => t.Question,
    output: t => t.Resolution);

// Alignment data from human corrections
var alignmentData = Dataset.From<SupportTicket>(
    format: DataFormat.Preference,
    where: t => t.ReviewStatus == ReviewStatus.Edited,
    prompt: t => t.Question,
    chosen: t => t.EditedResponse,
    rejected: t => t.OriginalAiResponse);

// ═══════════════════════════════════════════════════════════════════
// Step 5: Train improved model                          [Training.*]
// ═══════════════════════════════════════════════════════════════════

var job = await Training.Train(
    "acme-support:current",
    instructionData,
    method: TrainMethod.LoRA);

// Optionally: align with human corrections
var aligned = await Training.Align(
    job.Output,
    alignmentData,
    method: AlignMethod.DPO);

// ═══════════════════════════════════════════════════════════════════
// Step 6: Quality gate blocks bad models                [Eval.*]
// ═══════════════════════════════════════════════════════════════════

await Eval.Gate(aligned.Output,
    baseline: "acme-support:current",
    data: goldenTestSet,
    require: g => g
        .Metric(Metric.RougeL, min: 0.85)
        .NoRegression(tolerance: 0.02));

// ═══════════════════════════════════════════════════════════════════
// Step 7: Deploy to optimal runtime                     [Model.*]
// ═══════════════════════════════════════════════════════════════════

await Model.Deploy(aligned.Output, tag: "acme-support:current");

// ═══════════════════════════════════════════════════════════════════
// Step 8: Loop continues — new model serves better      [Client.*]
// ═══════════════════════════════════════════════════════════════════

// The deployed model now handles new tickets.
// New ratings and reviews feed the next training cycle.
// No ETL. No export. No separate systems. One type system.
```

**What makes this unique:**

1. **Zero ETL.** `Dataset.From<SupportTicket>()` queries live production data. No export step. No schema mapping. No data pipeline to maintain.
2. **Compiler-enforced schema.** If `SupportTicket.Question` is renamed to `SupportTicket.UserQuery`, the `Dataset.From<T>()` call fails at compile time — not at 3 AM when the nightly training job runs.
3. **Automatic lineage.** The trained model's `Lineage` records: base model, training method, dataset reference (including entity type and query), evaluation scores, timestamp. Auditors can trace any production model back to the exact data that trained it.
4. **Human corrections become alignment data.** Marta's corrections in the review queue (AI-0030) produce `Preference` format data. DPO training uses these corrections to align the model toward human-preferred outputs.
5. **Quality gates prevent regression.** `Eval.Gate()` (AI-0029) blocks deployment if the new model is worse than the current one. The loop is self-improving, not self-degrading.

### Part 9: Package Structure

```
Koan.AI.Training                    ← Training.*, Dataset.* facades, job engine,
                                       TrainOptions, DataFormat, ChunkStrategy,
                                       filesystem contract types
Koan.AI.Connector.TrainerContainer  ← Container adapter: registers IAiAdapter with
                                       Train/Align/Convert/Quantize capabilities,
                                       image selection, volume mounting, container
                                       lifecycle, progress tailing
Koan.AI.Connector.PythonSidecar     ← Python sidecar adapter: registers IAiAdapter
                                       with Train/Align/Convert/Quantize/MetricCompute
                                       capabilities, venv management, dependency
                                       installation, process lifecycle
```

**Dependencies:**

- `Koan.AI.Contracts.Shared` — shared boundary models (`ModelRef`, `DatasetRef`, `ComputeRequirement`, `JobRef`, `Lineage`), `IAiAdapter` interface, `AiCapability` constants
- `Koan.AI.Models` (AI-0023) — Model Catalog for base model resolution and output registration
- `Koan.AI.Compute` (AI-0024) — Compute Fabric for hardware context during adapter resolution
- `Koan.Data.Core` — entity system for `Dataset.From<T>()` expression-tree query translation
- `Koan.Core` — guard clauses, options registration, orchestration

`Koan.AI.Connector.TrainerContainer` and `Koan.AI.Connector.PythonSidecar` are separate packages because the runtime is a deployment concern. Each registers as an `IAiAdapter` with capabilities reflecting its environment. A production environment that only uses container training only needs the container adapter package — `AdapterResolver` finds it via `AiCapability.Train`. The same adapter handles `Training.Train()`, `Training.Align()`, and `Training.Run()` calls.

### Part 10: Configuration

```json
{
  "Koan": {
    "Ai": {
      "Training": {
        "Runtime": "container",
        "ContainerEngine": "docker",
        "ImagePrefix": "koan/trainer",
        "JobsPath": ".koan/jobs",
        "ModelCachePath": ".koan/cache/models",
        "DefaultMethod": "LoRA",
        "DefaultEpochs": 3,
        "MinDatasetSize": 10,
        "MaxJobTimeout": "24:00:00",
        "PythonPath": null
      },
      "HuggingFace": {
        "Token": null
      }
    }
  }
}
```

All settings have sensible defaults. Zero-config works for the common case (container runtime, Docker engine, LoRA method). The `MinDatasetSize` setting prevents training on trivially small datasets — configurable because some specialized domains legitimately have few examples.

## Consequences

### Positive

- **Entity-native ML bridge** eliminates ETL between production data and training. This is the capability no other framework provides.
- **Compiler-enforced schema** catches data pipeline breaks at build time, not at runtime during nightly training jobs.
- **Automatic lineage** enables auditable model provenance from production model back to training data, base model, and method.
- **Four-level escape hatch** serves teams from "I've never trained a model" (Level 1) through "I have my own training infrastructure" (Level 4), all producing the same catalog artifacts.
- **Container-first runtime** eliminates Python dependency management, ensures reproducibility across machines, and isolates training environments.
- **Live queries** mean datasets automatically include new data. Tomorrow's training run uses today's resolved tickets without pipeline changes.
- **Closed-loop learning** from entity → training → deployment → feedback → entity is possible entirely within Koan's type system, with no external orchestration.
- **`Training.Compare()`** automates hyperparameter sweeps that previously required manual scripting and result tracking.
- **`Training.Estimate()`** prevents wasted compute by surfacing resource requirements before job submission.
- **Filesystem contract** decouples training scripts from framework internals. Any Python script that reads from `input/` and writes to `output/` works — no Koan SDK required in the training code.

### Negative / Trade-offs

- **Python dependency is unavoidable.** Training runs PyTorch, transformers, and related libraries. Container-first mitigates environment issues but adds Docker/Podman as a soft dependency.
- **Expression tree translation has limits.** Complex field mappings (multi-step transformations, aggregations, joins) may not translate cleanly to all entity query providers. Workaround: materialize complex transformations as entity properties, then map the property.
- **Live queries are a double-edged sword.** A dataset that changes between training and evaluation can produce misleading results. Mitigation: `dataset.Save()` for pinning, `DatasetRef.Hash` for change detection.
- **Container startup latency.** Installing user `Dependencies` at container startup adds time. Mitigation: cache pip packages in a persistent volume; provide guidance on building custom images for frequently-used dependency sets.
- **`Training.Compare()` is compute-intensive.** N variations × full training runs. No early stopping across variations (each variation runs independently). Mitigation: `Training.Estimate()` before `Training.Compare()` to understand total cost.
- **Python sidecar adapter is explicitly second-class.** Dependency conflicts, non-reproducible environments, no isolation. Documented as development-only, not production-recommended.
- **Lineage at Level 4 is self-reported.** The framework cannot verify that an externally-trained model was actually trained on the claimed data with the claimed method. Lineage accuracy depends on user honesty.

## References

- AI-0022: Unified AI Lifecycle Vision (shared boundary models, bounded context map, phasing)
- AI-0023: Model Catalog (`Model.*` facade, `ModelEntry` entity, `Model.Register()` for Level 4)
- AI-0024: Compute Fabric (`Compute.*` facade, hardware abstraction, job routing, `Koan.AI.Worker`)
- AI-0021: Category-Driven AI with Convention Defaults (current `Client.*` surface used in closed loop)
- AI-0020: Entity-First AI and Transaction Coordination (`[Embedding]` lifecycle pattern, entity-native precedent)
- AI-0029: Eval — Quality Enforcement (`Eval.Gate()`, `Eval.Measure()`, metrics used by `Training.Compare()`)
- AI-0030: Review — Human Feedback Loop (`Review.*` queues that produce alignment data for `Dataset.From<T>()`)
- AI-0001: Native AI Baseline (original scope boundary that explicitly deferred training)
- `src/Koan.AI/` — Current AI implementation
- `src/Koan.AI.Contracts/` — Current contracts and shared models
- `src/Koan.Data.Core/` — Entity system, expression-tree query infrastructure
