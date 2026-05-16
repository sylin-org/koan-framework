# Embedding Best Practices

> **Production Guidance** for Koan Framework's attribute-driven embedding system

## Table of Contents

- [Cost Optimization](#cost-optimization)
- [Performance Optimization](#performance-optimization)
- [Quality & Accuracy](#quality--accuracy)
- [Production Operations](#production-operations)
- [Testing Strategies](#testing-strategies)
- [Security Considerations](#security-considerations)

---

## Cost Optimization

### Choose the Right Model for the Job

**Principle**: Not all content needs expensive embeddings.

```csharp
// ✅ Good: Free local embeddings for prototypes/dev
[Embedding(Source = "ollama-local", Model = "nomic-embed-text")]
public class DevPrototype : Entity<DevPrototype> { }

// ✅ Good: Expensive embeddings for revenue-critical features
[Embedding(Source = "openai-prod", Model = "text-embedding-3-large")]
public class PremiumRecommendation : Entity<PremiumRecommendation> { }

// ❌ Bad: Using expensive model for all entities
[Embedding(Model = "text-embedding-3-large")]  // $0.13/1M tokens
public class InternalLog : Entity<InternalLog> { }  // Logs don't need premium embeddings!
```

### Model Selection Matrix

| Use Case | Model | Cost/1M Tokens | When to Use |
|----------|-------|----------------|-------------|
| Development/Testing | nomic-embed-text (Ollama) | Free | Local development, CI/CD tests |
| High-volume, cost-sensitive | text-embedding-3-small | $0.02 | User-generated content, logs, internal search |
| Production search | text-embedding-3-large | $0.13 | E-commerce, documentation, customer-facing search |
| Multilingual | multilingual-e5-large | Varies | International content |

### Minimize Embedded Content

**Principle**: Only embed what you'll search for.

```csharp
// ❌ Bad: Embedding everything
[Embedding(Policy = EmbeddingPolicy.AllPublic)]
public class Article : Entity<Article>
{
    public string Title { get; set; }
    public string Content { get; set; }
    public string Author { get; set; }
    public string InternalNotes { get; set; }  // Don't need to search this!
    public string AuditLog { get; set; }       // Definitely not searchable!
}

// ✅ Good: Explicit control
[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Properties = new[] { nameof(Title), nameof(Summary) },
    Exclude = new[] { nameof(InternalNotes), nameof(AuditLog) }
)]
public class Article : Entity<Article>
{
    public string Title { get; set; }
    public string Summary { get; set; }  // Concise summary instead of full content

    [EmbeddingIgnore]
    public string Content { get; set; }  // Store but don't embed
}
```

### Use Token Limits

**Principle**: Prevent accidental cost explosions.

```csharp
// ✅ Good: Set explicit limits
[Embedding(
    Properties = new[] { nameof(Content) },
    MaxTokens = 2000,  // Cap at 2000 tokens (~8KB text)
    WarnOnTruncation = true
)]
public class BlogPost : Entity<BlogPost>
{
    public string Content { get; set; }
}
```

**Cost Impact:**
```
Without MaxTokens:
- 100 blog posts × 10,000 tokens avg = 1M tokens
- Cost: $0.13 (text-embedding-3-large)

With MaxTokens=2000:
- 100 blog posts × 2,000 tokens max = 200K tokens
- Cost: $0.026 (80% savings)
```

### Monitor and Alert

```csharp
// Set up cost alerts using telemetry
var telemetry = services.GetRequiredService<EmbeddingTelemetry>();
var stats = telemetry.CalculateStats(TimeSpan.FromDays(1));

if (stats.TotalCost > dailyBudget)
{
    logger.LogWarning("Daily embedding budget exceeded: ${Cost}", stats.TotalCost);
    // Send alert, pause async processing, etc.
}
```

---

## Performance Optimization

### Use Async for Bulk Operations

**Principle**: Don't block HTTP requests waiting for embeddings.

```csharp
// ❌ Bad: Blocking import (slows down user)
public async Task ImportProducts(List<ProductDto> dtos)
{
    foreach (var dto in dtos)
    {
        var product = MapToEntity(dto);
        await product.Save();  // Blocks for embedding generation (200-500ms each)
    }
    // Total time: 500ms × 1000 products = 8+ minutes!
}

// ✅ Good: Async processing (returns immediately)
[Embedding(Async = true, BatchSize = 100)]
public class Product : Entity<Product> { }

public async Task ImportProducts(List<ProductDto> dtos)
{
    foreach (var dto in dtos)
    {
        var product = MapToEntity(dto);
        await product.Save();  // Returns immediately, queued for background
    }
    // Total time: < 5 seconds (queue writes are fast)
}
```

### Batch Size Tuning

```csharp
// ✅ Good: Tune based on provider rate limits
[Embedding(
    Async = true,
    BatchSize = 100,              // Process 100 at a time
    RateLimitPerMinute = 1000     // OpenAI tier limit
)]
public class Product : Entity<Product> { }
```

**Guidelines:**
- OpenAI (free tier): `RateLimitPerMinute = 60`, `BatchSize = 20`
- OpenAI (paid tier): `RateLimitPerMinute = 3000`, `BatchSize = 100`
- Ollama (local): No limit, `BatchSize = 200+`
- Azure OpenAI: Check your deployment quota

### Cache Awareness

**Principle**: Leverage automatic content-based caching.

```csharp
// Framework caches embeddings by content signature
var article = await Article.Get("article-123");
article.Title = "New Title";
await article.Save();  // ✅ Cached - title not in embedding

article.Description = "New Description";
await article.Save();  // ❌ Cache miss - description IS in embedding (regenerates)
```

**Optimization:**
```csharp
// Group updates to minimize re-embeddings
article.Title = "New Title";
article.Author = "New Author";
article.Description = "New Description";  // Only change embedded fields last
await article.Save();  // Single embedding generation
```

### Choose Faster Models

```csharp
// Development: Use fast local models
#if DEBUG
[Embedding(Source = "ollama-local", Model = "nomic-embed-text")]  // ~50ms
#else
[Embedding(Source = "openai-prod", Model = "text-embedding-3-small")]  // ~200ms
#endif
public class Article : Entity<Article> { }
```

---

## Quality & Accuracy

### Write Search-Optimized Templates

**Principle**: Structure embedding text for better search results.

```csharp
// ❌ Bad: Generic concatenation
[Embedding(Policy = EmbeddingPolicy.AllStrings)]
// Generated: "iPhone 15 ProSmartphoneApple2023..."

// ✅ Good: Structured template with context
[Embedding(
    Template = @"Product: {Name}
Category: {Category}
Brand: {Brand}

{Description}

Key Features:
{Features}",
    Properties = new[] { nameof(Name), nameof(Category), nameof(Brand), nameof(Description), nameof(Features) }
)]
// Generated: "Product: iPhone 15 Pro
// Category: Smartphone
// Brand: Apple
//
// The most advanced iPhone ever...
//
// Key Features:
// A17 Pro chip, Titanium design, USB-C..."
```

### Handle Edge Cases

```csharp
[Embedding(
    Properties = new[] { nameof(Title), nameof(Description) },
    MaxTokens = 8191,
    WarnOnTruncation = true  // Alerts when content is truncated
)]
public class Article : Entity<Article>
{
    public string Title { get; set; }
    public string Description { get; set; }

    // ✅ Good: Provide a summary property for embedding
    public string Summary =>
        Description.Length > 1000
            ? Description.Substring(0, 1000) + "..."
            : Description;
}
```

### Test Search Quality

```csharp
[Theory]
[InlineData("wireless headphones", "Sony WH-1000XM5")]
[InlineData("noise cancelling", "Sony WH-1000XM5")]
[InlineData("bluetooth earbuds", "AirPods Pro")]
public async Task Semantic_Search_Returns_Expected_Product(string query, string expectedProduct)
{
    // Arrange
    var products = await SeedTestProducts();

    // Act
    var results = await EntityEmbeddingExtensions.SemanticSearch<Product>(
        query: query,
        limit: 10,
        threshold: 0.7
    );

    // Assert
    Assert.Contains(results, p => p.Name == expectedProduct);
}
```

### Use Appropriate Similarity Thresholds

```csharp
// Different thresholds for different use cases:

// ✅ Recommendations: Lower threshold (cast wide net)
var recommendations = await product.FindSimilar(
    limit: 10,
    threshold: 0.6  // 60% similarity
);

// ✅ Duplicate Detection: High threshold (precision)
var duplicates = await article.FindSimilar(
    limit: 5,
    threshold: 0.95  // 95% similarity
);

// ✅ Search: Medium threshold (balanced)
var results = await EntityEmbeddingExtensions.SemanticSearch<Article>(
    query: userQuery,
    limit: 20,
    threshold: 0.75  // 75% similarity
);
```

---

## Production Operations

### Version Management

**Principle**: Always version your embedding schemas.

```csharp
// ✅ Good: Explicit versioning
[Embedding(
    Properties = new[] { nameof(Title), nameof(Summary) },
    Version = 2  // Incremented from 1
)]
public class Article : Entity<Article>
{
    // Changed from "Description" to "Summary" - version bump required
    public string Summary { get; set; }
}
```

**Deployment Workflow:**
```bash
# 1. Deploy code with new version
git push origin main

# 2. Run migration (after deployment)
dotnet run --migrate-embeddings -- \
  --entity-type Article \
  --new-version 2 \
  --batch-size 100
```

### Zero-Downtime Migrations

```csharp
// Strategy: Dual-write during migration
public async Task MigrateWithZeroDowntime<TEntity>()
    where TEntity : class, IEntity<string>, new()
{
    // 1. Start background migration
    var migrationTask = EmbeddingMigrator.ReEmbedAll<TEntity>(
        batchSize: 100,
        parallel: false
    );

    // 2. New saves use new model (dual-write)
    // Old embeddings still searchable during migration

    // 3. Wait for migration complete
    await migrationTask;

    // 4. Clean up old embeddings (optional)
    await CleanupOldVersionEmbeddings<TEntity>();
}
```

### Monitoring

```csharp
// ✅ Good: Comprehensive monitoring setup
public void ConfigureEmbeddingMonitoring(IServiceCollection services)
{
    // Health checks
    services.AddHealthChecks()
        .AddCheck<EmbeddingHealthCheck>("embeddings", tags: new[] { "ai", "critical" });

    // Alerts on degraded state
    services.AddSingleton<IHostedService, EmbeddingHealthMonitor>();
}

public class EmbeddingHealthMonitor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var health = await _healthCheckService.CheckHealthAsync(stoppingToken);

            if (health.Status == HealthStatus.Degraded)
            {
                await _alerting.SendAlert("Embedding service degraded", health);
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

### Disaster Recovery

```csharp
// ✅ Good: Regular backups
public class EmbeddingBackupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH");
                await EmbeddingMigrator.ExportEmbeddings<Article>(
                    outputPath: $"backups/embeddings-article-{timestamp}.json"
                );

                // Upload to blob storage
                await UploadToS3($"embeddings-article-{timestamp}.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Embedding backup failed");
            }

            // Daily backups
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
```

---

## Testing Strategies

### Unit Tests: Embedding Generation

```csharp
[Fact]
public void EmbeddingMetadata_BuildsCorrectText()
{
    // Arrange
    var product = new Product
    {
        Name = "iPhone 15",
        Description = "Latest iPhone",
        Price = 999.99m
    };

    var metadata = EmbeddingMetadata.Get<Product>();

    // Act
    var text = metadata.BuildEmbeddingText(product);

    // Assert
    Assert.Contains("iPhone 15", text);
    Assert.Contains("Latest iPhone", text);
    Assert.DoesNotContain("999.99", text);  // Price not embedded
}
```

### Integration Tests: Search Quality

```csharp
[Fact]
public async Task SemanticSearch_FindsRelevantProducts()
{
    // Arrange
    await SeedDatabase();

    // Act
    var results = await EntityEmbeddingExtensions.SemanticSearch<Product>(
        query: "affordable wireless earbuds",
        limit: 10
    );

    // Assert
    Assert.All(results, p =>
    {
        Assert.True(p.Category == "Audio" || p.Category == "Accessories");
        Assert.True(p.Price < 200);  // "affordable"
    });
}
```

### Load Tests: Performance

```csharp
[Fact]
public async Task BulkImport_CompletesWithinSLA()
{
    // Arrange
    var products = GenerateProducts(count: 10_000);
    var stopwatch = Stopwatch.StartNew();

    // Act
    foreach (var product in products)
    {
        await product.Save();  // Async=true, queued
    }
    stopwatch.Stop();

    // Assert
    Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1),
        "Bulk import should complete in < 1 minute (async queueing)");

    // Wait for background processing
    await WaitForQueueEmpty(timeout: TimeSpan.FromMinutes(30));

    // Verify all embeddings generated
    var states = await EmbeddingState<Product>.Query(s => true);
    Assert.Equal(10_000, states.Count());
}
```

### Cost Tests

```csharp
[Fact]
public async Task Embedding_StaysWithinBudget()
{
    // Arrange
    var telemetry = GetService<EmbeddingTelemetry>();
    var initialStats = telemetry.CalculateStats(TimeSpan.FromDays(1));

    // Act
    await ImportProducts(count: 1000);
    await WaitForQueueEmpty();

    // Assert
    var finalStats = telemetry.CalculateStats(TimeSpan.FromDays(1));
    var cost = finalStats.TotalCost - initialStats.TotalCost;

    Assert.True(cost < 0.10m, $"1000 products should cost < $0.10, actual: ${cost:F4}");
}
```

---

## Security Considerations

### Don't Embed Sensitive Data

```csharp
// ❌ Bad: Embedding PII
[Embedding(Policy = EmbeddingPolicy.AllStrings)]
public class User : Entity<User>
{
    public string Name { get; set; }
    public string Email { get; set; }  // ⚠️ PII!
    public string SSN { get; set; }    // ⚠️ Sensitive!
}

// ✅ Good: Explicit exclusion
[Embedding(
    Policy = EmbeddingPolicy.Explicit,
    Properties = new[] { nameof(Bio), nameof(Interests) },
    Exclude = new[] { nameof(Email), nameof(SSN) }
)]
public class User : Entity<User>
{
    public string Bio { get; set; }
    public string[] Interests { get; set; }

    [EmbeddingIgnore]
    public string Email { get; set; }

    [EmbeddingIgnore]
    public string SSN { get; set; }
}
```

### API Key Management

```csharp
// ❌ Bad: Hardcoded API keys
[Embedding(Source = "openai-prod", Model = "text-embedding-3-large")]

// appsettings.json
{
  "Koan": {
    "AI": {
      "Sources": [{
        "Name": "openai-prod",
        "ApiKey": "sk-hardcoded-key"  // ❌ Don't do this!
      }]
    }
  }
}

// ✅ Good: Use secrets management
// Azure Key Vault, AWS Secrets Manager, etc.
{
  "Koan": {
    "AI": {
      "Sources": [{
        "Name": "openai-prod",
        "ApiKey": "${KeyVault:OpenAI-ApiKey}"  // ✅ Reference secret
      }]
    }
  }
}
```

### Access Control

```csharp
// ✅ Good: Partition embeddings by tenant
public async Task<List<Article>> SearchTenantArticles(string tenantId, string query)
{
    // Use partition context for multi-tenancy
    using (EntityContext.Partition(tenantId))
    {
        return await EntityEmbeddingExtensions.SemanticSearch<Article>(
            query: query,
            limit: 20
        );
    }
}
```

---

## Quick Reference

### Decision Tree: Sync vs Async

```
Is this a bulk import (>100 entities)?
├─ Yes → Use Async = true
└─ No
   └─ Is user waiting for result?
      ├─ Yes → Use Async = false (sync)
      └─ No → Use Async = true
```

### Decision Tree: Model Selection

```
What's your budget?
├─ Free → Ollama (nomic-embed-text)
└─ Paid
   └─ What's your quality requirement?
      ├─ High (customer-facing) → text-embedding-3-large
      ├─ Medium (internal tools) → text-embedding-3-small
      └─ Multilingual → multilingual-e5-large
```

### Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| High costs | Set `MaxTokens`, use cheaper models, minimize embedded content |
| Slow imports | Use `Async = true` |
| Poor search quality | Improve template structure, test threshold values |
| PII in embeddings | Use `Exclude` and `[EmbeddingIgnore]` |
| Cache not working | Increment `Version` after schema changes |
| Rate limit errors | Set `RateLimitPerMinute` based on provider tier |

---

## See Also

- [How-To: Embeddings](../how-to/embeddings.md) - Comprehensive usage guide
- [ADR AI-0020](../decisions/AI-0020-entity-first-ai-and-transaction-coordination.md) - Architecture decision
- [Sample: S5.Recs](../../samples/S5.Recs) - Production example
