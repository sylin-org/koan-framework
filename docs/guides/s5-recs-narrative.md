---
type: GUIDE
domain: platform
title: "S5.Recs: Intelligent Media Recommendations"
audience: [developers, architects]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  status: not-yet-tested
  scope: docs/guides/s5-recs-narrative.md
---

# S5.Recs: Building an Intelligent Media Recommendation Engine

**A Progressive Journey from Entity Storage to Semantic Search to Personalized AI**

---

## Introduction: The Vision

Picture a media recommendation engine for anime, manga, and diverse content. Users arrive with different needs: vague descriptions like "cute but powerful characters," exact non-English titles like "かぐや様は告らせたい," or personal taste learned from their rating history. Some browse anonymously; others expect personalized results.

S5.Recs shows how these capabilities build on each other within Koan Framework. Start with simple entity storage, add semantic search, layer in personalization. Each feature enriches what came before. The progression is deliberate.

This guide walks through the architecture as it evolved, revealing design decisions and their consequences. Whether you're evaluating modern patterns, exploring vector databases, or assessing provider-transparent architectures, the code speaks for itself.

---

## Foundation: Entity-First Storage

### Storing Media Content

Media content arrives from AniList's API. The first requirement: store it.

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

**Using the Model:**

```csharp
var media = new Media {
    Title = "Kaguya-sama: Love is War",
    Genres = new[] { "Romance", "Comedy" }
};
await media.Save();

var found = await Media.Get(media.Id);
var all = await Media.All();
```

Look at that `media.Save()` call. One line. Behind it, the framework handles persistence, provider selection, and error handling. No repositories, no DbContext, no dependency injection setup.

Compare this to traditional Entity Framework patterns:

Traditional EF requires ceremony:

```csharp
// Define repository
public interface IMediaRepository {
    Task<Media> GetAsync(string id);
    Task SaveAsync(Media media);
}

// Implement repository
public class MediaRepository : IMediaRepository { /* ... */ }

// Configure DI
services.AddDbContext<MediaDbContext>();
services.AddScoped<IMediaRepository, MediaRepository>();

// Use in services
public class MediaService(IMediaRepository repo) {
    public async Task Save(Media m) => await repo.SaveAsync(m);
}
```

EF's repository pattern offers explicit control and clear boundaries. For S5.Recs, Koan's `Entity<T>` trades that ceremony for velocity. The same provider transparency exists (swap MongoDB for PostgreSQL through configuration), but the abstraction sits in the framework rather than in your codebase. ActiveRecord-style methods on entities eliminate the setup ritual while maintaining testability and storage agnosticism.

The data layer here is straightforward. The simpler path serves better.

### The Identity Problem

Import 10,000 shows from AniList. Everything works. A week passes. AniList updates some metadata. Run the import again. Now you have 20,000 shows in the database. Half are duplicates.

The problem: guarantee that the same anime from the same provider generates the same ID every time.

**Random UUIDs (Traditional)**

```csharp
var media = new Media {
    Id = Guid.NewGuid(),  // Different every run
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

Every import creates new records. Deduplication requires comparing titles, checking external IDs, merging records. Fragile.

**Provider's External ID**

```csharp
var media = new Media {
    Id = "101921",  // AniList's ID
    ProviderCode = "anilist"
};
```

Collisions appear when MyAnimeList arrives (also has ID "101921" for different content). Composite keys or prefixing schemes add complexity.

**Composite Key String**

```csharp
var media = new Media {
    Id = "anilist:101921",  // Provider + ID
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

Better, but incomplete. What about media type? "anilist:101921:anime" versus "anilist:101921:manga"? Future schema changes?

**Deterministic Hashing**

```csharp
public sealed class Media : Entity<Media>
{
    public static string MakeId(string providerCode, string externalId, string mediaTypeId)
    {
        // SHA512 hash of: "anilist:101921:media-anime"
        // Same inputs always produce the same hash
        return IdGenerationUtilities.GenerateMediaId(providerCode, externalId, mediaTypeId);
    }
}

// Usage
var id = Media.MakeId("anilist", "101921", "media-anime");
// → "k8j3nf92...sd8f" (64-char SHA512 hash)

var media = new Media {
    Id = id,
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

SHA512 hashing provides five guarantees: determinism (same inputs produce the same hash every time), collision resistance (cryptographically impossible for different content to share a hash), opacity (internal structure stays hidden), future-proofing (add fields to the hash input without breaking existing IDs), and fixed length (unlike composite strings that grow unbounded).

**Result:**

```csharp
// First import
var id1 = Media.MakeId("anilist", "101921", "media-anime");
await new Media { Id = id1, Title = "Kaguya-sama" }.Save();

// Second import (weeks later, same data)
var id2 = Media.MakeId("anilist", "101921", "media-anime");
await new Media { Id = id2, Title = "Kaguya-sama: Love is War" }.Save();

// id1 == id2 → Update existing record, no duplicate
```

Cross-provider scenarios benefit too:

```csharp
// AniList record
var anilistId = Media.MakeId("anilist", "101921", "media-anime");

// MyAnimeList record (different external ID)
var malId = Media.MakeId("mal", "37999", "media-anime");

// Different IDs (correct behavior)
// Separate MediaLink entity marks them as same content
// Stable, predictable identities enable this
```

The trade-off: hashing sacrifices human readability. You can't look at an ID and know what it represents. In return, you gain idempotence, critical for data pipelines importing from external APIs.

---

## Adding Intelligence: Semantic Search

### Keyword Search Limits

Basic search works for exact matches:

```csharp
// Keyword search (SQL LIKE)
var results = await Media.Where(m =>
    m.Title.Contains(query) ||
    m.Synopsis.Contains(query)
);
```

Query "cute but powerful characters" returns zero results. Query "かぐや様" (Japanese title) returns nothing if stored as romaji. Query "shows like Cowboy Bebop" isn't possible with keywords alone.

Users want semantic search. Find content by meaning, not just exact text matches.

### Solving for Meaning

"Shows like Cowboy Bebop" should find tonally similar content. Keyword search can't capture this. Semantic understanding requires a different approach.

**Synonym Dictionaries**

```csharp
var synonyms = new Dictionary<string, string[]> {
    { "cat", new[] { "kitten", "feline", "kitty" } },
    { "sad", new[] { "melancholic", "depressing", "tragic" } }
};

// Expand query
var expandedQuery = ExpandWithSynonyms(query, synonyms);
var results = await Media.Where(m => m.Synopsis.Contains(expandedQuery));
```

Manual curation becomes impossible at scale. Language barriers remain (Japanese/Korean titles need separate dictionaries). Context disappears ("bank" means financial institution or river edge?). Matching stays binary (synonym matches or doesn't).

**TF-IDF + Latent Semantic Analysis**

Classic information retrieval from the 1990s:

```csharp
// Build term-document matrix (word frequency table)
var tfidf = BuildTfIdfMatrix(allMedia);

// Perform SVD to reduce dimensions
// Find patterns in which words appear together
var lsa = PerformSVD(tfidf, dimensions: 100);

// Project query into reduced space
var queryVector = TransformQuery(query, lsa);
var results = CosineSimilarity(queryVector, allDocuments);
```

Simplified example:
```
Documents:
  Doc1: "cat kitten meow"
  Doc2: "dog puppy bark"
  Doc3: "cat purr feline"

TF-IDF Matrix:
          Doc1  Doc2  Doc3
  cat     0.8   0.0   0.7
  kitten  0.6   0.0   0.0
  dog     0.0   0.9   0.0
  puppy   0.0   0.5   0.0

LSA learns: "cat, kitten, feline" cluster together
            "dog, puppy, bark" cluster together
```

Full corpus preprocessing becomes expensive on updates (need all documents upfront). Semantic understanding stays shallow (word co-occurrence, not actual meaning). Cross-lingual connections fail ("cat" and "猫" stay disconnected). Vocabulary fixes at training time (new words can't be searched).

**Pre-Trained Neural Embeddings**

AI models trained on billions of text examples convert text into coordinates in meaning-space:

```csharp
// Convert text to 384-dimensional vector
var embedding = await Ai.Embed("cute but powerful characters", ct);
// Result: [0.8, 0.2, 0.1, ...] (384 numbers representing meaning)
```

Traditional approaches treat words as distinct:
```
"cat" ≠ "kitten" ≠ "feline"  (exact matching or manual synonyms)
```

Neural embeddings learned from billions of examples place similar meanings close together:
```
"cat"    → [0.8, 0.2, 0.1, ...]
"kitten" → [0.79, 0.21, 0.09, ...]  (nearby in space)
"feline" → [0.77, 0.19, 0.11, ...]  (nearby)
"car"    → [0.1, 0.9, 0.3, ...]     (distant)
```

Distance in vector space equals semantic similarity.

**all-MiniLM-L6-v2 Model**

S5.Recs uses all-MiniLM-L6-v2 for four properties: 384 dimensions (compact yet powerful), multilingual support (English, Japanese romanization, basic cross-lingual understanding), fast inference (~10ms per embedding on CPU), and pre-trained weights (no training required).

**Implementation:**

```csharp
// 1. Generate embedding for media content
var embeddingText = BuildEmbeddingText(media);
var embedding = await Ai.Embed(embeddingText, ct);

// 2. Store alongside media
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

**Rich Embedding Context**

Titles alone provide insufficient semantic information. The model needs comprehensive context:

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

This approach delivers five advantages: zero manual work (no synonym curation or corpus preprocessing), contextual understanding ("bank" near "money" versus "river" produces different embeddings), multilingual handling (romanized Japanese, basic cross-language similarity), continuous similarity (0.89 match versus 0.45 match, not binary yes/no), and effortless scaling (new content just needs embedding computation).

Trade-offs: requires external AI model (Ollama in S5.Recs), 384-dimensional vectors consume more storage than keywords, slightly slower than exact string matching.

### Semantic Search in Action

Search now understands meaning:

```csharp
// Query: "cute but powerful"
var embedding = await Ai.Embed("cute but powerful characters");
var results = await Vector<Media>.Search(vector: embedding, topK: 10);

// Returns (example):
// 1. "K-On!" - Score: 0.89 (cute school music club)
// 2. "Laid-Back Camp" - Score: 0.87 (wholesome camping)
// 3. "Violet Evergarden" - Score: 0.82 (powerful emotional story)
```

The AI model converted the vague description into a 384-dimensional vector, compared it to all media embeddings via cosine similarity (measuring vector alignment), and returned the closest matches in meaning-space.

Cosine similarity measures directional alignment:
```
Vector A: [0.8, 0.2]  ↗
Vector B: [0.9, 0.3]  ↗  (same direction → high similarity: 0.98)

Vector A: [0.8, 0.2]  ↗
Vector C: [0.1, 0.9]  ↑  (different direction → low similarity: 0.32)

In 384 dimensions: aligned vectors equal similar meaning
```

No keywords matched. The semantic meaning aligned.

**Provider Transparency**

S5.Recs uses Weaviate as the vector database. Koan's abstraction allows swapping to Qdrant, Pinecone, or Elasticsearch without changing application code:

```csharp
// Works with any provider
var results = await Vector<Media>.Search(vector, topK: 20);
```

Infrastructure changes don't cascade through the codebase.

---

## Precision Meets Meaning: Hybrid Search

### The Japanese Title Problem

Semantic search works. Then users report a problem:

> "I search for 'Watashi no Kokoro wa Oji-san de Aru' and get random romance shows, not the actual manga."

The embedding model (all-MiniLM-L6-v2) trained primarily on English text. Japanese romanization has poor representation:

```
Query: "Watashi no Kokoro wa Oji-san de Aru"
Embedding: [0.3, 0.1, ...]  (weak representation)

Best match: Some random show with "heart" in synopsis
Score: 0.41

Actual title in DB: "Watashi no Kokoro wa Oji-san de Aru"
Score: 0.28  (too low to rank high)
```

Semantic search fails when exact lexical matching matters.

### Solving the Precision Problem

Both semantic understanding and exact title matching are required. Combining them takes strategy.

**Dual Queries with Client-Side Merge**

```csharp
// Run both searches
var semanticResults = await Vector<Media>.Search(vector, topK: 50);
var keywordResults = await Media.Where(m => m.Title.Contains(query)).Take(50);

// Merge manually
var combined = semanticResults.Concat(keywordResults).Distinct().Take(20);
```

Two separate queries add latency. Manual merge logic complicates ranking. Fetching 50+50 to return 20 doesn't scale. Client-side sorting loses provider optimizations.

**Pre-Filter with Keywords, Then Vector Search**

```csharp
// First narrow down by keyword
var candidates = await Media.Where(m =>
    m.Title.Contains(query) ||
    m.Synopsis.Contains(query)
).Take(1000);

// Then vector search within candidates
var candidateIds = candidates.Select(c => c.Id);
var results = await Vector<Media>.Search(
    vector,
    filter: new { id_in = candidateIds },
    topK: 20
);
```

Keyword pre-filter might exclude good semantic matches. Still requires two queries. Filter translation complexity varies across providers.

**Provider-Native Hybrid Search**

Vector databases support hybrid search, combining BM25 keyword matching with vector similarity in a single query. The provider internally fuses scores using algorithms like Reciprocal Rank Fusion rather than merging results in application code.

Reciprocal Rank Fusion example:
```
BM25 results: [DocA, DocB, DocC]  (ranked 1, 2, 3)
Vector results: [DocC, DocA, DocD] (ranked 1, 2, 3)

RRF score = 1/(rank+60) for each list, then sum:
  DocA: 1/(1+60) + 1/(2+60) = 0.0325
  DocC: 1/(3+60) + 1/(1+60) = 0.0323

Final ranking: [DocA, DocC, ...]
```

BM25 (Best Matching 25) is a keyword ranking algorithm from the 1970s-90s, still used in Elasticsearch, Weaviate, and search engines:

```
Query: "magic school"

Document A: "magic school magic school"
  Term frequency: "magic" 2x, "school" 2x
  Score: HIGH (exact matches, repeated terms)

Document B: "a story about a magical academy for wizards"
  Term frequency: "magic" 0x, "school" 0x
  Score: LOW (no exact matches, semantically related but missed)

BM25 = Keyword matching + Term frequency + Document length normalization
```

Document length normalization prevents long documents from scoring unfairly high simply because they contain more words.

**Hybrid Search with Alpha Blending**

```csharp
// Store searchable text alongside vectors
await Vector<Media>.Save(
    id: media.Id,
    embedding: embedding,
    metadata: new {
        searchText = BuildSearchText(media)  // All title variants
    }
);

// BuildSearchText: titles only for keyword matching
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

Alpha controls the blend:
```
alpha = 0.0  →  100% BM25 keyword matching (exact titles)
alpha = 0.5  →  50% semantic + 50% keyword (balanced)
alpha = 1.0  →  100% vector semantic (meaning only)
```

**Result:**

```
Query: "Watashi no Kokoro wa Oji-san de Aru"
With alpha=0.5:

1. "Watashi no Kokoro wa Oji-san de Aru" - Score: 0.94 ✅
   (BM25 perfect match + semantic similarity)

2. "Ore Monogatari!!" - Score: 0.71
   (Similar themes but not exact match)
```

**UI Integration**

S5.Recs exposes an alpha slider in the header:

```
🎯 Exact Match ← [======●===] → Meaning 🧠
                     0.5
```

Users control the balance between exact matching and conceptual similarity.

**Polyglot Content**

Hybrid search solves a multilingual problem. The embedding model has English bias, but the catalog spans Japanese, Korean, Thai, Russian titles. BM25 provides precision, vectors provide intelligence. The combination exceeds either alone.

---

## Learning From Users: Personalization

### The Cold Start Problem

Semantic search works. Users love it. But every user sees the same results for the same query. No personalization exists.

Cold start problem: in recommender systems, the challenge of personalizing for users with little or no interaction history.

```
New user arrives → Zero ratings → No preference data → Generic recommendations
(Everyone sees the same results until they rate content)
```

**Vision:**

- Alice loves wholesome slice-of-life shows
- Bob prefers dark fantasy with complex plots
- Same query: "school anime"
  - Alice sees "K-On!", "Laid-Back Camp"
  - Bob sees "Death Note", "Classroom of the Elite"

Learning from ratings makes this possible.

### Learning User Preferences

Users rate content. Capturing "what they like" in a way that improves future recommendations requires strategy.

**Genre Counters**

```csharp
// Track genre preferences
public class UserProfile
{
    public Dictionary<string, int> GenreCounts { get; set; }  // Romance: 15, Action: 3
}

// When user rates highly
foreach (var genre in media.Genres)
    profile.GenreCounts[genre]++;
```

Too coarse-grained (all "Romance" anime aren't the same). Doesn't capture tone, pacing, art style. Explodes with too many genres and tags.

**Collaborative Filtering (User-to-User)**

```csharp
// Find similar users
var similarUsers = FindUsersWith SimilarRatings(currentUser);

// Recommend what they liked
var recommendations = similarUsers.SelectMany(u => u.HighRatedContent);
```

Classic approach with problems: cold start (new users have no ratings to compare), sparsity (10,000 users × 10,000 shows equals 100M possible ratings, most empty), scalability (comparing users is O(N²), double the users and computation quadruples).

**Content-Based with Embeddings**

Capture user preferences in the same 384-dimensional space as content embeddings:

```csharp
public class UserProfile
{
    public float[] PrefVector { get; set; }  // 384 dimensions, like content
}
```

Each rating nudges the preference vector toward that content's embedding.

**Preference Vector with Exponential Moving Average**

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
        // First rating: initialize
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

**Exponential Moving Average (EMA)**

Technique from signal processing and time-series analysis. Gives more weight to recent data while retaining historical context.

```
Traditional average (equal weight):
  Rating 1: "K-On!" → [0.8, 0.2, 0.1]
  Rating 2: "Death Note" → [0.3, 0.6, 0.5]
  Average: [0.55, 0.4, 0.3]  (equal 50/50 split)

Exponential moving average (alpha = 0.3):
  Rating 1: "K-On!" → PrefVector = [0.8, 0.2, 0.1]
  Rating 2: "Death Note" → PrefVector = 70% old + 30% new
    = [0.8, 0.2, 0.1] × 0.7 + [0.3, 0.6, 0.5] × 0.3
    = [0.65, 0.32, 0.22]  (recent rating influences, doesn't dominate)

Why "exponential"?
  Each new rating has diminishing influence on older history:
  - Rating 2: 30% influence
  - Rating 3: 30% of remaining 70% = 21% influence
  - Rating 4: 30% of remaining 49% = 14.7% influence
  (Exponentially decaying weights)
```

**Behavior:**

```
User rates K-On! (5★):
  PrefVector = [0.8, 0.2, 0.1, ...]  (wholesome slice-of-life)

User rates Death Note (5★):
  PrefVector = [0.65, 0.35, 0.25, ...]  (blended: some wholesome, some dark)

User rates Made in Abyss (5★):
  PrefVector = [0.5, 0.45, 0.35, ...]  (darker preferences growing)
```

Each rating nudges the preference vector toward that content's embedding. Over time, it captures what the user likes while adapting to changing tastes.

### Balancing Intent vs. History

A logged-in user searches for "magic school anime". Two signals exist:

1. **Search intent**: What they explicitly want right now
2. **Learned preferences**: What they generally like based on ratings

Combining them requires balance.

**Only Use Search Intent (Ignore Preferences)**

```csharp
var searchVector = await Ai.Embed("magic school anime", ct);
var results = await Vector<Media>.Search(vector: searchVector);
```

Doesn't personalize. Alice and Bob get identical results despite different tastes.

**Only Use Preferences (Ignore Intent)**

```csharp
var userPrefVector = await UserProfile.GetPrefVector(userId);
var results = await Vector<Media>.Search(vector: userPrefVector);
```

Ignores what they actually searched for. Bob searches "magic school" but gets dark psychological thrillers because that's his history.

**50/50 Blend**

```csharp
var blendedVector = BlendVectors(searchVector, userPrefVector, weight: 0.5);
```

Seems balanced, but in practice the user's explicit search should matter more than history. If they search "cute slice of life", they probably don't want dark fantasy even if that's 50% of their history.

**66% Intent, 34% Preferences**

When a user explicitly searches for something, their current intent matters more than learned history.

```csharp
// 1. Search intent (what they want right now)
var searchVector = await Ai.Embed("magic school anime", ct);

// 2. User preferences (what they generally like)
var userPrefVector = await UserProfile.GetPrefVector(userId, ct);

// 3. Blend: 66% search intent + 34% user preferences
var blendedVector = BlendVectors(
    searchVector,
    userPrefVector,
    weight: 0.66  // Favor explicit intent
);

// 4. Hybrid search with personalized vector
var results = await Vector<Media>.Search(
    vector: blendedVector,
    text: "magic school anime",
    alpha: 0.5,
    topK: 50
);
```

**Why 66/34?**

Empirically tested ratios:
- **80/20**: Too much intent, barely personalized
- **50/50**: History overwhelms intent
- **66/34**: Sweet spot, respects search while adding personal touch

Give me magic school anime, but subtly favor my tastes.

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
    // Unit length: scale vector so its length is 1.0
    // Cosine similarity only cares about direction, not magnitude
    var magnitude = Math.Sqrt(result.Sum(x => (double)x * x));
    if (magnitude > 1e-8)
    {
        for (int i = 0; i < result.Length; i++)
            result[i] /= (float)magnitude;
    }

    return result;
}
```

66% search intent (user explicitly wants "magic school anime" right now) plus 34% preferences (nudge toward their general tastes). This balances explicit intent with implicit learning.

**Result:**

```
Alice searches "magic school anime":
  Blended toward: wholesome, slice-of-life
  Results: "Little Witch Academia" (top), "Flying Witch", "Kiki's Delivery Service"

Bob searches "magic school anime":
  Blended toward: dark, psychological
  Results: "The Irregular at Magic High School" (top), "Fate/Zero", "Madoka Magica"
```

Same query, personalized results.

### The Explainability Problem

The preference vector learns well. Then a business requirement arrives:

> "We need to show users WHY they're getting recommendations. 'Based on your interests in Romance and Comedy' makes sense. 'Based on vector coordinate [0.734, -0.891, ...]' does not."

Preference vectors capture nuanced taste but are impossible to interpret. Explainability requires a different approach.

**Only Use Preference Vector (Latent Semantics)**

```csharp
// Just the 384-dimensional preference vector
var blendedVector = BlendVectors(searchVector, userPrefVector, 0.66);
var results = await Vector<Media>.Search(vector: blendedVector);

// Try to explain...
// "We recommend this because vector dimensions 47, 103, and 298 are aligned"
// Meaningless to users
```

Zero explainability (can't tell user WHY they got a recommendation), no business rules (can't implement "never recommend Horror if user hates it"), no debugging (when recommendations are wrong, can't understand why), no user control (can't let users say "more Romance, less Action").

**Only Use Genre Weights (Explicit Metadata)**

```csharp
// Track explicit genre preferences
public Dictionary<string, double> GenreWeights { get; set; } = new();

// When user rates content
foreach (var genre in media.Genres)
{
    var target = (rating - 1) / 4.0;  // Convert 1-5 to 0-1
    GenreWeights[genre] = UpdateWeight(GenreWeights[genre], target);
}

// Score by genre match
var score = media.Genres.Sum(g => GenreWeights.GetValueOrDefault(g, 0));
```

Too coarse ("Romance" includes wholesome K-dramas AND dark psychological thrillers), misses nuance (can't capture "likes shows with strong female leads"), tag sparsity (most subtle preferences like pacing, art style, tone have no explicit tags), genre pollution ("Action/Adventure/Fantasy/Comedy" - which matters?).

**Use BOTH (Explicit + Latent)**

Track both interpretable genre weights and nuanced preference vectors:

```csharp
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    public Dictionary<string, double> GenreWeights { get; set; } = new();  // Explicit
    public float[]? PrefVector { get; set; }  // Latent
}
```

**Two-Tier Learning System**

S5.Recs uses multi-modal personalization:

**1. Genre Weights (Explicit Metadata)**

```csharp
// When user rates content
foreach (var genre in media.Genres)
{
    profile.GenreWeights.TryGetValue(genre, out var oldWeight);
    var target = (rating - 1) / 4.0;  // Convert 1-5 to 0-1 scale
    var updated = (1 - alpha) * oldWeight + alpha * target;
    profile.GenreWeights[genre] = Math.Clamp(updated, 0, 1);
}
```

**2. Preference Vector (Latent Semantics)**

```csharp
// Update the 384-dimensional vector with EMA
var contentEmbedding = await Ai.Embed(BuildEmbeddingText(media), ct);
for (int i = 0; i < profile.PrefVector.Length; i++)
    profile.PrefVector[i] = (float)((1 - alpha) * profile.PrefVector[i] + alpha * contentEmbedding[i]);
```

**3. Combined Scoring**

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

// Blend explicit and latent signals
var score = (0.4 * vectorScore) + (0.3 * popularityScore) + (0.2 * genreBoost);
```

**Why This Works:**

| Aspect | Genre Weights | Preference Vector |
|--------|--------------|-------------------|
| Interpretability | ✅ "Alice likes Romance: 90%" | ❌ [0.734, -0.891, ...] |
| Nuance | ❌ Coarse categories | ✅ Captures tone, pacing, style |
| Business Rules | ✅ "Never show Horror if weight < 0.2" | ❌ Can't apply rules to vectors |
| Explainability | ✅ "Based on your love of Romance" | ❌ No human explanation |
| Subtle Patterns | ❌ "Strong female lead" not a genre | ✅ Latent in embedding space |

**Example:**

Alice rates "Kaguya-sama: Love is War" (5★):

```
Genre Weights Update:
  Romance: 0.5 → 0.65
  Comedy: 0.4 → 0.58
  School: 0.3 → 0.51

Preference Vector Update:
  [0.2, 0.8, 0.1, ...] → [0.35, 0.82, 0.15, ...]
  (Nudged toward: witty dialogue, psychological games, tsundere characters)
```

**Explainability in UI:**

```javascript
// Show interpretable reasons
"Because you enjoy Romance (90%) and Comedy (85%)"

// Powered by latent preferences behind the scenes
// (subtle bias toward witty banter, character-driven plots)
```

**Production Patterns**

This two-tier learning system appears in production recommender systems. Explicit features (genres, tags) provide interpretability, support business logic, and enable user-facing explanations. Latent features (embeddings) capture complex patterns, improve precision, and handle long-tail preferences.

Industry examples: Netflix (genre preferences plus collaborative filtering embeddings), Spotify (playlist categories plus audio feature embeddings), Amazon (product categories plus item-to-item collaborative vectors).

Trade-offs: more storage (both systems tracked per user), slightly more complex scoring logic. Worth it for explainability plus nuanced recommendations.

---

## Performance at Scale: Embedding Cache

### The AI Cost Problem

The system works beautifully. Then the realization:

```
Import 10,000 media items:
  10,000 API calls to Ollama (embedding model)
  ~30 seconds (with batching)

Re-import same data (updates):
  Another 10,000 API calls
  Another 30 seconds

Total wasted: 100% redundant work
```

**Insight:**

For the same content, the embedding never changes. Already computed:

```
"Kaguya-sama: Love is War\n\nA battle of wits..."
→ [0.8, 0.2, 0.1, ...]
```

Why compute it again?

### Choosing a Cache Strategy

Caching embeddings avoids redundant AI calls. But how do you key the cache? Different strategies have different trade-offs.

**Cache by Entity ID**

```csharp
// Use media.Id as cache key
var cached = await _cache.GetAsync(media.Id);
if (cached != null) return cached;

var embedding = await Ai.Embed(embeddingText, ct);
await _cache.SetAsync(media.Id, embedding);
```

Over-invalidation (any metadata change invalidates cache), doesn't detect duplicates (same content from different providers equals different IDs equals cache miss), content changes missed (updated synopsis serves old embedding, stale cache).

**Cache by (ID + Timestamp)**

```csharp
// Use ID + UpdatedAt timestamp
var cacheKey = $"{media.Id}_{media.UpdatedAt:yyyyMMddHHmmss}";
var cached = await _cache.GetAsync(cacheKey);
```

Still misses duplicates (same content across providers equals cache miss), timestamp precision issues (UpdatedAt might change even when content doesn't), no cross-entity reuse (two shows with identical synopsis equal two separate cache entries).

**Content-Addressable Hashing**

Use the embedding text itself as the cache key via cryptographic hashing:

```csharp
// Hash the actual content we're embedding
var contentHash = ComputeSHA512Hash(embeddingText);
var cached = await _cache.GetAsync(contentHash);
```

Same content produces same hash (deterministic cache key), different content produces different hash (automatic invalidation), cross-entity deduplication (two shows with identical embedding text share one cache entry), no manual invalidation (content changes, hash changes, cache miss, correct behavior).

**SHA512 Content-Addressable Cache**

S5.Recs uses content hashing for embedding cache keys:

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

**Usage:**

```csharp
// Before generating embedding
var embeddingText = BuildEmbeddingText(media);
var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);

// Try cache first
var cached = await _embeddingCache.GetAsync(contentHash, modelId, typeof(Media).FullName);
if (cached != null)
{
    embedding = cached;  // Cache hit, no AI call
    _cacheHits++;
}
else
{
    // Cache miss, generate and store
    embedding = await Ai.Embed(embeddingText, ct);
    await _embeddingCache.SetAsync(contentHash, modelId, embedding, typeof(Media).FullName);
    _cacheMisses++;
}
```

SHA512 provides five properties: deterministic (same content always produces same hash), collision-resistant (cryptographically impossible for different content to produce same hash in 2^512 space), content-addressable (the content itself is the cache key), invalidation-free (content changes, different hash, automatic cache miss, no manual invalidation logic), cross-entity deduplication (two media items with identical embedding text share one cache entry).

**Result:**

```
First import (10,000 items):
  Cache hits: 0, Cache misses: 10,000 (0% hit rate)
  Time: 30 seconds

Second import (same data):
  Cache hits: 10,000, Cache misses: 0 (100% hit rate)
  Time: 2 seconds (15x faster)

Typical production (updates):
  Cache hits: 8,500, Cache misses: 1,500 (85% hit rate)
  Time: 5 seconds
```

**File Structure:**

```
.Koan/cache/embeddings/
  S5.Recs.Models.Media/
    default/  (model ID)
      8k3jf9s...kd9f.json  (content hash)
      fj39dks...32fs.json
```

**Production Patterns**

This pattern appears in production systems at scale. Git uses SHA1 content addressing for commits. Docker uses SHA256 for image layers. IPFS uses content-addressable storage for files. Same principle: content becomes its own cache key, making invalidation trivial (content changed, different hash).

---

## The Three Modes: UX Design

### Different Users, Different Needs

S5.Recs supports three distinct user experiences:

#### 1. For You (Personalized)

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

Use case: recommendations tailored to me.

#### 2. Free Browsing (Pure Semantic, No Login)

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

Use case: browsing without login, good semantic search.

#### 3. Library (User's Collection)

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

Use case: show what I've already watched/read.

**UI Toggle:**

```html
<div class="flex bg-slate-800 rounded-lg p-1">
  <button id="forYouBtn">For You</button>
  <button id="freeBrowsingBtn">Free Browsing</button>
  <button id="libraryBtn">Library</button>
</div>
```

Different contexts require different experiences. Personalized mode requires authentication and uses learned preferences. Discovery mode needs no login and provides pure semantic search. Collection mode handles personal library management. Context-aware UX respects user intent.

---

## How Features Enrich Each Other

### The Synergy Map

S5.Recs transcends simple CRUD. Every feature amplifies others:

```
┌─────────────────────┐
│  Entity Storage     │
│  (Foundation)       │
└──────┬──────────────┘
       │
       ├─► Deterministic IDs ──────────────┐
       │    (Idempotent imports)           │
       │                                   │
       ├─► Vector Storage ─────────────────┼─► Semantic Search
       │    (Meaning-based retrieval)      │    (Vague queries work)
       │                                   │
       ├─► Hybrid Search ──────────────────┼─► Exact Titles + Meaning
       │    (BM25 + Vectors)               │    (Japanese/Korean work)
       │                                   │
       ├─► User Ratings ───────────────────┼─► Preference Learning
       │    (Explicit feedback)            │    (Profile updates)
       │                                   │
       ├─► Preference Vector ──────────────┼─► Personalized Results
       │    (Learned tastes)               │    (Same query, different users)
       │                                   │
       ├─► Embedding Cache ────────────────┼─► Fast Re-imports
       │    (Content-addressable)          │    (85% cache hit rate)
       │                                   │
       └─► Library Entries ────────────────┘
            (User collection)
```

**Examples:**

1. **Deterministic IDs + Vector Storage:** Same content from different API responses produces same ID, updates existing vector, avoids duplicates.

2. **Hybrid Search + Preference Vector:** User blends learned tastes with exact title matching. Alice searches "Kaguya-sama", gets exact match plus similar wholesome shows.

3. **Embedding Cache + Hybrid Search:** Cached embeddings make re-indexing with searchText fast. Add new title synonyms, reindex uses cached vectors.

4. **User Ratings + Semantic Search:** Ratings build preference vector, enable personalized semantic search. "Shows like this" searches use learned tastes.

5. **Library + Recommendations:** "For You" mode excludes library items (already seen). Ratings from library feed preference learning.

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

Self-improving intelligence. Each interaction makes the system better for that user.

---

## Technical Deep Dive

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

### Provider Transparency

Same code, different providers:

```csharp
// Development: MongoDB + Weaviate
"Koan:Data:ConnectionStrings:Default": "mongodb://localhost:27017/s5recs"
"Koan:Vector:Weaviate:Url": "http://localhost:8080"

// Production: Couchbase + Qdrant
"Koan:Data:ConnectionStrings:Default": "couchbase://cluster/s5recs"
"Koan:Vector:Qdrant:Url": "https://qdrant.prod.example.com"
```

No code changes required. Koan's abstraction handles provider differences:

```csharp
// Works with any data provider
await media.Save();
var all = await Media.All();

// Works with any vector provider
await Vector<Media>.Save(id, embedding);
var results = await Vector<Media>.Search(vector, topK: 20);
```

### Graceful Degradation

S5.Recs works even when components fail:

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

Degraded mode: a service operates with reduced functionality due to component failures, but remains usable.

```
Normal mode: ✅ Semantic search + BM25 + personalization
Degraded mode: ⚠️ Basic keyword search only (slower, less accurate, works)
```

**UI Response:**

```javascript
if (response.degraded) {
  showWarning("⚠️ Semantic search unavailable - using basic search");
}
```

Production systems have partial failures. Vector DB might be down for maintenance. Graceful degradation means the app stays usable with reduced intelligence.

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

Upsert: "Update or Insert" - if record exists, update it; if not, insert it

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

HNSW (Hierarchical Navigable Small World): graph-based algorithm for fast approximate nearest neighbor search.

```
Without HNSW (brute force):
  Compare query vector to ALL 1,000,000 vectors
  Time: O(N), scales linearly

With HNSW index:
  Navigate graph structure to find nearest neighbors
  Time: O(log N), logarithmic scaling

  Example: 1M vectors
    Brute force: 1,000,000 comparisons
    HNSW: ~10-20 hops through graph (99.9% accuracy)
```

HNSW enables sub-second search even with millions of embeddings.

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

## Lessons for Your Architecture

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

90% less code, zero ceremony.

### Pattern 2: Provider Transparency Defers Infrastructure Decisions

Choosing MongoDB versus PostgreSQL versus Couchbase can wait. Start with whatever you have:

```csharp
// Day 1: Local JSON files
"Koan:Data:Provider": "json"

// Week 2: MongoDB for dev
"Koan:Data:Provider": "mongodb"

// Month 3: PostgreSQL for prod
"Koan:Data:Provider": "postgresql"
```

Same entity code. Infrastructure becomes a deployment detail.

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

This makes your app resilient to provider limitations.

### Pattern 4: Content-Addressable Caching

For expensive operations (AI, external APIs), use content hashing:

```csharp
var contentHash = SHA512(input);
var cached = await cache.Get(contentHash);

if (cached != null) return cached;

var result = await ExpensiveOperation(input);
await cache.Set(contentHash, result);
return result;
```

Idempotent, efficient, simple.

### Pattern 5: Progressive Enhancement in UX

Build experiences that work at multiple levels:

```
Level 1: Basic keyword search (always works)
Level 2: Semantic vector search (requires AI + Vector DB)
Level 3: Hybrid search (requires vector DB with BM25 support)
Level 4: Personalization (requires user login + rating history)
```

Users get better experiences as capabilities enable, but the app works at every level.

---

## Conclusion: The Power of Composition

S5.Recs demonstrates that modern application architectures compose patterns that enrich each other:

- Entity-first storage makes CRUD trivial
- Vector embeddings add semantic intelligence
- Hybrid search combines precision with meaning
- Personalization learns from user behavior
- Content caching makes it performant
- Provider transparency makes it flexible

Each piece works alone. Together, they create something greater than the sum of parts.

**For Beginners:**
A modern application built without repositories, without manual DI, without SQL queries. Entity-first patterns.

**For Intermediate Developers:**
Vector databases integrate with traditional CRUD, embeddings capture meaning, expensive AI operations get cached.

**For Solution Architects:**
Provider-transparent patterns, graceful degradation, capability-based development, and personalization systems at scale.

**The Koan Way:**
- Reference = Intent (add package, functionality enabled)
- Entity-First (no repositories)
- Provider-Transparent (swap infrastructure freely)
- Self-Reporting (capabilities visible at runtime)

S5.Recs is a living example of these principles. Study it. Extend it. Build intelligent systems following these patterns.

---

**Next Steps:**

1. Run S5.Recs locally: `./start.bat` in `samples/S5.Recs/docker`
2. Import media: Hit `/admin/import/anilist` to seed AniList data
3. Search semantically: Try "cute but powerful" in the UI
4. Add hybrid search: Try "かぐや様" with alpha slider
5. Rate content: See personalization learning in action
6. Export vectors: Use `/admin/cache/embeddings/export` to cache
7. Study the code: See patterns in practice

The best way to learn is to build. Fork S5.Recs. Add your own features. Push the boundaries.

---

*Last Updated: 2025-01-04*
*Framework Version: v0.6.3*
*Koan Framework - Intelligent Application Development*
