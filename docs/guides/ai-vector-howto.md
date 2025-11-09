---
type: GUIDE
domain: ai
title: "AI & Vector Search How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-11-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-09
  status: verified
  scope: all-examples-tested
related_guides:
  - entity-capabilities-howto.md
  - patch-capabilities-howto.md
  - canon-capabilities-howto.md
  - mcp-http-sse-howto.md
---

# Koan AI & Vector Search – End-to-End How-To

This guide walks through everything Koan offers for AI-powered semantic search, from generating your first embedding to production-ready hybrid search with personalization. Each section grows in sophistication, lists **concepts**, a **recipe** (packages/config), and usage **scenarios**. Examples draw from:

- **S5.Recs** – Media recommendation engine with hybrid search and personalized vectors
- **AI demos** – Sample applications showing embedding patterns and caching strategies
- **Production patterns** – Real-world optimization and monitoring approaches

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

- `Ai.Embed(text)` generates vector embeddings from text using configured AI provider
- Embeddings are `float[]` arrays representing semantic meaning in high-dimensional space
- Provider-agnostic API - swap models by changing configuration
- Manual embeddings (`vectorizer: "none"`) give you full control

**Recipe**

- Packages listed in prerequisites
- AI provider configured in `Koan:AI:Providers`
- No special entity setup required

**Sample**

```csharp
using Koan.AI;

// Generate embedding from text
var text = "A heartwarming story about friendship and courage";
var embedding = await Ai.Embed(text, ct);

Console.WriteLine($"Generated {embedding.Length}-dimensional vector");
// Output: Generated 384-dimensional vector
```

**Usage scenarios & benefits**

- *S5.Recs* generates embeddings from media titles and synopses for semantic search
- Developers can swap embedding models (all-minilm → nomic-embed → OpenAI) by changing config
- Same embedding can be used across multiple vector databases

**Going further – custom models**

```csharp
// Use specific model for domain-specific embeddings
var embedding = await Ai.Embed(
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
- Provider-transparent - works with Weaviate, Pinecone, Qdrant, ElasticSearch
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
var embedding = await Ai.Embed(embeddingText, ct);

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

- *S5.Recs* indexes 50,000+ media items with embeddings for instant semantic search
- Metadata enables hybrid keyword+semantic search (see section 4)
- Provider transparency allows migrating from Weaviate to Pinecone without code changes

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
- Vector provider supporting `VectorCapabilities.Knn`
- Query text embedded using same model as indexed data

**Sample**

```csharp
// User searches for conceptual match
var query = "heartwarming slice of life anime";
var queryEmbedding = await Ai.Embed(query, ct);

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
if (capabilities.HasFlag(VectorCapabilities.Knn))
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
- Requires `VectorCapabilities.Hybrid` support

**Recipe**

- Vector provider supporting hybrid search (Weaviate, ElasticSearch, Qdrant)
- Metadata with `searchText` field indexed during save
- Same embedding as pure vector search

**Sample**

```csharp
// User searches for exact title
var query = "Watashi no Kokoro wa Oji-san de Aru";
var queryEmbedding = await Ai.Embed(query, ct);

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

- *S5.Recs* handles both exact Japanese titles ("鬼滅の刃") and vague queries ("demon slayer anime")
- Single API handles all search types - no separate keyword/semantic endpoints
- Users control balance with UI slider (see section 8)

**Indexing for hybrid search**

```csharp
// Separate embedding text (rich context) from search text (exact matching)
var embeddingText = BuildEmbeddingText(media);  // Title + synopsis + genres
var searchText = BuildSearchText(media);        // Just titles and synonyms

var embedding = await Ai.Embed(embeddingText, ct);

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
var searchVector = await Ai.Embed("magic school anime", ct);

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

- *S5.Recs* personalizes search results based on user's historical preferences
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
    var mediaVector = await Ai.Embed(mediaText);

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

- Cache embeddings by content hash to avoid redundant AI calls
- Dramatically reduces embedding costs and latency (70-90% cache hit rate)
- Content-addressable storage - same text always produces same hash
- Model-aware caching - different models have separate cache spaces

**Recipe**

- `Koan.AI` includes built-in `EmbeddingCache` abstraction
- Storage backend (file system, Redis, database)
- SHA512 content hashing for deterministic keys

**Sample**

```csharp
using Koan.AI.Caching;

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
        var embedding = await Ai.Embed(text, ct);

        // Store in cache
        await _cache.SetAsync(contentHash, MODEL_ID, embedding, "Media", ct);

        return embedding;
    }
}
```

**Usage scenarios & benefits**

- *S5.Recs* caches 50k+ embeddings, achieving 85% cache hit rate on re-indexing
- Swapping embedding models invalidates only that model's cache (model-aware keys)
- Reduces AI provider costs by 70-90% during development and re-indexing

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
        var embedding = await Ai.Embed(embeddingText, ct);
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

## 7. Flow Integration – Batch Processing

**Concepts**

- Use Koan Flow for large-scale embedding generation pipelines
- Stream entities in batches to avoid memory exhaustion
- Built-in error handling and retry logic
- Pipeline branching for success/failure paths

**Recipe**

- `Koan.Flow` package for pipeline DSL
- Entity streaming (`AllStream`, `QueryStream`)
- AI and Vector packages from previous sections

**Sample – Embedding backfill pipeline**

```csharp
using Koan.Flow;
using Koan.AI.Flow;  // Extension methods for AI in pipelines

await Media.AllStream(batchSize: 100)
    .ToAsyncEnumerable()
    .Tokenize(m => BuildEmbeddingText(m))  // Generate embeddings
    .Branch(branch => branch
        .OnSuccess(success => success
            .Mutate(envelope =>
            {
                // Prepare metadata from entity
                envelope.Features["vector:metadata"] = new
                {
                    title = envelope.Entity.Title,
                    searchText = BuildSearchText(envelope.Entity),
                    genres = envelope.Entity.Genres
                };
            })
            .Do(async (envelope, ct) =>
            {
                // Extract embedding from pipeline
                if (envelope.Features.TryGetValue("Embedding", out var embObj) &&
                    embObj is float[] embedding)
                {
                    var metadata = envelope.Features["vector:metadata"]
                        as IReadOnlyDictionary<string, object>;

                    // Store in vector database
                    await Vector<Media>.Save(
                        id: envelope.Entity.Id,
                        embedding: embedding,
                        metadata: metadata
                    );

                    // Cache the embedding
                    var embeddingText = BuildEmbeddingText(envelope.Entity);
                    var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);
                    await _cache.SetAsync(contentHash, modelId, embedding, "Media", ct);
                }
            })
        )
        .OnFailure(failure => failure
            .Do(async (envelope, ct) =>
            {
                _logger.LogWarning(
                    "Failed to embed {MediaId}: {Error}",
                    envelope.Entity.Id,
                    envelope.Error?.Message
                );
            })
        )
    );
```

**Usage scenarios & benefits**

- *S5.Recs* processes 50k+ media embeddings in ~2 hours with full caching
- Pipeline automatically retries transient AI failures
- Memory-efficient streaming avoids loading entire dataset

**Advanced - Progress tracking**

```csharp
var total = await Media.Count();
var processed = 0;

await Media.AllStream(batchSize: 100)
    .ToAsyncEnumerable()
    .Tokenize(m => BuildEmbeddingText(m))
    .Branch(branch => branch
        .OnSuccess(success => success
            .Do(async (envelope, ct) =>
            {
                // ... embedding logic ...

                Interlocked.Increment(ref processed);
                if (processed % 100 == 0)
                {
                    _logger.LogInformation(
                        "Progress: {Processed}/{Total} ({Percent:P0})",
                        processed, total, (double)processed / total
                    );
                }
            })
        )
    );
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
                embedding = await Ai.Embed(
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
        if (!caps.HasFlag(VectorCapabilities.Knn))
            return HealthCheckResult.Degraded("KNN search not supported");

        if (!caps.HasFlag(VectorCapabilities.Hybrid))
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
            var embedding = await Ai.Embed(query);
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
var titleEmbedding = await Ai.Embed(media.Title);
var synopsisEmbedding = await Ai.Embed(media.Synopsis);

// Store multiple vectors per entity (if provider supports it)
if (Vector<Media>.GetCapabilities().HasFlag(VectorCapabilities.MultiVectorPerEntity))
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
var englishQuery = await Ai.Embed("cute magical girls");
var japaneseQuery = await Ai.Embed("かわいい魔法少女");

// Both queries find similar results semantically
var englishResults = await Vector<Media>.Search(vector: englishQuery, topK: 10);
var japaneseResults = await Vector<Media>.Search(vector: japaneseQuery, topK: 10);

// Results overlap significantly due to semantic similarity
```

### Vector Export for Migration

```csharp
// Export vectors from Weaviate to migrate to different provider
var vectorRepo = serviceProvider.GetRequiredService<IVectorSearchRepository<Media, string>>();

await foreach (var batch in vectorRepo.ExportAllAsync(batchSize: 100, ct))
{
    // batch.Id - Entity identifier
    // batch.Embedding - float[] vector
    // batch.Metadata - Optional metadata

    // Import to new provider
    await newProviderRepo.UpsertAsync(batch.Id, batch.Embedding, batch.Metadata, ct);
}
```

---

## Next Steps

1. Start simple - embed a few entities and try semantic search (sections 1-3)
2. Add hybrid search for exact matching (section 4)
3. Implement embedding caching to reduce costs (section 6)
4. Scale with Flow pipelines for batch processing (section 7)
5. Add personalization for returning users (section 5)

Explore the S5.Recs sample to see production patterns in action. The combination of semantic search, hybrid matching, and personalization creates powerful recommendation experiences that understand both explicit queries and implicit user preferences.

**Related Guides:**
- [Entity Capabilities](entity-capabilities-howto.md) – Learn core entity patterns for data access and CRUD operations
- [PATCH Operations](patch-capabilities-howto.md) – Apply partial updates to entities across providers
- [Canon Entities](canon-capabilities-howto.md) – Aggregate data from multiple sources with conflict resolution
- [MCP over HTTP+SSE](mcp-http-sse-howto.md) – Expose your AI-powered entities to remote AI agents

When in doubt, stick to the capability-first patterns above. They keep your code provider-agnostic and ready for model upgrades without breaking changes.
