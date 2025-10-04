# ADR-0051: Vector Hybrid Search with BM25 Fusion

**Status:** Accepted
**Date:** 2025-10-03
**Context:** S5.Recs omnisearch enhancement for exact title matching
**Decision Makers:** Architecture review
**Affected Components:** Koan.Data.Vector, Koan.Data.Vector.Connector.Weaviate, S5.Recs

---

## Context and Problem Statement

The S5.Recs application uses pure vector similarity search for media recommendations. While this works well for semantic queries like "cute but powerful", it struggles with exact title matching:

**Problem:** Searching for "Watashi no Kokoro wa Oji-san de Aru" (exact manga title) fails to return the correct result because:
1. Embedding models (all-minilm-L6-v2) are trained primarily on English
2. Japanese romanization has poor representation in embedding space
3. Pure cosine similarity doesn't preserve lexical fidelity for exact strings

**User Experience Impact:**
- Users searching for known titles get irrelevant semantic matches
- Japanese, Korean, and other non-English titles are particularly affected
- No way to balance semantic understanding vs exact keyword matching

**Additional Requirement:** Logged-in users have personalized preference vectors that should be blended with search intent.

---

## Decision Drivers

1. **Provider Transparency** - Solution must align with Koan's multi-provider architecture
2. **Existing Infrastructure** - Leverage current Weaviate deployment (already in use)
3. **Developer Experience** - Single, intuitive API for both simple and hybrid search
4. **User Control** - Allow users to tune semantic vs keyword balance
5. **Personalization** - Support blending user preference vectors with search intent
6. **Industry Standard** - Follow proven hybrid search patterns (BM25 + vector)

---

## Considered Options

### Option 1: Client-Side Text Pre-Filter
Add text similarity matching before vector search.

**Pros:**
- No infrastructure changes
- Works with any vector provider

**Cons:**
- Requires loading all entities into memory (doesn't scale)
- Manual merging logic needed
- No provider optimization

### Option 2: Dual Query with RRF Merge
Run separate BM25 and vector queries, merge with Reciprocal Rank Fusion.

**Pros:**
- Clean separation of concerns
- Tunable fusion weights

**Cons:**
- 2x query cost
- Manual merge implementation
- Not supported by vector-only providers

### Option 3: Framework-Level Hybrid Capability ⭐ **SELECTED**
Expose hybrid search as a first-class capability via `VectorCapabilities.Hybrid`.

**Pros:**
- Provider-native optimization (Weaviate, ElasticSearch, Qdrant support it)
- Single query with automatic fusion
- Framework transparency (graceful degradation if unsupported)
- Follows existing capability pattern (QueryCaps, VectorCapabilities)
- `VectorCapabilities.Hybrid` already exists in enum (anticipated design)

**Cons:**
- Requires provider implementation
- Not all providers support hybrid (e.g., Chroma)

---

## Decision

**Implement hybrid search as a framework capability** following Koan's provider transparency principles.

### API Design

```csharp
// Unified search interface - single method, optional parameters
public static Task<VectorQueryResult<string>> Search(
    float[] vector,           // Semantic component (always required)
    string? text = null,      // BM25 component (optional, enables hybrid)
    double? alpha = null,     // 0.0=keyword, 1.0=semantic, 0.5=balanced
    int? topK = null,
    object? filter = null,
    CancellationToken ct = default
)
```

**Usage Examples:**

```csharp
// Simple vector search (pure semantic)
var results = await Vector<Media>.Search(
    vector: embedding,
    topK: 10
);

// Hybrid search (semantic + keyword)
var results = await Vector<Media>.Search(
    vector: embedding,
    text: "Watashi no Kokoro wa Oji-san de Aru",
    alpha: 0.5,  // 50/50 blend
    topK: 10
);

// Personalized hybrid (user prefs + search intent)
var userPrefVector = await UserProfile.GetPrefVector(userId);
var searchVector = await Ai.Embed(searchText);
var blended = BlendVectors(searchVector, userPrefVector, 0.66); // 66% intent, 34% prefs

var results = await Vector<Media>.Search(
    vector: blended,
    text: searchText,
    alpha: userAlpha,  // From UI slider
    topK: 50
);
```

### Internal Transport

```csharp
// VectorQueryOptions (internal)
public sealed record VectorQueryOptions(
    float[] Query,                  // Vector embedding
    int? TopK = null,
    object? Filter = null,
    string? SearchText = null,      // NEW: Enables hybrid when provided
    double? Alpha = null,           // NEW: Semantic vs keyword weight
    // ... existing fields
);
```

### Capability Detection

```csharp
// Weaviate adapter
public VectorCapabilities Capabilities =>
    VectorCapabilities.Knn |
    VectorCapabilities.Filters |
    VectorCapabilities.Hybrid;  // NEW

// Framework usage
if (Vector<Media>.GetCapabilities().HasFlag(VectorCapabilities.Hybrid)) {
    // Native hybrid search
} else {
    // Graceful degradation (pure vector, log warning)
}
```

---

## Implementation Details

### Phase 1: Framework Core

**VectorQueryOptions Extension:**
```csharp
public sealed record VectorQueryOptions(
    float[] Query,
    int? TopK = null,
    string? ContinuationToken = null,
    object? Filter = null,
    TimeSpan? Timeout = null,
    string? VectorName = null,
    string? SearchText = null,      // NEW
    double? Alpha = null            // NEW
);
```

**Vector<T> API:**
```csharp
public static Task<VectorQueryResult<string>> Search(
    float[] vector,
    string? text = null,
    double? alpha = null,
    int? topK = null,
    object? filter = null,
    CancellationToken ct = default)
{
    return VectorData<TEntity>.SearchAsync(new VectorQueryOptions(
        Query: vector,
        SearchText: text,
        Alpha: alpha,
        TopK: topK,
        Filter: filter
    ), ct);
}
```

### Phase 2: Weaviate Adapter

**Schema Enhancement:**
```csharp
properties = new object[]
{
    new { name = "docId", dataType = new[] { "text" } },
    new {
        name = "searchText",
        dataType = new[] { "text" },
        indexSearchable = true,      // Enable BM25 indexing
        tokenization = "word"
    }
}
```

**Hybrid Search Implementation:**
```csharp
if (!string.IsNullOrWhiteSpace(options.SearchText))
{
    // Hybrid mode: vector + BM25
    var alpha = options.Alpha ?? 0.5;
    var escapedText = options.SearchText.Replace("\"", "\\\"");
    var vectorStr = string.Join(",", options.Query.Select(f => f.ToString()));

    searchClause = $@"hybrid: {{
        query: ""{escapedText}"",
        vector: [{vectorStr}],
        alpha: {alpha:F2}
    }}";
}
else
{
    // Pure vector mode
    searchClause = $"nearVector: {{ vector: [{vectorStr}] }}";
}
```

**UpsertAsync Enhancement:**
```csharp
if (metadata is IReadOnlyDictionary<string, object> metaDict &&
    metaDict.TryGetValue("searchText", out var searchText))
{
    properties["searchText"] = searchText;
}
```

### Phase 3: S5.Recs Integration

**Indexing Changes:**
```csharp
// SeedService.cs - Add searchText to metadata
var vectorMetadata = new Dictionary<string, object>
{
    ["title"] = media.Title,
    ["genres"] = media.Genres,
    ["popularity"] = media.Popularity,
    ["searchText"] = BuildSearchText(media)  // NEW
};

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

**Query Changes:**
```csharp
// RecsService.cs - Vector blending + hybrid search
const double SEARCH_INTENT_WEIGHT = 0.66;  // 66% search, 34% user prefs

var searchVector = !string.IsNullOrWhiteSpace(text)
    ? await Ai.Embed(text, ct)
    : null;

var userPrefVector = !string.IsNullOrWhiteSpace(userId)
    ? (await UserProfileDoc.Get(userId, ct))?.PrefVector
    : null;

float[] queryVector;
if (userPrefVector != null && searchVector != null)
{
    queryVector = BlendVectors(searchVector, userPrefVector, SEARCH_INTENT_WEIGHT);
}
else
{
    queryVector = searchVector ?? userPrefVector ?? fallback;
}

var results = await Vector<Media>.Search(
    vector: queryVector,
    text: text,
    alpha: alpha ?? 0.5,
    topK: topK
);
```

**UI Enhancement:**
```html
<div class="alpha-slider">
  <label>🎯 Exact Match ← → Meaning 🧠</label>
  <input type="range" v-model.number="query.alpha" min="0" max="1" step="0.1">
  <small>{{ alphaHint }}</small>
</div>
```

---

## Consequences

### Positive

✅ **Solves exact title matching** - "Watashi no Kokoro wa Oji-san de Aru" returns correct result
✅ **User control** - Alpha slider lets users tune semantic vs keyword balance
✅ **Personalization** - Blends user preferences with search intent (66/34 split)
✅ **Framework aligned** - Follows existing capability pattern
✅ **Provider transparent** - Graceful degradation for non-hybrid providers
✅ **Single API** - No new methods, just optional parameters
✅ **Zero breaking changes** - `text = null` behaves like pure vector search
✅ **Industry standard** - BM25 + vector hybrid is proven pattern

### Negative

⚠️ **Re-indexing required** - Need to add `searchText` to existing vectors (~1-2 hours)
⚠️ **Provider-specific** - Not all providers support hybrid (Chroma, basic Pinecone)
⚠️ **Storage overhead** - `searchText` field adds ~100-500 bytes per item
⚠️ **Complexity** - Two dimensions (alpha + personalization blend) to explain

### Neutral

➡️ **Embedding DX minimal change** - Just add `searchText` to metadata
➡️ **No hybrid embeddings** - Still one embedding per item, BM25 uses text field
➡️ **Weaviate-first** - Implementation optimized for Weaviate, adaptable to others

---

## Validation and Metrics

### Success Criteria

1. **Exact title search** - "Watashi no Kokoro wa Oji-san de Aru" returns correct manga in top 3 results
2. **Semantic search preserved** - "cute but powerful" still returns conceptually similar results
3. **Personalization works** - Logged-in users get results biased toward their preferences
4. **User satisfaction** - Alpha slider usage indicates users understand and value control

### Metrics to Track

- **Search quality:** Precision@10 for exact title queries (target: >90%)
- **Alpha distribution:** Most common alpha values (validates default 0.5 choice)
- **Hybrid usage:** % of searches with `text != null` and alpha tuning
- **Performance:** p95 query latency (target: <200ms, same as pure vector)

---

## Alternatives Considered (Rejected)

### Separate SearchVector and SearchHybrid Methods
**Rejected:** Violates Koan's simplicity principle. Single method with optional params is cleaner.

### Auto-Detection of Query Intent
**Rejected:** Heuristics are brittle. Explicit user control (alpha slider) is better UX.

### Client-Side Hybrid Fallback
**Deferred:** If needed, implement in Phase 2 when non-Weaviate providers are added.

### Dual Personalization Slider
**Rejected:** 66/34 search intent vs user prefs is a good default. Can add slider later if users request it.

---

## References

- Weaviate Hybrid Search: https://weaviate.io/developers/weaviate/search/hybrid
- BM25 Algorithm: https://en.wikipedia.org/wiki/Okapi_BM25
- Reciprocal Rank Fusion: https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf
- Koan VectorCapabilities: `src/Koan.Data.Vector.Abstractions/VectorCapabilities.cs`
- Koan QueryCapabilities Pattern: `src/Koan.Data.Core/Data.cs:22`

---

**Last Updated:** 2025-10-03
**Implementation Target:** Sprint 2025-Q4
**Status:** Ready for implementation
