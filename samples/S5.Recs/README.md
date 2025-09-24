# AnimeRadar

**This sample demonstrates how to build a complete recommendation engine using Koan.**

AnimeRadar shows you how to create a modern content recommendation system by combining MongoDB for data storage, optional vector search with Weaviate, and AI embeddings via Ollama. The sample walks through building personalized recommendations, semantic search, user preference modeling, and a responsive web interface-all using Koan's modular architecture.

## What this sample teaches

**Core recommendation patterns**  
This sample demonstrates how to build a hybrid recommendation system that combines popularity-based filtering with personalized user preferences. You'll learn to track user behavior (ratings, favorites, watch status) and use that data to generate increasingly accurate recommendations.

**AI integration without complexity**  
The sample shows how to add semantic search and vector embeddings to your application while maintaining graceful fallbacks. When AI services are unavailable, the system continues working with popularity and preference-based recommendations.

**Modular architecture in practice**  
You'll see how Koan's modular design lets you start simple (just MongoDB) and add complexity incrementally (vector search, AI embeddings) without restructuring your core application code.

**Production-ready patterns**  
The sample implements real-world concerns: data seeding pipelines, admin dashboards, health monitoring, user management, and responsive UI design using vanilla JavaScript and modern CSS.

## How to build an app like this

**[1] Start with basic data modeling**  
First, you'll need entities to represent your content and user interactions. We use `MediaDoc` for content metadata, `UserDoc` for user profiles, and `LibraryEntryDoc` to track user interactions with content.

**[2] Add simple recommendation logic**  
Begin with popularity-based recommendations filtered by user preferences. This gives you working recommendations immediately while you build more sophisticated features.

**[3] Implement user preference tracking**  
Build a system to track user behaviors (ratings, favorites, viewing status) and use that data to build preference profiles. The sample shows how to weight genres and tags based on user interactions.

**[4] Layer in semantic search**  
Add vector embeddings to enable semantic search ("find something like Cowboy Bebop") alongside keyword-based filtering.

_But wait-what exactly is a vector database, and why would we need one?_ Think of it this way: when you search for "space western," you're not just looking for media with those exact words. You want content that _feels_ like space westerns-the themes, atmosphere, and storytelling style. Vector databases store mathematical representations (embeddings) of content that capture semantic meaning, not just keywords.

_How does this tie to user preferences?_ As users rate content, we can build a "preference vector" that represents their taste in this mathematical space. Then we can find content that's similar to what they already love, even if it doesn't share obvious keywords.

The sample demonstrates graceful fallback when vector services are unavailable.

**[5] Build administration tools**  
Create admin interfaces for data management, system monitoring, and algorithm tuning. This sample includes data seeding, health checks, and recommendation parameter adjustment.

**[6] Polish the user experience**  
Implement a responsive web interface with search, filtering, personal libraries, and recommendation browsing. The sample uses vanilla JavaScript with modern patterns like event delegation and modular organization.

## Building the recommendation engine step-by-step

**Step 1: Define your content and user models**

Start by modeling your core entities. The sample uses three main documents:

```csharp
// Content metadata
public class MediaDoc : Entity<MediaDoc>
{
    public string Title { get; set; } = string.Empty;
    public List<string> Genres { get; set; } = [];
    public float Popularity { get; set; }
    public string Synopsis { get; set; } = string.Empty;

    // Optional: Vector embeddings for semantic search
    public float[]? Vector { get; set; }
}

// User interaction tracking
public class LibraryEntryDoc : Entity<LibraryEntryDoc>
{
    public string UserId { get; set; } = string.Empty;
    public string AnimeId { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public bool Watched { get; set; }
    public int? Rating { get; set; } // 1-5 stars
    public DateTime AddedAt { get; set; }
}
```

This foundation lets you track what users like and build preferences from their behavior.

**Step 2: Implement basic popularity recommendations**

Before adding complexity, start with simple popularity-based recommendations:

```csharp
[HttpPost("query")]
public async Task<RecsResponse> GetRecommendations([FromBody] RecsRequest request)
{
    // Start simple: sort by popularity, filter by preferences
    var query = _mediaCollection
        .Find(a => request.Genres.Count == 0 || a.Genres.Any(g => request.Genres.Contains(g)))
        .SortByDescending(a => a.Popularity);

    var results = await query.Limit(request.TopK).ToListAsync();
    return new RecsResponse { Items = results };
}
```

This gives you working recommendations immediately while you build more sophisticated features.

**Step 3: Add user preference modeling**

Track user interactions and build preference profiles:

```csharp
public async Task UpdateUserPreferences(string userId, string mediaId, int rating)
{
    // Get the media to extract genres
    var media = await _mediaCollection.Find(a => a.Id == mediaId).FirstOrDefaultAsync();
    if (media == null) return;

    // Update user's genre preferences based on rating
    var profile = await GetOrCreateUserProfile(userId);
    foreach (var genre in media.Genres)
    {
        // EWMA update: new preference = α * rating + (1-α) * old preference
        var currentWeight = profile.GenreWeights.GetValueOrDefault(genre, 0.5f);
        var normalizedRating = (rating - 3f) / 2f; // Convert 1-5 to -1 to 1
        profile.GenreWeights[genre] = 0.1f * normalizedRating + 0.9f * currentWeight;
    }

    await _profileCollection.ReplaceOneAsync(p => p.Id == profile.Id, profile);
}
```

_You might wonder: why use EWMA (Exponentially Weighted Moving Average) instead of just averaging ratings?_ EWMA lets recent preferences influence the profile more than old ones, so if your taste changes, the system adapts. The α (alpha) value of 0.1 means new ratings have some impact but don't completely override your established preferences.

This creates user taste profiles that improve recommendations over time.

**Step 4: Integrate vector search for semantic matching**

_Here's where it gets interesting: how do we find content similar to what users already like?_ Vector search lets you find semantically similar content:

```csharp
// Generate embeddings for new content
public async Task GenerateEmbeddings(MediaDoc media)
{
    var text = $"{media.Title} {string.Join(" ", media.Genres)} {media.Synopsis}";
    var embedding = await _aiService.GetEmbeddingAsync(text);

    media.Vector = embedding;
    await _mediaCollection.ReplaceOneAsync(a => a.Id == media.Id, media);
}

// Find similar content using vector similarity
public async Task<List<MediaDoc>> FindSimilar(string mediaId, int count = 10)
{
    var source = await _mediaCollection.Find(a => a.Id == mediaId).FirstOrDefaultAsync();
    if (source?.Vector == null) return [];

    // Use vector database to find similar embeddings
    var similar = await _vectorDb.SearchAsync(source.Vector, count);
    return similar.Select(result => result.Payload).ToList();
}
```

_What exactly is a vector database, and why would we need one?_ Think of it as a search engine that understands meaning, not just keywords. Traditional search looks for exact word matches. Vector search finds content with similar meanings-so searching "space cowboys" might surface "Firefly" even if that show's description never uses those exact words.

The vector database stores numerical representations (embeddings) of your content. When you search, it finds vectors that are "close" to your query vector in this multi-dimensional space. Closeness in vector space means similarity in meaning.

**Step 5: Combine approaches for hybrid recommendations**

_Why not just use one approach?_ Different recommendation strategies excel in different situations:

- **Popularity-based**: Great for new users with no history
- **Preference-based**: Excellent for users with established tastes
- **Vector/semantic**: Powerful for discovery and content-based similarity

The magic happens when you combine them:

```csharp
public async Task<List<MediaDoc>> GetSemanticRecommendations(string userId, string? searchText = null)
{
    List<MediaDoc> candidates;

    if (!string.IsNullOrEmpty(searchText))
    {
        // Generate embedding for search text
        var queryVector = await _aiService.GetEmbedding(searchText);

        // Vector similarity search
        candidates = await _vectorStore.SearchSimilar(queryVector, topK: 100);
    }
    else
    {
        // Use user's preference vector for personalized discovery
        var profile = await GetUserProfile(userId);
        if (profile?.PreferenceVector != null)
        {
            candidates = await _vectorStore.SearchSimilar(profile.PreferenceVector, topK: 100);
        }
        else
        {
            // Fallback to popularity when no vector available
            candidates = await GetPopularContent();
        }
    }

    // Apply user preference scoring to rerank results
    var userProfile = await GetUserProfile(userId);
    return candidates
        .Select(media => new {
            Media = media,
            Score = CalculatePersonalizedScore(media, userProfile)
        })
        .OrderByDescending(x => x.Score)
        .Select(x => x.Media)
        .Take(request.TopK)
        .ToList();
}
```

_Here's the key insight: vector search finds good candidates, but user preferences determine the final ranking._ This gives you both discovery (finding new content) and personalization (ranking by individual taste).

**Step 6: Build administrative interfaces**

_Why separate admin interfaces from user interfaces?_ Administrative tasks like seeding data and monitoring systems have different requirements-they need detailed feedback, longer timeouts, and often handle large datasets that would overwhelm a user-facing UI.

Create tools for data management and system monitoring:

```csharp
[HttpPost("seed/start")]
public async Task<ActionResult> StartSeeding([FromBody] SeedRequest request)
{
    var jobId = Guid.NewGuid().ToString();

    // Background task for data import
    _ = Task.Run(async () =>
    {
        try
        {
            // Import content from external API (AniList, etc.)
            var items = await _contentProvider.FetchContent(request.Source, request.Limit);

            // Generate embeddings if AI is available
            if (_aiService.IsAvailable)
            {
                foreach (var item in items)
                {
                    item.Vector = await _aiService.GetEmbedding(item.Synopsis);
                }
            }

            // Bulk insert with deduplication
            await _mediaCollection.BulkWrite(/* upsert operations */);

            _logger.LogInformation("Seeding completed: {Count} items", items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seeding failed for job {JobId}", jobId);
        }
    });

    return Ok(new { JobId = jobId });
}
```

_Why run this as a background task instead of blocking the API call?_ Data seeding can take several minutes when processing thousands of items and generating embeddings. Users shouldn't have to wait with a frozen browser tab-they should get an immediate job ID and can check progress later.

_What are embeddings, exactly?_ Think of them as "coordinates" in a multi-dimensional space where similar content clusters together. When we convert media content into an embedding vector, we're essentially saying "this story lives at coordinates [0.2, -0.5, 0.8, ...]" in a space where similar stories are nearby.

This provides essential operations tooling for production deployment.

**Step 7: Create a responsive user interface**

_Why vanilla JavaScript instead of a framework?_ For this sample, we want to show core concepts without framework-specific complexity. The patterns you learn here work regardless of whether you later adopt React, Vue, or another framework.

Build a modern web interface using progressive enhancement:

```javascript
// Modular JavaScript architecture
class RecommendationEngine {
  async getRecommendations(filters = {}) {
    const response = await fetch("/api/recs/query", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        userId: this.currentUser.id,
        topK: 50,
        filters: filters,
        preferTags: this.getSelectedTags(),
      }),
    });

    if (!response.ok) {
      this.showError("Failed to load recommendations");
      return [];
    }

    return await response.json();
  }

  // Event delegation for dynamic content
  setupEventHandlers() {
    document.addEventListener("click", (e) => {
      if (e.target.matches('[data-action="rate"]')) {
        this.handleRating(e.target);
      }
      if (e.target.matches('[data-action="filter-tag"]')) {
        this.handleTagFilter(e.target);
      }
    });
  }
}
```

_Why use event delegation instead of binding events to each card?_ When you have 100+ content cards, creating 100+ event listeners is inefficient. Event delegation uses JavaScript's event bubbling-one listener on the document catches clicks from all child elements. It's faster and works with dynamically added content.

The sample demonstrates how to build rich interactions without complex frontend frameworks.

## Key patterns and techniques demonstrated

**Hybrid recommendation scoring**  
_Why combine multiple approaches instead of picking the "best" one?_ Different recommendation methods excel in different situations. Vector similarity excels at content discovery, user preferences ensure personal relevance, and popularity provides a safety net for new users. Combining them gives you the strengths of each approach.

**Graceful degradation**  
_What happens when AI services go down?_ The system falls back to preference-based and popularity recommendations. This ensures your application remains functional even when optional services fail. Users might not get semantic search, but they still get recommendations.

**Real-time preference learning**  
_How quickly should the system adapt to new user behavior?_ User interactions immediately update preference profiles using exponentially weighted moving averages (EWMA). This balances responsiveness to new preferences with stability from historical data-recent actions matter more, but don't completely override established patterns.

**Modular JavaScript architecture**  
_Why not use a big framework for the frontend?_ This sample shows that modern vanilla JavaScript can handle complex interactions cleanly. The patterns here (event delegation, module organization, progressive enhancement) work in any framework-or no framework at all.

# S5.Recs - Recommendation Sample App

**Production data management**  
_What about real-world operations?_ The sample includes comprehensive tooling: background job processing, data seeding pipelines, health monitoring, and administrative controls. These are the pieces you need for production deployment.

## What you'll build by following this sample

**A complete recommendation system** with personalized feeds, semantic search, and user preference modeling

**Administrative tools** for content management, system monitoring, and algorithm tuning
**AI integration patterns** that enhance functionality without creating dependencies

**Prerequisites (managed automatically by start.bat)**

````bash
# Required: MongoDB for data storage
docker run -d -p 27017:27017 mongo:latest

# Optional: Weaviate for vector search

# Optional: Ollama for local AI embeddings
docker run -d -p 11434:11434 ollama/ollama
ollama pull nomic-embed-text

*Wait, why do we need all these services?* Great question! Let's break it down:

- **MongoDB** stores your content and user data-this is required
- **Weaviate** is a vector database that stores and searches embeddings-this enables semantic search like "find space westerns"
- **Ollama** runs AI models locally to convert text into embeddings-this powers the semantic understanding
*Can't we just use regular text search?* You could, but you'd miss the magic. Vector search finds content based on meaning, not just keywords. "Romantic space adventure" might find you "Cowboy Bebop" even though those exact words don't appear in the description.

**Start the application (recommended)**
```bash
start.bat
````

- Swagger UI: http://localhost:5084/swagger/index.html

**Initialize with sample data** 4. Navigate to `/` to browse recommendations

**Try the features**

- Search for media: "space western" or "slice of life romance"
- Rate a few titles to see personalization kick in
- Check your personal library and statistics

### Filters panel (AnimeRadar UX)

- Rating: dual-range 0–5 stars with 0.5 step. Moving one thumb pushes the other so min ≤ max.
- Year: dual-range over a rolling 30-year window up to the current year. Max at the top means “present”.
- Labels update live (e.g., “Rating: 2–4.5★”, “Year: 2010–present”).

- Cards and list rows show a horizontal star bar only on hover to reduce visual noise.
- Hovering a star previews the selection; clicking submits the rating immediately.
- Action clicks (rate/favorite/watched/dropped) take precedence over “open details” so you can rate without navigating away.

- Highlighting updates after render and whenever tag selection/weights change, so discovery feels responsive.

## Understanding the code structure

**Controllers/** - API endpoints following Koan's controller-only routing pattern
RecsController.cs - Recommendation queries and rating submission
UsersController.cs - User profile creation and statistics
AdminController.cs - Data seeding and system administration

```

RecsService.cs        - Core recommendation engine with hybrid scoring
SeedService.cs        - Background data import and processing
SettingsService.cs    - Configuration management for tunable parameters
```

**Models/** - Data contracts and entities

```
MediaDoc.cs          - Content metadata with optional vector embeddings
LibraryEntryDoc.cs   - User interaction tracking (ratings, favorites, status)
UserProfileDoc.cs    - Preference profiles with genre weights and vectors
SettingsDoc.cs       - System configuration (recommendation parameters)
```

**wwwroot/** - Static web interface using vanilla JavaScript

```
js/index.page.js     - Main browsing interface logic
js/dashboard.page.js - Administrative interface
js/api.js           - API client with error handling
js/cards.js         - Content rendering components
js/filters.js       - Search and filtering logic
```

## Authentication and login

S5.Recs uses Koan.Web.Auth for centralized provider discovery and login flows. Sessions are cookie-based; the UI sends requests with `credentials: 'include'` so the browser carries the session cookie.

- Discover providers (to render the Login menu):
  - GET `/.well-known/auth/providers` → descriptors `{ id, name, protocol, enabled, state, icon?, scopes? }`.
  - Present only `enabled` providers; `state` indicates basic config health (e.g., Healthy/Unhealthy).
- Start login (challenge/redirect):
  - GET `/auth/{provider}/challenge?return={relative-path}`.
  - Server sets short-lived `state` and `return` cookies and redirects to the IdP.
- Complete login (callback):
  - GET `/auth/{provider}/callback?code=...&state=...`.
  - On success, the server signs in using cookie scheme `Koan.cookie` and performs a local redirect to the sanitized return path (default `/`).
- Logout:
  - GET or POST `/auth/logout?return=/` to clear the session and redirect.

Prompt and re-authentication

- The UI appends `&prompt=login` to `/auth/{provider}/challenge` to prefer an explicit sign-in after logout.
- Actual behavior depends on the provider. In Dev, the TestProvider will show the form only when no remembered cookie exists; if present, it proceeds to issue a code without the form.

Return URL policy

- Return URLs must be a relative path, or match configured allow-listed prefixes.
- Configure via `Koan:Web:Auth:ReturnUrl:{ DefaultPath, AllowList[] }`.

Development TestProvider (optional)

- When the `Koan.Web.Auth.TestProvider` module is referenced, a local OAuth2 provider appears as "Test (Local)" (id: `test`).
- Normal login uses `/auth/test/challenge` and `/auth/test/callback` like other providers.
- The underlying dev IdP endpoints (internal exchange) are served at: `/.testoauth/authorize`, `/.testoauth/token`, `/.testoauth/userinfo`.
- First-time use shows a minimal HTML form for Name/Email and stores a local cookie to streamline subsequent logins.
- The central logout endpoint also deletes the TestProvider's `_tp_user` cookie so that logging out truly resets the next sign-in interaction during development.

## API reference

This sample exposes several REST endpoints that demonstrate different aspects of building recommendation APIs:

**Recommendations** (`/api/recs`)

- `POST /query` - Get personalized recommendations with optional filters and search text
- `POST /rate` - Submit user rating and automatically update preference profile

**User Library** (`/api/library`)

- `GET /{userId}` - Retrieve user's library with pagination, sorting, and status filtering
- `PUT /{userId}/{mediaId}` - Update favorite status, watch state, or rating
- `DELETE /{userId}/{mediaId}` - Remove item from user's library

**User Management** (`/api/users`)

- `GET /` - List all users (sample auto-creates default user if none exist)
- `POST /` - Create new user profile
- `GET /{id}/stats` - Get user's viewing statistics (favorites, watched, dropped counts)

**Administration** (`/admin`)

- `POST /seed/start` - Start background data import from external sources
- `GET /seed/status/{jobId}` - Check import progress and status
- `GET /stats` - System statistics (total content, users, vectors)
- `GET|POST /recs-settings` - View and update recommendation algorithm parameters

## Learning outcomes

**After exploring this sample, you'll understand:**

- How to design entities that support both simple queries and complex recommendation algorithms
- Techniques for building user preference models that improve over time
- Patterns for integrating AI services while maintaining system reliability
- Approaches to creating responsive web interfaces without complex frontend frameworks
- Methods for building administrative tools that support production operations
- Strategies for testing recommendation systems and measuring their effectiveness

**You'll also see practical implementations of:**

- Background job processing for data imports and AI operations
- Real-time UI updates with server-sent events
- Graceful error handling and system health monitoring
- Modular JavaScript architecture with event delegation
- RESTful API design following Koan conventions
- MongoDB document modeling for recommendation use cases

This sample serves as both a working application and a reference implementation for building similar systems in your own projects.
