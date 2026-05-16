# ADR-0053: Vector Search Native Continuation with Adapter Push-Down

**Status:** Accepted
**Date:** 2025-11-09
**Context:** KoanContext semantic search pagination optimization
**Decision Makers:** Architecture review
**Affected Components:** Koan.Data.Vector, Vector Adapters (Weaviate, Qdrant, Chroma, Pinecone), Koan.Service.KoanContext

---

## Context and Problem Statement

The KoanContext service implements semantic search with pagination via continuation tokens. The current implementation re-executes the entire search pipeline on every page request:

**Current Flow (All Pages):**
```
Page 1: Embedding (150ms) + Vector Search (50ms) + Hydration (50ms) = 250ms
Page 2: Embedding (150ms) + Vector Search (50ms) + Hydration (50ms) = 250ms  ← Wasteful
Page 3: Embedding (150ms) + Vector Search (50ms) + Hydration (50ms) = 250ms  ← Wasteful
```

**Problems:**
1. **Redundant embedding generation** - Same query text → same embedding vector (150ms wasted)
2. **Redundant vector search** - Same vector → same result set (50ms wasted)
3. **Redundant hydration** - Re-fetching chunks already seen (partial waste)
4. **Missed provider optimization** - Vector databases like Weaviate and Qdrant have native pagination APIs (cursor-based, offset-based) that are ignored
5. **Orchestration complexity** - Framework implements caching/pagination logic that providers already support

**User Experience Impact:**
- Multi-page searches feel slow (200ms+ per page)
- Unnecessary load on vector database and embedding service
- Scalability concerns with high concurrency (redundant operations × users)

**Architectural Tension:**
- **YAGNI/KISS:** Don't replicate what providers already do well
- **Provider Abstraction:** Framework must remain ignorant of specific implementations (no "Weaviate" knowledge)
- **Capability Detection:** Use informed capabilities, not implementation hints
- **SOC:** Pagination is a vector database concern, not orchestration layer concern

---

## Decision Drivers

1. **Performance** - Eliminate redundant operations (embedding, vector search)
2. **Push-Down Principle** - Maximize delegation to providers, minimize orchestration
3. **Provider Abstraction** - Framework APIs must be implementation-agnostic
4. **Capability-Based** - Use VectorCapabilities for feature detection
5. **Zero Breaking Changes** - Existing code continues working unchanged
6. **Great DX** - Developer calls `Search()` - optimal path chosen automatically
7. **Progressive Enhancement** - Providers add native continuation when ready

---

## Considered Options

### Option 1: Orchestration-Layer Result Caching

**Approach:** Cache result IDs in `Search.cs` after first query, paginate in-memory.

```csharp
// Search.cs maintains cache
private Dictionary<string, List<string>> _resultCache;

public async Task<SearchResult> SearchAsync(...)
{
    var cacheKey = $"{projectId}:{query}:{alpha}";

    if (!_resultCache.ContainsKey(cacheKey))
    {
        // First page: Full pipeline
        var results = await Vector<Chunk>.Search(...);
        _resultCache[cacheKey] = results.Select(r => r.Id).ToList();
    }

    // All pages: Paginate cached IDs
    var pageIds = _resultCache[cacheKey].Skip(offset).Take(limit);
    var chunks = await Chunk.Get(pageIds, ct);
}
```

**Pros:**
- ✅ Simple implementation (~50 LOC)
- ✅ Works with all providers (no provider changes needed)
- ✅ 95% latency reduction for pages 2+ (250ms → 10ms)

**Cons:**
- ❌ Violates SOC - Orchestration layer doing provider's job
- ❌ Memory overhead - Search.cs manages cache state
- ❌ Ignores native provider capabilities (Weaviate cursors, Qdrant offsets)
- ❌ Doesn't scale to multi-instance deployments (cache not shared)

**Verdict:** Simple but architecturally inferior. Replicates what providers do natively.

---

### Option 2: Embedding Cache Only

**Approach:** Cache embedding vectors, still re-query vector database.

```csharp
// Cache embeddings, not results
var cachedEmbedding = await _embeddingCache.GetOrCreateAsync(query,
    () => _embedding.EmbedAsync(query, ct));

var results = await Vector<Chunk>.Search(
    vector: cachedEmbedding,  // Cached (150ms saved)
    text: query,
    alpha: alpha,
    topK: 100,
    ct: ct);
```

**Pros:**
- ✅ Simple, focused optimization
- ✅ Embedding cache is universally beneficial (used for all searches)
- ✅ No pagination logic needed

**Cons:**
- ❌ Only saves 150ms of 250ms (60% improvement, not 95%)
- ❌ Still re-queries vector database unnecessarily
- ❌ Doesn't leverage provider-native pagination

**Verdict:** Good complementary optimization, but incomplete solution.

---

### Option 3: Provider-Specific Continuation API ⭐ **SELECTED**

**Approach:** Extend `IVectorSearchRepository` with optional continuation parameter. Providers return opaque continuation hints. Framework passes them through without inspection.

```csharp
public interface IVectorSearchRepository<TEntity, TKey>
{
    Task<VectorSearchResult> SearchAsync(
        float[] vector,
        string? text,
        float alpha,
        int topK,
        string? continuationHint = null,  // Opaque to framework
        CancellationToken ct = default);
}

public record VectorSearchResult(
    IReadOnlyList<VectorMatch> Matches,
    string? ContinuationHint);  // Provider-specific token
```

**Provider Implementation Strategies:**

**Weaviate (native cursor):**
```csharp
var result = await _client.Query
    .WithAfter(continuationHint)  // Weaviate cursor
    .ExecuteAsync(ct);
return new VectorSearchResult(matches, result.After);
```

**Qdrant (offset-based):**
```csharp
var offset = int.Parse(continuationHint ?? "0");
var result = await _client.SearchAsync(offset: offset, ...);
return new VectorSearchResult(matches, (offset + topK).ToString());
```

**ChromaDB (no native pagination):**
```csharp
// Fetch topK, return null hint (framework handles continuation)
var result = await _client.QueryAsync(...);
return new VectorSearchResult(matches, ContinuationHint: null);
```

**Pros:**
- ✅ **Push-down principle** - Providers own pagination strategy
- ✅ **YAGNI** - Framework doesn't replicate provider capabilities
- ✅ **KISS** - Opaque string token, no complex abstraction
- ✅ **SOC** - Pagination is vector database concern
- ✅ **Zero breaking changes** - `continuationHint: null` = current behavior
- ✅ **Progressive enhancement** - Providers add support when ready
- ✅ **Framework abstraction** - Search.cs never knows "Weaviate" exists
- ✅ **Great DX** - Optimal path chosen automatically

**Cons:**
- ⚠️ Requires provider implementation (gradual rollout)
- ⚠️ Providers without native pagination fall back to current behavior

**Verdict:** Architecturally superior. Aligns with Koan's push-down and capability-based principles.

---

## Decision

**Adopt Option 3: Provider-Specific Continuation API with Adapter Push-Down**

### Core Design Principles

1. **Opaque Continuation Hints** - Framework never inspects, parses, or interprets provider tokens
2. **Capability Detection** - `VectorCapabilities.NativeContinuation` flag for feature detection
3. **Graceful Degradation** - Providers without native support return `null`, framework handles fallback
4. **Progressive Enhancement** - Providers implement native continuation independently

### Three-Tier Efficiency Model

**Tier 1: Provider with Native Continuation** (Weaviate, Qdrant)
- Page 1: Embedding (150ms) + Vector Search (50ms) + Hydration (50ms) = 250ms
- Page 2: Embedding (cached 0ms) + Vector Search (10ms native) + Hydration (50ms) = 60ms
- **76% reduction**

**Tier 2: Provider without Native Continuation** (ChromaDB, Pinecone)
- Page 1: Embedding (150ms) + Vector Search (50ms) + Hydration (50ms) = 250ms
- Page 2: Embedding (cached 0ms) + Vector Search (50ms) + Hydration (50ms) = 100ms
- **60% reduction** (embedding cache only)

**Tier 3: Multi-Project Aggregation**
- Always fetches results upfront (orchestration-layer pagination)
- No provider hints used (aggregation happens above provider layer)

---

## Implementation Details

### Phase 1: Framework Core Extensions

#### 1.1 VectorCapabilities Enum

**File:** `src/Koan.Data.Vector.Abstractions/VectorCapabilities.cs`

```csharp
[Flags]
public enum VectorCapabilities
{
    None = 0,
    Knn = 1 << 0,
    Filters = 1 << 1,
    Hybrid = 1 << 2,
    MetadataFiltering = 1 << 3,
    SparseVectors = 1 << 4,
    MultiVector = 1 << 5,
    NativeContinuation = 1 << 6  // NEW: Provider supports efficient pagination
}
```

#### 1.2 IVectorSearchRepository Extension

**File:** `src/Koan.Data.Vector.Abstractions/IVectorSearchRepository.cs`

```csharp
public interface IVectorSearchRepository<TEntity, TKey>
    where TEntity : Entity<TEntity, TKey>
    where TKey : IComparable<TKey>, IEquatable<TKey>
{
    /// <summary>
    /// Searches for entities similar to the provided vector.
    /// </summary>
    /// <param name="vector">Query embedding vector</param>
    /// <param name="text">Optional text for hybrid search (BM25 + vector)</param>
    /// <param name="alpha">Semantic vs keyword weight (0.0=keyword, 1.0=semantic)</param>
    /// <param name="topK">Maximum results to return</param>
    /// <param name="continuationHint">
    /// Opaque provider-specific continuation token for pagination.
    /// Framework passes this through without inspection.
    /// Providers return this via VectorSearchResult.ContinuationHint.
    /// Null on first page or if provider doesn't support NativeContinuation.
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Search results with optional continuation hint</returns>
    Task<VectorSearchResult> SearchAsync(
        float[] vector,
        string? text = null,
        float alpha = 0.7f,
        int topK = 10,
        string? continuationHint = null,  // NEW PARAMETER
        CancellationToken ct = default);
}
```

#### 1.3 VectorSearchResult Extension

**File:** `src/Koan.Data.Vector.Abstractions/VectorSearchResult.cs`

```csharp
/// <summary>
/// Result of a vector search operation with optional continuation support
/// </summary>
public record VectorSearchResult
{
    /// <summary>
    /// Matching entities with similarity scores
    /// </summary>
    public IReadOnlyList<VectorMatch> Matches { get; init; } = Array.Empty<VectorMatch>();

    /// <summary>
    /// Opaque provider-specific continuation token for pagination.
    /// - Weaviate: Cursor token (e.g., "eyJpZCI6IjEyMyJ9")
    /// - Qdrant: Offset as string (e.g., "100")
    /// - ChromaDB/Pinecone: null (no native continuation)
    /// Framework never parses or interprets this value.
    /// </summary>
    public string? ContinuationHint { get; init; }
}
```

### Phase 2: Provider Implementations

#### 2.1 Weaviate Adapter (Native Cursor Support)

**File:** `src/Koan.Data.Vector.Connector.Weaviate/WeaviateVectorRepository.cs`

```csharp
public class WeaviateVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
{
    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters |
        VectorCapabilities.Hybrid |
        VectorCapabilities.NativeContinuation;  // NEW

    public async Task<VectorSearchResult> SearchAsync(
        float[] vector,
        string? text,
        float alpha,
        int topK,
        string? continuationHint,  // NEW
        CancellationToken ct)
    {
        var nearVector = new { vector };

        // Build GraphQL query
        var query = _client.Query
            .Get(_collectionName)
            .WithNearVector(nearVector)
            .WithLimit(topK);

        // Apply continuation cursor if provided
        if (!string.IsNullOrWhiteSpace(continuationHint))
        {
            query = query.WithAfter(continuationHint);  // Weaviate native cursor
            _logger.LogDebug("Resuming Weaviate search with cursor: {Cursor}", continuationHint);
        }

        // Execute query
        var result = await query.ExecuteAsync(ct);

        // Map results
        var matches = result.Data
            .Select(item => new VectorMatch(
                Id: item.Id,
                Score: item.Distance,
                Metadata: item.Properties))
            .ToList();

        // Extract next cursor from Weaviate response
        var nextCursor = result.After;  // Weaviate returns cursor for next page

        _logger.LogInformation(
            "Weaviate search returned {Count} matches, nextCursor: {HasCursor}",
            matches.Count,
            nextCursor != null ? "present" : "null");

        return new VectorSearchResult
        {
            Matches = matches,
            ContinuationHint = nextCursor  // Pass cursor back to framework
        };
    }
}
```

**Weaviate GraphQL Extension:**
```csharp
// WeaviateQueryBuilder.cs
public WeaviateQueryBuilder WithAfter(string cursor)
{
    _graphQLQuery += $", after: \"{cursor}\"";
    return this;
}
```

#### 2.2 Qdrant Adapter (Offset-Based Pagination)

**File:** `src/Koan.Data.Vector.Connector.Qdrant/QdrantVectorRepository.cs`

```csharp
public class QdrantVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
{
    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters |
        VectorCapabilities.NativeContinuation;  // NEW

    public async Task<VectorSearchResult> SearchAsync(
        float[] vector,
        string? text,
        float alpha,
        int topK,
        string? continuationHint,  // NEW
        CancellationToken ct)
    {
        // Parse offset from continuation hint (default to 0)
        var offset = 0;
        if (!string.IsNullOrWhiteSpace(continuationHint))
        {
            if (!int.TryParse(continuationHint, out offset))
            {
                _logger.LogWarning("Invalid Qdrant continuation hint: {Hint}", continuationHint);
                offset = 0;
            }
        }

        _logger.LogDebug("Qdrant search: offset={Offset}, limit={Limit}", offset, topK);

        // Execute Qdrant search with offset
        var result = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: vector,
            limit: (ulong)topK,
            offset: (ulong)offset,  // Qdrant native offset
            ct: ct);

        var matches = result
            .Select(item => new VectorMatch(
                Id: item.Id.ToString(),
                Score: item.Score,
                Metadata: item.Payload))
            .ToList();

        // Generate next offset (heuristic: full page = more results likely)
        var hasMore = matches.Count == topK;
        var nextOffset = hasMore ? offset + topK : (int?)null;

        _logger.LogInformation(
            "Qdrant search returned {Count} matches, nextOffset: {NextOffset}",
            matches.Count,
            nextOffset?.ToString() ?? "null");

        return new VectorSearchResult
        {
            Matches = matches,
            ContinuationHint = nextOffset?.ToString()  // Return offset as string
        };
    }
}
```

#### 2.3 ChromaDB Adapter (No Native Continuation)

**File:** `src/Koan.Data.Vector.Connector.Chroma/ChromaVectorRepository.cs`

```csharp
public class ChromaVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>
{
    public VectorCapabilities Capabilities =>
        VectorCapabilities.Knn |
        VectorCapabilities.Filters;
        // NOT NativeContinuation - ChromaDB doesn't support pagination

    public async Task<VectorSearchResult> SearchAsync(
        float[] vector,
        string? text,
        float alpha,
        int topK,
        string? continuationHint,  // Ignored - ChromaDB has no pagination API
        CancellationToken ct)
    {
        // ChromaDB always fetches topK, no continuation support
        // Framework will handle pagination via fallback mechanism

        var result = await _client.QueryAsync(
            collectionName: _collectionName,
            queryEmbeddings: new[] { vector },
            nResults: topK,
            ct: ct);

        var matches = result.Documents[0]
            .Select((doc, idx) => new VectorMatch(
                Id: result.Ids[0][idx],
                Score: result.Distances[0][idx],
                Metadata: result.Metadatas?[0][idx]))
            .ToList();

        _logger.LogInformation("ChromaDB search returned {Count} matches (no continuation)", matches.Count);

        return new VectorSearchResult
        {
            Matches = matches,
            ContinuationHint = null  // No continuation support
        };
    }
}
```

### Phase 3: Framework Continuation Token

#### 3.1 Framework Token Structure

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Services/Pagination.cs`

```csharp
/// <summary>
/// Framework continuation token for semantic search pagination
/// </summary>
public record ContinuationTokenData(
    string ProjectId,
    string Query,
    float Alpha,
    DateTime CreatedAt,
    int Page,

    // NEW: Provider continuation support
    string? ProviderHint,  // Opaque provider-specific token (Weaviate cursor, Qdrant offset, etc.)

    // Legacy: Framework-level fallback for providers without NativeContinuation
    string? LastChunkId,   // Last chunk ID returned (for skip-based pagination)
    int TokensRemaining,

    // Multi-project support
    List<string>? ProjectIds = null,
    int ChunkOffset = 0
);
```

**Key Design Points:**
- `ProviderHint` is **completely opaque** - framework never inspects it
- Framework stores whatever string the provider returns
- On next page, framework passes it back unchanged
- `LastChunkId` retained for backward compatibility (providers without NativeContinuation)

#### 3.2 Search.cs Orchestration

**File:** `src/Services/code-intelligence/Koan.Service.KoanContext/Services/Search.cs`

```csharp
public class Search
{
    private readonly Embedding _embedding;
    private readonly TokenCounter _tokenCounter;
    private readonly Pagination _pagination;
    private readonly IMemoryCache _embeddingCache;  // NEW: Embedding cache
    private readonly ILogger<Search> _logger;

    public async Task<SearchResult> SearchAsync(
        string projectId,
        string query,
        SearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedOptions = NormalizeOptions(options ?? new SearchOptions());

        // Parse framework continuation token
        ContinuationTokenData? continuationData = null;
        if (!string.IsNullOrWhiteSpace(normalizedOptions.ContinuationToken))
        {
            continuationData = _pagination.ParseToken(normalizedOptions.ContinuationToken);

            if (continuationData == null)
            {
                _logger.LogWarning("Invalid continuation token");
            }
            else if (continuationData.ProjectId != projectId || continuationData.Query != query)
            {
                _logger.LogWarning("Continuation token mismatch");
                continuationData = null;
            }
        }

        using (EntityContext.Partition(projectId))
        {
            // Get or create embedding (cached across all pages)
            var cacheKey = $"embedding:{query}";
            var queryEmbedding = await _embeddingCache.GetOrCreateAsync(
                cacheKey,
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return await _embedding.EmbedAsync(query, cancellationToken);
                });

            _logger.LogInformation(
                "Embedding: {Source} (dimension={Dim})",
                _embeddingCache.TryGetValue(cacheKey, out _) ? "cache hit" : "generated",
                queryEmbedding.Length);

            // Call vector search with provider hint (opaque pass-through)
            var vectorResult = await Vector<Chunk>.Search(
                vector: queryEmbedding,
                text: query,
                alpha: normalizedOptions.Alpha,
                topK: MaxTopK,
                continuationHint: continuationData?.ProviderHint,  // Opaque to framework
                ct: cancellationToken);

            _logger.LogInformation(
                "Vector search returned {Count} matches, providerHint: {HasHint}",
                vectorResult.Matches.Count,
                vectorResult.ContinuationHint != null ? "present" : "null");

            // Hydrate chunks (apply token budget, language filters, etc.)
            var chunks = await HydrateChunks(
                vectorResult.Matches,
                normalizedOptions,
                continuationData?.LastChunkId,  // Fallback for providers without NativeContinuation
                cancellationToken);

            // Generate continuation token
            string? nextToken = null;
            if (chunks.Any() &&
                (vectorResult.ContinuationHint != null || chunks.Count == normalizedOptions.MaxTokens))
            {
                var tokenData = new ContinuationTokenData(
                    ProjectId: projectId,
                    Query: query,
                    Alpha: normalizedOptions.Alpha,
                    CreatedAt: DateTime.UtcNow,
                    Page: (continuationData?.Page ?? 0) + 1,
                    ProviderHint: vectorResult.ContinuationHint,  // Store provider hint opaquely
                    LastChunkId: chunks.Last().Id,  // Fallback for providers without NativeContinuation
                    TokensRemaining: normalizedOptions.MaxTokens
                );

                nextToken = _pagination.CreateToken(tokenData);
            }

            return new SearchResult(
                Chunks: chunks,
                Metadata: BuildMetadata(...),
                Sources: BuildSources(...),
                Insights: BuildInsights(...),
                ContinuationToken: nextToken,
                Warnings: warnings
            );
        }
    }

    /// <summary>
    /// Hydrate chunks from vector matches, applying budget and filters
    /// </summary>
    private async Task<List<SearchResultChunk>> HydrateChunks(
        IReadOnlyList<VectorMatch> matches,
        SearchOptions options,
        string? lastChunkId,  // For providers without NativeContinuation
        CancellationToken ct)
    {
        var chunks = new List<SearchResultChunk>();
        var tokensReturned = 0;
        var shouldSkip = !string.IsNullOrWhiteSpace(lastChunkId);

        foreach (var match in matches)
        {
            var chunk = await Chunk.Get(match.Id, ct);
            if (chunk == null) continue;

            // Skip until we find continuation point (fallback for providers without NativeContinuation)
            if (shouldSkip)
            {
                if (chunk.Id == lastChunkId)
                {
                    shouldSkip = false;
                }
                continue;
            }

            // Apply language filter
            if (options.Languages != null &&
                options.Languages.Any() &&
                !string.IsNullOrWhiteSpace(chunk.Language))
            {
                if (!options.Languages.Contains(chunk.Language, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Apply token budget
            var chunkTokens = chunk.TokenCount > 0
                ? chunk.TokenCount
                : _tokenCounter.EstimateTokens(chunk.SearchText);

            if (tokensReturned + chunkTokens > options.MaxTokens && chunks.Any())
            {
                break;
            }

            tokensReturned += chunkTokens;

            chunks.Add(new SearchResultChunk(
                Id: chunk.Id,
                Text: chunk.SearchText,
                Score: (float)match.Score,
                Provenance: BuildProvenance(chunk),
                Reasoning: options.IncludeReasoning ? BuildReasoning(match.Score, options.Alpha) : null
            ));
        }

        return chunks;
    }
}
```

**Key Design Points:**
- Embedding cache is **orthogonal** to provider continuation (always beneficial)
- `ProviderHint` passed through opaquely (framework never inspects it)
- `LastChunkId` retained as fallback for providers without `NativeContinuation`
- Token budget and filters applied during hydration (not in provider)

### Phase 4: Capability-Based Routing

**File:** `src/Koan.Data.Vector/VectorData.cs`

```csharp
public static class VectorData<TEntity, TKey>
    where TEntity : Entity<TEntity, TKey>
    where TKey : IComparable<TKey>, IEquatable<TKey>
{
    public static async Task<VectorSearchResult> SearchAsync(
        float[] vector,
        string? text,
        float alpha,
        int topK,
        string? continuationHint,
        CancellationToken ct)
    {
        var provider = GetProvider();

        // Check if provider supports NativeContinuation
        if (!string.IsNullOrWhiteSpace(continuationHint) &&
            !provider.Capabilities.HasFlag(VectorCapabilities.NativeContinuation))
        {
            // Log warning: Provider doesn't support continuation, hint ignored
            // Framework will use LastChunkId fallback in Search.cs
            _logger.LogDebug(
                "Provider {Provider} does not support NativeContinuation, hint ignored",
                provider.GetType().Name);

            continuationHint = null;  // Don't pass to provider
        }

        // Call provider
        return await provider.SearchAsync(vector, text, alpha, topK, continuationHint, ct);
    }
}
```

---

## Consequences

### Positive

✅ **76% latency reduction** - Pages 2+ drop from 250ms → 60ms (providers with NativeContinuation)
✅ **Push-down principle** - Providers own pagination, framework delegates
✅ **YAGNI compliance** - Framework doesn't replicate provider capabilities
✅ **SOC adherence** - Pagination is vector database concern
✅ **Zero breaking changes** - Existing providers work unchanged (`continuationHint: null`)
✅ **Progressive enhancement** - Providers add support independently
✅ **Framework abstraction** - Search.cs never knows "Weaviate" or "Qdrant" exist
✅ **Great DX** - Developer calls `Search()`, optimal path chosen automatically
✅ **Embedding cache bonus** - 60% improvement even for providers without NativeContinuation
✅ **Capability-based** - Uses `VectorCapabilities` enum for detection

### Negative

⚠️ **Implementation effort** - Each provider requires continuation implementation (~100 LOC each)
⚠️ **Provider-specific knowledge** - Adapter authors need to understand provider pagination APIs
⚠️ **Graceful degradation complexity** - Framework maintains fallback path (`LastChunkId`)
⚠️ **Testing complexity** - Need to test both native continuation and fallback paths

### Neutral

➡️ **Embedding cache** - Universally beneficial, orthogonal to continuation feature
➡️ **Multi-project unchanged** - Aggregation pagination remains orchestration-layer
➡️ **Phased rollout** - Weaviate first, other providers when ready

---

## Migration Path

### Phase 1: Framework Extensions (Week 1)
- ✅ Add `NativeContinuation` to `VectorCapabilities`
- ✅ Extend `IVectorSearchRepository.SearchAsync` with `continuationHint` parameter
- ✅ Extend `VectorSearchResult` with `ContinuationHint` property
- ✅ Update `ContinuationTokenData` with `ProviderHint` field
- ✅ Add embedding cache to `Search.cs` (IMemoryCache)
- ✅ Update `Search.cs` to pass/receive provider hints

### Phase 2: Weaviate Implementation (Week 1-2)
- ✅ Add `VectorCapabilities.NativeContinuation` to Weaviate adapter
- ✅ Implement `WithAfter(cursor)` in WeaviateQueryBuilder
- ✅ Extract `After` cursor from Weaviate GraphQL response
- ✅ Test cursor-based pagination with multiple pages

### Phase 3: Qdrant Implementation (Week 2)
- ✅ Add `VectorCapabilities.NativeContinuation` to Qdrant adapter
- ✅ Implement offset parsing/generation
- ✅ Test offset-based pagination with multiple pages

### Phase 4: Validation & Observability (Week 3)
- ✅ Add metrics: cache hit rate, provider hint usage, latency by provider
- ✅ Add logging: continuation hint pass-through, fallback triggers
- ✅ Integration tests: Weaviate, Qdrant, ChromaDB (fallback)
- ✅ Performance benchmarks: Before/after comparison

### Phase 5: Other Providers (Future)
- ⏳ Pinecone: When cursor API becomes available
- ⏳ Chroma: When pagination API becomes available
- ⏳ Custom providers: Documentation and examples provided

---

## Performance Characteristics

### Expected Latency (Weaviate with NativeContinuation)

| Page | Scenario | Latency | Breakdown |
|------|----------|---------|-----------|
| Page 1 | Cold cache | 250ms | Embedding (150ms) + Vector (50ms) + Hydration (50ms) |
| Page 2 | Hot cache + cursor | 60ms | Embedding (0ms cached) + Vector (10ms cursor) + Hydration (50ms) |
| Page 3+ | Hot cache + cursor | 60ms | Same as page 2 |

### Expected Latency (ChromaDB without NativeContinuation)

| Page | Scenario | Latency | Breakdown |
|------|----------|---------|-----------|
| Page 1 | Cold cache | 250ms | Embedding (150ms) + Vector (50ms) + Hydration (50ms) |
| Page 2 | Hot cache + fallback | 100ms | Embedding (0ms cached) + Vector (50ms) + Hydration (50ms skip) |
| Page 3+ | Hot cache + fallback | 100ms | Same as page 2 |

### Database Load Reduction

**Before (All Pages Re-query):**
- Page 1: 1 embedding + 1 vector search
- Page 2: 1 embedding + 1 vector search  ← Redundant
- Page 3: 1 embedding + 1 vector search  ← Redundant
- **Total (3 pages):** 3 embeddings + 3 vector searches

**After (With NativeContinuation):**
- Page 1: 1 embedding + 1 vector search
- Page 2: 0 embeddings (cached) + 1 vector search (cursor)
- Page 3: 0 embeddings (cached) + 1 vector search (cursor)
- **Total (3 pages):** 1 embedding + 3 vector searches

**Embedding Savings:** 67% reduction
**Vector Search Cost:** Same number of queries, but continuation is cheaper (cursor vs full ranking)

---

## Implementation Checklist

### Core Framework
- [ ] Add `VectorCapabilities.NativeContinuation` enum value
- [ ] Extend `IVectorSearchRepository.SearchAsync` signature
- [ ] Update `VectorSearchResult` record
- [ ] Update `ContinuationTokenData` with `ProviderHint`
- [ ] Add embedding cache (IMemoryCache) to `Search.cs`
- [ ] Update `Search.cs` to pass/receive provider hints
- [ ] Add capability-based routing in `VectorData.cs`

### Weaviate Adapter
- [ ] Add `NativeContinuation` to capabilities
- [ ] Implement `WithAfter(cursor)` extension
- [ ] Extract `After` from GraphQL response
- [ ] Update SearchAsync to use continuationHint
- [ ] Add logging for cursor usage
- [ ] Write integration tests

### Qdrant Adapter
- [ ] Add `NativeContinuation` to capabilities
- [ ] Implement offset parsing/generation
- [ ] Update SearchAsync to use continuationHint
- [ ] Add logging for offset usage
- [ ] Write integration tests

### ChromaDB Adapter
- [ ] Verify `NativeContinuation` NOT in capabilities
- [ ] Ensure `ContinuationHint` returns null
- [ ] Add logging for fallback behavior
- [ ] Write integration tests (fallback path)

### Testing & Validation
- [ ] Unit tests: Continuation token encoding/decoding
- [ ] Integration tests: Weaviate cursor pagination
- [ ] Integration tests: Qdrant offset pagination
- [ ] Integration tests: ChromaDB fallback (skip-based)
- [ ] Performance benchmarks: Before/after comparison
- [ ] Load tests: Concurrent pagination requests

### Observability
- [ ] Metrics: Embedding cache hit rate
- [ ] Metrics: Provider hint usage (% with NativeContinuation)
- [ ] Metrics: Latency by provider and page number
- [ ] Logging: Continuation hint pass-through
- [ ] Logging: Fallback triggers
- [ ] Dashboard: Pagination performance by provider

---

## Future Enhancements

### Short-term (Next 3 months)
- **Pinecone cursor support:** When Pinecone adds pagination API
- **Embedding cache warming:** Pre-cache common queries
- **Multi-project optimization:** Parallel provider queries with cursors

### Long-term (6+ months)
- **Redis-backed embedding cache:** Share cache across API instances
- **Smart prefetching:** Background fetch page N+1 when user requests page N
- **Adaptive topK:** Dynamically adjust based on provider performance

---

## References

- [Weaviate Cursor Pagination](https://weaviate.io/developers/weaviate/api/graphql/search-operators#cursor-with-after)
- [Qdrant Scroll API](https://qdrant.tech/documentation/concepts/points/#scroll-points)
- [ChromaDB Query API](https://docs.trychroma.com/reference/py-collection#query) (no pagination)
- [Pinecone Pagination](https://docs.pinecone.io/guides/data/query-data#pagination) (cursor-based, limited)
- ADR-0051: Vector Hybrid Search with BM25 Fusion
- ADR-0052: Adaptive Sliding Window Pagination for Vector Search
- Koan Framework Principles: Provider Abstraction, Capability Detection, Push-Down

---

## Decision Log

**2025-11-09:** Initial proposal - Push-down approach with adapter delegation
**2025-11-09:** Accepted - Renamed capability from `NativePagination` to `NativeContinuation`
**2025-11-09:** Architecture finalized - Opaque provider hints with embedding cache

---

**Last Updated:** 2025-11-09
**Implementation Target:** Sprint 2025-Q4
**Status:** Ready for implementation
