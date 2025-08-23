# S5.Recs

**Anime recommendations shouldn't be rocket science. They should just work.**

S5.Recs demonstrates how to build a modern recommendation engine with Sora—complete with vector search, user personalization, and a polished UI. Start with basic popularity rankings, add AI-powered semantic search when you're ready, scale to complex preference modeling as you grow.

## What makes it compelling?

- **Start simple, scale smart**  
  Works perfectly with just MongoDB and popularity rankings. Add Weaviate for vector search and Ollama for embeddings when you want semantic matching—but they're optional.

- **Real user patterns**  
  Track favorites, ratings, watch status, and personal preferences. Build taste profiles that actually improve recommendations over time.

- **Production-ready patterns**  
  Graceful fallbacks, admin controls, real-time seeding, and health monitoring. Everything you'd expect in a production recommendation service.

- **Beautiful, responsive UI**  
  Browse anime with grid/list views, filter by genre and year, search semantically, and manage your personal library—all without writing frontend framework code.

## What you get out of the box

- **Personalized recommendations**  
  "For You" feed that learns from ratings, favorites, and viewing history

- **Semantic search**  
  Find anime by describing what you want: "space opera with political intrigue"

- **Smart filtering**  
  Genre, year, episode count, and rating filters with real-time tag suggestions

- **User library management**  
  Track favorites, watched status, ratings, and personal watchlists

- **Admin dashboard**  
  Data seeding, vector management, recommendation tuning, and system health

- **Adaptive UI**  
  Works on mobile, tablet, and desktop with keyboard navigation and accessibility support

## Real-world example

First, the essential packages are already included:

```bash
# Core framework
Sora.Core, Sora.Web

# Data access
Sora.Data.Mongo

# AI and vector search (optional)
Sora.AI, Sora.Ai.Provider.Ollama, Sora.Data.Weaviate
```

Then your models become instantly searchable:

```csharp
// Define your anime entity
public class AnimeDoc : Entity<AnimeDoc>
{
    public string Title { get; set; } = string.Empty;
    public List<string> Genres { get; set; } = [];
    public float Popularity { get; set; }
    public string Synopsis { get; set; } = string.Empty;
    
    // Vector embeddings automatically generated from synopsis
    public float[]? Vector { get; set; }
}

// Get full recommendation APIs
[Route("api/[controller]")]
public class RecsController : ControllerBase
{
    [HttpPost("query")]
    public async Task<RecsResponse> Query([FromBody] RecsRequest request)
    {
        // Hybrid scoring: vector similarity + popularity + user preferences
        var results = await _recsEngine.GetRecommendations(request);
        return results;
    }
}

// Use it naturally
var recs = await _recsEngine.GetRecommendations(new RecsRequest 
{ 
    UserId = user.Id,
    TopK = 50,
    Filters = new() { SpoilerSafe = true }
});
```

That's it. You now have:

- Personalized "For You" recommendations based on user history
- Semantic search: "Find me something like Cowboy Bebop but in space"
- Smart filtering by genre, year, popularity, and custom tags
- User library management with favorites, ratings, and watch status
- Admin dashboard for data management and recommendation tuning

**That's it.** Real AI-powered recommendations with graceful fallbacks when vectors aren't available.

## Want more? It's already there

**Need admin controls?**

Visit `/dashboard` to:
- Seed data from AniList or local JSON files
- Monitor recommendation engine health
- Tune algorithm weights in real-time
- View usage statistics and system metrics

**Want vector search?**

```bash
docker run -p 8080:8080 semitechnologies/weaviate:latest
```

Now your text searches become semantic: "romantic comedy in a school setting" finds relevant anime even without exact keyword matches.

**Need local AI embeddings?**

```bash
docker run -d -p 11434:11434 ollama/ollama
ollama pull nomic-embed-text
```

Your anime synopses are now automatically embedded for better similarity matching.

## The user experience

**Browse and discover**
- Grid or list view with beautiful cover art
- Real-time search with instant results  
- Filter by genre, year, rating, episode count
- "Try something new" with smart tag suggestions

**Personal library**
- Mark favorites with a single click
- Track watched/dropped status
- Rate anime from 1-5 stars
- View personalized statistics

**Smart recommendations**
- "For You" adapts to your taste over time
- Excludes already-watched content
- Boosts similar items to your favorites
- Introduces diversity to avoid echo chambers

## Technical architecture

**Built with Sora patterns**
- Controllers-only routing (no magic endpoints)
- Centralized constants (no scattered magic strings)
- Typed options configuration
- Clean separation of concerns

**Data design**
- User-centric library storage (`LibraryEntryDoc`)
- Preference profiles with genre/tag weights (`UserProfileDoc`)
- Tunable recommendation settings (`SettingsDoc`)
- Graceful schema evolution

**AI integration**
- Optional vector embeddings via Ollama
- Hybrid scoring (vector + popularity + preferences)
- Fallback to popularity when AI unavailable
- Real-time preference learning

## Getting started

1. **Prerequisites**
   ```bash
   # Required
   docker run -d -p 27017:27017 mongo:latest
   
   # Optional for vector search
   docker run -d -p 8080:8080 semitechnologies/weaviate:latest
   
   # Optional for embeddings
   docker run -d -p 11434:11434 ollama/ollama
   ```

2. **Run the sample**
   ```bash
   cd samples/S5.Recs
   dotnet run
   ```

3. **Seed some data**
   - Open `/dashboard`
   - Click "Seed Sample Data" to import from AniList
   - Or upload your own JSON files

4. **Start exploring**
   - Browse recommendations at `/`
   - Try searching: "space western" or "slice of life"
   - Rate a few anime and watch personalization kick in

## API surface

**Recommendations** (`/api/recs`)
- `POST /query` - Get personalized recommendations with filters
- `POST /rate` - Rate anime and update user preferences

**Library** (`/api/library`)  
- `GET /{userId}` - Get user's library with pagination and sorting
- `PUT /{userId}/{animeId}` - Update favorite/watched/rating status
- `DELETE /{userId}/{animeId}` - Remove from library

**Users** (`/api/users`)
- `GET /` - List users (sample creates default user)
- `POST /` - Create new user
- `GET /{id}/stats` - Get user's viewing statistics

**Admin** (`/admin`)
- Seeding: `POST /seed/start`, `GET /seed/status/{jobId}`
- Analytics: `GET /stats`, `GET /providers`
- Tuning: `GET|POST /recs-settings`

## What it teaches

**Modern web patterns**
- Hybrid SPA without complex frameworks
- Event delegation and modular JavaScript
- Accessible UI with ARIA support
- Mobile-responsive design

**Recommendation systems**
- User preference modeling
- Hybrid scoring (collaborative + content-based)
- Cold start handling
- Real-time personalization

**Production readiness**
- Graceful degradation
- Health monitoring
- Admin observability
- Performance optimization

## Built for

- **Learning modern .NET patterns** - See how Sora simplifies complex scenarios
- **Prototyping recommendation features** - Get ideas working fast
- **Understanding AI integration** - Vector search and embeddings made simple
- **Building production systems** - Patterns that scale to enterprise needs

## Community & support

- **Source code** - Fully documented and commented
- **Live demo** - See it running at localhost after `dotnet run`
- **Extensible** - Add new data sources, algorithms, or UI features

Built with ❤️ to show how recommendation engines should feel: powerful but not overwhelming, smart but predictable, complex under the hood but simple to use.

---

**Tech stack:** .NET 9, MongoDB, Weaviate (optional), Ollama (optional) | **UI:** Vanilla JS, Tailwind CSS | **Patterns:** Clean Architecture, Domain-Driven Design
