# S10.DevPortal - Koan Framework Capabilities Demo

## Executive Summary

S10.DevPortal is a focused Developer Portal sample specifically designed to demonstrate Koan Framework's core capabilities through a streamlined content management domain. This sample showcases entity-first development, multi-provider transparency, bulk operations, set routing, threaded relationships, and capability-based conditional processing.

## Core Koan Framework Patterns

### 1. Clean Koan Initialization Pattern

Minimal Program.cs configuration using only framework auto-registration:

```csharp
// S10.DevPortal Program.cs - Clean framework initialization
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan()
    .AsProxiedApi();
var app = builder.Build();
app.Run();
```

### 2. Static Site Integration Pattern

- **Location**: `wwwroot/` directory for AngularJS frontend
- **API Client**: Clean `window.DevPortalApi` exposure
- **Architecture**: Simple script pattern with fetch API

### 3. Container Architecture Pattern

Docker Compose multi-provider development stack:

```yaml
# S10.DevPortal docker/compose.yml
services:
  api: # .NET 9 Koan application
  mongo: # MongoDB provider
  postgres: # PostgreSQL provider (demo switching)
```

### 4. EntityController Inheritance Pattern

Framework-provided CRUD with Koan capabilities:

- Controllers inherit from `EntityController<TEntity>`
- Auto-generated CRUD endpoints with pagination, sorting, filtering
- Multi-provider transparency and capability detection

## S10.DevPortal Domain Architecture - Koan Framework Showcase

### Streamlined Domain Models (Koan Capabilities Focus)

#### Content Management Core

```csharp
// Demonstrates Entity<T> with auto GUID v7 generation
public class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public ResourceType Type { get; set; } = ResourceType.Article;
    public string? TechnologyId { get; set; }  // Parent relationship demo
    public string AuthorId { get; set; } = "";  // User relationship demo
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPublished { get; set; } = false;
}

public enum ResourceType
{
    Article, Tutorial  // Simplified for demo focus
}
```

#### Technology Taxonomy System (Relationship Navigation Demo)

```csharp
// Demonstrates self-referencing hierarchy + soft relationships
public class Technology : Entity<Technology>
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ParentId { get; set; }  // Hierarchical relationships
    public List<string> RelatedIds { get; set; } = new();  // Soft relationships demo
    public string? OfficialUrl { get; set; }
}
```

#### User and Engagement System

```csharp
// Basic user entity for authentication demo
public class User : Entity<User>
{
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

// Threaded comment system (Relationship Navigation Demo)
public class Comment : Entity<Comment>
{
    public string ArticleId { get; set; } = "";  // Parent article
    public string UserId { get; set; } = "";     // Comment author
    public string? ParentCommentId { get; set; } = "";  // Threading support
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

## Koan Framework Capabilities Demonstrations

### 1. Multi-Provider Transparency

Demonstrate same API working across different storage providers:

```csharp
// Same controller code works with MongoDB, SQLite, or PostgreSQL
[Route("api/[controller]")]
public class ArticlesController : EntityController<Article>
{
    // All CRUD operations auto-generated
    // Provider selection happens at configuration level
}
```

Frontend provider switching:

```javascript
// wwwroot/js/demo.js - Provider transparency demo
window.DevPortalDemo = {
  async switchProvider(provider) {
    await fetch(`/api/demo/switch-provider/${provider}`, { method: "POST" });
    location.reload(); // Reload to show same data, different storage
  },
};
```

### 2. Bulk Operations Demo

```csharp
[HttpPost("bulk-import")]
public async Task<IActionResult> BulkImport([FromBody] List<Article> articles)
{
    // Demonstrates framework bulk capabilities
    await Article.UpsertMany(articles);
    return Ok(new { imported = articles.Count });
}

[HttpDelete("bulk-delete")]
public async Task<IActionResult> BulkDelete([FromBody] List<string> ids)
{
    var count = await Article.Remove(ids);
    return Ok(new { deleted = count });
}
```

Frontend bulk operations:

```javascript
// Bulk import/export demo buttons
async bulkImportSampleData() {
    const sampleArticles = this.generateSampleArticles(100);
    await fetch('/api/articles/bulk-import', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(sampleArticles)
    });
}
```

### 3. Set Routing Demonstrations

```csharp
// Different logical sets of the same entity type
[HttpGet("published")]
public async Task<IActionResult> GetPublished()
{
    return await GetCollection(); // Uses ?set=published query parameter
}

[HttpGet("drafts")]
public async Task<IActionResult> GetDrafts()
{
    return await GetCollection(); // Uses ?set=drafts query parameter
}
```

Frontend set switching:

```javascript
// Demonstrate set routing with same entity, different views
async loadArticles(setName = 'published') {
    const response = await fetch(`/api/articles?set=${setName}`);
    return response.json();
}
```

### 4. Capability Detection Demo

```javascript
// Show what each provider can do
async displayProviderCapabilities() {
    const caps = await fetch('/api/demo/capabilities').then(r => r.json());
    // Display: "Current provider supports: FullTextSearch, Pagination, Sorting"
    // Show degraded experience when capabilities missing
}
```

## Controller Architecture (Koan EntityController Demo)

```csharp
// Minimal controller - framework provides full CRUD automatically
[Route("api/[controller]")]
public class ArticlesController : EntityController<Article>
{
    // Inherits all CRUD operations automatically
    // Demonstrates bulk operations, set routing, capability detection
}

[Route("api/[controller]")]
public class TechnologiesController : EntityController<Technology>
{
    // Custom endpoint demonstrating relationship navigation
    [HttpGet("{id}/children")]
    public async Task<IActionResult> GetChildren(string id)
    {
        var tech = await Technology.Get(id);
        var children = await tech.GetChildren<Technology>();
        return Ok(children);
    }
}

[Route("api/[controller]")]
public class CommentsController : EntityController<Comment>
{
    // Demonstrates threaded comments with parent/child navigation
    [HttpGet("thread/{articleId}")]
    public async Task<IActionResult> GetCommentThread(string articleId)
    {
        var comments = await Comment.Query($"ArticleId == '{articleId}'");
        return Ok(BuildCommentTree(comments));
    }
}
```

## Frontend Architecture (Streamlined AngularJS Demo)

```
wwwroot/
├── index.html                 # Single page demo app
├── css/
│   └── style.css             # Basic styling
├── js/
│   ├── app.js                # AngularJS app setup
│   ├── api.js                # Koan API client
│   ├── demo.js               # Framework capability demos
│   └── controllers.js        # Article/Tech/Comment controllers
└── views/
    ├── articles.html         # Article list/management
    ├── technologies.html     # Technology hierarchy demo
    └── demo.html            # Koan capabilities showcase
```

### Koan-Focused API Client

```javascript
// js/api.js - Framework capabilities demonstration
window.DevPortalApi = {
  // Multi-provider transparency demo
  async switchProvider(provider) {
    return await fetch(`/api/demo/switch-provider/${provider}`, {
      method: "POST",
    });
  },

  // Set routing demo
  async getArticles(set = "published") {
    return await fetch(`/api/articles?set=${set}`).then((r) => r.json());
  },

  // Bulk operations demo
  async bulkImportArticles(articles) {
    return await fetch("/api/articles/bulk-import", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(articles),
    });
  },

  // Capability detection demo
  async getProviderCapabilities() {
    return await fetch("/api/demo/capabilities").then((r) => r.json());
  },

  // Relationship navigation demo
  async getTechnologyWithChildren(id) {
    const tech = await fetch(`/api/technologies/${id}`).then((r) => r.json());
    const children = await fetch(`/api/technologies/${id}/children`).then((r) =>
      r.json()
    );
    return { ...tech, children };
  },
};
```

## Implementation Details

### Multi-Provider Configuration

```csharp
// appsettings.json - Multiple providers for demo switching
{
  "Koan": {
    "Data": {
      "Mongo": { "Database": "DevPortal" },
      "Postgres": { "ConnectionString": "..." },
      "Sqlite": { "ConnectionString": "Data Source=devportal.db" }
    }
  }
}
```

### Docker Compose (Multi-Provider Demo Stack)

```yaml
# docker/compose.yml - Multiple databases for provider switching demo
name: koan-s10-devportal
services:
  api:
    build:
      context: ../../..
      dockerfile: samples/S10.DevPortal/Dockerfile
    ports:
      - "5090:5090"
    environment:
      ASPNETCORE_ENVIRONMENT: "Development"
    depends_on:
      - mongo
      - postgres

  mongo:
    image: mongo:latest
    ports:
      - "5091:27017"

  postgres:
    image: postgres:latest
    environment:
      POSTGRES_DB: devportal
      POSTGRES_PASSWORD: dev
    ports:
      - "5092:5432"
```

### Demo Data and Capabilities

```csharp
// Seed service for demo data
public class DemoSeedService
{
    public async Task SeedDemoData()
    {
        // Create sample technology hierarchy
        var dotnet = new Technology { Name = ".NET", Description = "Microsoft .NET Platform" };
        await dotnet.Save();

        var aspnet = new Technology { Name = "ASP.NET Core", ParentId = dotnet.Id };
        await aspnet.Save();

        // Bulk import sample articles
        var articles = GenerateSampleArticles(50);
        await Article.UpsertMany(articles);
    }
}
```

## Implementation Roadmap (Streamlined for Framework Demo)

### Phase 1: Core Framework Demo (Week 1)

1. **Project Setup**

   - Create S10.DevPortal project with clean Koan initialization
   - Configure multi-provider Docker Compose (MongoDB + PostgreSQL)
   - Implement 3 core entities: Article, Technology, Comment

2. **EntityController Demo**
   - Create minimal controllers inheriting from EntityController<T>
   - Verify auto-generated CRUD endpoints
   - Add custom endpoints for relationship navigation

### Phase 2: Koan Capabilities Showcase (Week 2)

1. **Multi-Provider Transparency**

   - Implement provider switching demo endpoints
   - Create frontend buttons to switch storage providers
   - Display provider capabilities and performance differences

2. **Bulk Operations & Set Routing**
   - Add bulk import/export demonstration
   - Implement set routing for published/draft articles
   - Create frontend demos for both capabilities

### Phase 3: Relationship & Frontend (Week 3)

1. **Relationship Navigation**

   - Implement technology hierarchy with GetChildren<T>()
   - Add threaded comment system with parent/child navigation
   - Create soft relationship demos between technologies

2. **AngularJS Frontend**
   - Simple single-page app showcasing framework capabilities
   - Provider switching UI
   - Bulk operations interface
   - Relationship navigation demos

### Phase 4: Polish & Demo Readiness (Week 4)

1. **Demo Data & Automation**

   - Automated demo data seeding
   - Performance comparison displays
   - Boot report and capability detection UI

2. **Documentation & Deployment**
   - Framework capability documentation
   - Deployment scripts and Docker optimization
   - Demo walkthrough guide

## Project Structure (Focused on Framework Demo)

```
samples/S10.DevPortal/
├── Controllers/               # Minimal EntityController inheritance
├── Models/                   # 3 entities demonstrating relationships
├── Services/                 # DemoSeedService, ProviderSwitchingService
├── wwwroot/                  # Simple AngularJS capability showcase
├── docker/compose.yml        # Multi-provider demo stack
├── Program.cs               # Clean AddKoan() pattern
└── README.md                # Framework capabilities walkthrough
```

## Success Criteria

This sample successfully demonstrates Koan Framework when:

1. **Same code works across MongoDB, PostgreSQL, SQLite** - Multi-provider transparency
2. **EntityController provides full CRUD with zero boilerplate** - Entity-first development
3. **Bulk operations handle 1000+ records efficiently** - Scalability demonstration
4. **Set routing shows same entity, different logical views** - Data organization patterns
5. **Relationship navigation works seamlessly** - Entity<T> relationship capabilities
6. **Frontend can switch providers and show capability differences** - Live capability detection
