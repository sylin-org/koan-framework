# AnimeRadar

**This sample demonstrates how to build a complete recommendation engine using Sora.**

AnimeRadar shows you how to create a modern content recommendation system by combining MongoDB for data storage, optional vector search with Weaviate, and AI embeddings via Ollama. The sample walks through building personalized recommendations, semantic search, user preference modeling, and a responsive web interface—all using Sora's modular architecture.

## What this sample teaches

**Core recommendation patterns**  
This sample demonstrates how to build a hybrid recommendation system that combines popularity-based filtering with personalized user preferences. You'll learn to track user behavior (ratings, favorites, watch status) and use that data to generate increasingly accurate recommendations.

**AI integration without complexity**  
The sample shows how to add semantic search and vector embeddings to your application while maintaining graceful fallbacks. When AI services are unavailable, the system continues working with popularity and preference-based recommendations.

**Modular architecture in practice**  
You'll see how Sora's modular design lets you start simple (just MongoDB) and add complexity incrementally (vector search, AI embeddings) without restructuring your core application code.

**Production-ready patterns**  
The sample implements real-world concerns: data seeding pipelines, admin dashboards, health monitoring, user management, and responsive UI design using vanilla JavaScript and modern CSS.

## How to build an app like this

**[1] Start with basic data modeling**  
First, you'll need entities to represent your content and user interactions. We use `AnimeDoc` for content metadata, `UserDoc` for user profiles, and `LibraryEntryDoc` to track user interactions with content.

**[2] Add simple recommendation logic**  
Begin with popularity-based recommendations filtered by user preferences. This gives you working recommendations immediately while you build more sophisticated features.

**[3] Implement user preference tracking**  
Build a system to track user behaviors (ratings, favorites, viewing status) and use that data to build preference profiles. The sample shows how to weight genres and tags based on user interactions.

**[4] Layer in semantic search**  
Add vector embeddings to enable semantic search ("find something like Cowboy Bebop") alongside keyword-based filtering. 

*But wait—what exactly is a vector database, and why would we need one?* Think of it this way: when you search for "space western," you're not just looking for anime with those exact words. You want shows that *feel* like space westerns—the themes, atmosphere, and storytelling style. Vector databases store mathematical representations (embeddings) of content that capture semantic meaning, not just keywords.

*How does this tie to user preferences?* As users rate content, we can build a "preference vector" that represents their taste in this mathematical space. Then we can find content that's similar to what they already love, even if it doesn't share obvious keywords.

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
public class AnimeDoc : Entity<AnimeDoc>
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
    var query = _animeCollection
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
public async Task UpdateUserPreferences(string userId, string animeId, int rating)
{
    // Get the anime to extract genres
    var anime = await _animeCollection.Find(a => a.Id == animeId).FirstOrDefaultAsync();
    if (anime == null) return;
    
    // Update user's genre preferences based on rating
    var profile = await GetOrCreateUserProfile(userId);
    foreach (var genre in anime.Genres)
    {
        // EWMA update: new preference = α * rating + (1-α) * old preference
        var currentWeight = profile.GenreWeights.GetValueOrDefault(genre, 0.5f);
        var normalizedRating = (rating - 3f) / 2f; // Convert 1-5 to -1 to 1
        profile.GenreWeights[genre] = 0.1f * normalizedRating + 0.9f * currentWeight;
    }
    
    await _profileCollection.ReplaceOneAsync(p => p.Id == profile.Id, profile);
}
```

*You might wonder: why use EWMA (Exponentially Weighted Moving Average) instead of just averaging ratings?* EWMA lets recent preferences influence the profile more than old ones, so if your taste changes, the system adapts. The α (alpha) value of 0.1 means new ratings have some impact but don't completely override your established preferences.

This creates user taste profiles that improve recommendations over time.

**Step 4: Integrate vector search for semantic matching**

*Here's where it gets interesting: how do we find content similar to what users already like?* Vector search lets you find semantically similar content:

```csharp
// Generate embeddings for new content
public async Task GenerateEmbeddings(AnimeDoc anime)
{
    var text = $"{anime.Title} {string.Join(" ", anime.Genres)} {anime.Synopsis}";
    var embedding = await _aiService.GetEmbeddingAsync(text);
    
    anime.Vector = embedding;
    await _animeCollection.ReplaceOneAsync(a => a.Id == anime.Id, anime);
}

// Find similar content using vector similarity
public async Task<List<AnimeDoc>> FindSimilar(string animeId, int count = 10)
{
    var source = await _animeCollection.Find(a => a.Id == animeId).FirstOrDefaultAsync();
    if (source?.Vector == null) return [];
    
    // Use vector database to find similar embeddings
    var similar = await _vectorDb.SearchAsync(source.Vector, count);
    return similar.Select(result => result.Payload).ToList();
}
```

*What exactly is a vector database, and why would we need one?* Think of it as a search engine that understands meaning, not just keywords. Traditional search looks for exact word matches. Vector search finds content with similar meanings—so searching "space cowboys" might surface "Firefly" even if that show's description never uses those exact words.

The vector database stores numerical representations (embeddings) of your content. When you search, it finds vectors that are "close" to your query vector in this multi-dimensional space. Closeness in vector space means similarity in meaning.

**Step 5: Combine approaches for hybrid recommendations**

*Why not just use one approach?* Different recommendation strategies excel in different situations:

- **Popularity-based**: Great for new users with no history
- **Preference-based**: Excellent for users with established tastes  
- **Vector/semantic**: Powerful for discovery and content-based similarity

The magic happens when you combine them:

```csharp
public async Task<List<AnimeDoc>> GetSemanticRecommendations(string userId, string? searchText = null)
{
    List<AnimeDoc> candidates;
    
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
        .Select(anime => new { 
            Anime = anime, 
            Score = CalculatePersonalizedScore(anime, userProfile) 
        })
        .OrderByDescending(x => x.Score)
        .Select(x => x.Anime)
        .Take(request.TopK)
        .ToList();
}
```

*Here's the key insight: vector search finds good candidates, but user preferences determine the final ranking.* This gives you both discovery (finding new content) and personalization (ranking by individual taste).

**Step 6: Build administrative interfaces**

*Why separate admin interfaces from user interfaces?* Administrative tasks like seeding data and monitoring systems have different requirements—they need detailed feedback, longer timeouts, and often handle large datasets that would overwhelm a user-facing UI.

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
            await _animeCollection.BulkWrite(/* upsert operations */);
            
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

*Why run this as a background task instead of blocking the API call?* Data seeding can take several minutes when processing thousands of items and generating embeddings. Users shouldn't have to wait with a frozen browser tab—they should get an immediate job ID and can check progress later.

*What are embeddings, exactly?* Think of them as "coordinates" in a multi-dimensional space where similar content clusters together. When we convert an anime's synopsis into an embedding vector, we're essentially saying "this story lives at coordinates [0.2, -0.5, 0.8, ...]" in a space where similar stories are nearby.

This provides essential operations tooling for production deployment.

**Step 7: Create a responsive user interface**

*Why vanilla JavaScript instead of a framework?* For this sample, we want to show core concepts without framework-specific complexity. The patterns you learn here work regardless of whether you later adopt React, Vue, or another framework.

Build a modern web interface using progressive enhancement:

```javascript
// Modular JavaScript architecture
class RecommendationEngine {
    async getRecommendations(filters = {}) {
        const response = await fetch('/api/recs/query', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                userId: this.currentUser.id,
                topK: 50,
                filters: filters,
                preferTags: this.getSelectedTags()
            })
        });
        
        if (!response.ok) {
            this.showError('Failed to load recommendations');
            return [];
        }
        
        return await response.json();
    }
    
    // Event delegation for dynamic content
    setupEventHandlers() {
        document.addEventListener('click', (e) => {
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

*Why use event delegation instead of binding events to each card?* When you have 100+ content cards, creating 100+ event listeners is inefficient. Event delegation uses JavaScript's event bubbling—one listener on the document catches clicks from all child elements. It's faster and works with dynamically added content.

The sample demonstrates how to build rich interactions without complex frontend frameworks.

## Key patterns and techniques demonstrated

**Hybrid recommendation scoring**  
*Why combine multiple approaches instead of picking the "best" one?* Different recommendation methods excel in different situations. Vector similarity excels at content discovery, user preferences ensure personal relevance, and popularity provides a safety net for new users. Combining them gives you the strengths of each approach.

**Graceful degradation**  
*What happens when AI services go down?* The system falls back to preference-based and popularity recommendations. This ensures your application remains functional even when optional services fail. Users might not get semantic search, but they still get recommendations.

**Real-time preference learning**  
*How quickly should the system adapt to new user behavior?* User interactions immediately update preference profiles using exponentially weighted moving averages (EWMA). This balances responsiveness to new preferences with stability from historical data—recent actions matter more, but don't completely override established patterns.

**Modular JavaScript architecture**  
*Why not use a big framework for the frontend?* This sample shows that modern vanilla JavaScript can handle complex interactions cleanly. The patterns here (event delegation, module organization, progressive enhancement) work in any framework—or no framework at all.

**Production data management**  
*What about real-world operations?* The sample includes comprehensive tooling: background job processing, data seeding pipelines, health monitoring, and administrative controls. These are the pieces you need for production deployment.

## What you'll build by following this sample

**A complete recommendation system** with personalized feeds, semantic search, and user preference modeling

**Modern web interface** with responsive design, real-time filtering, and accessible interactions

**Administrative tools** for content management, system monitoring, and algorithm tuning

**AI integration patterns** that enhance functionality without creating dependencies

**Production-ready architecture** with health checks, background processing, and graceful error handling

## Running the sample

**Prerequisites needed**
```bash
# Required: MongoDB for data storage
docker run -d -p 27017:27017 mongo:latest

# Optional: Weaviate for vector search
docker run -d -p 8080:8080 semitechnologies/weaviate:latest

# Optional: Ollama for local AI embeddings  
docker run -d -p 11434:11434 ollama/ollama
ollama pull nomic-embed-text
```

*Wait, why do we need all these services?* Great question! Let's break it down:

- **MongoDB** stores your content and user data—this is required
- **Weaviate** is a vector database that stores and searches embeddings—this enables semantic search like "find space westerns" 
- **Ollama** runs AI models locally to convert text into embeddings—this powers the semantic understanding

*Can't we just use regular text search?* You could, but you'd miss the magic. Vector search finds content based on meaning, not just keywords. "Romantic space adventure" might find you "Cowboy Bebop" even though those exact words don't appear in the description.

**Start the application**
```bash
cd samples/S5.Recs
start.bat
```

This script starts MongoDB in Docker and then launches the .NET application. The application will start on `https://localhost:5001` (or the next available port).

**Initialize with sample data**
1. Open `/dashboard` in your browser
2. Click "Seed Sample Data" to import anime from AniList API
3. Wait for the import to complete (progress shown in real-time)
4. Navigate to `/` to browse recommendations

**Try the features**
- Search for anime: "space western" or "slice of life romance"
- Rate a few titles to see personalization kick in
- Use genre filters and the "Try something new" tag selector
- Toggle between grid and list views
- Check your personal library and statistics

## Understanding the code structure

**Controllers/** - API endpoints following Sora's controller-only routing pattern
```
RecsController.cs     - Recommendation queries and rating submission
LibraryController.cs  - User library management (favorites, watch status)  
UsersController.cs    - User profile creation and statistics
AdminController.cs    - Data seeding and system administration
```

**Services/** - Business logic and recommendation algorithms
```
RecsService.cs        - Core recommendation engine with hybrid scoring
SeedService.cs        - Background data import and processing
SettingsService.cs    - Configuration management for tunable parameters
```

**Models/** - Data contracts and entities
```
AnimeDoc.cs          - Content metadata with optional vector embeddings
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

## API reference

This sample exposes several REST endpoints that demonstrate different aspects of building recommendation APIs:

**Recommendations** (`/api/recs`)
- `POST /query` - Get personalized recommendations with optional filters and search text
- `POST /rate` - Submit user rating and automatically update preference profile

**User Library** (`/api/library`)
- `GET /{userId}` - Retrieve user's library with pagination, sorting, and status filtering
- `PUT /{userId}/{animeId}` - Update favorite status, watch state, or rating
- `DELETE /{userId}/{animeId}` - Remove item from user's library

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
- RESTful API design following Sora conventions
- MongoDB document modeling for recommendation use cases

This sample serves as both a working application and a reference implementation for building similar systems in your own projects.
