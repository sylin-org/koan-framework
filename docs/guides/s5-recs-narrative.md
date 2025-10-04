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

Notice something? **`media.Save()`** is a one-liner. No repositories. No DbContext. No dependency injection setup.

**Why this approach instead of Entity Framework's repository pattern?**

In traditional EF, you'd write:

```csharp
// Define repository
public interface IMediaRepository {
    Task<Media> GetAsync(string id);
    Task SaveAsync(Media media);
}

// Implement it
public class MediaRepository : IMediaRepository { /* ... */ }

// Configure DI
services.AddDbContext<MediaDbContext>();
services.AddScoped<IMediaRepository, MediaRepository>();

// Use it
public class MediaService(IMediaRepository repo) {
    public async Task Save(Media m) => await repo.SaveAsync(m);
}
```

That's perfectly valid—EF's repository pattern gives you explicit control and clear separation of concerns. But for S5.Recs (and most Koan apps), we chose a simpler path:

**The tradeoff:**
- **EF approach**: More setup, explicit abstractions, clear boundaries
- **Koan approach**: Less ceremony, implicit abstractions via `Entity<T>`, faster to build

Koan's `Entity<T>` provides the same provider transparency (swap MongoDB for PostgreSQL via config) without the setup ritual. It's ActiveRecord-style for developer velocity, while still being testable and storage-agnostic under the hood.

For S5.Recs, where we're building quickly and the data layer is straightforward, this choice keeps us moving.

### Scene 2: The Identity Problem

You're importing anime data from AniList's API. You run the import script. Everything works! 10,000 shows in your database.

A week later, AniList updates some metadata. You run the import again. Now you have **20,000 shows**—10,000 duplicates.

**The Challenge:**

How do you guarantee that the same anime from the same provider generates the same ID every time?

**Option 1: Random UUIDs (Traditional)**

```csharp
var media = new Media {
    Id = Guid.NewGuid(),  // Different every run
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

**Problem:** Every import creates new records. You'd need complex deduplication logic comparing titles, checking external IDs, merging records. Fragile and error-prone.

**Option 2: Use Provider's External ID Directly**

```csharp
var media = new Media {
    Id = "101921",  // AniList's ID
    ProviderCode = "anilist"
};
```

**Problem:** Collisions when you add MyAnimeList (also has ID "101921" for different content). You'd need composite keys or prefixing schemes. Still messy.

**Option 3: Composite Key String**

```csharp
var media = new Media {
    Id = "anilist:101921",  // Provider + ID
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

**Better!** But what about media type? "anilist:101921:anime" vs "anilist:101921:manga"? And what about future schema changes?

**The Decision: Deterministic Hashing**

```csharp
public sealed class Media : Entity<Media>
{
    public static string MakeId(string providerCode, string externalId, string mediaTypeId)
    {
        // SHA512 hash of: "anilist:101921:media-anime"
        // Always produces the same hash for same inputs
        return IdGenerationUtilities.GenerateMediaId(providerCode, externalId, mediaTypeId);
    }
}

// Usage
var id = Media.MakeId("anilist", "101921", "media-anime");
// → "k8j3nf92...sd8f" (64-char SHA512 hash, always same for these inputs)

var media = new Media {
    Id = id,
    ProviderCode = "anilist",
    ExternalId = "101921"
};
```

**Why SHA512 Hashing?**

1. **Deterministic**: Same inputs → same hash, every time
2. **Collision-resistant**: Virtually impossible for different content to get same hash
3. **Opaque**: Doesn't expose internal structure (security)
4. **Future-proof**: Can add more fields to hash input without breaking existing IDs
5. **No length limits**: Unlike composite strings, hash is fixed length

**The Result:**

```csharp
// First import
var id1 = Media.MakeId("anilist", "101921", "media-anime");
await new Media { Id = id1, Title = "Kaguya-sama" }.Save();

// Second import (weeks later, same data)
var id2 = Media.MakeId("anilist", "101921", "media-anime");
await new Media { Id = id2, Title = "Kaguya-sama: Love is War" }.Save();

// id1 == id2 → Update existing record, no duplicate!
```

**Cross-Provider Bonus:**

When you want to merge data from multiple sources, deterministic IDs enable it:

```csharp
// AniList record
var anilistId = Media.MakeId("anilist", "101921", "media-anime");

// MyAnimeList record (different external ID)
var malId = Media.MakeId("mal", "37999", "media-anime");

// These are different IDs (correctly!)
// You'd need a separate MediaLink entity to mark them as same content
// But at least you have stable, predictable identities to work with
```

**Trade-off Accepted:**

Hashing loses human readability. You can't look at an ID and know what it represents. But you gain **idempotence**, which is critical for data pipelines importing from external APIs.

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

### Scene 4: Solving the Meaning Problem

**The Challenge:**

Users want to search by meaning, not just keywords. "Shows like Cowboy Bebop" should find tonally similar shows, but keyword search can't do this. How do you enable semantic understanding?

**Option 1: Synonym Dictionaries**

```csharp
var synonyms = new Dictionary<string, string[]> {
    { "cat", new[] { "kitten", "feline", "kitty" } },
    { "sad", new[] { "melancholic", "depressing", "tragic" } }
};

// Expand query
var expandedQuery = ExpandWithSynonyms(query, synonyms);
var results = await Media.Where(m => m.Synopsis.Contains(expandedQuery));
```

**Problems:**
- Manual curation (impossible to maintain for all concepts)
- Language-specific (doesn't work for Japanese/Korean titles)
- No understanding of **context** ("bank" = financial institution or river edge?)
- Binary matching (either synonym matches or doesn't)

**Option 2: TF-IDF + Latent Semantic Analysis (LSA)**

*Classic information retrieval approach from the 1990s.*

```csharp
// Build term-document matrix (word frequency table)
// Rows = words, Columns = documents, Values = how important each word is to each document
var tfidf = BuildTfIdfMatrix(allMedia);

// Perform SVD (Singular Value Decomposition) to reduce dimensions
// "Find patterns in which words tend to appear together"
var lsa = PerformSVD(tfidf, dimensions: 100);

// Search by projecting query into this reduced space
var queryVector = TransformQuery(query, lsa);
var results = CosineSimilarity(queryVector, allDocuments);
```

**How it works (simplified):**
```
Documents:
  Doc1: "cat kitten meow"
  Doc2: "dog puppy bark"
  Doc3: "cat purr feline"

TF-IDF Matrix (word importance):
          Doc1  Doc2  Doc3
  cat     0.8   0.0   0.7
  kitten  0.6   0.0   0.0
  dog     0.0   0.9   0.0
  puppy   0.0   0.5   0.0

LSA learns: "cat, kitten, feline often appear together" → Group 1
            "dog, puppy, bark often appear together" → Group 2
```

**Problems:**
- Requires full corpus preprocessing (expensive on updates—need ALL documents upfront)
- Shallow semantic understanding (learns word co-occurrence, not actual meaning)
- No cross-lingual capabilities (can't connect "cat" with "猫")
- Fixed vocabulary (new words not in training corpus = can't be searched)

**Option 3: Pre-Trained Neural Embeddings**

Use AI models trained on billions of text examples to convert text into **coordinates in meaning-space**.

```csharp
// Convert text to 384-dimensional vector
var embedding = await Ai.Embed("cute but powerful characters", ct);
// Result: [0.8, 0.2, 0.1, ...] (384 numbers representing the MEANING)
```

**Why this works:**

```
Traditional approaches:
"cat" ≠ "kitten" ≠ "feline"  (exact string matching or manual synonyms)

Neural embeddings (learned from billions of examples):
"cat"    → [0.8, 0.2, 0.1, ...]  (384 numbers)
"kitten" → [0.79, 0.21, 0.09, ...] (very close in space)
"feline" → [0.77, 0.19, 0.11, ...] (close)
"car"    → [0.1, 0.9, 0.3, ...]   (far away)
```

Similar meanings have **similar vectors**. Distance in vector space = semantic similarity.

**The Decision: all-MiniLM-L6-v2 Embeddings**

S5.Recs uses the **all-MiniLM-L6-v2** model:
- **384 dimensions**: Compact yet powerful semantic representation
- **Multilingual**: Handles English, Japanese romanization, basic cross-lingual understanding
- **Fast inference**: ~10ms per embedding on CPU
- **Pre-trained**: No training required, works out of the box

**Implementation:**

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

**Building Rich Embedding Context:**

Just the title isn't enough—the model needs comprehensive semantic information:

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

**Why this approach wins:**
1. **Zero manual work**: No synonym curation or corpus preprocessing
2. **Contextual understanding**: "bank" near "money" vs "river" produces different embeddings
3. **Multilingual**: Handles romanized Japanese, basic cross-language similarity
4. **Continuous similarity**: 0.89 match vs 0.45 match (not just yes/no)
5. **Scales effortlessly**: New content = just compute embedding

**Trade-offs accepted:**
- Requires external AI model (Ollama in S5.Recs)
- 384-dimensional vectors use more storage than keywords
- Slightly slower than exact string matching (but worth it for semantic power)

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
2. Compared it to all media embeddings via **cosine similarity** (measures "how aligned" two vectors are)
3. Returned the closest matches in "meaning-space"

**Cosine Similarity Explained:**
```
Two vectors are "similar" if they point in the same direction:

  Vector A: [0.8, 0.2]  ↗
  Vector B: [0.9, 0.3]  ↗  (pointing same direction → high similarity: 0.98)

  Vector A: [0.8, 0.2]  ↗
  Vector C: [0.1, 0.9]  ↑  (pointing different direction → low similarity: 0.32)

In 384 dimensions, same principle: aligned vectors = similar meaning
```

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

### Scene 7: Solving the Precision Problem

**The Challenge:**

You need both semantic understanding AND exact title matching. How do you combine them?

**Option 1: Dual Queries with Client-Side Merge**

```csharp
// Run both searches
var semanticResults = await Vector<Media>.Search(vector, topK: 50);
var keywordResults = await Media.Where(m => m.Title.Contains(query)).Take(50);

// Merge manually
var combined = semanticResults.Concat(keywordResults).Distinct().Take(20);
```

**Problems:**
- Two separate queries (latency)
- Manual merge logic (how do you rank them?)
- Doesn't scale (fetching 50+50 to get 20)
- Client-side sorting loses provider optimizations

**Option 2: Pre-Filter with Keywords, Then Vector Search**

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

**Problems:**
- Keyword pre-filter might exclude good semantic matches
- Still two queries
- Complex filter translation across providers

**Option 3: Provider-Native Hybrid Search**

Many vector databases support **hybrid search**—combining **BM25 keyword matching** with **vector similarity** in a **single query** with **provider-optimized fusion**.

*"Provider-optimized fusion" = The vector database internally combines BM25 and vector scores using efficient algorithms (like Reciprocal Rank Fusion - RRF), rather than you merging results in application code.*

```
Reciprocal Rank Fusion (simplified):
  BM25 results: [DocA, DocB, DocC]  (ranked 1, 2, 3)
  Vector results: [DocC, DocA, DocD] (ranked 1, 2, 3)

  RRF score = 1/(rank+60) for each list, then sum:
    DocA: 1/(1+60) + 1/(2+60) = 0.0164 + 0.0161 = 0.0325
    DocC: 1/(3+60) + 1/(1+60) = 0.0159 + 0.0164 = 0.0323

  Final ranking: [DocA, DocC, ...] (combined best from both)
```

**What is BM25?**
*"Best Matching 25" - A keyword ranking algorithm from the 1970s-90s, still used in Elasticsearch, Weaviate, and search engines.*

```
How BM25 scores documents:
  Query: "magic school"

  Document A: "magic school magic school"
    - Term frequency: "magic" appears 2x, "school" appears 2x
    - Score: HIGH (exact matches, repeated terms)

  Document B: "a story about a magical academy for wizards"
    - Term frequency: "magic" appears 0x, "school" appears 0x
    - Score: LOW (no exact matches, even though semantically related)

BM25 = Keyword matching + Term frequency + Document length normalization

*Document length normalization = Prevents long documents from getting unfairly high scores just because they contain more words*
```

**The Decision: Hybrid Search with Alpha Blending**

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

**The "Cold Start Problem":**
*In recommender systems, the challenge of personalizing for users with little or no interaction history.*

```
New user arrives → Zero ratings → No preference data → Generic recommendations
(Everyone sees the same results until they rate content)
```

**The Vision:**

- Alice loves wholesome slice-of-life shows
- Bob prefers dark fantasy with complex plots
- Same query: "school anime"
  - Alice should see "K-On!", "Laid-Back Camp"
  - Bob should see "Death Note", "Classroom of the Elite"

**How?** By learning from ratings.

### Scene 9: Learning User Preferences

**The Challenge:**

Users rate content. How do you capture "what they like" in a way that improves future recommendations?

**Option 1: Genre Counters**

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

**Problems:**
- Too coarse-grained (all "Romance" anime aren't the same)
- Doesn't capture tone, pacing, art style
- Explodes with too many genres/tags

**Option 2: Collaborative Filtering (User-to-User)**

```csharp
// Find similar users
var similarUsers = FindUsersWith SimilarRatings(currentUser);

// Recommend what they liked
var recommendations = similarUsers.SelectMany(u => u.HighRatedContent);
```

**Classic approach!** But problems:
- Cold start: New users have no ratings to compare
- Sparsity: 10,000 users × 10,000 shows = 100M possible ratings, most empty
- Scalability: Comparing users is O(N²) *(means if you double users, computation time quadruples)*

**Option 3: Content-Based with Embeddings**

What if you capture user preferences in the **same 384-dimensional space** as your content embeddings?

```csharp
public class UserProfile
{
    public float[] PrefVector { get; set; }  // 384 dimensions, just like content
}
```

Each rating **nudges** the preference vector toward that content's embedding.

**The Decision: Preference Vector with Exponential Moving Average**

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

**Exponential Moving Average (EMA) Explained:**

*A technique from signal processing and time-series analysis that gives more weight to recent data while retaining historical context.*

```
Traditional average (equal weight):
  Rating 1: "K-On!" → [0.8, 0.2, 0.1]
  Rating 2: "Death Note" → [0.3, 0.6, 0.5]
  Average: [0.55, 0.4, 0.3]  ← Equal 50/50 split

Exponential moving average (alpha = 0.3):
  Rating 1: "K-On!" → PrefVector = [0.8, 0.2, 0.1]
  Rating 2: "Death Note" → PrefVector = 70% old + 30% new
    = [0.8, 0.2, 0.1] × 0.7 + [0.3, 0.6, 0.5] × 0.3
    = [0.65, 0.32, 0.22]  ← Recent rating influences, but doesn't dominate

Why "exponential"?
  Each new rating has diminishing influence on older history:
  - Rating 2: 30% influence
  - Rating 3: 30% of remaining 70% = 21% influence
  - Rating 4: 30% of remaining 49% = 14.7% influence
  (Exponentially decaying weights)
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

Each rating **nudges** the preference vector toward that content's embedding. Over time, it captures **what the user likes** while adapting to changing tastes.

### Scene 10: Balancing Intent vs. History

**The Challenge:**

A logged-in user searches for "magic school anime". You have two signals:

1. **Search intent**: What they explicitly want right now
2. **Learned preferences**: What they generally like based on ratings

How do you combine them?

**Option 1: Only Use Search Intent (Ignore Preferences)**

```csharp
var searchVector = await Ai.Embed("magic school anime", ct);
var results = await Vector<Media>.Search(vector: searchVector);
```

**Problem:** Doesn't personalize. Alice and Bob get identical results despite different tastes.

**Option 2: Only Use Preferences (Ignore Intent)**

```csharp
var userPrefVector = await UserProfile.GetPrefVector(userId);
var results = await Vector<Media>.Search(vector: userPrefVector);
```

**Problem:** Ignores what they *actually searched for*. Bob searches "magic school" but gets dark psychological thrillers because that's his history.

**Option 3: 50/50 Blend**

```csharp
var blendedVector = BlendVectors(searchVector, userPrefVector, weight: 0.5);
```

**Seems balanced!** But in practice:
- User's explicit search should matter more than history
- If I search "cute slice of life", I probably don't want dark fantasy even if that's 50% of my history

**The Decision: 66% Intent, 34% Preferences**

When a user explicitly searches for something, **their current intent matters more** than learned history.

```csharp
// 1. Search intent (what they want right now)
var searchVector = await Ai.Embed("magic school anime", ct);

// 2. User preferences (what they generally like)
var userPrefVector = await UserProfile.GetPrefVector(userId, ct);

// 3. Blend: 66% search intent + 34% user preferences
var blendedVector = BlendVectors(
    searchVector,
    userPrefVector,
    weight: 0.66  // ← Favor explicit intent
);

// 4. Hybrid search with personalized vector
var results = await Vector<Media>.Search(
    vector: blendedVector,
    text: "magic school anime",
    alpha: 0.5,
    topK: 50
);
```

**Why 66/34 Specifically?**

Empirically tested ratios:
- **80/20**: Too much intent, barely personalized
- **50/50**: History overwhelms intent
- **66/34**: Sweet spot—respects search while adding personal touch

Think of it as: "Give me magic school anime, but **subtly** favor my tastes."

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
    // "Unit length" = Scale vector so its length is 1.0
    // Why? Cosine similarity only cares about DIRECTION, not magnitude
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

### Scene 11: The Explainability Problem

**The Challenge:**

Your preference vector is learning well, but you run into a business requirement:

> "We need to show users WHY they're getting recommendations. 'Based on your interests in Romance and Comedy' makes sense. 'Based on vector coordinate [0.734, -0.891, ...]' does not."

Preference vectors capture nuanced taste, but they're **impossible to interpret**. How do you provide explainability?

**Option 1: Only Use Preference Vector (Latent Semantics)**

```csharp
// Just the 384-dimensional preference vector
var blendedVector = BlendVectors(searchVector, userPrefVector, 0.66);
var results = await Vector<Media>.Search(vector: blendedVector);

// Try to explain...
// "We recommend this because vector dimensions 47, 103, and 298 are aligned"
// ❌ Meaningless to users
```

**Problems:**
- **Zero explainability**: Can't tell user WHY they got a recommendation
- **No business rules**: Can't implement "never recommend Horror if user hates it"
- **No debugging**: When recommendations are wrong, can't understand why
- **No user control**: Can't let users say "more Romance, less Action"

**Option 2: Only Use Genre Weights (Explicit Metadata)**

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

**Problems:**
- **Too coarse**: "Romance" includes wholesome K-dramas AND dark psychological thrillers
- **Misses nuance**: Can't capture "likes shows with strong female leads" (not a genre)
- **Tag sparsity**: Most subtle preferences (pacing, art style, tone) have no explicit tags
- **Genre pollution**: "Action/Adventure/Fantasy/Comedy" - which genre matters?

**Option 3: Use BOTH (Explicit + Latent)**

Track both interpretable genre weights AND nuanced preference vectors:

```csharp
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    public Dictionary<string, double> GenreWeights { get; set; } = new();  // Explicit
    public float[]? PrefVector { get; set; }  // Latent
}
```

**The Decision: Two-Tier Learning System**

S5.Recs uses **multi-modal personalization**:

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

**Why This Hybrid Works:**

| Aspect | Genre Weights | Preference Vector |
|--------|--------------|-------------------|
| **Interpretability** | ✅ "Alice likes Romance: 90%" | ❌ [0.734, -0.891, ...] |
| **Nuance** | ❌ Coarse categories | ✅ Captures tone, pacing, style |
| **Business Rules** | ✅ "Never show Horror if weight < 0.2" | ❌ Can't apply rules to vectors |
| **Explainability** | ✅ "Based on your love of Romance" | ❌ No human explanation |
| **Subtle Patterns** | ❌ "Strong female lead" not a genre | ✅ Latent in embedding space |

**Real Example:**

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
// (subtle bias toward witty banter, character-driven plots, etc.)
```

**Rationale (For Architects):**

This **two-tier learning system** is used in production recommender systems:
1. **Explicit features** (genres, tags) - Interpretable, supports business logic, user-facing explanations
2. **Latent features** (embeddings) - Captures complex patterns, improves precision, handles long-tail preferences

Examples in industry:
- **Netflix**: Genre preferences + collaborative filtering embeddings
- **Spotify**: Playlist categories + audio feature embeddings
- **Amazon**: Product categories + item-to-item collaborative vectors

**Trade-offs accepted:**
- More storage (both systems tracked per user)
- Slightly more complex scoring logic
- Worth it for explainability + nuanced recommendations

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

### Scene 13: Choosing a Cache Strategy

**The Challenge:**

You need to cache embeddings to avoid redundant AI calls. But how do you key the cache? Different strategies have different trade-offs.

**Option 1: Cache by Entity ID**

```csharp
// Use media.Id as cache key
var cached = await _cache.GetAsync(media.Id);
if (cached != null) return cached;

var embedding = await Ai.Embed(embeddingText, ct);
await _cache.SetAsync(media.Id, embedding);
```

**Problems:**
- **Over-invalidation**: If ANY metadata changes (e.g., fixing a typo in synopsis), cache invalidates
- **Doesn't detect duplicates**: Same content from different providers = different IDs = cache miss
- **Content changes missed**: If you update synopsis, old embedding is still served (stale cache)

**Option 2: Cache by (ID + Timestamp)**

```csharp
// Use ID + UpdatedAt timestamp
var cacheKey = $"{media.Id}_{media.UpdatedAt:yyyyMMddHHmmss}";
var cached = await _cache.GetAsync(cacheKey);
```

**Problems:**
- **Still misses duplicates**: Same content across providers = cache miss
- **Timestamp precision**: UpdatedAt might change even when content doesn't (metadata updates)
- **No cross-entity reuse**: Two shows with identical synopsis = two separate cache entries

**Option 3: Content-Addressable Hashing**

Use the **embedding text itself** as the cache key via cryptographic hashing:

```csharp
// Hash the actual content we're embedding
var contentHash = ComputeSHA512Hash(embeddingText);
var cached = await _cache.GetAsync(contentHash);
```

**Why this works:**
- **Same content → Same hash**: Deterministic cache key
- **Different content → Different hash**: Automatic invalidation
- **Cross-entity deduplication**: If two shows have identical embedding text, one cache entry serves both
- **No manual invalidation**: Content changes → hash changes → cache miss (correct behavior)

**The Decision: SHA512 Content-Addressable Cache**

S5.Recs uses **content hashing** for embedding cache keys:

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

**Why SHA512 Specifically?**

1. **Deterministic**: Same content always produces same hash
2. **Collision-resistant**: Cryptographically impossible for different content to produce same hash (2^512 space)
3. **Content-addressable**: The content itself IS the cache key
4. **Invalidation-free**: Content changes → different hash → automatic cache miss (no manual invalidation logic)
5. **Cross-entity deduplication**: Two media items with identical embedding text share one cache entry

**The Result:**

```
First import (10,000 items):
  Cache hits: 0, Cache misses: 10,000 (0% hit rate)
  Time: 30 seconds

Second import (same data):
  Cache hits: 10,000, Cache misses: 0 (100% hit rate)
  Time: 2 seconds (15x faster!)

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

**What is "Degraded Mode"?**
*When a service operates with reduced functionality due to component failures, but remains usable.*

```
Normal mode: ✅ Semantic search + BM25 + personalization
Degraded mode: ⚠️ Basic keyword search only (slower, less accurate, but works)
```

**UI Response:**

```javascript
if (response.degraded) {
  showWarning("⚠️ Semantic search unavailable - using basic search");
}
```

**Why This Matters:**

Production systems have **partial failures**. Your vector DB might be down for maintenance. Graceful degradation means your app stays usable, just with reduced intelligence.

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

*Upsert = "Update or Insert" - If record exists, update it; if not, insert it*

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

**What is HNSW?**
*"Hierarchical Navigable Small World" - A graph-based algorithm for fast approximate nearest neighbor search.*

```
Without HNSW (brute force):
  Compare query vector to ALL 1,000,000 vectors
  Time: O(N) - scales linearly

With HNSW index:
  Navigate graph structure to find nearest neighbors
  Time: O(log N) - logarithmic scaling

  Example: 1M vectors
    Brute force: 1,000,000 comparisons
    HNSW: ~10-20 hops through graph (99.9% accuracy)
```

HNSW enables **sub-second search** even with millions of embeddings.

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
