---
type: GUIDE
domain: ai
title: "AI & Vector Search How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: source-first
validation:
  status: not-yet-tested
  scope: docs/guides/ai-vector-howto.md
related_guides:
  - entity-capabilities-howto.md
  - patch-capabilities-howto.md
  - canon-capabilities-howto.md
  - mcp-http-sse-howto.md
---

# Koan AI & Vector Search – End-to-End How-To

This guide walks through Koan's AI-powered semantic-search surfaces, from a first embedding to hybrid
and personalization patterns. The executable companion is [GardenCoop Chapter 2](../../samples/journeys/GardenCoop/02-LocalDiscovery/),
which saves Produce entities, embeds them with local ONNX, and searches them with sqlite-vec. Advanced
hybrid and personalization recipes are compositional patterns, not a certified production workload.

**Related Guides:**
- [Entity Capabilities](entity-capabilities-howto.md) – Core entity patterns for data access
- [PATCH Operations](patch-capabilities-howto.md) – Partial update patterns for entities
- [Canon Entities](canon-capabilities-howto.md) – Multi-source aggregation and conflict resolution
- [MCP over HTTP+SSE](mcp-http-sse-howto.md) – Expose entities to AI agents

---

## 0. Prerequisites

1. Add the Koan AI and Vector packages:
   ```xml
   <PackageReference Include="Koan.AI" Version="0.6.3" />
   <PackageReference Include="Koan.AI.Contracts" Version="0.6.3" />
   <PackageReference Include="Koan.Data.Vector" Version="0.6.3" />
   <PackageReference Include="Koan.Data.Vector.Abstractions" Version="0.6.3" />
   ```

2. Reference at least one AI connector (Ollama example):
   ```xml
   <PackageReference Include="Koan.AI.Connector.Ollama" Version="0.6.3" />
   ```

3. Reference a vector database adapter (Weaviate example):
   ```xml
   <PackageReference Include="Koan.Data.Vector.Connector.Weaviate" Version="0.6.3" />
   ```

4. Configure AI and vector services:
   ```json
   {
     "Koan": {
       "AI": {
         "Providers": {
           "Ollama": {
             "Endpoint": "http://localhost:11434",
             "DefaultModel": "llama3.2:latest",
             "DefaultEmbeddingModel": "all-minilm:latest"
           }
         }
       },
       "Vector": {
         "Providers": {
           "Weaviate": {
             "Endpoint": "http://localhost:8080",
             "Dimension": 384
           }
         }
       }
     }
   }
   ```

5. Boot the runtime with `builder.Services.AddKoan();` in your `Program.cs`.

With that in place, you can leverage AI embeddings and vector search as described below.

---

## 1. Foundations – Generating Embeddings

**Concepts**

- `Koan.AI.Client.Embed(text)` generates vector embeddings from text using configured AI provider
- Embeddings are `float[]` arrays representing semantic meaning in high-dimensional space
- Provider-agnostic API - swap models by changing configuration
- Manual embeddings (`vectorizer: "none"`) give you full control

**Recipe**

- Packages listed in prerequisites
- AI provider configured under `Koan:Ai` (e.g. `Koan:Ai:Embed:Source`/`Model`)
- No special entity setup required

**Sample**

```csharp
using Koan.AI;

// Generate embedding from text
var text = "A heartwarming story about friendship and courage";
var embedding = await Koan.AI.Client.Embed(text, ct);

Console.WriteLine($"Generated {embedding.Length}-dimensional vector");
// Output: Generated 384-dimensional vector
```

**Usage scenarios & benefits**

- *GardenCoop Chapter 2* generates embeddings from produce names and descriptions for local semantic search
- Developers can swap embedding models (all-minilm → nomic-embed → OpenAI) by changing config
- Same embedding can be used across multiple vector databases

**Going further – custom models**

```csharp
// Use specific model for domain-specific embeddings
var embedding = await Koan.AI.Client.Embed(
    text,
    new AiOptions { Model = "nomic-embed-text:latest" },
    ct
);
```

---

## 2. Vector Storage – Saving Embeddings

**Concepts**

- `Vector<T>.Save()` stores embeddings with entity IDs and optional metadata
- Metadata enables hybrid search and filtering (titles, tags, categories)
- Provider-transparent across the shipped Weaviate, Qdrant, Milvus, SQLite-vec, Elasticsearch,
  OpenSearch, and InMemory connectors, subject to each connector's advertised capabilities
- Supports bulk operations for efficient batch indexing

**Recipe**

- Vector provider configured in `Koan:Vector:Providers`
- Entity must have string-based ID (GUID v7 recommended)
- Optional metadata dictionary for hybrid search

**Sample**

```csharp
using Koan.Data.Vector;

public class Media : Entity<Media>
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string[] Genres { get; set; } = Array.Empty<string>();
}

// Generate embedding
var media = await Media.Get(mediaId);
var embeddingText = $"{media.Title}\n\n{media.Synopsis}\n\nGenres: {string.Join(", ", media.Genres)}";
var embedding = await Koan.AI.Client.Embed(embeddingText, ct);

// Store with metadata
await Vector<Media>.Save(
    id: media.Id,
    embedding: embedding,
    metadata: new Dictionary<string, object>
    {
        ["title"] = media.Title,
        ["searchText"] = media.Title,  // For hybrid search
        ["genres"] = media.Genres
    }
);
```

**Usage scenarios & benefits**

- *GardenCoop Chapter 2* demonstrates Entity indexing for semantic search
- Metadata enables hybrid keyword+semantic search (see section 4)
- The same Vector API can target another shipped connector; verify capability and migration support
  before changing providers

**Batch indexing**

```csharp
var batch = mediaItems.Select(m => (
    Id: m.Id,
    Embedding: GenerateEmbedding(m),
    Metadata: BuildMetadata(m)
));

var count = await Vector<Media>.Save(batch);
Console.WriteLine($"Indexed {count} items");
```

---

## 3. Vector Search – Semantic Similarity

**Concepts**

- `Vector<T>.Search()` finds semantically similar items using cosine similarity
- Pure vector search excels at conceptual queries ("cute but powerful characters")
- Query embedding compared against all stored embeddings
- Results ranked by similarity score (1.0 = identical, 0.0 = orthogonal)

**Recipe**

- Entities indexed with `Vector<T>.Save()` (section 2)
- Vector provider supporting `VectorCaps.Knn`
- Query text embedded using same model as indexed data

**Sample**

```csharp
// User searches for conceptual match
var query = "heartwarming slice of life anime";
var queryEmbedding = await Koan.AI.Client.Embed(query, ct);

// Semantic search
var results = await Vector<Media>.Search(
    vector: queryEmbedding,
    topK: 20
);

foreach (var match in results.Matches)
{
    var media = await Media.Get(match.Id);
    Console.WriteLine($"{media.Title} - Score: {match.Score:F2}");
}
```

**Usage scenarios & benefits**

- Users find content based on vague descriptions or moods
- Works across languages - "kawaii" matches "cute" semantically
- No exact keyword matching required - understands synonyms and concepts

**Advanced - capability detection**

```csharp
if (!Vector<Media>.IsAvailable)
{
    throw new InvalidOperationException("Vector search not configured");
}

var capabilities = Vector<Media>.GetCapabilities();
if (capabilities.Has(VectorCaps.Knn))
{
    // Perform vector search
}
```

---

## 4. Hybrid Search – Semantic + Keyword

**Concepts**

- Combines vector similarity (semantic) with BM25 keyword matching (lexical)
- Solves exact title matching for non-English content
- `alpha` parameter controls semantic vs keyword balance (0.0=keyword, 1.0=semantic)
- Provider-native fusion (Weaviate, ElasticSearch) for optimal performance
- Requires `VectorCaps.Hybrid` support

**Recipe**

- Vector provider supporting hybrid search (Weaviate, ElasticSearch, Qdrant)
- Metadata with `searchText` field indexed during save
- Same embedding as pure vector search

**Sample**

```csharp
// User searches for exact title
var query = "Watashi no Kokoro wa Oji-san de Aru";
var queryEmbedding = await Koan.AI.Client.Embed(query, ct);

// Hybrid search: semantic + keyword
var results = await Vector<Media>.Search(
    vector: queryEmbedding,
    text: query,        // Enables BM25 keyword matching
    alpha: 0.5,         // 50% semantic, 50% keyword
    topK: 10
);
```

**Alpha tuning guide**

```csharp
// Exact title searches - favor keywords
var exactResults = await Vector<Media>.Search(
    vector: embedding,
    text: "Attack on Titan",
    alpha: 0.3,  // 70% keyword, 30% semantic
    topK: 10
);

// Conceptual searches - favor semantics
var conceptResults = await Vector<Media>.Search(
    vector: embedding,
    text: "dark fantasy with magic",
    alpha: 0.8,  // 80% semantic, 20% keyword
    topK: 10
);

// Balanced - default
var balancedResults = await Vector<Media>.Search(
    vector: embedding,
    text: searchQuery,
    alpha: 0.5,  // 50/50 blend
    topK: 10
);
```

**Usage scenarios & benefits**

- One API can combine exact terms with semantic intent when the selected connector advertises both capabilities
- Single API handles all search types - no separate keyword/semantic endpoints
- Users control balance with UI slider (see section 8)

**Indexing for hybrid search**

```csharp
// Separate embedding text (rich context) from search text (exact matching)
var embeddingText = BuildEmbeddingText(media);  // Title + synopsis + genres
var searchText = BuildSearchText(media);        // Just titles and synonyms

var embedding = await Koan.AI.Client.Embed(embeddingText, ct);

await Vector<Media>.Save(
    id: media.Id,
    embedding: embedding,
    metadata: new Dictionary<string, object>
    {
        ["title"] = media.Title,
        ["searchText"] = searchText,  // Required for hybrid search
        ["genres"] = media.Genres
    }
);

// Helper methods
string BuildEmbeddingText(Media m) =>
    $"{m.Title}\n\n{m.Synopsis}\n\nGenres: {string.Join(", ", m.Genres)}";

string BuildSearchText(Media m)
{
    var titles = new[] { m.Title, m.TitleEnglish, m.TitleRomaji, m.TitleNative }
        .Concat(m.Synonyms ?? [])
        .Where(t => !string.IsNullOrWhiteSpace(t))
        .Distinct();
    return string.Join(" ", titles);
}
```

---

## 5. Personalization – Vector Blending

**Concepts**

- Combine user preference vectors with search intent vectors
- User preference vector learned from historical interactions (ratings, views)
- Blended vector provides personalized semantic search
- Blend weight controls search intent vs user preferences (typically 66/34)

**Recipe**

- User profile entity storing learned preference vector
- Vector blending helper function
- Same hybrid search API as section 4

**Sample**

```csharp
public class UserProfile : Entity<UserProfile>
{
    public string UserId { get; set; } = "";
    public float[]? PrefVector { get; set; }  // Learned from interactions
    public Dictionary<string, double> GenreWeights { get; set; } = new();
}

// Get user's learned preferences
var profile = await UserProfile.Get(userId);
var userPrefVector = profile?.PrefVector;

// Generate search intent vector
var searchVector = await Koan.AI.Client.Embed("magic school anime", ct);

// Blend: 66% search intent, 34% user preferences
var blended = BlendVectors(searchVector, userPrefVector, weight: 0.66);

// Personalized hybrid search
var results = await Vector<Media>.Search(
    vector: blended,
    text: "magic school anime",
    alpha: 0.5,
    topK: 50
);

// Helper: Blend two vectors with normalization
float[] BlendVectors(float[] vec1, float[] vec2, double weight1)
{
    if (vec1.Length != vec2.Length)
        throw new ArgumentException("Vector dimension mismatch");

    var result = new float[vec1.Length];
    var weight2 = 1.0 - weight1;

    for (int i = 0; i < result.Length; i++)
    {
        result[i] = (float)((weight1 * vec1[i]) + (weight2 * vec2[i]));
    }

    // Normalize to unit length (preserves cosine similarity)
    var magnitude = Math.Sqrt(result.Sum(x => (double)x * x));
    if (magnitude > 1e-8)
    {
        for (int i = 0; i < result.Length; i++)
            result[i] /= (float)magnitude;
    }

    return result;
}
```

**Usage scenarios & benefits**

- Applications can blend explicit search with an application-owned preference vector
- New users get pure search results; returning users get personalized blends
- 66/34 split prioritizes explicit search intent over learned preferences

**Learning user preferences**

```csharp
// Update user preference vector from rating
public async Task UpdateUserPreferences(string userId, string mediaId, int rating)
{
    var media = await Media.Get(mediaId);
    var profile = await UserProfile.Get(userId) ?? new UserProfile { Id = userId };

    // Generate embedding for rated media
    var mediaText = $"{media.Title}\n\n{media.Synopsis}";
    var mediaVector = await Koan.AI.Client.Embed(mediaText);

    const double LEARNING_RATE = 0.3;
    var target = (rating - 1) / 4.0;  // Normalize 1-5 rating to 0-1

    if (profile.PrefVector == null)
    {
        profile.PrefVector = mediaVector;
    }
    else
    {
        // Exponential moving average
        for (int i = 0; i < profile.PrefVector.Length; i++)
        {
            profile.PrefVector[i] = (float)(
                (1 - LEARNING_RATE) * profile.PrefVector[i] +
                LEARNING_RATE * target * mediaVector[i]
            );
        }
    }

    await profile.Save();
}
```

---

## 6. Embedding Caching – Performance Optimization

**Concepts**

- An application-owned cache can avoid repeated AI calls when the normalized content and model match
- Its latency and cost benefit depends on how often the workload repeats identical inputs
- Content-addressable storage - same text always produces same hash
- Model-aware caching - different models have separate cache spaces

**Recipe**

- `EmbeddingCache` below is an **application pattern**, not a framework type; own its policy when measured repetition justifies it
- Storage backend (file system, Redis, database)
- SHA512 content hashing for deterministic keys

**Sample**

```csharp
// Application-owned example: Koan.AI does not ship this cache abstraction.

public class EmbeddingService
{
    private readonly IEmbeddingCache _cache;
    private const string MODEL_ID = "all-minilm:latest";

    // Generate or retrieve cached embedding
    public async Task<float[]> GetEmbedding(string text, CancellationToken ct)
    {
        var contentHash = EmbeddingCache.ComputeContentHash(text);

        // Check cache first
        var cached = await _cache.GetAsync(contentHash, MODEL_ID, "Media", ct);
        if (cached != null)
        {
            return cached.Embedding;
        }

        // Cache miss - generate new embedding
        var embedding = await Koan.AI.Client.Embed(text, ct);

        // Store in cache
        await _cache.SetAsync(contentHash, MODEL_ID, embedding, "Media", ct);

        return embedding;
    }
}
```

**Usage scenarios & benefits**

- Swapping embedding models invalidates only that model's cache (model-aware keys)
- Measure hit rate, latency, storage, and provider cost before deciding whether this app-owned pattern
  is worth operating

**Batch caching pattern**

```csharp
// Process items with cache-aware pipeline
foreach (var media in mediaItems)
{
    var embeddingText = BuildEmbeddingText(media);
    var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);

    var cached = await _cache.GetAsync(contentHash, modelId, typeof(Media).Name, ct);

    if (cached != null)
    {
        // Cache hit - fast path
        await Vector<Media>.Save(media.Id, cached.Embedding, BuildMetadata(media));
        cacheHits++;
    }
    else
    {
        // Cache miss - generate and store
        var embedding = await Koan.AI.Client.Embed(embeddingText, ct);
        await _cache.SetAsync(contentHash, modelId, embedding, typeof(Media).Name, ct);
        await Vector<Media>.Save(media.Id, embedding, BuildMetadata(media));
        cacheMisses++;
    }
}

_logger.LogInformation(
    "Embedding cache: {Hits} hits, {Misses} misses ({HitRate:P1})",
    cacheHits, cacheMisses, (double)cacheHits / (cacheHits + cacheMisses)
);
```

---

## 7. Embedding Lifecycle and Explicit Backfills

**Concepts**

- `[Embedding]` makes ordinary Entity saves the shortest path to vector indexing
- `Async = true` defers embedding work without changing the application save operation
- `EmbeddingMigrator.ReEmbed(...)` owns an explicit finite-set rebuild
- `EmbeddingMigrator.ReEmbedAll<T>(...)` owns an intentional whole-collection model transition
- Lifecycle, deferred work, and migration share one vector-only writer; none re-save the domain Entity

**Recipe**

- Add `[Embedding]` to the Entity and keep ordinary application code business-focused
- Use the migrator only for operator-initiated backfills or model transitions
- Inspect `MigrationResult`; migration is observable but not atomic and does not retry failures
- Configure deferred throughput and retry once at the host through `EmbeddingWorkerOptions`

There is intentionally no `.Index()` alias or collection `.Embed()` terminal. Ordinary indexing is
already the Entity lifecycle meaning, while explicit rebuilds have different outcomes and belong to
the migration control plane.

**Sample – ordinary writes**

```csharp
[Embedding(Async = true, Template = "{Title}\n\n{Description}")]
public sealed class Media : Entity<Media>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

await media.Save(ct); // persistence succeeds; embedding is lifecycle-owned and deferred
```

Use synchronous `[Embedding]` when the save must wait for indexing. With `Async = true`, inspect the
framework embedding ledger and logs for completion; the initial save is not a claim that the vector
write has completed.

**Sample – explicit subset backfill**

```csharp
var stale = await Media.Query(media => media.NeedsReindex, ct);
var result = await EmbeddingMigrator.ReEmbed(
    stale,
    targetModel: "all-minilm",
    batchSize: 100,
    logger: logger,
    ct: ct);

logger.LogInformation(
    "Indexed {Succeeded}/{Total}; failed={Failed}",
    result.SuccessfulEntities,
    result.TotalEntities,
    result.FailedEntities);
```

`ReEmbed` materializes only the supplied finite set, processes it in batches, and performs vector-only
writes so it does not recursively trigger persistence lifecycle. It reports partial success; it does
not promise collection atomicity, retry, or rollback.

**Usage scenarios & benefits**

- Ordinary imports need only `Save`; the Entity declaration owns embedding intent
- A selected subset can be re-indexed without touching unrelated entities
- A whole-collection model change has a dedicated API that resets mixed-model protection deliberately

**Whole-collection model transition**

```csharp
var result = await EmbeddingMigrator.ReEmbedAll<Media>(
    targetModel: "text-embedding-3-large",
    batchSize: 100,
    logger: logger,
    ct: ct);
```

---

## 8. Production Patterns

**Concepts**

- Re-indexing strategies for model upgrades
- Monitoring and health checks
- User-facing controls (alpha slider)
- Graceful degradation when vector search unavailable

**Recipe**

- All previous sections
- Health check package for monitoring
- Frontend UI for user control

**Sample – Re-indexing with new model**

> **Mixed-space guard.** Vectors from different models aren't comparable, so the framework throws
> `VectorModelMismatchException` if you write a new-model vector into an index still built from the old
> model. Re-index the whole collection through `EmbeddingMigrator.ReEmbedAll<T>(targetModel: …)` — it
> resets the model registry for the by-design transition. A manual loop like the one below must reset
> the registry first (or it trips the guard on the first save).

```csharp
public async Task ReindexWithNewModel(string newModelId, CancellationToken ct)
{
    _logger.LogInformation("Starting re-index with model {Model}", newModelId);

    var total = await Media.Count();
    var processed = 0;
    var errors = 0;

    await foreach (var media in Media.AllStream(batchSize: 100, ct))
    {
        try
        {
            var embeddingText = BuildEmbeddingText(media);
            var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);

            // Check cache for new model
            var cached = await _cache.GetAsync(contentHash, newModelId, "Media", ct);

            float[] embedding;
            if (cached != null)
            {
                embedding = cached.Embedding;
            }
            else
            {
                // Generate with new model
                embedding = await Koan.AI.Client.Embed(
                    embeddingText,
                    new AiOptions { Model = newModelId },
                    ct
                );
                await _cache.SetAsync(contentHash, newModelId, embedding, "Media", ct);
            }

            await Vector<Media>.Save(
                id: media.Id,
                embedding: embedding,
                metadata: new { searchText = BuildSearchText(media) }
            );

            processed++;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-index {MediaId}", media.Id);
            errors++;
        }

        if (processed % 1000 == 0)
        {
            _logger.LogInformation(
                "Re-index progress: {Processed}/{Total} ({Errors} errors)",
                processed, total, errors
            );
        }
    }

    _logger.LogInformation(
        "Re-index complete: {Processed} processed, {Errors} errors",
        processed, errors
    );
}
```

**Health checks**

```csharp
// Verify vector search availability
services.AddHealthChecks()
    .AddCheck("vector-search", () =>
    {
        if (!Vector<Media>.IsAvailable)
            return HealthCheckResult.Unhealthy("Vector search not configured");

        var caps = Vector<Media>.GetCapabilities();
        if (!caps.Has(VectorCaps.Knn))
            return HealthCheckResult.Degraded("KNN search not supported");

        if (!caps.Has(VectorCaps.Hybrid))
            return HealthCheckResult.Degraded("Hybrid search not supported");

        return HealthCheckResult.Healthy("Vector search operational");
    });
```

**User-facing alpha control**

```html
<!-- Frontend UI for alpha slider -->
<div class="search-controls">
  <input type="text" v-model="query.text" placeholder="Search...">

  <div class="alpha-slider">
    <label>Search Balance:</label>
    <input
      type="range"
      v-model.number="query.alpha"
      min="0"
      max="1"
      step="0.1">

    <div class="alpha-labels">
      <span :class="{ active: query.alpha < 0.35 }">📝 Exact Match</span>
      <span :class="{ active: query.alpha >= 0.35 && query.alpha <= 0.65 }">🔀 Balanced</span>
      <span :class="{ active: query.alpha > 0.65 }">🧠 Semantic</span>
    </div>

    <small>{{ alphaHint }}</small>
  </div>
</div>

<script>
computed: {
  alphaHint() {
    const a = this.query.alpha;
    if (a < 0.3) return "Best for exact titles like 'Attack on Titan'";
    if (a > 0.7) return "Best for concepts like 'heartwarming slice of life'";
    return "Balanced exact + semantic matching";
  }
}
</script>
```

**Graceful degradation**

```csharp
public async Task<List<Media>> Search(string query, double alpha, int topK)
{
    if (Vector<Media>.IsAvailable)
    {
        try
        {
            var embedding = await Koan.AI.Client.Embed(query);
            var results = await Vector<Media>.Search(
                vector: embedding,
                text: query,
                alpha: alpha,
                topK: topK
            );

            return await LoadEntities(results.Matches);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed, falling back to database");
        }
    }

    // Fallback: database query with simple text matching
    return await Media.Query(m =>
        m.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (m.Synopsis != null && m.Synopsis.Contains(query, StringComparison.OrdinalIgnoreCase))
    );
}
```

---

## 9. Advanced Scenarios

### Multi-Vector Search

```csharp
// Search with multiple semantic dimensions
var titleEmbedding = await Koan.AI.Client.Embed(media.Title);
var synopsisEmbedding = await Koan.AI.Client.Embed(media.Synopsis);

// Store multiple vectors per entity (if provider supports it)
if (Vector<Media>.GetCapabilities().Has(VectorCaps.MultiVectorPerEntity))
{
    await Vector<Media>.Save(
        id: media.Id,
        embedding: titleEmbedding,
        metadata: new { vectorName = "title" }
    );

    await Vector<Media>.Save(
        id: media.Id,
        embedding: synopsisEmbedding,
        metadata: new { vectorName = "synopsis" }
    );
}
```

### Cross-Lingual Search

```csharp
// Multilingual embeddings work across languages
var englishQuery = await Koan.AI.Client.Embed("cute magical girls");
var japaneseQuery = await Koan.AI.Client.Embed("かわいい魔法少女");

// Both queries find similar results semantically
var englishResults = await Vector<Media>.Search(vector: englishQuery, topK: 10);
var japaneseResults = await Vector<Media>.Search(vector: japaneseQuery, topK: 10);

// Results overlap significantly due to semantic similarity
```

### Vector Export for Migration

```csharp
// Export vectors from Weaviate to migrate to different provider
var vectorRepo = serviceProvider.GetRequiredService<IVectorSearchRepository<Media, string>>();

await foreach (var batch in vectorRepo.ExportAll(batchSize: 100, ct))
{
    // batch.Id - Entity identifier
    // batch.Embedding - float[] vector
    // batch.Metadata - Optional metadata

    // Import to new provider
    await newProviderRepo.Upsert(batch.Id, batch.Embedding, batch.Metadata, ct);
}
```

---

## Next Steps

1. Start simple - embed a few entities and try semantic search (sections 1-3)
2. Add hybrid search for exact matching (section 4)
3. Add an app-owned embedding cache when measured input repetition justifies it (section 6)
4. Use Entity lifecycle for ordinary indexing and explicit migrators for backfills (section 7)
5. Add personalization for returning users (section 5)

Run [GardenCoop Chapter 2](../../samples/journeys/GardenCoop/02-LocalDiscovery/) for the current local
embed-save-search path. Treat hybrid matching, personalization, and production tuning as separate claims
that need their own provider and workload evidence.

**Related Guides:**
- [Entity Capabilities](entity-capabilities-howto.md) – Learn core entity patterns for data access and CRUD operations
- [PATCH Operations](patch-capabilities-howto.md) – Apply partial updates to entities across providers
- [Canon Entities](canon-capabilities-howto.md) – Aggregate data from multiple sources with conflict resolution
- [MCP over HTTP+SSE](mcp-http-sse-howto.md) – Expose your AI-powered entities to remote AI agents

When in doubt, stick to the capability-first patterns above. They keep your code provider-agnostic and ready for model upgrades without breaking changes.
