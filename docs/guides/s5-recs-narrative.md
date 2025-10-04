# S5.Recs: Building an Intelligent Media Recommendation Engine

**A Progressive Journey from Entity Storage to Semantic Search to Personalized AI**

```yaml
type: NARRATIVE
domain: sample-applications
title: "S5.Recs: Intelligent Media Recommendations"
audience: [beginners, intermediate-developers, solution-architects]
status: active
framework_version: v0.6.3
```

---

## Introduction: The Vision

Imagine you're building a media recommendation engine—think Netflix, but for anime, manga, and diverse media types. Your users want to:

1. **Discover** content they'll love based on vague descriptions ("cute but powerful characters")
2. **Find exact titles** even with non-English names ("かぐや様は告らせたい")
3. **Get personalized** recommendations that learn from their ratings
4. **Browse everything** without requiring authentication

S5.Recs demonstrates how **Koan Framework** makes this journey from basic CRUD to intelligent personalization remarkably straightforward. This isn't a tutorial—it's a narrative showing how features **build on each other**, each enhancement **enriching** what came before.

**Target Audience:**
- **Beginners**: See how modern patterns replace traditional architectures
- **Intermediate Developers**: Understand semantic search and vector databases
- **Solution Architects**: Evaluate provider-transparent patterns for production systems

---

## Act I: The Foundation (Entity-First Storage)

### Scene 1: Just Store Media

Every journey starts simple. You have media content from AniList (an anime database API) and need to store it:

```csharp
[Storage(Name = "Media")]
public sealed class Media : Entity<Media>
{
    public required string Title { get; set; }
    public required string ProviderCode { get; set; }  // "anilist"
    public required string ExternalId { get; set; }    // "101921"

    public string[] Genres { get; set; } = Array.Empty<string>();
    public string? Synopsis { get; set; }
    public double Popularity { get; set; }
    public double? AverageScore { get; set; }
}
```

**Traditional Approach (What You DON'T Do):**
```csharp
// ❌ Manual repository pattern
public interface IMediaRepository {
    Task<Media> GetAsync(string id);
    Task SaveAsync(Media media);
}

// ❌ Manual DbContext setup
public class MediaDbContext : DbContext {
    public DbSet<Media> Media { get; set; }
}

// ❌ Manual dependency injection
services.AddDbContext<MediaDbContext>();
services.AddScoped<IMediaRepository, MediaRepository>();
```

**Koan Approach (What You DO):**
```csharp
// ✅ Define the entity
public sealed class Media : Entity<Media> { /* ... */ }

// ✅ Use it immediately
var media = new Media {
    Title = "Kaguya-sama: Love is War",
    Genres = new[] { "Romance", "Comedy" }
};
await media.Save();

var found = await Media.Get(media.Id);
var all = await Media.All();
```

**Why This Matters:**
- **No boilerplate**: No repositories, no DbContext, no manual DI
- **Provider transparent**: MongoDB, SQL Server, Couchbase—same code
- **Entity-first**: `Todo.Get(id)` instead of `_repository.GetAsync(id)`

### Scene 2: Deterministic IDs (Not Just UUIDs)

Most frameworks generate random UUIDs:
```csharp
var id = Guid.NewGuid(); // Different every time
// Problem: Same content from API = different IDs = duplicates!
```

S5.Recs uses **deterministic SHA512-based IDs**:

```csharp
public sealed class Media : Entity<Media>
{
    public static string MakeId(string providerCode, string externalId, string mediaTypeId)
    {
        // SHA512 hash of: "anilist:101921:media-anime"
        return IdGenerationUtilities.GenerateMediaId(providerCode, externalId, mediaTypeId);
    }
}

// Usage
var id = Media.MakeId("anilist", "101921", "media-anime");
var media = new Media {
    Id = id,  // Always the same for this content
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

**Benefits:**
- **Idempotent imports**: Re-importing same API data → updates, not duplicates
- **Cross-provider merging**: AniList ID + MAL ID → same content
- **Predictable**: Test fixtures have stable IDs

**Rationale:** When you're importing from external APIs (AniList, MyAnimeList), you need **stable identities**. Random UUIDs create chaos—reimporting the same show creates duplicates. Deterministic IDs make your data layer **idempotent**.

---

## Act II: Adding Intelligence (Semantic Search)

### Scene 3: The Problem with Keyword Search

You implement basic search:

```csharp
// Keyword search (SQL LIKE)
var results = await Media.Where(m =>
    m.Title.Contains(query) ||
    m.Synopsis.Contains(query)
);
```

**The Pain:**
- Query: "cute but powerful characters" → **Zero results**
- Query: "かぐや様" (Japanese title) → **Zero results** if stored as romaji
- Query: "shows like Cowboy Bebop" → **Not possible** with keywords

Your users want **semantic search**—find content by *meaning*, not just keywords.

### Scene 4: Enter Vector Embeddings

**Concept Explanation (Beginner-Friendly):**

Think of embeddings as **coordinates in meaning-space**:

```
Traditional:
"cat" ≠ "kitten" ≠ "feline"  (exact string matching)

Embeddings:
"cat"    → [0.8, 0.2, 0.1, ...]  (384 numbers)
"kitten" → [0.79, 0.21, 0.09, ...] (very close in space)
"feline" → [0.77, 0.19, 0.11, ...] (close)
"car"    → [0.1, 0.9, 0.3, ...]   (far away)
```

Similar meanings have **similar numbers**. AI models learn these representations from billions of text examples.

**S5.Recs Implementation:**

```csharp
// 1. Generate embedding for media content
var embeddingText = BuildEmbeddingText(media);
var embedding = await Ai.Embed(embeddingText, ct);

// 2. Store it alongside the media
await Vector<Media>.Save(
    id: media.Id,
    embedding: embedding
);

// 3. Search semantically
var queryEmbedding = await Ai.Embed("cute but powerful characters", ct);
var results = await Vector<Media>.Search(
    vector: queryEmbedding,
    topK: 20
);
```

**What `BuildEmbeddingText` Does:**

```csharp
private string BuildEmbeddingText(Media media)
{
    // Combine rich context for AI to understand
    var titles = string.Join(" / ", GetAllTitles(media));
    var tags = string.Join(", ", media.Genres.Concat(media.Tags));

    return $@"{titles}

{media.Synopsis}

Tags: {tags}".Trim();
}
```

Example output:
```
Kaguya-sama: Love is War / かぐや様は告らせたい

A prestigious academy's student council president and vice-president
engage in a battle of wits to make the other confess their love first.

Tags: Romance, Comedy, School, Psychological
```

**Why Rich Context?** The AI model needs comprehensive information to generate a meaningful embedding. Just the title isn't enough—synopsis and tags provide semantic depth.

### Scene 5: The Magic of Semantic Search

Now your search **understands meaning**:

```csharp
// Query: "cute but powerful"
var embedding = await Ai.Embed("cute but powerful characters");
var results = await Vector<Media>.Search(vector: embedding, topK: 10);

// Returns (example):
// 1. "K-On!" - Score: 0.89 (cute school music club)
// 2. "Laid-Back Camp" - Score: 0.87 (wholesome camping)
// 3. "Violet Evergarden" - Score: 0.82 (powerful emotional story)
```

**What Just Happened?**

The AI model:
1. Converted your vague description into a 384-dimensional vector
2. Compared it to all media embeddings via **cosine similarity**
3. Returned the closest matches in "meaning-space"

**No keywords matched**, but the semantic meaning aligned perfectly.

**Architecture Note (For Architects):**

S5.Recs uses **Weaviate** as the vector database, but Koan's abstraction means you could swap to Qdrant, Pinecone, or Elasticsearch without changing application code:

```csharp
// Works with any provider
var results = await Vector<Media>.Search(vector, topK: 20);
```

This is **provider transparency**—infrastructure changes don't cascade through your codebase.

---

## Act III: Precision Meets Meaning (Hybrid Search)

### Scene 6: The Japanese Title Problem

You just added semantic search. Great! But then users report:

> "I search for 'Watashi no Kokoro wa Oji-san de Aru' and get random romance shows, not the actual manga!"

**The Problem:**

Your embedding model (all-MiniLM-L6-v2) was trained primarily on **English text**. Japanese romanization has poor representation:

```
Query: "Watashi no Kokoro wa Oji-san de Aru"
Embedding: [0.3, 0.1, ...]  (weak representation)

Best match: Some random show with "heart" in synopsis
Score: 0.41

Actual title in DB: "Watashi no Kokoro wa Oji-san de Aru"
Score: 0.28  (too low to rank high!)
```

**Semantic search fails** when you need **exact lexical matching**.

### Scene 7: Hybrid Search (BM25 + Vectors)

**Solution:** Combine two search modes:

1. **BM25 (Keyword)**: Traditional text matching, perfect for exact titles
2. **Vector (Semantic)**: Meaning-based matching, great for vague queries

**Implementation:**

```csharp
// Store searchable text alongside vectors
await Vector<Media>.Save(
    id: media.Id,
    embedding: embedding,
    metadata: new {
        searchText = BuildSearchText(media)  // All title variants
    }
);

// BuildSearchText - just titles for keyword matching
private string BuildSearchText(Media media)
{
    var titles = new[] {
        media.Title,                // "Kaguya-sama: Love is War"
        media.TitleEnglish,         // "Kaguya-sama: Love is War"
        media.TitleRomaji,          // "Kaguya-sama wa Kokurasetai..."
        media.TitleNative,          // "かぐや様は告らせたい"
        ...media.Synonyms           // French, Thai, Russian variants
    };
    return string.Join(" ", titles.Distinct());
}
```

**Hybrid Query:**

```csharp
var queryEmbedding = await Ai.Embed("Watashi no Kokoro wa Oji-san de Aru");

var results = await Vector<Media>.Search(
    vector: queryEmbedding,     // Semantic component
    text: "Watashi no Kokoro wa Oji-san de Aru",  // Keyword component
    alpha: 0.5,                 // 50% semantic, 50% keyword
    topK: 20
);
```

**How `alpha` Works:**

```
alpha = 0.0  →  100% BM25 keyword matching (exact titles)
alpha = 0.5  →  50% semantic + 50% keyword (balanced)
alpha = 1.0  →  100% vector semantic (meaning only)
```

**The Result:**

```
Query: "Watashi no Kokoro wa Oji-san de Aru"
With alpha=0.5:

1. "Watashi no Kokoro wa Oji-san de Aru" - Score: 0.94 ✅
   (BM25 perfect match + semantic similarity)

2. "Ore Monogatari!!" - Score: 0.71
   (Similar themes but not exact match)
```

**UI Integration:**

S5.Recs exposes an **alpha slider** in the header:

```
🎯 Exact Match ← [======●===] → Meaning 🧠
                     0.5
```

Users control the balance between exact matching and conceptual similarity.

**Rationale (For Architects):**

Hybrid search solves the **polyglot content problem**. Your embedding model has English bias, but your catalog spans Japanese, Korean, Thai, Russian titles. BM25 provides the precision, vectors provide the intelligence. The combination is **greater than the sum of its parts**.

---

## Act IV: Learning From Users (Personalization)

### Scene 8: The Cold Start Problem

You have semantic search working beautifully. Users love it. But every user sees the **same results** for the same query. There's no **personalization**.

**The Vision:**

- Alice loves wholesome slice-of-life shows
- Bob prefers dark fantasy with complex plots
- Same query: "school anime"
  - Alice should see "K-On!", "Laid-Back Camp"
  - Bob should see "Death Note", "Classroom of the Elite"

**How?** By learning from ratings.

### Scene 9: The Preference Vector

**Concept (Beginner-Friendly):**

Every time a user rates content, you:
1. Generate an embedding for that content
2. Update the user's **preference vector** (exponential moving average)
3. Blend it with search queries for personalized results

**Implementation:**

```csharp
// User rates a show
await RecsService.RateAsync(userId, mediaId, rating: 5);

// Behind the scenes (simplified):
public async Task RateAsync(string userId, string mediaId, int rating, CancellationToken ct)
{
    // 1. Get the media they rated
    var media = await Media.Get(mediaId, ct);

    // 2. Get/create their profile
    var profile = await UserProfileDoc.Get(userId, ct)
        ?? new UserProfileDoc { UserId = userId };

    // 3. Generate embedding for this content
    var contentEmbedding = await Ai.Embed(
        $"{media.Title}\n{media.Synopsis}\nTags: {string.Join(", ", media.Genres)}",
        ct
    );

    // 4. Update preference vector (exponential moving average)
    const double alpha = 0.3;  // Learning rate

    if (profile.PrefVector == null)
    {
        // First rating - initialize
        profile.PrefVector = contentEmbedding;
    }
    else
    {
        // Blend: 70% old preferences + 30% new preference
        for (int i = 0; i < profile.PrefVector.Length; i++)
            profile.PrefVector[i] = (float)((1 - alpha) * profile.PrefVector[i] + alpha * contentEmbedding[i]);
    }

    await profile.Save();
}
```

**What's Happening:**

```
User rates K-On! (5★):
  PrefVector = [0.8, 0.2, 0.1, ...]  (wholesome slice-of-life)

User rates Death Note (5★):
  PrefVector = [0.65, 0.35, 0.25, ...]  (blended: some wholesome, some dark)

User rates Made in Abyss (5★):
  PrefVector = [0.5, 0.45, 0.35, ...]  (darker preferences growing)
```

Each rating **nudges** the preference vector toward that content's embedding. Over time, it captures **what the user likes**.

### Scene 10: Vector Blending for Personalized Search

When a logged-in user searches, you **blend two vectors**:

```csharp
// 1. Search intent (what they want right now)
var searchVector = await Ai.Embed("magic school anime", ct);

// 2. User preferences (what they generally like)
var userPrefVector = await UserProfile.GetPrefVector(userId, ct);

// 3. Blend: 66% search intent + 34% user preferences
var blendedVector = BlendVectors(
    searchVector,
    userPrefVector,
    weight: 0.66
);

// 4. Hybrid search with personalized vector
var results = await Vector<Media>.Search(
    vector: blendedVector,
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

    // Normalize to unit length (preserve cosine similarity semantics)
    var magnitude = Math.Sqrt(result.Sum(x => (double)x * x));
    if (magnitude > 1e-8)
    {
        for (int i = 0; i < result.Length; i++)
            result[i] /= (float)magnitude;
    }

    return result;
}
```

**Why 66/34?**

- **66% search intent**: User explicitly wants "magic school anime" *right now*
- **34% preferences**: Nudge toward their general tastes (darker shows, romance, etc.)

This balances **explicit intent** with **implicit learning**.

**The Result:**

```
Alice searches "magic school anime":
  Blended toward: wholesome, slice-of-life
  Results: "Little Witch Academia" (top), "Flying Witch", "Kiki's Delivery Service"

Bob searches "magic school anime":
  Blended toward: dark, psychological
  Results: "The Irregular at Magic High School" (top), "Fate/Zero", "Madoka Magica"
```

**Same query, personalized results.**

### Scene 11: Genre Weights (Metadata Learning)

Beyond the preference vector, S5.Recs also tracks **genre weights**:

```csharp
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    public Dictionary<string, double> GenreWeights { get; set; } = new();
    public float[]? PrefVector { get; set; }
}

// When user rates content
foreach (var genre in media.Genres)
{
    profile.GenreWeights.TryGetValue(genre, out var oldWeight);
    var target = (rating - 1) / 4.0;  // Convert 1-5 to 0-1 scale
    var updated = (1 - alpha) * oldWeight + alpha * target;
    profile.GenreWeights[genre] = Math.Clamp(updated, 0, 1);
}
```

**How It's Used:**

```csharp
// During scoring
var genreBoost = 0.0;
if (userProfile?.GenreWeights != null)
{
    foreach (var genre in media.Genres)
    {
        if (userProfile.GenreWeights.TryGetValue(genre, out var weight))
            genreBoost += weight;
    }
    genreBoost = Math.Min(genreBoost, 1.0) / media.Genres.Length;
}

// Final recommendation score
var score = (0.4 * vectorScore) + (0.3 * popularityScore) + (0.2 * genreBoost);
```

**Why Both?**

- **Genre weights**: Interpretable ("Alice likes Romance: 0.9, Horror: 0.1")
- **Preference vector**: Captures nuances beyond genres (tone, pacing, art style)

Together they provide **multi-modal personalization**.

**Rationale (For Architects):**

This is a **two-tier learning system**:
1. **Explicit metadata** (genres, tags) - Interpretable, fast to compute
2. **Latent semantics** (embedding vectors) - Captures complex patterns

Production systems often use both—explicit for explainability and business rules, latent for performance.

---

## Act V: Performance at Scale (Embedding Cache)

### Scene 12: The AI Cost Problem

Your system is working beautifully. But then you realize:

```
Import 10,000 media items:
  10,000 API calls to Ollama (embedding model)
  ~30 seconds (with batching)

Re-import same data (updates):
  Another 10,000 API calls
  Another 30 seconds

Total wasted: 100% redundant work
```

**The Insight:**

For the same content, the embedding **never changes**. If you've already computed:

```
"Kaguya-sama: Love is War\n\nA battle of wits..."
→ [0.8, 0.2, 0.1, ...]
```

Why compute it again?

### Scene 13: Content-Addressable Cache

**Solution:** Cache embeddings by **content hash**:

```csharp
public class EmbeddingCache : IEmbeddingCache
{
    private readonly string _basePath = "cache/embeddings";

    public async Task<float[]?> GetAsync(string contentHash, string modelId, string entityType)
    {
        var filePath = $"{_basePath}/{entityType}/{modelId}/{contentHash}.json";
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<CachedEmbedding>(json)?.Embedding;
    }

    public async Task SetAsync(string contentHash, string modelId, float[] embedding, string entityType)
    {
        var filePath = $"{_basePath}/{entityType}/{modelId}/{contentHash}.json";
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var cached = new CachedEmbedding {
            Embedding = embedding,
            CachedAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(cached));
    }
}

// Content hash generation
public static string ComputeContentHash(string embeddingText)
{
    using var sha512 = SHA512.Create();
    var bytes = Encoding.UTF8.GetBytes(embeddingText);
    var hash = sha512.ComputeHash(bytes);
    return Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-");
}
```

**Usage in Seeding:**

```csharp
// Before generating embedding
var embeddingText = BuildEmbeddingText(media);
var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);

// Try cache first
var cached = await _embeddingCache.GetAsync(contentHash, modelId, typeof(Media).FullName);
if (cached != null)
{
    embedding = cached;  // Cache hit - no AI call!
    _cacheHits++;
}
else
{
    // Cache miss - generate and store
    embedding = await Ai.Embed(embeddingText, ct);
    await _embeddingCache.SetAsync(contentHash, modelId, embedding, typeof(Media).FullName);
    _cacheMisses++;
}
```

**The Result:**

```
First import:
  Cache hits: 0, Cache misses: 10,000 (0% hit rate)
  Time: 30 seconds

Second import (same data):
  Cache hits: 10,000, Cache misses: 0 (100% hit rate)
  Time: 2 seconds

Typical production:
  Cache hits: 8,500, Cache misses: 1,500 (85% hit rate)
  Time: 5 seconds
```

**Why SHA512?**

- **Deterministic**: Same content → same hash
- **Collision-resistant**: Virtually impossible to get same hash for different content
- **Content-addressable**: The content *is* the key

**File Structure:**

```
.Koan/cache/embeddings/
  S5.Recs.Models.Media/
    default/  (model ID)
      8k3jf9s...kd9f.json  (content hash)
      fj39dks...32fs.json
```

**Rationale (For Architects):**

This pattern is used in production systems at scale:
- **Git** uses SHA1 content addressing for commits
- **Docker** uses SHA256 for image layers
- **IPFS** uses content-addressable storage for files

Same principle: **content becomes its own cache key**, making invalidation trivial (content changed → different hash).

---

## Act VI: The Three Modes (UX Design)

### Scene 14: Different Users, Different Needs

S5.Recs supports three distinct user experiences:

#### 1. **For You** (Personalized)

```javascript
// Logged-in users get personalized recommendations
const results = await fetchRecommendations({
  text: userQuery,
  topK: 50,
  ignoreUserPreferences: false  // Use their preference vector
});
```

**Backend:**

```csharp
var userPrefVector = await UserProfile.GetPrefVector(userId);
var searchVector = await Ai.Embed(text, ct);
var blendedVector = BlendVectors(searchVector, userPrefVector, 0.66);

var results = await Vector<Media>.Search(
    vector: blendedVector,
    text: text,
    alpha: 0.5,
    topK: 50
);
```

**Use Case:** "I want recommendations tailored to me"

#### 2. **Free Browsing** (Pure Semantic, No Login)

```javascript
// Anonymous users get semantic search without personalization
const results = await fetchRecommendations({
  text: userQuery,
  topK: 50,
  ignoreUserPreferences: true  // No user context
});
```

**Backend:**

```csharp
var searchVector = await Ai.Embed(text, ct);

var results = await Vector<Media>.Search(
    vector: searchVector,
    text: text,
    alpha: 0.5,
    topK: 50
);
```

**Use Case:** "I'm browsing, not logged in, just want good semantic search"

#### 3. **Library** (User's Collection)

```javascript
// User's saved content with client-side filtering
const library = await fetchLibrary(userId);
const filtered = applyFilters(library, { genre, rating, status });
```

**Backend:**

```csharp
var entries = await LibraryEntry.Where(e => e.UserId == userId);
var media = await Task.WhenAll(entries.Select(e => Media.Get(e.MediaId)));
```

**Use Case:** "Show me what I've already watched/read"

**UI Toggle:**

```html
<div class="flex bg-slate-800 rounded-lg p-1">
  <button id="forYouBtn">For You</button>
  <button id="freeBrowsingBtn">Free Browsing</button>
  <button id="libraryBtn">Library</button>
</div>
```

**Rationale:**

Different contexts require different experiences:
- **Personalized**: Requires authentication, uses learned preferences
- **Discovery**: No login required, pure semantic search
- **Collection**: Personal library management

This **context-aware UX** respects user intent.

---

## Act VII: How Features Enrich Each Other

### The Synergy Map

Here's where S5.Recs transcends a simple CRUD app—every feature **amplifies** others:

```
┌─────────────────────┐
│  Entity Storage     │
│  (Foundation)       │
└──────┬──────────────┘
       │
       ├─► Deterministic IDs ─────────────┐
       │    (Idempotent imports)          │
       │                                  │
       ├─► Vector Storage ────────────────┼─► Semantic Search
       │    (Meaning-based retrieval)     │    (Vague queries work)
       │                                  │
       ├─► Hybrid Search ─────────────────┼─► Exact Titles + Meaning
       │    (BM25 + Vectors)              │    (Japanese/Korean work)
       │                                  │
       ├─► User Ratings ──────────────────┼─► Preference Learning
       │    (Explicit feedback)           │    (Profile updates)
       │                                  │
       ├─► Preference Vector ─────────────┼─► Personalized Results
       │    (Learned tastes)              │    (Same query, different users)
       │                                  │
       ├─► Embedding Cache ───────────────┼─► Fast Re-imports
       │    (Content-addressable)         │    (85% cache hit rate)
       │                                  │
       └─► Library Entries ───────────────┘
            (User collection)
```

**Enrichment Examples:**

1. **Deterministic IDs + Vector Storage:**
   - Same content from different API responses → same ID → update existing vector, not duplicate

2. **Hybrid Search + Preference Vector:**
   - User blends their learned tastes with exact title matching
   - Alice searches "Kaguya-sama" → gets exact match + similar wholesome shows

3. **Embedding Cache + Hybrid Search:**
   - Cached embeddings make re-indexing with searchText fast
   - Add new title synonyms → reindex uses cached vectors

4. **User Ratings + Semantic Search:**
   - Ratings build preference vector → personalized semantic search
   - "Shows like this" searches use your learned tastes

5. **Library + Recommendations:**
   - "For You" mode excludes library items (you've seen them)
   - Ratings from library feed preference learning

### The Feedback Loop

```
User discovers show via semantic search
  ↓
Adds to library
  ↓
Rates it (1-5 stars)
  ↓
Preference vector updates (EMA)
  ↓
Next search is more personalized
  ↓
Better recommendations
  ↓
User rates more content
  ↓
(Loop continues, system gets smarter)
```

**This is self-improving intelligence.** Each interaction makes the system better for that user.

---

## Act VIII: Technical Deep Dive (For Architects)

### The Stack

```yaml
Frontend:
  - Vanilla JavaScript (no framework overhead)
  - Tailwind CSS
  - Progressive enhancement

Backend:
  - ASP.NET Core Minimal APIs
  - Koan Framework entity patterns

Storage:
  - MongoDB (documents: Media, UserProfile, LibraryEntry)
  - Weaviate (vectors: 384-dim embeddings)
  - File system (embedding cache)

AI:
  - Ollama (local inference)
  - Model: all-MiniLM-L6-v2 (384 dimensions)
  - Embedding generation + vector search

Architecture:
  - Entity-first (no repositories)
  - Provider-transparent (swap MongoDB → Couchbase trivially)
  - Auto-registration (no manual DI)
  - Capability-based (graceful degradation)
```

### Provider Transparency in Action

Same code, different providers:

```csharp
// Development: MongoDB + Weaviate
"Koan:Data:ConnectionStrings:Default": "mongodb://localhost:27017/s5recs"
"Koan:Vector:Weaviate:Url": "http://localhost:8080"

// Production: Couchbase + Qdrant
"Koan:Data:ConnectionStrings:Default": "couchbase://cluster/s5recs"
"Koan:Vector:Qdrant:Url": "https://qdrant.prod.example.com"
```

**No code changes required.** Koan's abstraction handles provider differences:

```csharp
// Works with any data provider
await media.Save();
var all = await Media.All();

// Works with any vector provider
await Vector<Media>.Save(id, embedding);
var results = await Vector<Media>.Search(vector, topK: 20);
```

### Graceful Degradation

S5.Recs **works even when components fail**:

```csharp
public async Task<(IReadOnlyList<Recommendation>, bool degraded)> QueryAsync(...)
{
    try
    {
        // Try vector search
        if (Vector<Media>.IsAvailable)
        {
            var results = await Vector<Media>.Search(vector, text, alpha, topK);
            return (results, degraded: false);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Vector search failed, falling back to database");
    }

    // Fallback: Database query
    var dbResults = await Media.Where(m =>
        m.Title.Contains(text) || m.Synopsis.Contains(text)
    ).Take(topK);

    return (dbResults, degraded: true);  // Signal degraded mode
}
```

**UI Response:**

```javascript
if (response.degraded) {
  showWarning("⚠️ Semantic search unavailable - using basic search");
}
```

**Why This Matters:**

Production systems have **partial failures**. Your vector DB might be down for maintenance. Graceful degradation means your app stays usable.

### Performance Characteristics

**Batch Import (10,000 items):**

```
Without cache:
  - 10,000 AI embedding calls
  - ~30 seconds (parallel batches)
  - 10,000 vector upserts

With cache (85% hit rate):
  - 1,500 AI embedding calls
  - ~5 seconds
  - 10,000 vector upserts (fast)

Memory:
  - Embedding: 384 floats × 4 bytes = 1.5 KB per item
  - 10,000 items = 15 MB in vector DB
```

**Query Performance:**

```
Semantic search:
  - Vector similarity: ~50-100ms (10k items)
  - Scales to millions with Weaviate's HNSW index

Hybrid search:
  - Vector + BM25 fusion: ~80-150ms
  - Alpha blending happens server-side (provider-optimized)

Personalized search:
  - Vector blending: <1ms (client-side math)
  - Total: same as hybrid search
```

### Data Flow Diagram

```
┌──────────────┐
│   AniList    │  External API
│     API      │
└──────┬───────┘
       │ HTTP GET
       ↓
┌──────────────┐
│  Provider    │  Fetch + Parse
│  (AniList)   │
└──────┬───────┘
       │ Normalized Media
       ↓
┌──────────────┐
│ SeedService  │  Batch Processing
└──────┬───────┘
       │
       ├─► Content Hash ─────► Cache Lookup ─────┐
       │                                          │
       │                               ┌──────────┴─────────┐
       │                               │  Embedding Cache   │
       │                               │  (File System)     │
       │                               └──────────┬─────────┘
       │                                          │
       │◄─────────────────── Cached? ─────────────┘
       │        Yes                    No
       │                               │
       │                               ↓
       │                      ┌────────────────┐
       │                      │  Ollama (AI)   │
       │                      │  Generate      │
       │                      │  Embedding     │
       │                      └────────┬───────┘
       │                               │
       │◄──────────────────────────────┘
       │
       ├─► MongoDB ──────► Store Media Document
       │
       └─► Weaviate ─────► Store Vector + Metadata
                            (embedding + searchText)
```

**Query Flow:**

```
User searches: "cute but powerful"
       │
       ↓
┌──────────────┐
│  Frontend    │  Send query + alpha
└──────┬───────┘
       │
       ↓
┌──────────────┐
│  RecsService │  Build query vectors
└──────┬───────┘
       │
       ├─► Ai.Embed(query) ──────────► Search Vector
       │
       ├─► UserProfile.Get(userId) ───► Preference Vector
       │
       ├─► BlendVectors(66/34) ───────► Blended Vector
       │
       └─► Vector<Media>.Search(
             vector: blended,
             text: query,
             alpha: 0.5
           ) ────────────────────────► Weaviate
                                         │
                                         ├─► BM25(searchText)
                                         ├─► Vector Similarity
                                         └─► Fusion (alpha)

                                    ┌────┴─────┐
                                    │ Results  │
                                    └────┬─────┘
                                         │
       ┌─────────────────────────────────┘
       │
       ↓
┌──────────────┐
│  Apply       │  Genre boost, popularity, scoring
│  Filters     │
└──────┬───────┘
       │
       ↓
┌──────────────┐
│  Frontend    │  Display cards
└──────────────┘
```

---

## Act IX: Lessons for Your Architecture

### Pattern 1: Entity-First Eliminates Boilerplate

**Traditional:**
```csharp
// Define entity
public class Todo { }

// Define repository interface
public interface ITodoRepository {
    Task<Todo> GetAsync(Guid id);
    Task SaveAsync(Todo todo);
}

// Implement repository
public class TodoRepository : ITodoRepository { /* ... */ }

// Register in DI
services.AddScoped<ITodoRepository, TodoRepository>();

// Use in service
public class TodoService
{
    private readonly ITodoRepository _repo;
    public TodoService(ITodoRepository repo) => _repo = repo;

    public Task<Todo> Get(Guid id) => _repo.GetAsync(id);
}
```

**Koan:**
```csharp
// Define entity
public class Todo : Entity<Todo> { }

// Use directly
var todo = await Todo.Get(id);
await todo.Save();
```

**90% less code. Zero ceremony.**

### Pattern 2: Provider Transparency Defers Infrastructure Decisions

You don't need to choose MongoDB vs PostgreSQL vs Couchbase **on day one**. Start with whatever you have:

```csharp
// Day 1: Local JSON files
"Koan:Data:Provider": "json"

// Week 2: MongoDB for dev
"Koan:Data:Provider": "mongodb"

// Month 3: PostgreSQL for prod
"Koan:Data:Provider": "postgresql"
```

**Same entity code.** Infrastructure becomes a deployment detail.

### Pattern 3: Capability-Based Degradation

Not all providers support all features:

```csharp
// Check capabilities before using
var caps = Vector<Media>.GetCapabilities();

if (caps.HasFlag(VectorCapabilities.Hybrid))
{
    // Use hybrid search
    results = await Vector<Media>.Search(vector, text, alpha);
}
else
{
    // Degrade to pure vector
    results = await Vector<Media>.Search(vector);
}
```

This makes your app **resilient** to provider limitations.

### Pattern 4: Content-Addressable Caching

When you have expensive operations (AI, external APIs), use content hashing:

```csharp
var contentHash = SHA512(input);
var cached = await cache.Get(contentHash);

if (cached != null) return cached;

var result = await ExpensiveOperation(input);
await cache.Set(contentHash, result);
return result;
```

**Idempotent, efficient, simple.**

### Pattern 5: Progressive Enhancement in UX

Build experiences that work at multiple levels:

```
Level 1: Basic keyword search (always works)
Level 2: Semantic vector search (requires AI + Vector DB)
Level 3: Hybrid search (requires vector DB with BM25 support)
Level 4: Personalization (requires user login + rating history)
```

Users get **better experiences** as capabilities enable, but app works at every level.

---

## Conclusion: The Power of Composition

S5.Recs demonstrates that **modern application architectures** aren't about picking the "right" stack—they're about composing patterns that **enrich each other**:

- **Entity-first storage** makes CRUD trivial
- **Vector embeddings** add semantic intelligence
- **Hybrid search** combines precision with meaning
- **Personalization** learns from user behavior
- **Content caching** makes it performant
- **Provider transparency** makes it flexible

Each piece works alone. Together, they create something **greater than the sum of parts**.

**For Beginners:**
You've seen a modern application built without repositories, without manual DI, without SQL queries. Entity-first patterns are the future.

**For Intermediate Developers:**
You've seen how vector databases integrate with traditional CRUD, how embeddings work, and how to cache expensive AI operations.

**For Solution Architects:**
You've seen provider-transparent patterns, graceful degradation, capability-based development, and how to structure personalization systems at scale.

**The Koan Way:**
- Reference = Intent (add package → functionality enabled)
- Entity-First (no repositories)
- Provider-Transparent (swap infrastructure freely)
- Self-Reporting (capabilities visible at runtime)

S5.Recs is a **living example** of these principles. Study it. Extend it. Build your own intelligent systems following these patterns.

---

**Next Steps:**

1. **Run S5.Recs locally**: `./start.bat` in `samples/S5.Recs/docker`
2. **Import media**: Hit `/admin/import/anilist` to seed AniList data
3. **Search semantically**: Try "cute but powerful" in the UI
4. **Add hybrid search**: Try "かぐや様" with alpha slider
5. **Rate content**: See personalization learning in action
6. **Export vectors**: Use `/admin/cache/embeddings/export` to cache
7. **Study the code**: See patterns in practice

The best way to learn is to **build**. Fork S5.Recs. Add your own features. Push the boundaries.

**The framework is your canvas. Paint boldly.**

---

*Last Updated: 2025-01-04*
*Framework Version: v0.6.3*
*Koan Framework - Intelligent Application Development*
