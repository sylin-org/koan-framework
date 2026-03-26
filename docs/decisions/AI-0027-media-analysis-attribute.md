---
id: AI-0027
slug: AI-0027-media-analysis-attribute
domain: AI
status: Accepted
date: 2026-03-20
---

# ADR: `[MediaAnalysis]` — Attribute-Driven AI Processing for Media Entities

**Contract**

- **Inputs:** `MediaEntity<T>` subclasses decorated with `[MediaAnalysis]`, raw media bytes available through `StorageEntity<T>.OpenRead()` (cache-aware via `ReplicatedStorageProvider`), optional `PromptEntry` catalog entries (AI-0025) for extraction prompts, per-category AI routing from `Koan:Ai:{Category}:*` (AI-0021).
- **Outputs:** Convention-detected or explicitly-mapped entity properties populated with AI-generated content (`string` for Describe/Ocr/Transcribe, `string` or `enum` for Classify, typed `T` for Extract); analysis state tracking via `MediaAnalysisState<T>`; downstream `[Embedding]` integration where analysis text becomes embedding source; background processing via `MediaAnalysisWorker`.
- **Error Modes:** AI provider unavailable: entity saved without analysis, `MediaAnalysisState<T>` marked `Failed`, background worker retries with exponential backoff. No convention-matching property found for a requested analysis mode: bootstrap warning with remediation guidance listing expected property names. `Prompt` name not found in catalog: falls back to built-in extraction prompt, logs dev-mode guidance. Media bytes unreadable (corrupt file, storage unavailable): analysis skipped for that mode, entity retains existing property values, state records failure reason. `[MediaAnalysis]` on non-`MediaEntity<T>` type: registrar throws clear error during bootstrap. Multiple analysis modes requested but entity lacks properties for some: only modes with matching properties execute; others log warnings.
- **Acceptance Criteria:** A `MediaEntity<T>` subclass with `[MediaAnalysis(Analysis = MediaAnalysis.Describe)]` and a `string? AiDescription` property auto-populates `AiDescription` on upload with zero additional code. A `MediaEntity<T>` with both `[MediaAnalysis]` and `[Embedding]` auto-generates embedding text from analysis results (cross-modal search works). `Version` bump triggers re-analysis of all existing entities. `Async = true` (default) saves the entity immediately after upload and queues analysis for background processing. S6.SnapVault's `PhotoProcessingService.GenerateDetailedDescriptionAsync` (~300 lines) is replaceable by `[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Classify)]` on `PhotoAsset`.

**Edge Cases**

- Entity uploaded with `Async = false` but AI provider is slow (>30s): Request completes synchronously with configurable timeout; on timeout, falls back to async processing and logs warning. Entity saved with partial results (completed modes populated, timed-out modes queued).
- `[MediaAnalysis(Analysis = MediaAnalysis.Transcribe)]` on an image entity: Transcribe mode detects incompatible content type (`image/*`), skips silently with debug log. No error — the attribute may be on a base class shared by image and audio subtypes.
- `[MediaAnalysis]` with `[Embedding]` but no explicit `ToEmbeddingText()` method: Framework concatenates analysis results in deterministic order (AiDescription, OcrText, Transcript, classification label, extracted data `ToString()`) separated by `"\n"`. Custom `ToEmbeddingText()` override takes precedence.
- `Version` bumped from 1 to 2 while background worker is still processing version 1 items: Worker skips version 1 items and processes only version 2. In-flight version 1 results are discarded on save (version check).
- Entity with `[MediaAnalysis(Prompt = "receipt-extractor")]` but `PromptEntry` named "receipt-extractor" does not exist: Throws `PromptNotFoundException` during bootstrap registration (fail-fast), not at runtime. Prompt existence validated eagerly.
- Large media file (>100MB video) with `Transcribe` mode: Worker streams bytes through transcription pipeline without loading entire file into memory. `OpenRead()` returns a seekable stream from `IStorageProvider`.
- Re-upload of same entity (content change detected by `ContentHash`): Previous analysis cleared, new analysis queued. `[Embedding]` re-embeds after new analysis completes.
- Entity saved without upload (metadata-only update): `[MediaAnalysis]` does not trigger — analysis only fires on content change (new upload or content hash change), not on property-only saves.

## Context

The Koan framework provides a complete media and storage hierarchy:

```
Entity<T>
  └── StorageEntity<T>    — Key, ContentType, Size, ReadAllBytes(), OpenRead()
        └── MediaEntity<T> — SourceMediaId, derivatives, Upload()
```

`StorageEntity<T>` (STOR-0001) handles file I/O, metadata, and profile-based routing via `[StorageBinding]`. `MediaEntity<T>` (MEDIA-0001) adds media lineage, derivative management, and the `Upload()` static method. `ReplicatedStorageProvider` (STOR-0010) provides transparent local cache with durable tier pull-through, meaning analysis can read bytes from the fast local cache without explicit cache management.

The `[Embedding]` attribute (AI-0020) established the pattern: an attribute on an entity class opts into automatic lifecycle processing. When an entity is saved, the framework detects content changes, generates embeddings, and stores them atomically. `EmbeddingState<T>` tracks processing state. Background workers handle async processing. This pattern is proven and well-understood by framework users.

AI-0021 established category-driven routing: `Client.Chat()`, `Client.Embed()`, `Client.Ocr()` route independently to different providers. Vision analysis uses `Client.Chat()` with image bytes via `ChatOptions.Image`. OCR uses `Client.Ocr()` or delegates through Chat via the `Via` protocol. These categories provide the inference surface that `[MediaAnalysis]` consumes.

**The problem `[MediaAnalysis]` solves** is the gap between media storage and AI understanding. Today, every application that stores media and wants AI analysis must write the same boilerplate:

1. Read bytes from storage (cache-aware)
2. Send bytes to vision/OCR/transcription AI
3. Parse structured results
4. Write results back to entity properties
5. Trigger embedding generation from analysis text
6. Handle failures, retries, and async processing
7. Support re-analysis when prompts or models improve

S6.SnapVault's `PhotoProcessingService` demonstrates this pain. The `GenerateDetailedDescriptionAsync` method alone is ~100 lines of manual vision-to-entity wiring: loading gallery bytes via `OpenRead()`, building a prompt, calling `Client.Chat()` with image bytes, parsing JSON responses with three fallback strategies, writing structured results to `PhotoAsset.AiAnalysis`, and saving. The `ClassifyImageStyleAsync` method adds another ~60 lines for classification. The `RegenerateAIAnalysisAsync` method adds ~100 lines for re-analysis with locked-fact preservation. The `PhotoProcessingWorker` adds another ~130 lines of background processing infrastructure.

This is approximately **400 lines of application code** that follows a pattern the framework should own. Every media-heavy application (photo management, document processing, audio transcription, contract analysis) would duplicate this pattern. `[MediaAnalysis]` makes it a single attribute.

## Decision

### Part 1: The Attribute and Flags Enum

```csharp
namespace Koan.AI.Attributes;

/// <summary>
/// Declares automatic AI analysis on media upload or content change.
/// Properties are populated by convention or explicit mapping.
/// Mirrors the [Embedding] lifecycle pattern (AI-0020) for media-specific AI.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class MediaAnalysisAttribute : Attribute
{
    /// <summary>
    /// Which analysis operations to perform. Defaults to Describe (vision).
    /// Combine with bitwise OR: MediaAnalysis.Describe | MediaAnalysis.Ocr
    /// </summary>
    public MediaAnalysis Analysis { get; set; } = MediaAnalysis.Describe;

    /// <summary>
    /// Target property for Describe results. If null, detected by convention:
    /// AiDescription, Description, Summary (first match, string type).
    /// </summary>
    public string? DescriptionProperty { get; set; }

    /// <summary>
    /// Target property for Transcribe results. If null, detected by convention:
    /// Transcript, Transcription (first match, string type).
    /// </summary>
    public string? TranscriptProperty { get; set; }

    /// <summary>
    /// Target property for Ocr results. If null, detected by convention:
    /// OcrText, ExtractedText, Text (first match, string type).
    /// </summary>
    public string? OcrTextProperty { get; set; }

    /// <summary>
    /// Target property for Extract results. Required when Analysis includes Extract
    /// and the target type is ambiguous. Maps to a typed property (deserialized from
    /// structured AI output).
    /// </summary>
    public string? ExtractedDataProperty { get; set; }

    /// <summary>
    /// Whether to process asynchronously (default: true).
    /// When true: entity saved immediately after upload, analysis queued for background processing.
    /// When false: analysis runs synchronously during save, with configurable timeout.
    /// </summary>
    public bool Async { get; set; } = true;

    /// <summary>
    /// Named prompt from the PromptEntry catalog (AI-0025) for Extract and Classify modes.
    /// When set, the prompt is loaded via Prompt.Load(name) and used for structured extraction.
    /// Domain experts can edit the prompt without code deploys.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Analysis version. Increment to trigger re-analysis of all existing entities of this type.
    /// Stored in MediaAnalysisState; entities with lower version are queued for re-processing.
    /// Useful when: prompt improved, model upgraded, extraction schema changed.
    /// </summary>
    public int Version { get; set; } = 1;
}
```

```csharp
namespace Koan.AI.Attributes;

/// <summary>
/// Flags enum for media analysis operations.
/// Each flag maps to a distinct AI capability and convention-detected target property.
/// </summary>
[Flags]
public enum MediaAnalysis
{
    /// <summary>No analysis.</summary>
    None        = 0,

    /// <summary>
    /// Vision: generate a natural-language description of the media content.
    /// Applies to: images, video keyframes.
    /// Routes through: Client.Chat() with image bytes (AI-0021 Chat category).
    /// Convention properties: AiDescription, Description, Summary.
    /// </summary>
    Describe    = 1,

    /// <summary>
    /// Extract visible text from images, documents, or screenshots.
    /// Routes through: Client.Ocr() (AI-0021 Ocr category, may delegate via Chat).
    /// Convention properties: OcrText, ExtractedText, Text.
    /// </summary>
    Ocr         = 2,

    /// <summary>
    /// Speech-to-text for audio and video content.
    /// Routes through: transcription pipeline (Whisper-compatible adapter).
    /// Convention properties: Transcript, Transcription.
    /// </summary>
    Transcribe  = 4,

    /// <summary>
    /// Categorize content into predefined classes (image type, document type, mood, etc.).
    /// Routes through: Client.Chat() with classification prompt.
    /// Convention properties: Category, Classification, MediaType (string or enum).
    /// </summary>
    Classify    = 8,

    /// <summary>
    /// Structured extraction via named Prompt (AI-0025) into a typed property.
    /// Requires: Prompt property set on [MediaAnalysis] attribute.
    /// Routes through: Client.Chat&lt;T&gt;() with extraction prompt and media bytes.
    /// Convention: uses ExtractedDataProperty or first non-primitive, non-string property.
    /// </summary>
    Extract     = 16,

    /// <summary>All analysis modes.</summary>
    All         = Describe | Ocr | Transcribe | Classify | Extract
}
```

### Part 2: Convention-Based Property Detection

When property names are not explicitly specified in the attribute, the framework detects target properties by convention at bootstrap time. This follows the same pattern as `EmbeddingMetadata.Resolve<T>()` (AI-0021): scan public instance properties, match by name convention, cache the mapping.

```csharp
namespace Koan.AI.Media;

/// <summary>
/// Resolves target properties for each analysis mode using convention or explicit attribute mapping.
/// Cached per entity type at bootstrap — no per-request reflection cost.
/// </summary>
internal static class MediaAnalysisMetadata
{
    public static MediaAnalysisMapping Resolve<TEntity>() where TEntity : class
    {
        return Cache<TEntity>.Mapping;
    }

    private static class Cache<TEntity> where TEntity : class
    {
        public static readonly MediaAnalysisMapping Mapping = BuildMapping();

        private static MediaAnalysisMapping BuildMapping()
        {
            var attr = typeof(TEntity).GetCustomAttribute<MediaAnalysisAttribute>();
            if (attr == null)
                return MediaAnalysisMapping.Empty;

            var props = typeof(TEntity)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .ToList();

            return new MediaAnalysisMapping
            {
                Analysis = attr.Analysis,
                Async = attr.Async,
                Version = attr.Version,
                PromptName = attr.Prompt,

                DescriptionTarget = ResolveProperty(
                    attr.DescriptionProperty, props, typeof(string),
                    "AiDescription", "Description", "Summary"),

                OcrTextTarget = ResolveProperty(
                    attr.OcrTextProperty, props, typeof(string),
                    "OcrText", "ExtractedText", "Text"),

                TranscriptTarget = ResolveProperty(
                    attr.TranscriptProperty, props, typeof(string),
                    "Transcript", "Transcription"),

                ClassifyTarget = ResolveClassifyProperty(props),

                ExtractTarget = ResolveExtractProperty(
                    attr.ExtractedDataProperty, props),
            };
        }
    }
}
```

**Convention resolution rules per mode:**

| Mode | Convention Names (priority order) | Property Type | Fallback |
|------|-----------------------------------|---------------|----------|
| Describe | `AiDescription`, `Description`, `Summary` | `string` | Bootstrap warning |
| Ocr | `OcrText`, `ExtractedText`, `Text` | `string` | Bootstrap warning |
| Transcribe | `Transcript`, `Transcription` | `string` | Bootstrap warning |
| Classify | `Category`, `Classification`, `MediaType` | `string` or `enum` | Bootstrap warning |
| Extract | `ExtractedDataProperty` attribute value | Any non-primitive | Required if ambiguous |

When a mode is requested but no matching property is found, the registrar emits a bootstrap warning:

```
[MediaAnalysis] on PhotoAsset requests Ocr but no target property found.
  Expected one of: OcrText (string), ExtractedText (string), Text (string)
  Add a property or set OcrTextProperty in the attribute.
```

### Part 3: The Processing Pipeline

The pipeline hooks into the `MediaEntity<T>.Upload()` lifecycle, mirroring how `[Embedding]` hooks into `entity.Save()`.

```
MediaEntity<T>.Upload(stream, fileName, contentType)
     │
     ▼
StorageEntity<T> onboard              ← Bytes written to IStorageProvider
     │                                   (local → async push to durable via STOR-0010)
     ▼
MediaAnalysisInterceptor.OnUpload()   ← Lifecycle hook detects [MediaAnalysis]
     │
     ├── Async = true (default)
     │   ├── Entity saved immediately (no analysis yet)
     │   ├── MediaAnalysisState<T> created: { Status: Queued, Version: N }
     │   └── Job enqueued to MediaAnalysisWorker
     │
     └── Async = false
         ├── Analysis runs synchronously (with timeout)
         ├── Results written to entity properties
         └── Falls through to [Embedding] check
     │
     ▼
Analysis execution (sync or via worker):
     │
     ├── Describe: bytes → Client.Chat(visionPrompt, new ChatOptions { Image = bytes })
     │              → result written to entity.AiDescription (or convention target)
     │
     ├── Ocr:      bytes → Client.Ocr(bytes)
     │              → result written to entity.OcrText (or convention target)
     │
     ├── Transcribe: bytes → Client.Transcribe(audioStream)
     │              → result written to entity.Transcript (or convention target)
     │
     ├── Classify:  bytes → Client.Chat<TCategory>(classifyPrompt, new ChatOptions { Image = bytes })
     │              → result written to entity.Category (or convention target)
     │
     └── Extract:   bytes → Client.Chat<TExtract>(Prompt.Load(name), new ChatOptions { Image = bytes })
                    → result deserialized and written to entity.Terms (or convention target)
     │
     ▼
[Embedding] detected on same entity?
     │
     ├── Yes: Analysis text concatenated → Client.Embed() → entity.Embedding
     │        Convention order: AiDescription + OcrText + Transcript + Classification + ExtractedData.ToString()
     │        Custom ToEmbeddingText() takes precedence when defined.
     │
     └── No: Skip embedding
     │
     ▼
entity.Save()                          ← Entity + vector saved atomically
                                         (within EntityContext transaction, AI-0020 pattern)
```

**Key design point: `[MediaAnalysis]` feeds `[Embedding]`.** The vision description, OCR text, and transcript become the embedding source text. Cross-modal search works because media content is represented as text in the vector space. An image of a sunset has an embedding derived from "A vibrant sunset over mountain peaks with orange and purple clouds" — text queries find it naturally.

### Part 4: Analysis State Tracking

Mirrors `EmbeddingState<T>` (AI-0020) for tracking analysis processing state:

```csharp
namespace Koan.AI.Media;

/// <summary>
/// Tracks the processing state of media analysis for an entity.
/// Stored alongside the entity, queryable for monitoring and retry.
/// Follows the same pattern as EmbeddingState<T> (AI-0020).
/// </summary>
public sealed record MediaAnalysisState<T> where T : class
{
    public string EntityId { get; init; } = "";
    public MediaAnalysisStatus Status { get; init; } = MediaAnalysisStatus.Pending;
    public int Version { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>
    /// Per-mode completion tracking. Allows partial completion
    /// (e.g., Describe succeeded but Ocr failed).
    /// </summary>
    public IReadOnlyDictionary<MediaAnalysis, ModeStatus> ModeStatuses { get; init; }
        = new Dictionary<MediaAnalysis, ModeStatus>();
}

public enum MediaAnalysisStatus
{
    Pending,
    Queued,
    Processing,
    Completed,
    PartiallyCompleted,  // Some modes succeeded, others failed
    Failed
}

public sealed record ModeStatus(
    bool Completed,
    DateTimeOffset? CompletedAt,
    string? Error);
```

Per-mode status tracking enables partial completion: if Describe succeeds but Ocr fails (provider down), the description is retained and only Ocr is retried. This avoids re-running expensive vision calls when a cheaper OCR retry would suffice.

### Part 5: Background Worker

```csharp
namespace Koan.AI.Media;

/// <summary>
/// Background service that processes queued media analysis jobs.
/// Follows the same structural pattern as EmbeddingWorker (AI-0020).
/// Reads bytes via IStorageService (cache-aware via ReplicatedStorageProvider, STOR-0010).
/// </summary>
internal sealed class MediaAnalysisWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaAnalysisWorker> _logger;
    private readonly MediaAnalysisOptions _options;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await DequeuePendingAsync(_options.BatchSize, stoppingToken);

            foreach (var job in batch)
            {
                await ProcessJobAsync(job, stoppingToken);
            }

            if (batch.Count == 0)
                await Task.Delay(_options.PollInterval, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(AnalysisJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        using var tx = EntityContext.Transaction($"media-analysis-{job.EntityId}");

        try
        {
            // 1. Load entity
            var entity = await LoadEntityAsync(job.EntityType, job.EntityId, ct);
            if (entity == null) return;

            // 2. Check version — skip if entity already at current version
            var state = await GetStateAsync(job.EntityType, job.EntityId, ct);
            if (state.Version >= job.Version)
            {
                _logger.LogDebug("Skipping {EntityId} — already at version {Version}",
                    job.EntityId, state.Version);
                return;
            }

            // 3. Read bytes via IStorageService (cache-aware)
            //    ReplicatedStorageProvider serves from local cache when available,
            //    pulls through from durable tier on miss (STOR-0010)
            await using var mediaStream = await OpenMediaBytesAsync(entity, ct);

            // 4. Run each requested analysis mode
            var mapping = ResolveMapping(job.EntityType);
            var results = await ExecuteAnalysisModesAsync(
                entity, mediaStream, mapping, ct);

            // 5. Write results to entity properties
            ApplyResults(entity, mapping, results);

            // 6. Update state
            await UpdateStateAsync(job.EntityType, job.EntityId,
                state with
                {
                    Status = results.AllSucceeded
                        ? MediaAnalysisStatus.Completed
                        : MediaAnalysisStatus.PartiallyCompleted,
                    Version = job.Version,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ModeStatuses = results.ModeStatuses
                }, ct);

            // 7. Save entity — triggers [Embedding] if present (AI-0020 lifecycle)
            await SaveEntityAsync(entity, ct);
            await EntityContext.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media analysis failed for {EntityType}:{EntityId}",
                job.EntityType.Name, job.EntityId);

            await EntityContext.RollbackAsync(ct);
            await RecordFailureAsync(job, ex, ct);
        }
    }
}
```

**Retry strategy:** Exponential backoff with jitter, matching the `EmbeddingWorker` pattern. Max retries configurable via `MediaAnalysisOptions.MaxRetries` (default: 3). After max retries, state moves to `Failed` and the entity is skipped until `Version` is bumped or manual re-trigger.

### Part 6: Storage-Aware Byte Access

Because `MediaEntity<T>` reads bytes through `IStorageService` and `IStorageProvider`, the `ReplicatedStorageProvider` (STOR-0010) works transparently:

```csharp
// Inside MediaAnalysisWorker — reading bytes for analysis
await using var mediaStream = await entity.OpenRead(ct);

// What happens under the hood (STOR-0010 ReplicatedStorageProvider):
//
// 1. Check local cache:
//    └── Cache hit → return local stream (fast, no network)
//
// 2. Cache miss:
//    ├── Pull from durable tier (S3, MinIO, etc.)
//    ├── Write to local cache (for next access)
//    └── Return stream
//
// The worker doesn't need to know about storage topology.
// Hot media (recently uploaded): served from local cache.
// Cold media (version bump re-analysis): pulled through on demand.
```

This means version-bump re-analysis of large media libraries works efficiently: recently accessed media serves from cache, while cold media is pulled through once and cached for the analysis pipeline.

### Part 7: `[Embedding]` Integration — Cross-Modal Search

When both `[MediaAnalysis]` and `[Embedding]` are present on the same entity, the framework automatically bridges analysis results into embedding text:

```csharp
// Framework-generated embedding text composition (convention)
internal static class MediaAnalysisEmbeddingBridge
{
    /// <summary>
    /// Builds embedding text from analysis results.
    /// Called by [Embedding] lifecycle when [MediaAnalysis] is also present.
    /// Custom ToEmbeddingText() on the entity takes precedence.
    /// </summary>
    public static string ComposeEmbeddingText<T>(T entity, MediaAnalysisMapping mapping)
        where T : class
    {
        var parts = new List<string>();

        // Deterministic order: Describe → Ocr → Transcribe → Classify → Extract
        AppendIfPresent(parts, entity, mapping.DescriptionTarget);
        AppendIfPresent(parts, entity, mapping.OcrTextTarget);
        AppendIfPresent(parts, entity, mapping.TranscriptTarget);

        if (mapping.ClassifyTarget != null)
        {
            var classValue = mapping.ClassifyTarget.GetValue(entity);
            if (classValue != null)
                parts.Add($"Category: {classValue}");
        }

        if (mapping.ExtractTarget != null)
        {
            var extractValue = mapping.ExtractTarget.GetValue(entity);
            if (extractValue != null)
                parts.Add(extractValue.ToString()!);
        }

        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
```

**Cross-modal search in action:**

```csharp
// Text query finds images — image descriptions are in the embedding space
var photos = await Vector<PhotoAsset>.Search("sunset over mountains", topK: 10);

// Text query finds audio — transcripts are in the embedding space
var calls = await Vector<CallRecording>.Search("customer complaint about shipping", topK: 5);

// Text query finds documents — OCR text is in the embedding space
var contracts = await Vector<Contract>.Search("indemnification clause", topK: 3);
```

This works because `[MediaAnalysis]` converts media into text (descriptions, OCR, transcripts), and `[Embedding]` converts that text into vectors. The vector space is unified: text queries match images, audio, and documents through their textual representations.

### Part 8: Version Bumping and Re-Analysis

When the `Version` property is incremented on the `[MediaAnalysis]` attribute:

```csharp
// Before: Version = 1
[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr, Version = 1)]

// After: Version = 2 (prompt improved, model upgraded, or extraction schema changed)
[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr, Version = 2)]
```

The framework detects the version mismatch at startup:

1. `KoanAutoRegistrar.Describe()` reads `[MediaAnalysis].Version` from the attribute.
2. Queries `MediaAnalysisState<T>` for entities with `Version < 2`.
3. Queues all stale entities for re-analysis via `MediaAnalysisWorker`.
4. Worker processes them in batches, writing new results and updating state to `Version = 2`.
5. If `[Embedding]` is present, re-embedding triggers automatically (the analysis text changed).

**Boot report output during version migration:**

```
[MediaAnalysis] PhotoAsset: version 1 → 2, 12,847 entities queued for re-analysis.
  Estimated time: ~42 minutes at current throughput (5.1 entities/sec).
```

This is the same principle as `[Embedding].Version` (AI-0020) — a declarative way to say "my understanding of this content has improved, re-process everything."

### Part 9: Usage Examples

#### Image entity — auto-analyzed on upload

```csharp
[StorageBinding(Profile = "cold", Container = "photos")]
[MediaAnalysis(Analysis = MediaAnalysis.Describe | MediaAnalysis.Ocr | MediaAnalysis.Classify)]
[Embedding]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    // Convention-detected targets:
    public string? AiDescription { get; set; }   // ← Describe writes here
    public string? OcrText { get; set; }         // ← Ocr writes here
    public string? Category { get; set; }        // ← Classify writes here
    public float[]? Embedding { get; set; }      // ← [Embedding] from analysis text

    // Non-AI metadata (unaffected)
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CameraModel { get; set; }
    public GpsCoordinates? Location { get; set; }
}

// Upload — everything happens automatically
var photo = await PhotoAsset.Upload(stream, "sunset.jpg", "image/jpeg");
// 1. Bytes stored in cold tier (via [StorageBinding])
// 2. Entity saved immediately (Async = true default)
// 3. Analysis queued: Describe, Ocr, Classify
// 4. Worker processes: AiDescription, OcrText, Category populated
// 5. [Embedding] triggers: embedding generated from analysis text
// 6. Entity + vector saved atomically
```

#### Audio entity — auto-transcribed

```csharp
[StorageBinding(Profile = "standard", Container = "recordings")]
[MediaAnalysis(Analysis = MediaAnalysis.Transcribe | MediaAnalysis.Describe)]
[Embedding]
public class CallRecording : MediaEntity<CallRecording>
{
    public string? Transcript { get; set; }      // ← Transcribe writes here
    public string? AiDescription { get; set; }   // ← Describe writes here (call summary)
    public float[]? Embedding { get; set; }      // ← [Embedding] from transcript + description
    public TimeSpan Duration { get; set; }
    public string? CallerName { get; set; }
}
```

#### Document with structured extraction

```csharp
[StorageBinding(Profile = "secure", Container = "contracts")]
[MediaAnalysis(
    Analysis = MediaAnalysis.Ocr | MediaAnalysis.Extract,
    Prompt = "contract-extractor",
    ExtractedDataProperty = nameof(Terms))]
[Embedding]
public class Contract : MediaEntity<Contract>
{
    public string? OcrText { get; set; }         // ← Ocr writes here
    public ContractTerms? Terms { get; set; }    // ← Extract writes here (typed)
    public float[]? Embedding { get; set; }      // ← [Embedding] from OcrText + Terms
}

public record ContractTerms(
    string PartyA,
    string PartyB,
    DateOnly EffectiveDate,
    decimal? TotalValue,
    string[] KeyClauses);

// The "contract-extractor" PromptEntry (AI-0025) is editable by domain experts:
// "Extract the following fields from this contract document:
//  - Party A (full legal name)
//  - Party B (full legal name)
//  - Effective date
//  - Total value (if stated)
//  - Key clauses (list the 3 most important clauses)"
//
// Marta edits this prompt in the admin UI. No code deploy needed.
// Bump Version to re-extract all contracts with the improved prompt.
```

#### Multi-modal entity with all analysis modes

```csharp
[StorageBinding(Profile = "standard", Container = "lessons")]
[MediaAnalysis(Analysis = MediaAnalysis.All, Prompt = "lesson-extractor")]
[Embedding]
public class VideoLesson : MediaEntity<VideoLesson>
{
    public string? AiDescription { get; set; }       // ← Describe (keyframe analysis)
    public string? OcrText { get; set; }             // ← Ocr (slides, whiteboard text)
    public string? Transcript { get; set; }          // ← Transcribe (spoken content)
    public string? Category { get; set; }            // ← Classify (topic: "mathematics", "physics")
    public LessonOutline? ExtractedData { get; set; } // ← Extract (structured outline)
    public float[]? Embedding { get; set; }          // ← [Embedding] from all of the above
    public TimeSpan Duration { get; set; }
}
```

### Part 10: S6.SnapVault Migration — Before and After

**Before (`[MediaAnalysis]`):** S6.SnapVault's `PhotoProcessingService` manually wires vision AI to entity properties:

```csharp
// PhotoProcessingService.cs — ~100 lines for vision analysis
private async Task GenerateDetailedDescriptionAsync(PhotoAsset photo, string? analysisStyleId, CancellationToken ct)
{
    // 1. Resolve analysis style entity (~30 lines)
    var styleEntity = await ResolveAnalysisStyleAsync(analysisStyleId, photo, ct);

    // 2. Load gallery image bytes via OpenRead() (~10 lines)
    var gallery = await PhotoGallery.Get(photo.GalleryMediaId, ct);
    await using var imageStream = await gallery.OpenRead(ct);
    using var ms = new MemoryStream();
    await imageStream.CopyToAsync(ms, ct);
    var imageBytes = ms.ToArray();

    // 3. Build prompt from factory (~15 lines)
    string prompt = styleEntity == null
        ? _promptFactory.RenderPrompt()
        : _promptFactory.RenderPromptFor(styleEntity);
    prompt = _promptFactory.SubstituteVariables(prompt, context);

    // 4. Call Client.Chat with image bytes (~5 lines)
    var response = await Client.Chat(prompt, new ChatOptions
    {
        Image = imageBytes,
        ImageMimeType = "image/jpeg"
    }, ct);

    // 5. Parse JSON response with 3 fallback strategies (~100 lines)
    var analysis = ParseAiResponse(response);

    // 6. Write results to entity (~10 lines)
    photo.AiAnalysis = analysis;
    await photo.Save(ct);
}

// ClassifyImageStyleAsync — ~60 lines for classification
// RegenerateAIAnalysisAsync — ~100 lines for re-analysis with locked facts
// PhotoProcessingWorker — ~130 lines for background processing
// FormFileWrapper — ~30 lines for IFormFile adapter
//
// Total: ~400+ lines of media-AI wiring code
```

**After (`[MediaAnalysis]`):**

```csharp
// PhotoAsset.cs — the attribute replaces ~400 lines of service code
[StorageBinding(Profile = "cold", Container = "photos")]
[MediaAnalysis(
    Analysis = MediaAnalysis.Describe | MediaAnalysis.Classify,
    Prompt = "photo-analyzer",
    Version = 1)]
[Embedding(Policy = EmbeddingPolicy.Explicit, Async = true, MaxTokens = 8191)]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string? AiDescription { get; set; }        // ← auto-populated
    public string? Category { get; set; }             // ← auto-populated
    public float[]? Embedding { get; set; }           // ← auto-populated

    public int Width { get; set; }
    public int Height { get; set; }
    public string? CameraModel { get; set; }
    // ... remaining EXIF metadata properties
}

// Upload — one line, everything handled by framework
var photo = await PhotoAsset.Upload(stream, "sunset.jpg", "image/jpeg");
```

What `PhotoProcessingService` previously did is now framework infrastructure:
- **Byte loading** → `MediaAnalysisWorker` reads via `OpenRead()` (STOR-0010 cache-aware)
- **Vision prompt** → `Prompt.Load("photo-analyzer")` from `PromptEntry` catalog (AI-0025)
- **AI call** → `Client.Chat()` with image bytes (AI-0021 Chat category)
- **Response parsing** → Framework handles structured JSON extraction
- **Entity update** → Convention-detected property writing
- **Background processing** → `MediaAnalysisWorker` (replaces `PhotoProcessingWorker`)
- **Re-analysis** → Bump `Version` (replaces `RegenerateAIAnalysisAsync`)
- **Embedding** → `[Embedding]` feeds on analysis text (AI-0020 lifecycle)

S6.SnapVault retains application-specific concerns that `[MediaAnalysis]` intentionally does not absorb:
- EXIF metadata extraction (camera, GPS, date — not AI)
- Derivative generation (gallery, thumbnails — DX-0047 fluent transform API)
- Event auto-assignment (daily albums — application logic)
- SignalR progress events (UI-specific real-time updates)
- Locked-fact preservation on re-analysis (application-specific UX mechanic)

### Part 11: Prompt Integration (AI-0025)

The `Prompt` property on `[MediaAnalysis]` connects to the `PromptEntry` catalog:

```csharp
[MediaAnalysis(Prompt = "receipt-extractor")]
```

At bootstrap, the framework validates that "receipt-extractor" exists as a `PromptEntry`. At runtime:

1. `Prompt.Load("receipt-extractor")` loads the prompt (AI-0025)
2. Variables in the prompt are resolved from entity context (content type, file name, dimensions for images)
3. The prompt is sent with media bytes to `Client.Chat<T>()`
4. Structured response is deserialized into the target property

**Domain expert workflow (Marta persona):**

1. Marta edits "receipt-extractor" prompt in admin UI — no code deploy
2. Developer bumps `Version` from 1 to 2 on the `[MediaAnalysis]` attribute
3. All receipts are re-processed with the improved prompt
4. New extraction results replace old ones; `[Embedding]` re-embeds

**Prompt versioning without code deploy (future):** When `MediaAnalysisOptions.AutoBumpOnPromptChange = true`, the framework detects prompt content changes and automatically queues re-analysis without requiring a `Version` bump in code. This is opt-in because prompt edits may be experimental and not warrant full re-processing.

### Part 12: Configuration

```csharp
namespace Koan.AI.Media;

public sealed class MediaAnalysisOptions
{
    /// <summary>
    /// Maximum number of entities to process per batch in the background worker.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Interval between polling for new analysis jobs when the queue is empty.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum retry attempts for failed analysis before marking as permanently failed.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout for synchronous analysis (Async = false).
    /// On timeout, falls back to async processing.
    /// </summary>
    public TimeSpan SyncTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to automatically queue re-analysis when a referenced PromptEntry changes.
    /// Opt-in because prompt edits may be experimental.
    /// </summary>
    public bool AutoBumpOnPromptChange { get; set; } = false;

    /// <summary>
    /// Maximum concurrent analysis operations across all entity types.
    /// Prevents overwhelming AI providers during version-bump bulk re-analysis.
    /// </summary>
    public int MaxConcurrency { get; set; } = 3;
}
```

Configuration in `appsettings.json`:

```json
{
  "Koan": {
    "Ai": {
      "MediaAnalysis": {
        "BatchSize": 10,
        "PollInterval": "00:00:05",
        "MaxRetries": 3,
        "SyncTimeout": "00:00:30",
        "MaxConcurrency": 3
      }
    }
  }
}
```

### Part 13: Boot Report

`KoanAutoRegistrar.Describe()` reports `[MediaAnalysis]` registrations:

```
╔══ Module: Koan.AI ═══════════════════════════════════════════════════════╗
║                                                                         ║
║ Categories                                                              ║
║   Chat     → ollama-local (llama3.2)                                   ║
║   Embed    → ollama-local (nomic-embed-text)                           ║
║   Ocr      → ollama-local (glm-ocr) via Chat                          ║
║                                                                         ║
║ Media Analysis                                                          ║
║   PhotoAsset       Describe | Classify  async  v1  prompt: photo-analyzer║
║   CallRecording    Transcribe | Describe  async  v1                     ║
║   Contract         Ocr | Extract  async  v1  prompt: contract-extractor ║
║                                                                         ║
║   Worker: running  batch: 10  concurrency: 3                           ║
║   Pending: 0  Failed: 0                                                ║
║                                                                         ║
╚═════════════════════════════════════════════════════════════════════════╝
```

### Part 14: Content-Type Compatibility

Each analysis mode applies only to compatible content types. The worker checks content type before executing a mode:

| Mode | Compatible Content Types | Incompatible Behavior |
|------|--------------------------|----------------------|
| Describe | `image/*`, `video/*` | Skip with debug log |
| Ocr | `image/*`, `application/pdf` | Skip with debug log |
| Transcribe | `audio/*`, `video/*` | Skip with debug log |
| Classify | `image/*`, `video/*`, `audio/*`, `application/pdf` | Skip with debug log |
| Extract | All (content-type-agnostic — prompt determines extraction) | Always runs |

This enables a single `[MediaAnalysis]` attribute on a base class shared by image and audio subtypes. Inapplicable modes are silently skipped, not errored.

### Part 15: Package Location

`[MediaAnalysis]` lives in `Koan.AI`, extending the existing package with media-aware processing. It depends on:

- **`Koan.AI`** — `Client.Chat()`, `Client.Ocr()`, `Client.Embed()` (AI-0021 facades)
- **`Koan.AI.Contracts`** — `ChatOptions`, `OcrOptions` (adapter contracts)
- **`Koan.Media.Abstractions`** — `MediaEntity<T>`, `StorageEntity<T>` (entity hierarchy)
- **`Koan.Storage`** — `IStorageProvider`, `IStorageService` (byte access)
- **`Koan.Data.AI`** — `[Embedding]`, `EmbeddingState<T>` (embedding lifecycle integration)

No new package is introduced. `[MediaAnalysis]` is a natural extension of `Koan.AI`'s entity-first philosophy: `[Embedding]` handles text→vector, `[MediaAnalysis]` handles media→text→vector.

## Consequences

### Positive

- **~400 lines of per-application boilerplate eliminated.** S6.SnapVault's `PhotoProcessingService` vision wiring, `PhotoProcessingWorker` background processing, and `FormFileWrapper` adapter are replaced by a single attribute declaration.
- **Cross-modal search as a framework primitive.** Images, audio, and documents become searchable by text queries through the `[MediaAnalysis]` → `[Embedding]` pipeline, without application-specific wiring.
- **Convention-driven DX.** Developers add `string? AiDescription` and `[MediaAnalysis]` — the framework connects them. No manual property mapping, no byte loading, no AI client calls.
- **Version bumping enables prompt iteration.** Domain experts (Marta persona) edit extraction prompts; developers bump `Version` to re-process. The feedback loop between prompt quality and extraction accuracy is declarative.
- **Storage-aware by default.** `ReplicatedStorageProvider` (STOR-0010) transparently serves bytes from local cache for hot media, pulls through for cold media. No special handling in application code.
- **Partial failure tolerance.** Per-mode status tracking enables targeted retries. A failed OCR call does not discard a successful vision description.
- **Consistent lifecycle model.** `MediaAnalysisState<T>` mirrors `EmbeddingState<T>` — developers learn one pattern for both embedding and media analysis lifecycle tracking.
- **Progressive disclosure maintained.** Bare `[MediaAnalysis]` with convention properties is Tier 0. Explicit property mapping, custom prompts, and version bumping are Tiers 1-2. `MediaAnalysisOptions` is Tier 3.

### Negative / Trade-offs

- **Implicit behavior may surprise.** Developers unfamiliar with the attribute pattern may not expect automatic AI calls on upload. Mitigated by boot report visibility and clear documentation.
- **Background processing adds eventual consistency.** With `Async = true` (default), entity properties are null between upload and analysis completion. Consumers must handle null analysis gracefully. Mitigated by `MediaAnalysisState<T>` for status checking.
- **Prompt existence validated at bootstrap, not compile time.** A misspelled prompt name fails at startup, not at build. This is consistent with `[StorageBinding(Profile = "cold")]` validation timing.
- **Complex entities with custom analysis logic may outgrow the attribute.** Locked-fact preservation (S6.SnapVault's reroll-with-holds), multi-stage classification (classify → style-specific prompt → analyze), and application-specific response parsing are not covered. These remain in application code, calling `Client.Chat()` directly. `[MediaAnalysis]` covers the 80% case.
- **Transcription pipeline is a new integration surface.** Unlike Describe (via Chat) and Ocr (existing category), Transcribe requires a transcription adapter (Whisper-compatible). This may not be available in all deployments. Mode is silently skipped when no transcription capability is registered.

## References

- AI-0020: Entity-First AI and Transaction Coordination — `[Embedding]` lifecycle pattern, `EmbeddingState<T>`, background worker, version bumping
- AI-0021: Category-Driven AI with Convention Defaults — `Client.Chat()`, `Client.Ocr()`, `Client.Embed()` facades, convention-inferred metadata
- AI-0022: Unified AI Lifecycle Vision — `[MediaAnalysis]` as Part 7 of the lifecycle vision, cross-modal search concept
- AI-0025: Prompt Primitive (planned) — `Prompt.Load()`, `PromptEntry` catalog for domain-expert-editable extraction prompts
- MEDIA-0001: Media Pillar Baseline and Storage Integration — `MediaEntity<T>`, derivatives, `Upload()` static method
- STOR-0001: Storage Module and Contracts — `StorageEntity<T>`, `IStorageProvider`, `IStorageService`
- STOR-0010: Replicated Storage with Local Cache Tier — `ReplicatedStorageProvider`, local cache + durable tier
- DX-0047: Fluent Media Transform API — derivative generation (complementary, not replaced)
- `src/Koan.AI/` — Existing AI implementation (attribute, worker patterns)
- `src/Koan.Media.Abstractions/` — Media entity hierarchy
- `src/Koan.Storage/` — Storage abstractions and providers
- `samples/S6.SnapVault/Services/PhotoProcessingService.cs` — Reference implementation that `[MediaAnalysis]` replaces
- `samples/S6.SnapVault/Services/PhotoProcessingWorker.cs` — Background worker pattern that `[MediaAnalysis]` subsumes
- `samples/S6.SnapVault/Models/PhotoAsset.cs` — Target entity for before/after comparison
