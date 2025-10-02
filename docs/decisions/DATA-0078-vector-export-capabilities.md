---
id: DATA-0078
slug: data-0078-vector-export-capabilities
domain: DATA
status: Proposed
date: 2025-10-02
---

# DATA-0078: Vector Database Export Capabilities - Provider-Agnostic Batch Export

Date: 2025-10-02

Status: Proposed

## Context

### Problem Statement

While implementing embedding cache optimization for S5.Recs, a critical use case emerged: **exporting existing vector embeddings from the vector database to populate a cache layer**. This enables:

1. **Adapter Migration**: Porting embeddings when switching vector providers (e.g., Weaviate → ElasticSearch → Qdrant)
2. **Cache Population**: Extracting stored embeddings to avoid expensive AI re-generation when rebuilding caches
3. **Backup/Analytics**: Exporting vectors for backup, analysis, or debugging purposes
4. **Cost Optimization**: Reusing expensive embeddings across different vector DB backends

### Current Framework Gap

The Koan Framework's `IVectorSearchRepository<TEntity, TKey>` currently provides:
- `UpsertAsync` / `UpsertManyAsync` - Write vectors
- `SearchAsync` - Similarity search (returns IDs and scores, not necessarily embeddings)
- `DeleteAsync` / `DeleteManyAsync` - Remove vectors

**Missing**: A standard way to **retrieve all stored vectors with their embeddings** for batch export operations.

### Research: Vector Database Export Capabilities

Investigation of major vector database providers reveals consistent patterns:

| Provider | Export Method | API Pattern | Native Support |
|----------|---------------|-------------|----------------|
| **ElasticSearch** | Scroll API | `POST /_search?scroll=1m` with `match_all` query, include `dense_vector` in `_source` | ✅ Native |
| **Weaviate** | Cursor Iteration | `with_after(cursor)` + `_additional { id vector }` for batch retrieval | ✅ Native |
| **Milvus** | Query Broadcast | `query("id >= 0", output_fields=["id", "vector"])` - noted as heavy operation | ✅ Native (with caveats) |
| **Qdrant** | Scroll + Snapshots | Offset-based `scroll` with `with_vector: true`; dedicated snapshot export | ✅ Native + Enhanced |
| **Pinecone** | Limited | Serverless: `list` IDs → `fetch` batches. Pod-based: **No direct method** | ⚠️ Limited/Workarounds |

#### Key Findings

1. **Universal Pattern**: 4 out of 5 major providers support batch vector export natively
2. **Common Characteristics**:
   - Pagination/batching to prevent memory exhaustion
   - Vectors excluded by default (opt-in via parameters for performance)
   - Cursor/scroll/offset-based iteration for consistency
   - Configurable batch sizes (typically 100-1000 records)

3. **Real-World Usage**:
   - Migration between providers (documented use case in Weaviate/Qdrant communities)
   - Backup/restore operations
   - Cache warming and optimization
   - Debugging and analytics

#### ElasticSearch Example
```http
POST /my-index/_search?scroll=1m
{
  "size": 1000,
  "query": { "match_all": {} },
  "_source": ["id", "embedding_vector", "metadata"]
}

POST /_search/scroll
{
  "scroll": "1m",
  "scroll_id": "DXF1ZXJ5QW5kRmV0Y2gBAA..."
}
```

#### Weaviate Example
```python
collection.iterator(
    return_properties=["id"],
    include_vector=True,  # Explicit opt-in
    batch_size=1000
)
```

#### Qdrant Example
```json
POST /collections/{collection_name}/points/scroll
{
  "limit": 1000,
  "with_vector": true,
  "offset": "last-point-id"
}
```

### Alignment with Framework Principles

This capability aligns with core Koan Framework principles:

1. **Provider Transparency**: Same operation works across ElasticSearch, Weaviate, Milvus, Qdrant
2. **Capability Detection**: Adapters can report support via boot reports; unsupported providers throw clear exceptions
3. **Streaming-First**: `IAsyncEnumerable` pattern matches Koan's existing streaming patterns (see ADR-0050)
4. **Zero-Config**: Works out-of-box once implemented; no configuration required
5. **Enterprise-Ready**: Handles large datasets without OOM issues via batching

### Related Decisions

- **ADR-0050**: Data access pagination and streaming - established `IAsyncEnumerable` pattern for large datasets
- **AI-0014**: Source abstraction and capability mapping - established capability reporting pattern

## Decision

Add **provider-agnostic batch vector export** capability to the Vector abstraction layer.

### API Design

#### 1. Add Export Method to IVectorSearchRepository

```csharp
namespace Koan.Data.Vector.Abstractions;

public interface IVectorSearchRepository<TEntity, TKey>
    where TEntity : IEntity<TKey>
    where TKey : notnull
{
    // Existing methods...
    Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default);
    Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default);

    // NEW: Export all vectors in batches
    /// <summary>
    /// Exports all stored vectors from the vector database in batches.
    /// Streams results to avoid materializing entire dataset in memory.
    /// Use for migration between providers, cache population, or backup operations.
    /// </summary>
    /// <param name="batchSize">Number of vectors per batch (default: provider-specific, typically 100-1000)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async stream of vector batches with IDs, embeddings, and metadata</returns>
    IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
        int? batchSize = null,
        CancellationToken ct = default
    )
    {
        // Default implementation: throw NotSupportedException for providers without native support
        throw new NotSupportedException(
            $"Vector export is not supported by this adapter. " +
            $"Provider: {GetType().Name}. " +
            $"Consider using an adapter with native export capabilities (ElasticSearch, Weaviate, Qdrant)."
        );
    }
}
```

#### 2. Export Result Record

```csharp
namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Represents a single exported vector with its ID, embedding, and metadata.
/// Returned in batches by ExportAllAsync for memory-efficient streaming.
/// </summary>
/// <typeparam name="TKey">Type of entity identifier</typeparam>
public sealed record VectorExportBatch<TKey>(
    /// <summary>Entity ID this vector belongs to</summary>
    TKey Id,

    /// <summary>The embedding vector (dense float array)</summary>
    float[] Embedding,

    /// <summary>Optional metadata stored with the vector</summary>
    object? Metadata = null
) where TKey : notnull;
```

### Implementation Per Adapter

#### ElasticSearch
```csharp
public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
    int? batchSize = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var size = batchSize ?? 1000; // ElasticSearch scroll default
    var scrollTime = "2m";

    // Initial search with scroll
    var initUrl = $"/{IndexName}/_search?scroll={scrollTime}";
    var initBody = new JObject
    {
        ["size"] = size,
        ["query"] = new JObject { ["match_all"] = new JObject() },
        ["_source"] = new JArray { _options.IdField, _options.VectorField, _options.MetadataField }
    };

    var resp = await _http.PostAsync(initUrl, JsonContent(initBody), ct);
    var json = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
    var scrollId = json["_scroll_id"]?.Value<string>();

    while (scrollId != null)
    {
        var hits = json["hits"]?["hits"] as JArray ?? new JArray();
        if (hits.Count == 0) break;

        foreach (var hit in hits.OfType<JObject>())
        {
            var id = hit["_source"]?[_options.IdField]?.Value<string>();
            var vectorArray = hit["_source"]?[_options.VectorField] as JArray;
            var metadata = hit["_source"]?[_options.MetadataField]?.ToObject<object>();

            if (id != null && vectorArray != null)
            {
                var embedding = vectorArray.Select(v => (float)v).ToArray();
                yield return new VectorExportBatch<TKey>(ConvertId(id), embedding, metadata);
            }
        }

        // Next scroll batch
        var scrollUrl = "/_search/scroll";
        var scrollBody = new JObject { ["scroll"] = scrollTime, ["scroll_id"] = scrollId };
        resp = await _http.PostAsync(scrollUrl, JsonContent(scrollBody), ct);
        json = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
        scrollId = json["_scroll_id"]?.Value<string>();
    }

    // Clear scroll
    if (scrollId != null)
    {
        await _http.DeleteAsync($"/_search/scroll/{scrollId}", ct);
    }
}
```

#### Weaviate
```csharp
public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
    int? batchSize = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var size = batchSize ?? 100; // Weaviate cursor default
    string? cursor = null;

    do
    {
        var query = new JObject
        {
            ["class"] = _className,
            ["limit"] = size,
            ["withVector"] = true // Explicitly include vectors
        };

        if (cursor != null)
        {
            query["after"] = cursor; // Cursor-based pagination
        }

        var resp = await QueryAsync(query, ct);
        var objects = resp["data"]?["Get"]?[_className] as JArray ?? new JArray();

        if (objects.Count == 0) break;

        foreach (var obj in objects.OfType<JObject>())
        {
            var id = obj["_additional"]?["id"]?.Value<string>();
            var vectorArray = obj["_additional"]?["vector"] as JArray;

            if (id != null && vectorArray != null)
            {
                var embedding = vectorArray.Select(v => (float)v).ToArray();
                var metadata = obj.ToObject<object>(); // Entire object as metadata
                yield return new VectorExportBatch<TKey>(ConvertId(id), embedding, metadata);
            }

            cursor = id; // Last ID becomes next cursor
        }

    } while (cursor != null);
}
```

#### Qdrant
```csharp
public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
    int? batchSize = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var limit = batchSize ?? 1000; // Qdrant scroll default
    string? offset = null;

    do
    {
        var scrollBody = new JObject
        {
            ["limit"] = limit,
            ["with_vector"] = true, // Explicitly request vectors
            ["with_payload"] = true
        };

        if (offset != null)
        {
            scrollBody["offset"] = offset;
        }

        var resp = await _http.PostAsync(
            $"/collections/{_collectionName}/points/scroll",
            JsonContent(scrollBody),
            ct
        );

        var json = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
        var points = json["result"]?["points"] as JArray ?? new JArray();

        if (points.Count == 0) break;

        foreach (var point in points.OfType<JObject>())
        {
            var id = point["id"]?.Value<string>();
            var vectorArray = point["vector"] as JArray;
            var payload = point["payload"]?.ToObject<object>();

            if (id != null && vectorArray != null)
            {
                var embedding = vectorArray.Select(v => (float)v).ToArray();
                yield return new VectorExportBatch<TKey>(ConvertId(id), embedding, payload);
            }
        }

        offset = json["result"]?["next_page_offset"]?.Value<string>();

    } while (offset != null);
}
```

#### Milvus
```csharp
public async IAsyncEnumerable<VectorExportBatch<TKey>> ExportAllAsync(
    int? batchSize = null,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var size = batchSize ?? 1000;
    var offset = 0;

    while (true)
    {
        var queryBody = new JObject
        {
            ["collection_name"] = _collectionName,
            ["expr"] = "id >= 0", // Broadcast query
            ["output_fields"] = new JArray { "id", "vector" },
            ["limit"] = size,
            ["offset"] = offset
        };

        var resp = await QueryAsync(queryBody, ct);
        var results = resp["data"] as JArray ?? new JArray();

        if (results.Count == 0) break;

        foreach (var result in results.OfType<JObject>())
        {
            var id = result["id"]?.Value<string>();
            var vectorArray = result["vector"] as JArray;

            if (id != null && vectorArray != null)
            {
                var embedding = vectorArray.Select(v => (float)v).ToArray();
                yield return new VectorExportBatch<TKey>(ConvertId(id), embedding, null);
            }
        }

        offset += size;

        if (results.Count < size) break; // Last page
    }
}
```

#### Pinecone (Unsupported - Default Implementation)
```csharp
// Uses default interface implementation throwing NotSupportedException
// Reason: Pod-based indexes lack native export; serverless requires workarounds
// Users should consider Qdrant/ElasticSearch for migration scenarios
```

### Usage Example: S5.Recs Cache Population

```csharp
// AdminController.cs - Export vectors to embedding cache
[HttpPost("cache/embeddings/export")]
public async Task<IActionResult> ExportVectorsToCache(
    [FromServices] IVectorSearchRepository<Media, string> vectorRepo,
    [FromServices] IEmbeddingCache cache,
    CancellationToken ct)
{
    var count = 0;
    var modelId = "nomic-embed-text"; // From config

    await foreach (var batch in vectorRepo.ExportAllAsync(batchSize: 100, ct))
    {
        // Load original media to reconstruct embedding text
        var media = await Media.Get(batch.Id, ct);
        if (media == null) continue;

        var embeddingText = BuildEmbeddingText(media);
        var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);

        // Cache the existing embedding
        await cache.SetAsync(contentHash, modelId, batch.Embedding, ct);
        count++;

        if (count % 100 == 0)
        {
            _logger.LogInformation("Exported {Count} vectors to cache...", count);
        }
    }

    return Ok(new { exported = count, message = "Vector export complete" });
}
```

## Consequences

### Positive

✅ **Provider Transparency**: Single API works across ElasticSearch, Weaviate, Milvus, Qdrant
✅ **Streaming-First**: Memory-efficient for large datasets via `IAsyncEnumerable`
✅ **Adapter Migration**: Enables zero-cost switching between vector providers
✅ **Cache Optimization**: Reuse expensive embeddings across system restarts/rebuilds
✅ **Framework Consistency**: Aligns with ADR-0050 streaming patterns
✅ **Capability Detection**: Clear exceptions for unsupported providers
✅ **Enterprise-Ready**: Batch sizes prevent OOM; cancellation support for long operations

### Negative / Risks

⚠️ **Performance Impact**: Exporting large vector stores can take significant time
⚠️ **Network Traffic**: Full vector retrieval increases bandwidth usage
⚠️ **Provider-Specific Nuances**: Each adapter has unique pagination semantics
⚠️ **Pinecone Limitation**: Pod-based indexes lack native support

### Mitigations

1. **Performance**: Document as "infrequent operation" for migration/backup only
2. **Network**: Configurable batch sizes allow tuning for network conditions
3. **Provider Nuances**: Abstraction hides differences; adapters optimize for their backend
4. **Pinecone**: Clear error message directs users to supported adapters

### Trade-offs Considered

#### Alternative 1: Instruction-Based Pattern
```csharp
var result = await vectorRepo.ExecuteAsync<IEnumerable<VectorExportBatch<TKey>>>(
    new Instruction(VectorInstructions.ExportAll), ct
);
```

**Rejected**: Instruction pattern doesn't support streaming; materializes entire dataset.

#### Alternative 2: Snapshot/Backup Instruction
```csharp
await vectorRepo.ExecuteAsync(
    new Instruction(VectorInstructions.CreateSnapshot, new { path = "/backup" }), ct
);
```

**Rejected**: Snapshot format is provider-specific (Qdrant's tar vs Weaviate's JSON); doesn't enable cross-provider migration.

#### Alternative 3: Search-Based Export
```csharp
var allVectors = await vectorRepo.SearchAsync(new VectorQueryOptions
{
    Query = randomVector,
    TopK = int.MaxValue
}, ct);
```

**Rejected**: Requires dummy query vector; limited by TopK caps (e.g., 10,000); not semantically correct.

## Implementation Plan

### Phase 1: Core Abstraction
- [ ] Add `VectorExportBatch<TKey>` record to `Koan.Data.Vector.Abstractions`
- [ ] Add `ExportAllAsync` method to `IVectorSearchRepository` with default NotSupportedException
- [ ] Update `VectorInstructions` if instruction pattern preferred (TBD)

### Phase 2: Adapter Implementation
- [ ] Implement ElasticSearch scroll-based export
- [ ] Implement Weaviate cursor-based export
- [ ] Implement Qdrant scroll-based export
- [ ] Implement Milvus query-based export (with performance warnings)
- [ ] Document Pinecone limitations

### Phase 3: S5.Recs Integration
- [ ] Add `ISeedService.StartExportVectorsAsync()` method
- [ ] Update `AdminController` export endpoint to use `ExportAllAsync`
- [ ] Update dashboard UI (already exists, just needs backend fix)
- [ ] Add logging/progress reporting for long operations

### Phase 4: Testing & Documentation
- [ ] Unit tests: Each adapter's export implementation
- [ ] Integration tests: Export from seeded vector stores
- [ ] Performance tests: Large dataset export (10K+ vectors)
- [ ] Documentation: Migration guide (Weaviate → ElasticSearch example)
- [ ] Boot report: Show if adapter supports export capability

## References

### Online Research
- [ElasticSearch Scroll API Documentation](https://www.elastic.co/guide/en/elasticsearch/reference/current/scroll-api.html)
- [Weaviate Cursor Iteration](https://weaviate.io/developers/weaviate/manage-data/read-all-objects)
- [Qdrant Scroll API](https://api.qdrant.tech/api-reference/points/scroll-points)
- [Pinecone Community: Export All Vectors](https://community.pinecone.io/t/is-there-any-method-for-exporting-all-vectors-in-collection/583)
- [Milvus Query API](https://milvus.io/docs/query.md)

### Related ADRs
- ADR-0050: Data access pagination and streaming
- AI-0014: Source abstraction and capability mapping

### Implementation Context
- S5.Recs embedding cache optimization
- Vector adapter portability requirements
- Cost optimization for AI embeddings

---

## Future Considerations

### Partial Export (Filter-Based)
```csharp
IAsyncEnumerable<VectorExportBatch<TKey>> ExportAsync(
    Expression<Func<TEntity, bool>> predicate,
    int? batchSize = null,
    CancellationToken ct = default
);
```

**Use Case**: Export only vectors matching criteria (e.g., date range, category)

### Progress Reporting
```csharp
IAsyncEnumerable<(VectorExportBatch<TKey> Batch, int Progress, int? Total)> ExportWithProgressAsync(...);
```

**Use Case**: Long-running exports need progress feedback for UX

### Snapshot Format Standardization
Define Koan-specific snapshot format for perfect fidelity across all providers (includes index settings, mappings, etc.)

**Use Case**: True backup/restore without re-indexing
