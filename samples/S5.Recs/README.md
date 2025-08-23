# S5.Recs

**This sample demonstrates how to build a complete recommendation engine using Sora.**

S5.Recs shows you how to create a modern content recommendation system by combining MongoDB for data storage, optional vector search with Weaviate, and AI embeddings via Ollama. The sample walks through building personalized recommendations, semantic search, user preference modeling, and a responsive web interface—all using Sora's modular architecture.

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
Add vector embeddings to enable semantic search ("find something like Cowboy Bebop") alongside keyword-based filtering. The sample demonstrates graceful fallback when vector services are unavailable.

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

This creates user taste profiles that improve recommendations over time.

**Step 4: Integrate vector search for semantic matching**

Add AI-powered semantic search alongside preference-based recommendations:

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
    
    // Apply user preference scoring
    return RankByUserPreferences(candidates, userId);
}
```

The key insight: always provide fallbacks. When vector search isn't available, the system continues working.

**Step 5: Build administrative interfaces**

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

This provides essential operations tooling for production deployment.

**Step 6: Create a responsive user interface**

Build a modern web interface using vanilla JavaScript and progressive enhancement:

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
        return await response.json();
    }
    
    async updateUserRating(animeId, rating) {
        await fetch('/api/recs/rate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                userId: this.currentUser.id,
                animeId: animeId,
                rating: rating
            })
        });
        
        // Refresh recommendations after rating
        await this.loadRecommendations();
    }
}
```

The sample demonstrates how to build rich interactions without complex frontend frameworks.

## Key patterns and techniques demonstrated

**Hybrid recommendation scoring**  
The sample shows how to combine multiple signals (vector similarity, popularity, user preferences) into a unified relevance score. This approach provides better results than any single method alone.

**Graceful degradation**  
When AI services are unavailable, the system falls back to preference-based and popularity recommendations. This ensures your application remains functional even when optional services fail.

**Real-time preference learning**  
User interactions immediately update preference profiles using exponentially weighted moving averages (EWMA). This balances responsiveness to new preferences with stability from historical data.

**Modular JavaScript architecture**  
The frontend demonstrates modern vanilla JavaScript patterns: event delegation, module organization, and progressive enhancement. No complex frameworks required.

**Production data management**  
The sample includes comprehensive tooling: background job processing, data seeding pipelines, health monitoring, and administrative controls.

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

**Start the application**
```bash
cd samples/S5.Recs
dotnet run
```

The application will start on `https://localhost:5001` (or the next available port).

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
