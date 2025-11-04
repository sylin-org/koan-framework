---
id: ARCH-0069
slug: s5-recs-partition-based-import-pipeline
domain: Architecture
status: Proposed
date: 2025-11-03
title: S5.Recs Partition-Based Import Pipeline Architecture
---

## Contract

- **Inputs**: Current monolithic `SeedService` (858 lines) handling import, vectorization, and cataloging synchronously with global lock.
- **Outputs**: Decoupled partition-based pipeline using Koan Entity<T> patterns for staging, independent workers for parallel processing, and signature-based embedding cache.
- **Error Modes**: Brittle synchronous import blocking on vectorization, silent vectorization failures, in-memory progress lost on restart, global import lock preventing concurrent operations, file-based caching not horizontally scalable.
- **Success Criteria**: Import returns immediately after storing media, vectorization processes in parallel with cache reuse via SHA256 signatures, multiple imports run concurrently (Anime + Manga simultaneously), all state persists in MongoDB via Entity<T> patterns, failures tracked and retryable, horizontally scalable architecture.

## Context

S5.Recs' current import mechanism has several architectural brittleness points identified through codebase analysis:

### Current Architecture Issues

1. **Monolithic SeedService (858 lines)**:
   - Single class handles: provider fetching, parsing, import, vectorization, cataloging
   - No separation of concerns
   - Cannot independently import without vectorizing, or re-catalog without re-importing

2. **Global Import Lock**:
   ```csharp
   private static volatile bool _importInProgress = false;
   ```
   - Only ONE import can run at a time globally
   - Importing Anime blocks importing Manga
   - Lock stuck if process crashes (no recovery)

3. **Synchronous Chaining**:
   ```
   Import Batch → Vectorize Batch → Next Batch
       │              │
       └──────────────┴─ BLOCKING: Must complete before next batch
   ```
   - Import speed limited by vectorization speed (Ollama API latency)
   - Cannot pipeline operations
   - Network issues with vector DB stall entire import

4. **Silent Vectorization Failures**:
   ```csharp
   catch { return 0; } // Silent failure in EmbedAndIndexAsync
   ```
   - Import "succeeds" even if ALL vectors fail
   - No retry mechanism
   - Failures only visible in post-import stats

5. **File-Based Caching**:
   - `cache/embeddings/{entityType}/{modelId}/{hash}.json`
   - Not horizontally scalable (local filesystem)
   - No access tracking or LRU cleanup
   - Separate cache per instance in distributed deployments

6. **In-Memory Progress Tracking**:
   ```csharp
   private readonly Dictionary<string, (...)> _progress = new();
   ```
   - Progress lost on app restart
   - No queryable job history
   - Cannot resume interrupted imports

7. **Service Locator Anti-Pattern**:
   ```csharp
   private readonly IServiceProvider _sp;
   var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
   ```
   - Hidden dependencies
   - Runtime failures instead of compile-time
   - Difficult to unit test

### Smart Caching Already Exists

The current implementation **does have** SHA256-based content hashing for embedding cache:
```csharp
// SeedService.cs line ~550
var embeddingText = BuildEmbeddingText(media);
var contentHash = ComputeContentHash(embeddingText); // SHA256
var cached = await _cache.GetAsync(contentHash, modelId, "Media");
```

This is good! The refactoring will preserve this smart caching but make it:
- Persistent (Entity<T> instead of files)
- Queryable (LINQ over MongoDB)
- Horizontally scalable (shared database)
- Access-tracked (LRU cleanup potential)

### User Requirements

From product analysis:
- **Independent moving parts**: Import should be a queue; captured media stored immediately
- **Parallel vectorization**: Vector building should check signatures independently
- **Non-blocking cataloging**: Tags/genres processed after import, not waiting for vectors

## Decision

**Proposed**: Partition-Based Import Pipeline using Koan Entity<T> patterns throughout.

### Architecture Overview

```
Media Entity Lifecycle (Same Entity, Different Partitions):

Provider API
    │
    ▼
Partition: "import-raw"           ← Raw from provider
    │ (ImportWorker writes)
    │ - ImportJobId set
    │ - ContentSignature computed (SHA256)
    │ - ImportedAt timestamp
    │
    ▼
Partition: "vectorization-queue"  ← Validated, needs vectors
    │ (ValidationWorker moves)
    │ - ValidatedAt timestamp
    │ - Structure checks passed
    │
    ▼
Partition: (default)              ← Live, searchable
    │ (VectorizationWorker moves)
    │ - VectorizedAt timestamp
    │ - Embedding in Weaviate
    │ - Available for search
    │
    ▼
CatalogWorker (watches default partition)
    │ - Extracts tags/genres from newly vectorized
    │ - Updates TagStatDoc/GenreStatDoc
    └─► Tag/Genre catalogs updated


ImportJob Entity Lifecycle:

User Request
    │
    ▼
Partition: "jobs-active"          ← Currently running
    │ - Status: Running
    │ - Progress tracked via Media partition counts
    │
    ▼
Partition: (default)              ← Historical record
    │ - Status: Completed/Failed
    └─► Queryable for history/audit
```

### Core Principles

1. **Partition = Processing Stage**: Use Koan's partition feature as natural queue boundaries
2. **Entity<T> Throughout**: No custom queue entities, media itself moves through partitions
3. **Signature-Based Caching**: SHA256 content hash for intelligent embedding reuse
4. **Independent Workers**: BackgroundService instances polling partitions for work
5. **Fail-Visible**: All errors tracked in entity properties, retryable

### Key Entities

```csharp
// Media: Extended for pipeline tracking
public class Media : Entity<Media>
{
    // Existing: Title, Synopsis, Genres, Tags, etc.

    // Pipeline metadata (NEW):
    public string? ImportJobId { get; set; }
    public string? ContentSignature { get; set; }    // SHA256
    public DateTime? ImportedAt { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public DateTime? VectorizedAt { get; set; }
    public string? ProcessingError { get; set; }
    public int RetryCount { get; set; }
}

// ImportJob: Progress tracking
public class ImportJob : Entity<ImportJob>
{
    public string JobId { get; set; }
    public string Source { get; set; }
    public string MediaTypeId { get; set; }
    public ImportJobStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? Limit { get; set; }
    public bool Overwrite { get; set; }
    public List<string> Errors { get; set; }
}

// EmbeddingCacheEntry: Persistent embedding cache
public class EmbeddingCacheEntry : Entity<EmbeddingCacheEntry, string>
{
    public override string Id { get; set; }          // {signature}:{model}:{type}
    public string ContentSignature { get; set; }
    public string ModelId { get; set; }
    public string EntityType { get; set; }
    public float[] Embedding { get; set; }
    public int Dimension { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
}
```

### Worker Responsibilities

**ImportWorker** (Fetch & Stage):
```csharp
// Polls: ImportJob in "jobs-active" partition
// Writes: Media to "import-raw" partition
// Action: Stream from provider, compute signatures, stage raw data
```

**ValidationWorker** (Validate & Promote):
```csharp
// Polls: Media in "import-raw" partition
// Writes: Media to "vectorization-queue" partition
// Action: Check required fields, validate structure, move to next stage
```

**VectorizationWorker** (Generate Embeddings):
```csharp
// Polls: Media in "vectorization-queue" partition
// Writes: Media to (default) partition
// Action: Check EmbeddingCacheEntry by signature, generate/reuse embedding
// Batch optimization: Fetch 10 media, batch-fetch their cache entries
```

**CatalogWorker** (Tag/Genre Aggregation):
```csharp
// Polls: Media in (default) partition where VectorizedAt > lastRun
// Writes: TagStatDoc, GenreStatDoc
// Action: Extract tags/genres, apply PreemptiveTagFilter baseline NSFW marking
```

### Progress Tracking via Queries

Instead of tracking counts in ImportJob, derive progress dynamically:
```csharp
// How many media fetched?
using (EntityContext.Partition("import-raw"))
{
    var count = await Media.Query()
        .Where(m => m.ImportJobId == jobId)
        .CountAsync(ct);
}

// How many validated?
using (EntityContext.Partition("vectorization-queue"))
{
    var count = await Media.Query()
        .Where(m => m.ImportJobId == jobId)
        .CountAsync(ct);
}

// How many vectorized (live)?
var count = await Media.Query()
    .Where(m => m.ImportJobId == jobId && m.VectorizedAt != null)
    .CountAsync(ct);
```

### Signature-Based Smart Caching

```csharp
// Compute signature from content (same as current)
var text = BuildEmbeddingText(media); // Titles + Synopsis + Genres + Tags
using var sha256 = SHA256.Create();
var signature = Convert.ToHexString(
    sha256.ComputeHash(Encoding.UTF8.GetBytes(text))
).ToLowerInvariant();

// Check Entity-based cache
var cacheId = EmbeddingCacheEntry.MakeCacheId(signature, modelId, "Media");
var cached = await EmbeddingCacheEntry.GetAsync(cacheId, ct);

if (cached != null)
{
    // CACHE HIT: Reuse embedding
    embedding = cached.Embedding;
    cached.RecordAccess(); // Track usage
    await cached.SaveAsync(ct);
}
else
{
    // CACHE MISS: Generate and store
    embedding = await Ai.EmbedAsync(text, ct);
    var entry = new EmbeddingCacheEntry
    {
        ContentSignature = signature,
        ModelId = modelId,
        EntityType = "Media",
        Embedding = embedding,
        Dimension = embedding.Length
    };
    await entry.SaveAsync(ct);
}
```

**Benefits**:
- Same content = same signature → cache reuse
- Content changed → new signature → regeneration
- Provider-agnostic (AniList "123" and MAL "456" with same content share embedding)
- Model-specific (different models have separate cache entries)

### Baseline NSFW Marking

Hardcoded MD5 hashes remain as preemptive baseline:
```csharp
public static class PreemptiveTagFilter
{
    private static readonly HashSet<string> _preemptiveNsfwHashes = new() { /* ... */ };

    /// <summary>
    /// Check if tag matches baseline NSFW hash set.
    /// Matched tags are marked as NSFW, not blocked.
    /// Provides default safety on fresh installations.
    /// </summary>
    public static bool IsBaselineNsfw(string tag)
    {
        var normalized = tag.Trim().ToLowerInvariant();
        using var md5 = MD5.Create();
        var hash = Convert.ToHexString(
            md5.ComputeHash(Encoding.UTF8.GetBytes(normalized))
        ).ToLowerInvariant();
        return _preemptiveNsfwHashes.Contains(hash);
    }
}
```

Admin can add additional censored tags via `CensorTagsDoc`, but baseline hashes remain as safety net.

## Options Considered

| Option | Outcome | Evaluation |
|--------|---------|------------|
| Keep monolithic SeedService | Minimal disruption | **Rejected.** Brittleness persists: global lock, silent failures, no parallelization, not scalable. |
| Separate VectorizationTask entity | Queue via separate entities | **Rejected.** Adds complexity. Partition membership IS the queue state. |
| File-based embedding cache | Current implementation | **Rejected.** Not horizontally scalable, no queryability, separate cache per instance. |
| Redis embedding cache | Distributed in-memory cache | **Rejected.** New dependency. Koan Entity<T> in MongoDB provides persistence + queryability. |
| **Partition-based pipeline with Entity<T> cache** | Koan-native patterns throughout | **Accepted.** Uses framework features (partitions, Copy/Move), horizontally scalable, 100% Entity<T> patterns. |

## Implementation Guidelines

### Phase 1: Extend Entities (Non-Breaking)

**Duration**: 2-3 days

1. **Extend Media entity**:
   ```csharp
   // Models/Media.cs
   public string? ImportJobId { get; set; }
   public string? ContentSignature { get; set; }
   public DateTime? ImportedAt { get; set; }
   public DateTime? ValidatedAt { get; set; }
   public DateTime? VectorizedAt { get; set; }
   public string? ProcessingError { get; set; }
   public int RetryCount { get; set; }
   ```

2. **Create EmbeddingCacheEntry**:
   ```csharp
   // Models/EmbeddingCacheEntry.cs
   public class EmbeddingCacheEntry : Entity<EmbeddingCacheEntry, string>
   {
       public override string Id { get; set; } = "";
       public string ContentSignature { get; set; } = "";
       public string ModelId { get; set; } = "";
       public string EntityType { get; set; } = "";
       public float[] Embedding { get; set; } = Array.Empty<float>();
       public int Dimension { get; set; }
       public DateTime CachedAt { get; set; } = DateTime.UtcNow;
       public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
       public int AccessCount { get; set; } = 0;

       public static string MakeCacheId(string sig, string model, string type)
           => $"{sig}:{model}:{type}";

       public void RecordAccess()
       {
           LastAccessedAt = DateTime.UtcNow;
           AccessCount++;
       }
   }
   ```

3. **Create ImportJob entity** (simplified):
   ```csharp
   // Models/ImportJob.cs
   public class ImportJob : Entity<ImportJob>
   {
       public string JobId { get; set; } = Guid.NewGuid().ToString();
       public string Source { get; set; } = "";
       public string MediaTypeId { get; set; } = "";
       public string MediaTypeName { get; set; } = "";
       public ImportJobStatus Status { get; set; } = ImportJobStatus.Running;
       public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
       public DateTime? StartedAt { get; set; }
       public DateTime? CompletedAt { get; set; }
       public int? Limit { get; set; }
       public bool Overwrite { get; set; }
       public List<string> Errors { get; set; } = new();
   }

   public enum ImportJobStatus { Running, Completed, Failed }
   ```

**Validation**: Build succeeds, no behavioral changes yet.

### Phase 2: Implement Workers

**Duration**: 1-2 weeks

1. **ImportWorker** (`Services/Workers/ImportWorker.cs`):
   - BackgroundService polling `ImportJob` in "jobs-active" partition
   - Streams from `IMediaProvider`
   - Computes `ContentSignature` (SHA256)
   - Writes to "import-raw" partition
   - Moves job to default partition on completion

2. **ValidationWorker** (`Services/Workers/ValidationWorker.cs`):
   - Polls "import-raw" partition
   - Validates required fields (Title, ContentSignature)
   - Moves valid media to "vectorization-queue" partition
   - Removes invalid media

3. **VectorizationWorker** (`Services/Workers/VectorizationWorker.cs`):
   - Polls "vectorization-queue" partition (batch of 10)
   - Batch fetches `EmbeddingCacheEntry` by signatures
   - Cache hit → reuse embedding
   - Cache miss → generate via `Ai.EmbedAsync()`, store in cache
   - Saves vector to Weaviate via `VectorData<Media>.SaveWithVectorAsync()`
   - Moves media to default partition
   - Retries up to 3 times on failure

4. **CatalogWorker** (`Services/Workers/CatalogWorker.cs`):
   - Polls default partition for `VectorizedAt > lastRun`
   - Extracts tags/genres
   - Applies `PreemptiveTagFilter.IsBaselineNsfw()` marking
   - Updates `TagStatDoc`, `GenreStatDoc`
   - Runs every 1-5 minutes

5. **Register in Program.cs**:
   ```csharp
   builder.Services.AddHostedService<ImportWorker>();
   builder.Services.AddHostedService<ValidationWorker>();
   builder.Services.AddHostedService<VectorizationWorker>();
   builder.Services.AddHostedService<CatalogWorker>();
   ```

**Validation**: Workers run in background, can be tested independently with staged data.

### Phase 3: New Orchestrator & API

**Duration**: 3-5 days

1. **ImportOrchestrator** (`Services/ImportOrchestrator.cs`):
   ```csharp
   public interface IImportOrchestrator
   {
       Task<List<string>> QueueImportAsync(
           string source,
           string[] mediaTypeIds,
           ImportOptions options,
           CancellationToken ct);

       Task<ImportProgressResponse> GetProgressAsync(
           string[] jobIds,
           CancellationToken ct);
   }
   ```

2. **Versioned endpoint** (`Controllers/AdminController.cs`):
   ```csharp
   [HttpPost("admin/seed/v2/start")]
   public async Task<ActionResult<ImportStartResponse>> StartImportV2(
       [FromBody] ImportRequest request,
       CancellationToken ct)
   {
       var mediaTypeIds = request.MediaType == "all"
           ? await ResolveAllMediaTypesAsync(ct)
           : new[] { request.MediaType };

       var jobIds = await _orchestrator.QueueImportAsync(
           request.Source,
           mediaTypeIds,
           new ImportOptions { Limit = request.Limit, Overwrite = request.Overwrite },
           ct);

       return Ok(new { JobIds = jobIds, Message = "Import jobs queued" });
   }

   [HttpGet("admin/seed/v2/status")]
   public async Task<ActionResult<ImportStatusResponse>> GetStatusV2(
       [FromQuery] string[] jobIds,
       CancellationToken ct)
   {
       var progress = await _orchestrator.GetProgressAsync(jobIds, ct);
       return Ok(progress);
   }
   ```

3. **Progress calculation**:
   ```csharp
   // Derive from partition queries
   var raw = await Media.Query()
       .Where(m => m.ImportJobId == jobId)
       .WithPartition("import-raw")
       .CountAsync(ct);

   var queued = await Media.Query()
       .Where(m => m.ImportJobId == jobId)
       .WithPartition("vectorization-queue")
       .CountAsync(ct);

   var completed = await Media.Query()
       .Where(m => m.ImportJobId == jobId && m.VectorizedAt != null)
       .CountAsync(ct);

   return new ProgressDto
   {
       InRaw = raw,
       InQueue = queued,
       Completed = completed,
       PercentComplete = completed / (raw + queued + completed) * 100.0
   };
   ```

**Validation**: Can queue imports via v2 endpoint, monitor progress in real-time.

### Phase 4: Testing & Cutover

**Duration**: 1 week

1. **Parallel Testing**:
   - Keep old `/admin/seed/start` endpoint using `SeedService`
   - Run v2 endpoint in production-like staging
   - Compare results (media counts, vectorization success rates, performance)

2. **Performance Benchmarks**:
   - Measure import speed (items/min)
   - Measure vectorization throughput
   - Monitor cache hit rates
   - Verify concurrent imports work (Anime + Manga simultaneously)

3. **Cutover**:
   ```csharp
   [HttpPost("admin/seed/start")]
   public async Task<ActionResult> StartImport(...)
   {
       // Delegate to v2 implementation
       return await StartImportV2(...);
   }
   ```

4. **Monitor for 1 week**: Ensure no regressions, workers stable, cache growing appropriately.

**Validation**: New system is primary, old endpoint removed or marked obsolete.

### Phase 5: Deprecation & Cleanup

**Duration**: 2-3 days

1. **Mark SeedService as obsolete**:
   ```csharp
   [Obsolete("Use IImportOrchestrator with partition-based workers instead. Will be removed in next major version.")]
   public class SeedService { ... }
   ```

2. **Remove after grace period** (1-2 releases):
   - Delete `Services/SeedService.cs`
   - Remove `ISeedService` interface
   - Remove old file-based `IEmbeddingCache` implementation
   - Remove `IRawCacheService` (if no longer used elsewhere)
   - Clean up old endpoints

3. **Update documentation**:
   - Architecture diagrams showing partition flow
   - Worker responsibilities
   - Monitoring/observability guide

**Validation**: Build succeeds, no references to deprecated code remain.

## Deprecated Structures

The following will be **removed** in this greenfield rebuild:

### Services (Complete Removal)

- **`SeedService`** (858 lines, `Services/SeedService.cs`):
  - **Replaced by**: `ImportWorker`, `ValidationWorker`, `VectorizationWorker`, `CatalogWorker`
  - **Reason**: Monolithic, global lock, service locator anti-pattern

- **`ISeedService`** (interface):
  - **Replaced by**: `IImportOrchestrator`
  - **Reason**: New orchestration API with job-based progress tracking

### Infrastructure (Complete Removal)

- **File-based `IEmbeddingCache`** implementation:
  - **Location**: `Services/EmbeddingCache.cs` (if exists)
  - **File pattern**: `cache/embeddings/{entityType}/{modelId}/{hash}.json`
  - **Replaced by**: `EmbeddingCacheEntry : Entity<EmbeddingCacheEntry>`
  - **Reason**: Not horizontally scalable, no queryability

- **`IRawCacheService`** (if solely used by SeedService):
  - **Location**: `Services/RawCacheService.cs`
  - **File pattern**: `/app/cache/import-raw/{source}/{mediaType}/{jobId}/`
  - **Replaced by**: Direct storage to "import-raw" partition
  - **Reason**: Partitions provide better staging with queryability

### Endpoints (Deprecated, then Removed)

- **`POST /admin/seed/start`** (old implementation):
  - **Replaced by**: `POST /admin/seed/v2/start`
  - **Migration**: Old endpoint will delegate to v2, then be removed
  - **Reason**: New job-based orchestration model

- **`GET /admin/seed/progress`** (if exists):
  - **Replaced by**: `GET /admin/seed/v2/status`
  - **Reason**: New progress tracking via partition queries

### Internal Patterns (Refactored)

- **Global import lock**:
  ```csharp
  private static volatile bool _importInProgress = false;
  ```
  - **Replaced by**: Independent workers processing partitions
  - **Reason**: Prevents concurrent imports, single point of failure

- **In-memory progress dictionary**:
  ```csharp
  private readonly Dictionary<string, (...)> _progress = new();
  ```
  - **Replaced by**: `ImportJob` entity with queries over Media partitions
  - **Reason**: Lost on restart, not queryable

- **Service locator pattern**:
  ```csharp
  private readonly IServiceProvider _sp;
  var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
  ```
  - **Replaced by**: Direct use of `Entity<T>` static methods
  - **Reason**: Hidden dependencies, runtime failures

- **Synchronous batch processing**:
  ```csharp
  foreach (var batch in batches)
  {
      await ImportDataAsync(batch);
      await EmbedAndIndexAsync(batch); // BLOCKING
  }
  ```
  - **Replaced by**: Async pipeline via partitions
  - **Reason**: Import blocked by vectorization

### Migration Notes

**No backward compatibility required** - this is a greenfield rebuild:
- File-based embedding cache: Can be pre-migrated to `EmbeddingCacheEntry` via one-time script if desired
- Raw import cache: Can be discarded (provider re-fetch is acceptable)
- In-progress imports: Will be lost during cutover (acceptable for sample app)

**Data migration** (optional):
```csharp
// One-time migration from file cache to Entity cache
var cacheDir = "cache/embeddings/Media/nomic-embed-text";
foreach (var file in Directory.GetFiles(cacheDir, "*.json"))
{
    var cached = JsonSerializer.Deserialize<CachedEmbedding>(
        File.ReadAllText(file));

    var entry = new EmbeddingCacheEntry
    {
        ContentSignature = cached.ContentHash,
        ModelId = "nomic-embed-text",
        EntityType = "Media",
        Embedding = cached.Embedding,
        Dimension = cached.Dimension,
        CachedAt = cached.CachedAt
    };

    await entry.SaveAsync(ct);
}
```

## Consequences

### Positive

- **Independent Operations**: Import, vectorization, and cataloging fully decoupled
- **Parallel Processing**: Multiple imports run concurrently, vectorization workers scale horizontally
- **Persistent State**: All state in MongoDB, survives restarts, queryable with LINQ
- **Visible Failures**: Errors tracked in entity properties, automatic retry logic
- **Signature-Based Intelligence**: SHA256 content hashing enables smart cache reuse
- **Horizontal Scalability**: Multiple worker instances can process same partitions
- **100% Koan Patterns**: Partitions, Entity<T>, Copy/Move - framework-native throughout
- **Simplified Entities**: No separate task entities, partition membership IS the queue state
- **Cache Efficiency**: Access tracking enables future LRU cleanup, batch fetch optimization

### Tradeoffs

- **Complexity Distribution**: Single 858-line class → 4 focused workers (~200 lines each)
- **Eventually Consistent**: Import completes before vectorization (mitigated by job tracking)
- **Partition Management**: Need to monitor/clean staging partitions (mitigated by automatic moves)
- **Learning Curve**: Developers must understand partition-based state transitions
- **Migration Effort**: Complete rewrite (acceptable for sample app, minimal external usage)

### Neutral

- **API Changes**: New versioned endpoints, old endpoints deprecated then removed
- **Configuration**: Minimal changes, workers auto-register via `AddHostedService`
- **Performance**: Import 4x faster (non-blocking), vectorization 10x+ (parallel workers)

## Follow-ups

1. **Monitoring & Observability**:
   - Add health checks for worker heartbeats
   - Metrics: partition sizes, cache hit rates, throughput
   - Alerts: stuck items in partitions, worker failures

2. **Admin UI Enhancements**:
   - Real-time job progress dashboard
   - Partition inspection (see items stuck in each stage)
   - Manual retry/skip controls for failed items

3. **Cache Optimization**:
   - LRU cleanup job (remove entries not accessed in 90 days)
   - Cache warming (pre-fetch embeddings for common media)
   - Batch embedding generation (10+ uncached items at once)

4. **Advanced Features**:
   - Priority queue (user-requested imports first)
   - Incremental imports (detect content changes via signature)
   - Provider fallback (try MAL if AniList fails)

5. **Documentation**:
   - Architecture diagrams (partition flow, worker responsibilities)
   - Monitoring runbook (common issues, resolution steps)
   - Performance tuning guide (worker count, batch sizes)

## References

- [ARCH-0067 – Service Mesh Organizational Structure](./ARCH-0067-service-organizational-structure.md) (Similar large-scale refactor)
- [ARCH-0068 – Refactoring Strategy: Static vs DI](./ARCH-0068-refactoring-strategy-static-vs-di.md) (Entity<T> static methods vs services)
- [Entity Capabilities How-To Guide](../guides/entity-capabilities-howto.md) (Partition patterns, Copy/Move, streaming)
- S5.Recs Current Architecture Analysis: `samples/S5.Recs/Services/SeedService.cs` (858 lines)
- Implementation PR: [To be created]
