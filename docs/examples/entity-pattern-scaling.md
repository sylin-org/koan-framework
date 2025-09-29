---
type: EXAMPLES
domain: core
title: "Entity<> Pattern Scaling: From Simple to Sophisticated"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Entity<> Pattern Scaling: From Simple to Sophisticated

**One pattern that grows with your ambition - practical examples from CRUD to enterprise architecture.**

**Target Audience**: Developers exploring Koan Framework's scaling capabilities
**Framework Version**: v0.2.18+

---

## The Beautiful Scaling Pattern

The `Entity<T>` pattern is Koan Framework's secret weapon: **one mental model that scales infinitely**. This document demonstrates how the same pattern elegantly handles every level of complexity.

### **Mental Model Consistency**

| **Domain** | **Pattern** | **Mental Model** |
|------------|-------------|------------------|
| **Data Operations** | `await user.Save()` | "Store this entity" |
| **Message Events** | `await event.Send()` | "Send this entity" |
| **AI Chat** | `await ai.Chat(prompt)` | "Process this input" |
| **File Storage** | `await file.Store()` | "Save this file entity" |
| **Vector Search** | `await Product.SemanticSearch(query)` | "Find similar entities" |
| **Cache Operations** | `await data.Cache()` | "Cache this entity" |

**The magic:** Same API, same patterns, growing capabilities.

---

## Stage 1: Basic Data Operations

**The Foundation:** Simple CRUD operations through Entity<> inheritance.

### **Basic Entity Model**
```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### **Basic Operations**
```csharp
// Create and save
var product = new Product {
    Name = "Wireless Headphones",
    Price = 99.99m,
    Category = "Electronics",
    Description = "High-quality wireless headphones"
};
await product.Save();  // Auto-generates GUID v7 ID

// Retrieve
var found = await Product.Get(product.Id);

// Query
var electronics = await Product.Query(p => p.Category == "Electronics");

// Update
product.Price = 89.99m;
await product.Save();  // Same method for create/update

// Delete
await product.Delete();
```

**Provider Transparency:**
```bash
# Development: SQLite (automatic)
dotnet add package Koan.Data.Sqlite
# Same code ↑ works with SQLite

# Production: PostgreSQL
dotnet add package Koan.Data.Postgres
# Same code ↑ now works with PostgreSQL
```

**Feeling:** *"This is how data access should work."*

---

## Stage 2: Event-Driven Messaging

**The Extension:** Same pattern extends to messaging and events.

### **Event Entities**
```csharp
// Events are entities too - same inheritance pattern
public class ProductPriceChanged : Entity<ProductPriceChanged>
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string ChangedBy { get; set; } = "";
}

public class ProductOutOfStock : Entity<ProductOutOfStock>
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int LastQuantity { get; set; }
    public DateTime OutOfStockAt { get; set; } = DateTime.UtcNow;
}
```

### **Business Logic with Events**
```csharp
public class ProductsController : EntityController<Product>
{
    public override async Task<ActionResult<Product>> Put(string id, Product product)
    {
        var existing = await Product.Get(id);
        if (existing == null) return NotFound();

        // Business logic
        if (product.Price != existing.Price)
        {
            // Send event - same .Send() pattern as .Save()
            await new ProductPriceChanged {
                ProductId = product.Id,
                ProductName = product.Name,
                OldPrice = existing.Price,
                NewPrice = product.Price,
                ChangedBy = User.Identity?.Name ?? "System"
            }.Send();
        }

        // Standard save operation
        return await base.Put(id, product);
    }
}
```

### **Event Handlers**
```csharp
// Event handlers using same Entity<> patterns
Flow.OnCreate<ProductPriceChanged>(async (priceChanged) => {
    Console.WriteLine($"Price alert: {priceChanged.ProductName} " +
                     $"changed from ${priceChanged.OldPrice} to ${priceChanged.NewPrice}");

    // Could trigger notifications, analytics updates, etc.
    // All using the same Entity<> patterns
    return UpdateResult.Continue();
});

Flow.OnUpdate<Product>(async (product, previous) => {
    // Automatic stock monitoring
    if (product.StockQuantity == 0 && previous.StockQuantity > 0)
    {
        await new ProductOutOfStock {
            ProductId = product.Id,
            ProductName = product.Name,
            LastQuantity = previous.StockQuantity
        }.Send();
    }

    return UpdateResult.Continue();
});
```

**Reference = Intent:**
```bash
# Want messaging? Reference it.
dotnet add package Koan.Messaging.InMemory    # Development
dotnet add package Koan.Messaging.RabbitMq   # Production
# Events now route through chosen message provider
```

**Feeling:** *"Events feel just like data operations."*

---

## Stage 3: AI-Native Integration

**The Enhancement:** AI capabilities through familiar patterns.

### **AI-Enhanced Entity**
```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // AI-searchable content (when Koan.Data.Vector is referenced)
    [VectorSearchable]
    public string SearchableContent => $"{Name} {Description} {Category}";
}
```

### **AI Operations**
```csharp
public class ProductsController : EntityController<Product>
{
    private readonly IAiService _ai;

    public ProductsController(IAiService ai) => _ai = ai;

    [HttpPost("generate-description")]
    public async Task<ActionResult<string>> GenerateDescription([FromBody] DescriptionRequest request)
    {
        // AI integration feels like any other service call
        var description = await _ai.Chat($@"
            Generate a compelling product description for:
            Name: {request.ProductName}
            Category: {request.Category}
            Key Features: {request.Features}

            Make it engaging and SEO-friendly.
        ");

        return Ok(description);
    }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IEnumerable<Product>>> SemanticSearch([FromQuery] string query)
    {
        // Semantic search through familiar Entity<> patterns
        var results = await Product.SemanticSearch(query);
        return Ok(results);
    }

    [HttpPost("{id}/ai-pricing")]
    public async Task<ActionResult<decimal>> GetAIPricingSuggestion(string id)
    {
        var product = await Product.Get(id);
        if (product == null) return NotFound();

        // AI-powered pricing analysis
        var pricingAnalysis = await _ai.Chat($@"
            Analyze pricing for this product:
            Name: {product.Name}
            Current Price: ${product.Price}
            Category: {product.Category}
            Description: {product.Description}

            Suggest optimal pricing based on market analysis.
            Return just the recommended price as a decimal.
        ");

        var suggestedPrice = decimal.Parse(pricingAnalysis);
        return Ok(suggestedPrice);
    }
}
```

### **AI-Enhanced Events**
```csharp
public class ProductCreated : Entity<ProductCreated>
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// AI-powered event processing
Flow.OnCreate<ProductCreated>(async (created) => {
    var ai = serviceProvider.GetService<IAiService>();

    // Generate marketing copy automatically
    var marketingCopy = await ai.Chat($@"
        A new product was added: {created.ProductName} in {created.Category}.
        Generate:
        1. Social media post
        2. Email marketing snippet
        3. SEO keywords

        Return as structured JSON.
    ");

    // Send marketing content creation event
    await new MarketingContentGenerated {
        ProductId = created.ProductId,
        Content = marketingCopy,
        GeneratedAt = DateTime.UtcNow
    }.Send();

    return UpdateResult.Continue();
});
```

**Reference = Intent:**
```bash
# Want AI? Reference it.
dotnet add package Koan.AI.Ollama        # Local AI
dotnet add package Koan.AI.OpenAI       # Cloud AI
dotnet add package Koan.Data.Vector     # Semantic search

# AI now integrates with Entity<> patterns
```

**Feeling:** *"AI capabilities feel native, not bolted-on."*

---

## Stage 4: Multi-Provider Sophistication

**The Scaling:** Same entities, multiple storage strategies.

### **Provider-Specific Entity Strategies**
```csharp
// Core product data - PostgreSQL for ACID compliance
[SourceAdapter("postgres")]
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";

    [VectorSearchable]
    public string SearchableContent => $"{Name} {Description} {Category}";
}

// High-frequency analytics - MongoDB for flexible schema
[SourceAdapter("mongo")]
public class ProductAnalytics : Entity<ProductAnalytics>
{
    public string ProductId { get; set; } = "";
    public Dictionary<string, object> Metrics { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// Session data - Redis for performance
[SourceAdapter("redis")]
public class ProductViewSession : Entity<ProductViewSession>
{
    public string ProductId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan ViewDuration { get; set; }
}

// Search embeddings - Vector DB for semantic operations
[SourceAdapter("vector")]
public class ProductEmbedding : Entity<ProductEmbedding>
{
    public string ProductId { get; set; } = "";
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

### **Sophisticated Business Logic**
```csharp
public class AdvancedProductsController : EntityController<Product>
{
    private readonly IAiService _ai;

    public AdvancedProductsController(IAiService ai) => _ai = ai;

    public override async Task<ActionResult<Product>> Post(Product product)
    {
        // 1. Save core product data (PostgreSQL)
        var result = await base.Post(product);

        // 2. Generate AI embedding for semantic search (Vector DB)
        var embedding = await _ai.GetEmbedding(product.SearchableContent);
        await new ProductEmbedding {
            ProductId = product.Id,
            Embedding = embedding,
            Metadata = new Dictionary<string, object> {
                ["category"] = product.Category,
                ["price_range"] = GetPriceRange(product.Price),
                ["created_at"] = product.CreatedAt
            }
        }.Save();

        // 3. Initialize analytics tracking (MongoDB)
        await new ProductAnalytics {
            ProductId = product.Id,
            Metrics = new Dictionary<string, object> {
                ["views"] = 0,
                ["impressions"] = 0,
                ["conversion_rate"] = 0.0
            }
        }.Save();

        // 4. Send creation event for downstream processing
        await new ProductCreated {
            ProductId = product.Id,
            ProductName = product.Name,
            Category = product.Category
        }.Send();

        return result;
    }

    [HttpGet("{id}/comprehensive")]
    public async Task<ActionResult<object>> GetComprehensiveProduct(string id)
    {
        // Parallel queries across multiple providers
        var tasks = new Task[] {
            Product.Get(id),                                    // PostgreSQL
            ProductAnalytics.Query(a => a.ProductId == id),    // MongoDB
            ProductViewSession.Query(v => v.ProductId == id),  // Redis
            ProductEmbedding.Get(id)                           // Vector DB
        };

        await Task.WhenAll(tasks);

        var product = ((Task<Product?>)tasks[0]).Result;
        var analytics = ((Task<IReadOnlyList<ProductAnalytics>>)tasks[1]).Result;
        var sessions = ((Task<IReadOnlyList<ProductViewSession>>)tasks[2]).Result;
        var embedding = ((Task<ProductEmbedding?>)tasks[3]).Result;

        return Ok(new {
            Product = product,
            Analytics = analytics.FirstOrDefault()?.Metrics,
            RecentSessions = sessions.Take(10),
            HasSemanticSearch = embedding != null
        });
    }

    [HttpGet("intelligent-recommendations/{id}")]
    public async Task<ActionResult<IEnumerable<Product>>> GetIntelligentRecommendations(string id)
    {
        // 1. Get semantic similarity (Vector DB)
        var similarProducts = await Product.SemanticSearch($"similar to product {id}", limit: 10);

        // 2. Get behavioral analytics (MongoDB)
        var analytics = await ProductAnalytics.Query(a =>
            similarProducts.Select(p => p.Id).Contains(a.ProductId));

        // 3. AI-powered ranking based on multiple signals
        var rankingPrompt = $@"
            Rank these products for recommendation based on:
            Semantic similarity: {string.Join(", ", similarProducts.Select(p => p.Name))}
            Analytics data: {string.Join(", ", analytics.Select(a => $"{a.ProductId}:{a.Metrics}"))}

            Return top 5 product IDs in order of recommendation strength.
        ";

        var rankedIds = await _ai.Chat(rankingPrompt);
        // Parse and return ranked products...

        return Ok(similarProducts.Take(5));
    }
}
```

**Configuration Strategy:**
```json
{
  "ConnectionStrings": {
    "postgres": "Host=localhost;Database=products;Username=app;Password=***",
    "mongo": "mongodb://localhost:27017/analytics",
    "redis": "localhost:6379",
    "vector": "http://localhost:8080"
  }
}
```

**Feeling:** *"Enterprise complexity through simple patterns."*

---

## Stage 5: Event-Driven Enterprise Architecture

**The Sophistication:** Complete event-driven system with AI and multi-provider patterns.

### **Complex Event Workflows**
```csharp
// Multi-stage event processing pipeline
Flow.OnUpdate<Product>(async (product, previous) => {
    var events = new List<Task>();

    // Price change workflow
    if (product.Price != previous.Price)
    {
        events.Add(new ProductPriceChanged {
            ProductId = product.Id,
            ProductName = product.Name,
            OldPrice = previous.Price,
            NewPrice = product.Price,
            ChangePercentage = ((product.Price - previous.Price) / previous.Price) * 100
        }.Send());

        // AI analysis of price change impact
        var ai = serviceProvider.GetService<IAiService>();
        var impactAnalysis = await ai.Chat($@"
            Product '{product.Name}' price changed from ${previous.Price} to ${product.Price}.
            Analyze:
            1. Market positioning impact
            2. Customer reaction prediction
            3. Competitor response likelihood
            4. Revenue impact forecast

            Provide actionable insights.
        ");

        events.Add(new PriceChangeAnalysis {
            ProductId = product.Id,
            Analysis = impactAnalysis,
            AnalyzedAt = DateTime.UtcNow
        }.Send());
    }

    // Stock level monitoring
    if (product.StockQuantity != previous.StockQuantity)
    {
        events.Add(new ProductAnalytics {
            ProductId = product.Id,
            Metrics = new Dictionary<string, object> {
                ["stock_change"] = product.StockQuantity - previous.StockQuantity,
                ["previous_stock"] = previous.StockQuantity,
                ["current_stock"] = product.StockQuantity,
                ["timestamp"] = DateTime.UtcNow
            }
        }.Save());

        // Low stock alerts
        if (product.StockQuantity <= 10 && previous.StockQuantity > 10)
        {
            events.Add(new LowStockAlert {
                ProductId = product.Id,
                ProductName = product.Name,
                CurrentStock = product.StockQuantity,
                AlertLevel = product.StockQuantity <= 5 ? "Critical" : "Warning"
            }.Send());
        }
    }

    // Category change implications
    if (product.Category != previous.Category)
    {
        events.Add(new ProductCategoryChanged {
            ProductId = product.Id,
            ProductName = product.Name,
            OldCategory = previous.Category,
            NewCategory = product.Category
        }.Send());

        // Regenerate AI embeddings for new category context
        var ai = serviceProvider.GetService<IAiService>();
        var newEmbedding = await ai.GetEmbedding(product.SearchableContent);

        events.Add(new ProductEmbedding {
            ProductId = product.Id,
            Embedding = newEmbedding,
            Metadata = new Dictionary<string, object> {
                ["category"] = product.Category,
                ["recategorization_date"] = DateTime.UtcNow,
                ["previous_category"] = previous.Category
            }
        }.Save());
    }

    // Execute all events in parallel
    await Task.WhenAll(events);

    return UpdateResult.Continue();
});

// Complex event orchestration
Flow.OnCreate<ProductPriceChanged>(async (priceChanged) => {
    var workflows = new List<Task>();

    // Customer communication workflow
    workflows.Add(new CustomerPriceNotification {
        ProductId = priceChanged.ProductId,
        ProductName = priceChanged.ProductName,
        PriceChange = priceChanged.NewPrice - priceChanged.OldPrice,
        NotificationType = priceChanged.NewPrice > priceChanged.OldPrice ? "Increase" : "Decrease"
    }.Send());

    // Competitor analysis workflow
    var ai = serviceProvider.GetService<IAiService>();
    var competitorAnalysis = await ai.Chat($@"
        Our product '{priceChanged.ProductName}' price changed to ${priceChanged.NewPrice}.
        Analyze competitor positioning and suggest marketing strategy adjustments.
    ");

    workflows.Add(new CompetitorAnalysisUpdate {
        ProductId = priceChanged.ProductId,
        Analysis = competitorAnalysis,
        TriggeredBy = "PriceChange"
    }.Send());

    // Inventory optimization workflow
    if (priceChanged.NewPrice < priceChanged.OldPrice) // Price decrease
    {
        workflows.Add(new InventoryOptimizationTrigger {
            ProductId = priceChanged.ProductId,
            Reason = "PriceReduction",
            ExpectedDemandIncrease = CalculateExpectedDemandIncrease(priceChanged)
        }.Send());
    }

    await Task.WhenAll(workflows);
    return UpdateResult.Continue();
});
```

### **AI-Powered Decision Making**
```csharp
Flow.OnCreate<LowStockAlert>(async (alert) => {
    var ai = serviceProvider.GetService<IAiService>();

    // Get product and analytics context
    var product = await Product.Get(alert.ProductId);
    var analytics = await ProductAnalytics.Query(a =>
        a.ProductId == alert.ProductId &&
        a.Timestamp >= DateTime.UtcNow.AddDays(-30));

    // AI-powered restocking recommendation
    var restockingAnalysis = await ai.Chat($@"
        Product '{alert.ProductName}' is low in stock ({alert.CurrentStock} units).

        Historical data (last 30 days):
        {string.Join("\n", analytics.Select(a => $"- {a.Timestamp:yyyy-MM-dd}: {a.Metrics}"))}

        Product details:
        - Category: {product.Category}
        - Price: ${product.Price}
        - Current status: {alert.AlertLevel}

        Recommend:
        1. Optimal restock quantity
        2. Urgency level (1-10)
        3. Suggested suppliers if applicable
        4. Marketing actions to manage demand

        Provide actionable recommendations.
    ");

    // Generate restocking recommendation event
    await new RestockingRecommendation {
        ProductId = alert.ProductId,
        ProductName = alert.ProductName,
        CurrentStock = alert.CurrentStock,
        AIRecommendation = restockingAnalysis,
        UrgencyLevel = ExtractUrgencyLevel(restockingAnalysis),
        GeneratedAt = DateTime.UtcNow
    }.Send();

    return UpdateResult.Continue();
});
```

**Feeling:** *"Enterprise event-driven architecture feels manageable and powerful."*

---

## Stage 6: Full Ecosystem Integration

**The Complete Picture:** AI + Events + Multi-Provider + File Storage + Caching + External APIs.

### **Comprehensive Entity Ecosystem**
```csharp
// Core product with full ecosystem integration
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";

    // Multi-provider attributes
    [VectorSearchable]
    public string SearchableContent => $"{Name} {Description} {Category}";

    [Cacheable(ExpirationMinutes = 60)]
    public bool IsHighDemand => ViewCount > 1000;

    [FileStorage]
    public List<string> ImageUrls { get; set; } = new();

    // Analytics tracking
    public int ViewCount { get; set; }
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }
}
```

### **Sophisticated Controller Integration**
```csharp
public class EcosystemProductsController : EntityController<Product>
{
    private readonly IAiService _ai;
    private readonly IFileStorageService _files;
    private readonly IExternalApiService _external;

    public EcosystemProductsController(
        IAiService ai,
        IFileStorageService files,
        IExternalApiService external)
    {
        _ai = ai;
        _files = files;
        _external = external;
    }

    [HttpPost("with-images")]
    public async Task<ActionResult<Product>> CreateProductWithImages(
        [FromForm] CreateProductRequest request,
        [FromForm] List<IFormFile> images)
    {
        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            Category = request.Category,
            Description = request.Description
        };

        // Multi-provider orchestration
        var tasks = new List<Task>();

        // 1. Save core product data (PostgreSQL)
        tasks.Add(product.Save());

        // 2. Process and store images (File Storage)
        if (images.Any())
        {
            var imageUrls = new List<string>();
            foreach (var image in images)
            {
                var processedImage = await ProcessImageWithAI(image);
                var url = await _files.StoreAsync(processedImage, $"products/{product.Id}");
                imageUrls.Add(url);
            }
            product.ImageUrls = imageUrls;
        }

        // 3. Generate AI embedding (Vector DB)
        tasks.Add(GenerateProductEmbedding(product));

        // 4. Initialize analytics (MongoDB)
        tasks.Add(InitializeProductAnalytics(product));

        // 5. Cache for performance (Redis)
        tasks.Add(CacheProductData(product));

        // 6. External API integrations
        tasks.Add(RegisterWithExternalServices(product));

        await Task.WhenAll(tasks);

        // 7. Send comprehensive creation event
        await new ProductEcosystemCreated
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Category = product.Category,
            HasImages = product.ImageUrls.Any(),
            IntegratedServices = new[] { "Vector", "Analytics", "Cache", "External" }
        }.Send();

        return Ok(product);
    }

    [HttpGet("{id}/ecosystem-status")]
    public async Task<ActionResult<object>> GetEcosystemStatus(string id)
    {
        // Parallel status checks across all providers
        var tasks = new Dictionary<string, Task>
        {
            ["product"] = Product.Get(id),
            ["analytics"] = ProductAnalytics.Query(a => a.ProductId == id),
            ["cache"] = CheckCacheStatus(id),
            ["files"] = CheckFileStorageStatus(id),
            ["vector"] = CheckVectorEmbeddingStatus(id),
            ["external"] = CheckExternalApiStatus(id)
        };

        await Task.WhenAll(tasks.Values);

        return Ok(new
        {
            ProductId = id,
            Ecosystem = new
            {
                Core = tasks["product"].IsCompletedSuccessfully,
                Analytics = tasks["analytics"].IsCompletedSuccessfully,
                Cache = tasks["cache"].IsCompletedSuccessfully,
                Files = tasks["files"].IsCompletedSuccessfully,
                Vector = tasks["vector"].IsCompletedSuccessfully,
                External = tasks["external"].IsCompletedSuccessfully
            },
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task<IFormFile> ProcessImageWithAI(IFormFile image)
    {
        // AI-powered image optimization
        var analysis = await _ai.AnalyzeImage(image);
        var optimizationInstructions = await _ai.Chat($@"
            Image analysis: {analysis}
            Provide optimization instructions for product photography:
            1. Recommended crop/resize
            2. Color enhancement needs
            3. Background removal suggestions
        ");

        // Apply AI recommendations (simplified)
        return await ApplyImageOptimizations(image, optimizationInstructions);
    }

    private async Task GenerateProductEmbedding(Product product)
    {
        var embedding = await _ai.GetEmbedding(product.SearchableContent);
        await new ProductEmbedding
        {
            ProductId = product.Id,
            Embedding = embedding,
            Metadata = new Dictionary<string, object>
            {
                ["category"] = product.Category,
                ["price_range"] = GetPriceRange(product.Price),
                ["has_images"] = product.ImageUrls.Any(),
                ["created_at"] = DateTime.UtcNow
            }
        }.Save();
    }
}
```

**Configuration for Full Ecosystem:**
```json
{
  "ConnectionStrings": {
    "postgres": "Host=prod-db;Database=products;Username=***",
    "mongo": "mongodb://analytics-cluster/productanalytics",
    "redis": "prod-cache:6379",
    "vector": "http://vector-db:8080"
  },
  "Koan": {
    "AI": {
      "Provider": "OpenAI",
      "OpenAI": {
        "ApiKey": "${OPENAI_API_KEY}",
        "DefaultModel": "gpt-4"
      }
    },
    "Storage": {
      "Provider": "AWS",
      "AWS": {
        "BucketName": "product-images-prod",
        "Region": "us-west-2"
      }
    },
    "Messaging": {
      "Provider": "RabbitMQ",
      "RabbitMQ": {
        "ConnectionString": "${RABBITMQ_PROD}"
      }
    }
  }
}
```

**Feeling:** *"Complete enterprise ecosystem through consistent patterns."*

---

## Pattern Scaling Summary

### **Scaling Progression**

1. **Stage 1**: `Product.Save()` - Basic CRUD
2. **Stage 2**: `event.Send()` - Event-driven messaging
3. **Stage 3**: `Product.SemanticSearch()` - AI integration
4. **Stage 4**: Multiple providers, same patterns
5. **Stage 5**: Complex event workflows with AI
6. **Stage 6**: Full ecosystem integration

### **Mental Model Consistency**

**Throughout all stages, the patterns remain familiar:**
- **Same inheritance**: `Entity<T>` for everything
- **Same methods**: `.Save()`, `.Get()`, `.Query()`, `.Send()`
- **Same mental model**: Entity operations with growing capabilities
- **Same configuration**: Reference = Intent through package references

### **Business Impact by Stage**

| **Stage** | **Capability** | **Team Size** | **Time to Build** |
|-----------|----------------|---------------|-------------------|
| **Stage 1** | Basic CRUD API | 1 developer | 2 hours |
| **Stage 2** | Event-driven business logic | 1-2 developers | 1 day |
| **Stage 3** | AI-native features | 1-2 developers | 2-3 days |
| **Stage 4** | Multi-provider architecture | 2-3 developers | 1 week |
| **Stage 5** | Enterprise event workflows | 2-3 developers | 2 weeks |
| **Stage 6** | Full ecosystem integration | 3-4 developers | 3-4 weeks |

**Traditional approach:** Each stage would typically require 2-5x more time and larger teams.

### **Key Insights**

1. **Learning curve flattens** - Once you know `Entity<T>`, you know the whole framework
2. **Refactoring minimized** - Same patterns scale, no architectural rewrites needed
3. **Team onboarding accelerates** - New developers learn one pattern, apply everywhere
4. **Business velocity maintained** - Complexity hidden behind familiar interfaces
5. **Risk reduced** - Proven patterns prevent architectural mistakes

---

## Next Steps

### **Try the Scaling Progression**

1. **[5-Minute Quickstart](../docs/getting-started/quickstart.md)** - Experience Stage 1 & 2
2. **[Complete Getting Started](../docs/getting-started/guide.md)** - Build through all stages
3. **[Enterprise Adoption Guide](../docs/getting-started/enterprise-adoption.md)** - Strategic scaling approach

### **Deep Dive Resources**

- **[AI Integration Patterns](../guides/ai-integration.md)** - Stage 3 & 4 AI capabilities
- **[Event-Driven Architecture](../guides/event-driven-patterns.md)** - Stage 5 & 6 workflows
- **[Multi-Provider Strategies](../guides/multi-provider-patterns.md)** - Stage 4+ scaling
- **[Performance Optimization](../guides/performance.md)** - Enterprise-scale patterns

---

**The Entity<> pattern: One concept that grows infinitely. Simple to start, powerful to scale.**

---

**Last Updated**: 2025-01-17 by Framework Development Team
**Framework Version**: v0.2.18+