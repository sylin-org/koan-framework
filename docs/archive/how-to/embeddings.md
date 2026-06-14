# How-To: Entity Embeddings

> **Part of ADR AI-0020**: Entity-First AI Integration and Transaction Coordination

## Overview

Koan Framework provides automatic embedding generation for entities via the `[Embedding]` attribute. Embeddings enable semantic search, recommendations, and AI-powered features with minimal code.

**Key Features:**
- Automatic embedding generation on `entity.Save()`
- Transaction-aware vector operations (rollback safety)
- Multi-provider support (OpenAI, Ollama, Azure, etc.)
- Source routing for different models/environments
- Token management and cost tracking
- Background processing with rate limiting
- Health monitoring and telemetry

---

## Quick Start

### 1. Add the [Embedding] Attribute

Mark your entity for automatic embedding:

```csharp
using Koan.Data.AI.Attributes;
using Koan.Data.Core.Model;

[Embedding(
    Properties = new[] { nameof(Title), nameof(Description) },
    Async = false  // Embed immediately on Save()
)]
public sealed class Article : Entity<Article>
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? Content { get; set; }
}
```

### 2. Save an Entity

Embeddings are generated automatically:

```csharp
var article = new Article
{
    Id = Guid.CreateVersion7().ToString(),
    Title = "Introduction to Koan Framework",
    Description = "A modern .NET framework for building scalable applications"
};

await article.Save();  // Embedding generated and stored automatically
```

### 3. Perform Semantic Search

Find similar entities:

```csharp
using Koan.Data.AI;

// Search by natural language query
var results = await EntityEmbeddingExtensions.SemanticSearch<Article>(
    query: "building web applications with .NET",
    limit: 10,
    threshold: 0.7
);

// Find similar articles
var similar = await article.FindSimilar(limit: 5, threshold: 0.8);
```

---

## Embedding Policies

Control which properties are embedded:

### AllStrings (Default)

Automatically includes all `string` and `string[]` properties:

```csharp
[Embedding]  // Policy = EmbeddingPolicy.AllStrings (default)
public sealed class Product : Entity<Product>
{
    public string Name { get; set; }        // ✅ Embedded
    public string Description { get; set; }  // ✅ Embedded
    public decimal Price { get; set; }       // ❌ Not embedded (not a string)

    [EmbeddingIgnore]
    public string InternalNotes { get; set; } // ❌ Explicitly excluded
}
```

### Explicit Properties

Specify exactly which properties to embed:

```csharp
[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Properties = new[] { nameof(Title), nameof(Synopsis), nameof(Genres) }
)]
public sealed class Movie : Entity<Movie>
{
    public string Title { get; set; }     // ✅ Embedded
    public string Synopsis { get; set; }  // ✅ Embedded
    public string[] Genres { get; set; }  // ✅ Embedded
    public string Director { get; set; }  // ❌ Not specified
}
```

### Template-Based

Use a template to control text composition:

```csharp
[Embedding(
    Template = "{Title}\n\nGenres: {Genres}\n\n{Synopsis}",
    Properties = new[] { nameof(Title), nameof(Genres), nameof(Synopsis) }
)]
public sealed class Movie : Entity<Movie>
{
    public string Title { get; set; }
    public string[] Genres { get; set; }
    public string Synopsis { get; set; }
}

// Generated text:
// "Inception
//
// Genres: Sci-Fi, Thriller
//
// A thief who steals corporate secrets..."
```

### FullJson

Serialize entire entity to JSON (for complex objects):

```csharp
[Embedding(
    Policy = EmbeddingPolicy.FullJson,
    MaxDepth = 3,  // Prevent infinite recursion
    Exclude = new[] { "Id", "CreatedAt" }
)]
public sealed class ComplexEntity : Entity<ComplexEntity>
{
    public string Name { get; set; }
    public Address Address { get; set; }
    public List<Tag> Tags { get; set; }
}
```

---

## Advanced Configuration

### Source Routing

Route embeddings to specific AI providers:

```csharp
[Embedding(
    Properties = new[] { nameof(Title), nameof(Content) },
    Source = "openai-prod",     // Use production OpenAI
    Model = "text-embedding-3-large"
)]
public sealed class PremiumArticle : Entity<PremiumArticle>
{
    public string Title { get; set; }
    public string Content { get; set; }
}
```

**Environment-Specific Routing:**
```csharp
// Development: Use local Ollama
[Embedding(Source = "ollama-local", Model = "nomic-embed-text")]

// Production: Use Azure OpenAI
[Embedding(Source = "azure-embeddings", Model = "text-embedding-3-large")]
```

### Token Management

Set token limits to prevent truncation warnings:

```csharp
[Embedding(
    Properties = new[] { nameof(Title), nameof(Content) },
    MaxTokens = 8191,           // text-embedding-3-large limit
    WarnOnTruncation = true     // Log warning if truncated (default)
)]
public sealed class LongArticle : Entity<LongArticle>
{
    public string Title { get; set; }
    public string Content { get; set; }  // May be very long
}
```

**Token Estimation:**
- Uses ~4 chars/token heuristic
- Warns in development if content exceeds limit
- Automatically truncates to fit model constraints

### Async Processing

Queue embeddings for background processing:

```csharp
[Embedding(
    Properties = new[] { nameof(Title), nameof(Description) },
    Async = true,                // Queue for background worker
    BatchSize = 50,              // Process 50 at a time
    RateLimitPerMinute = 100     // Max 100 embeddings/minute
)]
public sealed class Product : Entity<Product>
{
    public string Title { get; set; }
    public string Description { get; set; }
}
```

**When to Use Async:**
- High-volume imports (thousands of entities)
- Rate-limited API providers
- Non-critical search features (can tolerate delay)

**Sync vs Async:**
```csharp
// Sync (default): Embedding generated immediately
await article.Save();  // Blocks until embedding complete

// Async: Job queued, save returns immediately
await product.Save();  // Returns fast, embedding processed in background
```

### Version Management

Force re-embedding when schema changes:

```csharp
[Embedding(
    Properties = new[] { nameof(Title), nameof(Summary) },
    Version = 2  // Increment to force re-embedding
)]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; }
    public string Summary { get; set; }  // Changed from "Description" to "Summary"
}
```

**Version Change Workflow:**
1. Update entity properties or template
2. Increment `Version` in `[Embedding]`
3. Run migration: `await EmbeddingMigrator.MigrateToVersion<Article>(newVersion: 2)`
4. All entities re-embedded with new schema

---

## Transaction Coordination

Embeddings participate in entity transactions:

```csharp
using var tx = EntityContext.BeginTransaction("save-article-with-vector");

try
{
    // Both entity and vector deferred until commit
    await article.Save();
    await relatedMetadata.Save();

    await tx.Commit();  // Both entity and embedding saved atomically
}
catch
{
    await tx.Rollback();  // Both entity and embedding rolled back
}
```

**Guarantees:**
- Entity saved ⟺ Embedding saved (no orphans)
- Rollback removes both entity and embedding
- Partial failures throw `VectorCoordinationException`

**Error Handling:**
```csharp
try
{
    await article.Save();
}
catch (VectorCoordinationException ex)
{
    if (ex.EntitySaved && !ex.VectorSaved)
    {
        // Entity in DB but embedding failed - queue for retry
        await EmbedJob<Article>.Enqueue(ex.EntityId);
    }
}
```

---

## Semantic Search

### Basic Search

Find entities matching a natural language query:

```csharp
var articles = await EntityEmbeddingExtensions.SemanticSearch<Article>(
    query: "machine learning tutorials",
    limit: 10,
    threshold: 0.7  // Minimum similarity (0-1)
);

foreach (var article in articles)
{
    Console.WriteLine($"{article.Title} - {article.Description}");
}
```

### Find Similar Entities

Get related items:

```csharp
var currentArticle = await Article.Get("article-123");

var similarArticles = await currentArticle.FindSimilar(
    limit: 5,
    threshold: 0.8,
    includeSource: false  // Exclude current article from results
);
```

### Filtered Search

Combine semantic and structured filters:

```csharp
// First: Semantic search for candidates
var candidates = await EntityEmbeddingExtensions.SemanticSearch<Article>(
    query: "kubernetes deployment",
    limit: 100,
    threshold: 0.6
);

// Then: Filter by structured criteria
var results = candidates
    .Where(a => a.PublishedAt > DateTimeOffset.UtcNow.AddMonths(-6))
    .Where(a => a.Category == "DevOps")
    .OrderByDescending(a => a.ViewCount)
    .Take(10);
```

---

## Provider Migration

### Switching Models

Migrate from Ollama to OpenAI:

```csharp
using Koan.Data.AI.Migration;

// Re-embed all articles with OpenAI
var result = await EmbeddingMigrator.ReEmbedAll<Article>(
    targetModel: "text-embedding-3-large",
    targetSource: "openai-prod",
    targetProvider: "openai",
    batchSize: 50,
    parallel: false,  // Sequential for safety
    logger: logger
);

Console.WriteLine($"Migrated {result.SuccessfulEntities}/{result.TotalEntities} articles");
Console.WriteLine($"Duration: {result.Duration}");
Console.WriteLine($"Success Rate: {result.SuccessRate:F1}%");
```

### Upgrading Model Versions

```csharp
// Upgrade from ada-002 to text-embedding-3-large
await EmbeddingMigrator.ReEmbedAll<Product>(
    targetModel: "text-embedding-3-large",
    batchSize: 100
);
```

### Backup & Restore

```csharp
// Export embeddings to JSON
await EmbeddingMigrator.ExportEmbeddings<Article>(
    outputPath: "embeddings-backup-2025-01-13.json"
);

// Clean up orphaned states (entities deleted but states remain)
var removed = await EmbeddingMigrator.CleanupOrphanedStates<Article>();
Console.WriteLine($"Removed {removed} orphaned embedding states");
```

---

## Monitoring & Observability

### Health Checks

Embedding health is automatically monitored:

```csharp
// In Program.cs - already configured by framework
app.MapHealthChecks("/health");
```

**Health Check Response:**
```json
{
  "status": "Healthy",
  "results": {
    "embeddings": {
      "status": "Healthy",
      "description": "Embedding service healthy (1245 embeddings, 0.4% error rate)",
      "data": {
        "total_embeddings": 1245,
        "successful_embeddings": 1240,
        "failed_embeddings": 5,
        "error_rate_percent": 0.4,
        "avg_latency_ms": 142.5,
        "p95_latency_ms": 289.3,
        "queue_pending": 12,
        "total_cost_usd": 0.003125
      }
    }
  }
}
```

### OpenTelemetry Metrics

Metrics are automatically exported:

```csharp
// Available metrics (viewable in Prometheus/Grafana):
// - koan.embeddings.generated.total (counter)
// - koan.embeddings.latency (histogram)
// - koan.embeddings.errors.total (counter)
// - koan.embeddings.tokens (histogram)
// - koan.embeddings.cost.total (counter)
// - koan.embeddings.queue.pending (gauge)
// - koan.embeddings.cache.hits.total (counter)
```

### Cost Tracking

Monitor embedding costs:

```csharp
using Koan.Data.AI.Telemetry;

// Get telemetry instance
var telemetry = app.Services.GetRequiredService<EmbeddingTelemetry>();

// Calculate stats for last 24 hours
var stats = telemetry.CalculateStats(TimeSpan.FromDays(1));

Console.WriteLine($"Total Embeddings: {stats.TotalEmbeddings}");
Console.WriteLine($"Success Rate: {stats.SuccessfulEmbeddings / (double)stats.TotalEmbeddings:P}");
Console.WriteLine($"Total Tokens: {stats.TotalTokens:N0}");
Console.WriteLine($"Total Cost: ${stats.TotalCost:F4}");
Console.WriteLine($"Avg Latency: {stats.AvgLatencyMs:F0}ms");
Console.WriteLine($"P95 Latency: {stats.P95LatencyMs:F0}ms");
```

**Cost by Model:**
```csharp
// Framework automatically tracks costs per model/provider
// View in your metrics dashboard:
// - koan.embeddings.model.cost{model="text-embedding-3-large",provider="openai"}
// - koan.embeddings.model.cost{model="nomic-embed-text",provider="ollama"}
```

---

## Configuration

### Worker Options

Configure background processing:

```json
// appsettings.json
{
  "Koan": {
    "Data": {
      "AI": {
        "EmbeddingWorker": {
          "Enabled": true,
          "BatchSize": 50,
          "GlobalRateLimitPerMinute": 100,
          "PollInterval": "00:00:05",
          "IdlePollInterval": "00:00:30",
          "MaxRetries": 3,
          "InitialRetryDelay": "00:00:10",
          "RetryBackoffMultiplier": 2.0,
          "MaxRetryDelay": "00:10:00",
          "AutoCleanupCompleted": true,
          "CompletedJobRetention": "7.00:00:00"
        }
      }
    }
  }
}
```

### AI Provider Configuration

Configure AI sources:

```json
{
  "Koan": {
    "AI": {
      "Sources": [
        {
          "Name": "ollama-local",
          "Provider": "ollama",
          "BaseUrl": "http://localhost:11434",
          "DefaultModel": "nomic-embed-text"
        },
        {
          "Name": "openai-prod",
          "Provider": "openai",
          "ApiKey": "sk-...",
          "DefaultModel": "text-embedding-3-large"
        },
        {
          "Name": "azure-embeddings",
          "Provider": "azure",
          "Endpoint": "https://myresource.openai.azure.com",
          "ApiKey": "...",
          "DefaultModel": "text-embedding-3-large"
        }
      ]
    }
  }
}
```

---

## Best Practices

### 1. Choose the Right Policy

- **Small entities** (< 5 properties): `AllStrings` or `Explicit`
- **Medium entities** (5-15 properties): `Template` for control
- **Complex entities** (nested objects): `FullJson`

### 2. Set Token Limits

Always set `MaxTokens` for production:
```csharp
[Embedding(
    Properties = new[] { nameof(Content) },
    MaxTokens = 8191,  // Model-specific limit
    WarnOnTruncation = true
)]
```

### 3. Use Async for Bulk Imports

```csharp
// Importing 10,000 products
foreach (var product in products)
{
    await product.Save();  // Queued, not blocking
}
// Worker processes in background at controlled rate
```

### 4. Version Your Schemas

Increment `Version` when changing:
- Properties list
- Template structure
- Policy type
- Exclude list

### 5. Monitor Costs

Track costs per entity type:
```csharp
// Use different sources for different entity types
[Embedding(Source = "ollama-local")]  // Free for prototypes
public class Prototype : Entity<Prototype> { }

[Embedding(Source = "openai-prod")]   // Paid for production
public class PremiumContent : Entity<PremiumContent> { }
```

### 6. Test Semantic Search Quality

```csharp
[Fact]
public async Task Semantic_Search_Returns_Relevant_Results()
{
    // Arrange
    var article = new Article
    {
        Title = "Introduction to Machine Learning",
        Content = "Machine learning is a subset of AI..."
    };
    await article.Save();

    // Act
    var results = await EntityEmbeddingExtensions.SemanticSearch<Article>(
        query: "AI and machine learning basics",
        limit: 10
    );

    // Assert
    Assert.Contains(results, a => a.Id == article.Id);
}
```

---

## Troubleshooting

### Embeddings Not Generated

**Check:**
1. Entity has `[Embedding]` attribute
2. Vector database configured (Milvus, Weaviate, etc.)
3. AI provider configured (Ollama, OpenAI, etc.)
4. Check logs for errors

```bash
# View logs
dotnet run
# Look for: "EmbeddingWorker started"
```

### High Latency

**Solutions:**
1. Use async processing: `Async = true`
2. Switch to faster model: `Model = "text-embedding-3-small"`
3. Use local provider: `Source = "ollama-local"`
4. Batch operations

### Token Limit Errors

**Fix:**
```csharp
[Embedding(
    MaxTokens = 8191,  // Set appropriate limit
    WarnOnTruncation = true  // Get notified
)]
```

Or reduce content:
```csharp
[Embedding(
    Template = "{Title}\n{Summary}",  // Don't embed full content
    Exclude = new[] { "FullContent" }
)]
```

### Cache Not Working

Embeddings are cached by content signature. Cache invalidates when:
- Entity properties change
- `Version` incremented
- Template modified

**Force re-embedding:**
```csharp
await EmbeddingMigrator.MigrateToVersion<Article>(newVersion: 2);
```

---

## Examples

### E-Commerce Product Search

```csharp
[Embedding(
    Template = "{Name}\n\nCategory: {Category}\n\n{Description}\n\nFeatures: {Features}",
    Properties = new[] { nameof(Name), nameof(Category), nameof(Description), nameof(Features) },
    MaxTokens = 8191,
    Async = true,
    RateLimitPerMinute = 100
)]
public sealed class Product : Entity<Product>
{
    public string Name { get; set; }
    public string Category { get; set; }
    public string Description { get; set; }
    public string[] Features { get; set; }

    [EmbeddingIgnore]
    public decimal Price { get; set; }
}

// Search
var results = await EntityEmbeddingExtensions.SemanticSearch<Product>(
    query: "wireless headphones with noise cancellation",
    limit: 20
);
```

### Document Management

```csharp
[Embedding(
    Properties = new[] { nameof(Title), nameof(Summary), nameof(Tags) },
    Source = "openai-prod",
    Model = "text-embedding-3-large",
    Version = 1
)]
public sealed class Document : Entity<Document>
{
    public string Title { get; set; }
    public string Summary { get; set; }
    public string[] Tags { get; set; }

    [EmbeddingIgnore]
    public byte[] FileContent { get; set; }  // Don't embed raw bytes
}
```

### Multi-Language Content

```csharp
[Embedding(
    Template = "{Title}\n\n{Content}",
    Properties = new[] { nameof(Title), nameof(Content) },
    Model = "multilingual-e5-large"  // Multilingual model
)]
public sealed class Article : Entity<Article>
{
    public string Title { get; set; }
    public string Content { get; set; }
    public string Language { get; set; }  // Not embedded, but filterable
}
```

---

## See Also

- [ADR AI-0020: Entity-First AI Integration](../decisions/AI-0020-entity-first-ai-and-transaction-coordination.md)
- [Vector Database Configuration](vector-databases.md)
- [AI Provider Configuration](ai-providers.md)
- [Sample: S5.Recs](../../samples/S5.Recs) - Media recommendation system
