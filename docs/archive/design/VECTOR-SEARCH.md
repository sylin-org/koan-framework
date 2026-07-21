# Vector Search Design

**Version:** 2.0
**Date:** 2025-10-03
**Status:** Active

---

## Overview

Koan Framework provides a provider-transparent vector search abstraction supporting both pure semantic search and hybrid semantic+keyword search. The design follows Koan's core principles of multi-provider transparency with capability-based feature detection.

---

## Core Principles

### 1. Provider Transparency

Same code works across different vector databases:

```csharp
// Works with Weaviate, ElasticSearch, Qdrant, Pinecone, etc.
var results = await Vector<Media>.Search(
    vector: embedding,
    text: "search text",
    topK: 10
);
```

### 2. Capability-Based Features

Providers advertise supported capabilities via `VectorCapabilities` enum:

```csharp
[Flags]
public enum VectorCapabilities
{
    None = 0,
    Knn = 1 << 0,              // K-nearest neighbors search
    Filters = 1 << 1,          // Filter pushdown
    Hybrid = 1 << 2,           // Hybrid semantic + keyword search
    PaginationToken = 1 << 3,  // Token-based pagination
    StreamingResults = 1 << 4, // Streaming query results
    MultiVectorPerEntity = 1 << 5,
    BulkUpsert = 1 << 6,
    BulkDelete = 1 << 7,
    AtomicBatch = 1 << 8,
    ScoreNormalization = 1 << 9
}
```

**Runtime Detection:**

```csharp
var caps = Vector<Media>.GetCapabilities();

if (caps.HasFlag(VectorCapabilities.Hybrid)) {
    // Use hybrid search
} else {
    // Graceful degradation to pure vector
}
```

### 3. Manual Embeddings

Koan uses `vectorizer: "none"` pattern - developers generate embeddings explicitly:

```csharp
// Application controls embedding generation
var embedding = await Ai.Embed(text, ct);

// Store with metadata
await Vector<Media>.Save(
    id: mediaId,
    embedding: embedding,
    metadata: new { title = "...", searchText = "..." }
);
```

**Why manual?**
- ✅ Full control over embedding models (easy to swap)
- ✅ Embedding caching at application level
- ✅ Same embedding can be stored in multiple vector DBs
- ✅ No vendor lock-in to specific embedding providers

---

## Search Modes

### Pure Vector Search (Semantic)

Traditional cosine similarity search in embedding space.

**Use Case:** Conceptual queries, vague descriptions

```csharp
var embedding = await Ai.Embed("cute but powerful anime characters");

var results = await Vector<Media>.Search(
    vector: embedding,
    topK: 20
);
```

**How it works:**
1. Query embedding compared to all stored embeddings via cosine similarity
2. Top-k nearest neighbors returned
3. Pure semantic understanding, no keyword matching

**Best for:**
- "Shows like Cowboy Bebop"
- "Dark fantasy with magic"
- "Emotionally powerful stories"

---

### Hybrid Search (Semantic + Keyword)

Combines vector similarity (semantic) with BM25 keyword matching.

**Use Case:** Exact names, titles, specific terms

```csharp
var embedding = await Ai.Embed("Attack on Titan");

var results = await Vector<Media>.Search(
    vector: embedding,
    text: "Attack on Titan",  // Enables BM25 component
    alpha: 0.5,               // 50% semantic, 50% keyword
    topK: 20
);
```

**How it works:**
1. **Vector component:** Cosine similarity on embeddings (semantic)
2. **BM25 component:** Keyword matching on `searchText` field (lexical)
3. **Fusion:** Provider combines scores based on `alpha` parameter

**Alpha Parameter:**
- `0.0` - Pure BM25 (keyword matching only)
- `0.5` - Balanced (default)
- `1.0` - Pure vector (semantic only)

**Best for:**
- "Watashi no Kokoro wa Oji-san de Aru" (exact titles)
- "Naruto Shippuden" (specific names)
- "Studio Ghibli films" (proper nouns)

**Provider Support:**
| Provider | Hybrid Support | Implementation |
|----------|---------------|----------------|
| Weaviate | ✅ Yes | `hybrid { query, vector, alpha }` |
| ElasticSearch | ✅ Yes | kNN + BM25 with script_score |
| Qdrant | ✅ Yes | Sparse + Dense vectors |
| Pinecone | ✅ Yes | Sparse-Dense hybrid |
| Milvus | ⚠️ Partial | Requires external BM25 |
| Chroma | ❌ No | Vector-only |

---

## Vector Blending (Personalization)

Combine multiple embeddings before querying.

**Use Case:** Personalized search - user preferences + search intent

```csharp
// User's learned preference vector
var userPrefVector = await UserProfile.GetPrefVector(userId);

// Current search intent
var searchVector = await Ai.Embed("magic school anime");

// Blend: 66% search intent, 34% user preferences
var blended = BlendVectors(searchVector, userPrefVector, weight: 0.66);

// Hybrid search with personalized vector
var results = await Vector<Media>.Search(
    vector: blended,
    text: "magic school anime",
    alpha: 0.5,
    topK: 50
);
```

**BlendVectors Implementation:**

```csharp
private static float[] BlendVectors(float[] vec1, float[] vec2, double weight1)
{
    var result = new float[vec1.Length];
    var weight2 = 1.0 - weight1;

    for (int i = 0; i < result.Length; i++)
    {
        result[i] = (float)((weight1 * vec1[i]) + (weight2 * vec2[i]));
    }

    // Normalize to unit length
    var magnitude = Math.Sqrt(result.Sum(x => (double)x * x));
    if (magnitude > 1e-8)
    {
        for (int i = 0; i < result.Length; i++)
            result[i] /= (float)magnitude;
    }

    return result;
}
```

**Why normalize?** Preserves cosine similarity semantics after linear combination.

---

## API Reference

### Vector<T> Class

```csharp
public class Vector<TEntity> where TEntity : class, IEntity<string>
{
    // Check availability
    public static bool IsAvailable { get; }

    // Get capabilities
    public static VectorCapabilities GetCapabilities();

    // Save vectors
    public static Task Save(string id, float[] embedding, object? metadata = null, CancellationToken ct = default);
    public static Task<int> Save(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default);

    // Search (unified interface)
    public static Task<VectorQueryResult<string>> Search(
        float[] vector,
        string? text = null,      // Optional: enables hybrid
        double? alpha = null,     // Optional: semantic vs keyword weight
        int? topK = null,
        object? filter = null,
        CancellationToken ct = default);

    // Advanced search
    public static Task<VectorQueryResult<string>> Search(VectorQueryOptions options, CancellationToken ct = default);

    // Delete
    public static Task<bool> Delete(string id, CancellationToken ct = default);
    public static Task<int> Delete(IEnumerable<string> ids, CancellationToken ct = default);

    // Maintenance
    public static Task EnsureCreated(CancellationToken ct = default);
    public static Task<int> Stats(CancellationToken ct = default);
}
```

### VectorQueryOptions

```csharp
public sealed record VectorQueryOptions(
    float[] Query,                  // Vector embedding (required)
    int? TopK = null,               // Results limit
    string? ContinuationToken = null,
    object? Filter = null,          // Provider-specific filters
    TimeSpan? Timeout = null,
    string? VectorName = null,      // Multi-vector support
    string? SearchText = null,      // Hybrid: text for BM25
    double? Alpha = null            // Hybrid: semantic vs keyword weight
);
```

---

## Indexing Patterns

### Basic Vector Storage

```csharp
var embeddingText = BuildEmbeddingText(entity);
var embedding = await Ai.Embed(embeddingText, ct);

await Vector<Media>.Save(
    id: entity.Id,
    embedding: embedding
);
```

### Hybrid-Ready Storage

```csharp
var embeddingText = BuildEmbeddingText(entity);  // Rich context for semantic
var searchText = BuildSearchText(entity);        // Titles for BM25

var embedding = await Ai.Embed(embeddingText, ct);

await Vector<Media>.Save(
    id: entity.Id,
    embedding: embedding,
    metadata: new Dictionary<string, object>
    {
        ["title"] = entity.Title,
        ["searchText"] = searchText,  // Required for hybrid search
        ["genres"] = entity.Genres,
        ["popularity"] = entity.Popularity
    }
);
```

**BuildSearchText Example:**

```csharp
private string BuildSearchText(Media media)
{
    // Combine all title variants for BM25 keyword matching
    var titles = new[] {
        media.Title,
        media.TitleEnglish,
        media.TitleRomaji,
        media.TitleNative
    }.Concat(media.Synonyms ?? [])
     .Where(t => !string.IsNullOrWhiteSpace(t))
     .Distinct();

    return string.Join(" ", titles);
}
```

**BuildEmbeddingText Example:**

```csharp
private string BuildEmbeddingText(Media media)
{
    // Rich context for semantic understanding
    var titles = string.Join(" / ", GetAllTitles(media));
    var tags = string.Join(", ", media.Genres.Concat(media.Tags));

    return $"{titles}\n\n{media.Synopsis}\n\nTags: {tags}".Trim();
}
```

**Why separate?**
- **Embedding text** - Rich context (synopsis, tags) for semantic understanding
- **Search text** - Just titles for precise BM25 keyword matching

---

## Provider Implementation Guide

### Implementing VectorCapabilities.Hybrid

**1. Advertise Capability**

```csharp
public VectorCapabilities Capabilities =>
    VectorCapabilities.Knn |
    VectorCapabilities.Filters |
    VectorCapabilities.Hybrid;  // Add this
```

**2. Schema with BM25-Indexed Text**

```csharp
properties = new object[]
{
    new { name = "docId", dataType = new[] { "text" } },
    new {
        name = "searchText",
        dataType = new[] { "text" },
        indexSearchable = true,    // Enable BM25
        tokenization = "word"
    }
}
```

**3. SearchAsync Implementation**

```csharp
public async Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct)
{
    if (!string.IsNullOrWhiteSpace(options.SearchText))
    {
        // Hybrid mode
        var alpha = options.Alpha ?? 0.5;
        return await HybridSearchAsync(options.Query, options.SearchText, alpha, options.TopK, ct);
    }
    else
    {
        // Pure vector mode
        return await VectorSearchAsync(options.Query, options.TopK, ct);
    }
}
```

**4. UpsertAsync Metadata Handling**

```csharp
if (metadata is IReadOnlyDictionary<string, object> metaDict &&
    metaDict.TryGetValue("searchText", out var searchText))
{
    properties["searchText"] = searchText;
}
```

---

## Performance Considerations

### Embedding Caching

```csharp
// Hash-based cache lookup
var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);
var cached = await embeddingCache.GetAsync(contentHash, modelId, ct);

if (cached != null)
{
    embedding = cached.Embedding;  // Skip AI call
}
else
{
    embedding = await Ai.Embed(embeddingText, ct);
    await embeddingCache.SetAsync(contentHash, modelId, embedding, ct);
}
```

**Cache hit rate:** Typically 70-90% for content that doesn't change frequently.

### Batch Operations

```csharp
// Bulk upsert for efficiency
var batch = items.Select(item => (
    Id: item.Id,
    Embedding: item.Embedding,
    Metadata: item.Metadata
));

await Vector<Media>.Save(batch);
```

### Query Optimization

```csharp
// Over-fetch and filter in memory
var results = await Vector<Media>.Search(
    vector: queryVector,
    text: searchText,
    topK: topK * 2  // Over-fetch
);

// Apply complex filters not pushable to provider
var filtered = results.Matches
    .Where(m => ComplexBusinessLogic(m))
    .Take(topK);
```

---

## Common Patterns

### Pattern 1: Simple Semantic Search

```csharp
var embedding = await Ai.Embed(userQuery);
var results = await Vector<Media>.Search(vector: embedding, topK: 20);
```

### Pattern 2: Exact Title with Semantic Fallback

```csharp
var embedding = await Ai.Embed(titleQuery);
var results = await Vector<Media>.Search(
    vector: embedding,
    text: titleQuery,
    alpha: 0.3,  // Favor keyword matching
    topK: 10
);
```

### Pattern 3: Personalized Search

```csharp
var userPrefVector = await UserProfile.GetPrefVector(userId);
var searchVector = await Ai.Embed(query);
var blended = BlendVectors(searchVector, userPrefVector, 0.66);

var results = await Vector<Media>.Search(
    vector: blended,
    text: query,
    alpha: 0.5,
    topK: 50
);
```

### Pattern 4: Discovery Mode (Preferences Only)

```csharp
var userPrefVector = await UserProfile.GetPrefVector(userId);
var results = await Vector<Media>.Search(
    vector: userPrefVector,
    topK: 20
);
```

---

## Migration Guide

### From Pure Vector to Hybrid

**Step 1:** Add `searchText` to metadata

```diff
  await Vector<Media>.Save(
      id: media.Id,
      embedding: embedding,
      metadata: new {
          title = media.Title,
+         searchText = BuildSearchText(media)
      }
  );
```

**Step 2:** Enable hybrid in queries

```diff
  var results = await Vector<Media>.Search(
      vector: embedding,
+     text: query,
+     alpha: 0.5,
      topK: 10
  );
```

**Step 3:** Re-index existing data

```csharp
var allMedia = await Media.All();
foreach (var media in allMedia)
{
    var embedding = /* retrieve from vector DB or regenerate */;
    await Vector<Media>.Save(
        id: media.Id,
        embedding: embedding,
        metadata: new { searchText = BuildSearchText(media) }
    );
}
```

---

## References

- **ADR-0051:** Vector Hybrid Search Decision
- **Weaviate Docs:** https://weaviate.io/developers/weaviate/search/hybrid
- **BM25 Algorithm:** https://en.wikipedia.org/wiki/Okapi_BM25
- **Vector Search Abstractions:** `src/Koan.Data.Vector.Abstractions/`
- **Weaviate Connector:** `src/Connectors/Data/Vector/Weaviate/`

---

**Last Updated:** 2025-10-03
**Version:** 2.0 (Added hybrid search support)
