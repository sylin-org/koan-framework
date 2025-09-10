# Usage Patterns

**Document Type**: Reference Documentation (REF)  
**Target Audience**: Developers, AI Agents  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## 🎨 Sora Framework Usage Patterns

This document covers common usage patterns, best practices, and architectural approaches when building with Sora Framework.

---

## 🏗️ Core Development Patterns

### 1. **Entity-First Development**

Models are first-class citizens that drive everything else:

```csharp
// Define your domain model
public class Product : Entity<Product>
{
    // Properties define your schema
    [AggregationKey] // For Flow pipeline
    public string SKU { get; set; } = "";
    
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal OriginalPrice { get; set; }
    public string Category { get; set; } = "";
    public bool IsActive { get; set; } = true;
    
    // Business logic stays with the model
    public static Task<Product[]> OnSale() => 
        All().Where(p => p.Price < p.OriginalPrice);
        
    public static Task<Product[]> InCategory(string category) =>
        All().Where(p => p.Category == category && p.IsActive);
        
    public static Task<Product[]> Search(string query) =>
        All().Where(p => p.Name.Contains(query) || p.SKU.Contains(query));
    
    // Instance methods for business operations
    public async Task ApplyDiscount(decimal percentage)
    {
        Price = OriginalPrice * (1 - percentage / 100);
        await SaveAsync();
    }
}

// Get automatic REST API
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    // Automatically provides:
    // GET /api/products
    // GET /api/products/{id}
    // POST /api/products
    // PUT /api/products/{id}
    // DELETE /api/products/{id}
    
    // Add custom endpoints for business methods
    [HttpGet("on-sale")]
    public Task<Product[]> GetOnSale() => Product.OnSale();
    
    [HttpGet("category/{category}")]
    public Task<Product[]> GetByCategory(string category) => Product.InCategory(category);
    
    [HttpGet("search")]
    public Task<Product[]> Search([FromQuery] string q) => Product.Search(q);
    
    [HttpPost("{id}/discount")]
    public async Task<IActionResult> ApplyDiscount(string id, [FromBody] decimal percentage)
    {
        var product = await Product.ByIdAsync(id);
        if (product == null) return NotFound();
        
        await product.ApplyDiscount(percentage);
        return Ok(product);
    }
}
```

### 2. **Progressive Enhancement Pattern**

Start simple, add complexity incrementally:

```csharp
// Phase 1: Basic Web API
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSora(); // Includes Core, Web, SQLite
var app = builder.Build();
await app.RunAsync();

// Phase 2: Add messaging
// dotnet add package Sora.Messaging.RabbitMq
// No code changes needed - auto-discovered

// Phase 3: Add AI
// dotnet add package Sora.AI
// dotnet add package Sora.AI.Provider.Ollama

[Route("api/[controller]")]
[ApiController]  
public class AiController : ControllerBase
{
    private readonly IAi _ai;
    
    public AiController(IAi ai) => _ai = ai;
    
    [HttpPost("summarize")]
    public async Task<string> Summarize([FromBody] string text)
    {
        var request = new AiChatRequest
        {
            Messages = [new() { 
                Role = AiMessageRole.User, 
                Content = $"Summarize this in 2-3 sentences: {text}" 
            }]
        };
        
        var response = await _ai.ChatAsync(request);
        return response.Choices?.FirstOrDefault()?.Message?.Content ?? "";
    }
}

// Phase 4: Add vector search
// dotnet add package Sora.Data.Vector
// dotnet add package Sora.Data.Redis

public class Document : Entity<Document>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    
    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];
    
    public static Task<Document[]> SimilarTo(string query, int limit = 10) =>
        Vector<Document>.SearchAsync(query, limit);
}
```

### 3. **Configuration-Driven Behavior**

Use configuration to control framework behavior without code changes:

```json
{
  "Sora": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Sqlite": {
        "ConnectionString": "Data Source=:memory:"
      },
      "Postgres": {
        "ConnectionString": "Host=localhost;Database=myapp;Username=user;Password=pass"
      }
    },
    "Web": {
      "EnableSwagger": true,
      "CorsOrigins": ["http://localhost:3000", "https://myapp.com"],
      "SecureHeaders": {
        "EnableHsts": true,
        "ContentSecurityPolicy": "default-src 'self'"
      }
    },
    "AI": {
      "DefaultProvider": "Ollama",
      "Budget": {
        "MaxTokensPerRequest": 1000,
        "MaxRequestsPerMinute": 60
      },
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama2"
      }
    },
    "Messaging": {
      "DefaultProvider": "RabbitMq",
      "RabbitMq": {
        "ConnectionString": "amqp://guest:guest@localhost:5672"
      }
    },
    "Flow": {
      "EnableInterceptors": true,
      "ExternalIdPolicy": "AutoPopulate"
    }
  }
}
```

---

## 🔄 Event-Driven Patterns

### 1. **Message-Based Communication**

Use messaging for decoupled, reliable communication:

```csharp
// Define domain events
public class OrderCreated
{
    public string OrderId { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class OrderShipped
{
    public string OrderId { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public DateTimeOffset ShippedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Business logic publishes events
public class Order : Entity<Order>
{
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public string? TrackingNumber { get; set; }
    
    public async Task MarkShipped(string trackingNumber)
    {
        Status = OrderStatus.Shipped;
        TrackingNumber = trackingNumber;
        await SaveAsync();
        
        // Publish event
        await new OrderShipped 
        { 
            OrderId = Id, 
            TrackingNumber = trackingNumber 
        }.Send();
    }
}

// Event handlers
public class EmailService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Handle order events
        await this.On<OrderCreated>(async evt =>
        {
            await SendOrderConfirmation(evt.CustomerEmail, evt.OrderId);
        });
        
        await this.On<OrderShipped>(async evt =>
        {
            await SendShippingNotification(evt.CustomerEmail, evt.TrackingNumber);
        });
        
        // Keep service running
        await Task.Delay(Timeout.Infinite, ct);
    }
    
    private async Task SendOrderConfirmation(string email, string orderId)
    {
        // Email implementation
        Console.WriteLine($"Sending order confirmation to {email} for order {orderId}");
    }
    
    private async Task SendShippingNotification(string email, string trackingNumber)
    {
        // Email implementation  
        Console.WriteLine($"Sending shipping notification to {email} with tracking {trackingNumber}");
    }
}

public class InventoryService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<OrderCreated>(async evt =>
        {
            await UpdateInventoryForOrder(evt.OrderId);
        });
        
        await Task.Delay(Timeout.Infinite, ct);
    }
    
    private async Task UpdateInventoryForOrder(string orderId)
    {
        // Inventory logic
        Console.WriteLine($"Updating inventory for order {orderId}");
    }
}
```

### 2. **CQRS with Messaging**

Separate read and write models using messaging:

```csharp
// Write model (commands)
public class CreateProductCommand
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}

public class UpdateProductPriceCommand
{
    public string ProductId { get; set; } = "";
    public decimal NewPrice { get; set; }
}

// Events
public class ProductCreated
{
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ProductPriceUpdated
{
    public string ProductId { get; set; } = "";
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Command handlers
[Route("api/[controller]")]
[ApiController]
public class ProductCommandsController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductCommand cmd)
    {
        var product = new Product
        {
            Id = Ulid.NewUlid().ToString(),
            Name = cmd.Name,
            Price = cmd.Price,
            Category = cmd.Category
        };
        
        await product.SaveAsync();
        
        await new ProductCreated
        {
            ProductId = product.Id,
            Name = product.Name,
            Price = product.Price,
            Category = product.Category
        }.Send();
        
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }
    
    [HttpPut("{id}/price")]
    public async Task<IActionResult> UpdatePrice(string id, [FromBody] UpdateProductPriceCommand cmd)
    {
        var product = await Product.ByIdAsync(id);
        if (product == null) return NotFound();
        
        var oldPrice = product.Price;
        product.Price = cmd.NewPrice;
        await product.SaveAsync();
        
        await new ProductPriceUpdated
        {
            ProductId = id,
            OldPrice = oldPrice,
            NewPrice = cmd.NewPrice
        }.Send();
        
        return Ok();
    }
}

// Read model projections
public class ProductProjection : Entity<ProductProjection>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public int ViewCount { get; set; }
    public DateTimeOffset LastViewed { get; set; }
    
    public static Task<ProductProjection[]> PopularInCategory(string category) =>
        All().Where(p => p.Category == category)
             .OrderByDescending(p => p.ViewCount)
             .Take(10);
}

// Projection service
public class ProductProjectionService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await this.On<ProductCreated>(async evt =>
        {
            await new ProductProjection
            {
                Id = evt.ProductId,
                Name = evt.Name,
                Price = evt.Price,
                Category = evt.Category
            }.SaveAsync();
        });
        
        await this.On<ProductPriceUpdated>(async evt =>
        {
            var projection = await ProductProjection.ByIdAsync(evt.ProductId);
            if (projection != null)
            {
                projection.Price = evt.NewPrice;
                await projection.SaveAsync();
            }
        });
        
        await Task.Delay(Timeout.Infinite, ct);
    }
}
```

---

## 🤖 AI Integration Patterns

### 1. **Local AI with Ollama**

Use local models for privacy and cost control:

```csharp
// AI-enhanced entities
public class BlogPost : Entity<BlogPost>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Summary { get; set; }
    public string[] Tags { get; set; } = [];
    
    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];
    
    // AI-powered methods
    public async Task GenerateSummary(IAi ai)
    {
        var request = new AiChatRequest
        {
            Messages = [new() {
                Role = AiMessageRole.User,
                Content = $"Summarize this blog post in 2-3 sentences:\n\nTitle: {Title}\n\nContent: {Content}"
            }]
        };
        
        var response = await ai.ChatAsync(request);
        Summary = response.Choices?.FirstOrDefault()?.Message?.Content;
        await SaveAsync();
    }
    
    public async Task GenerateTags(IAi ai)
    {
        var request = new AiChatRequest
        {
            Messages = [new() {
                Role = AiMessageRole.User,
                Content = $"Generate 3-5 relevant tags for this blog post (comma-separated):\n\nTitle: {Title}\n\nContent: {Content}"
            }]
        };
        
        var response = await ai.ChatAsync(request);
        var tagsText = response.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        Tags = tagsText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(t => t.Trim())
                      .ToArray();
        await SaveAsync();
    }
    
    // Semantic search
    public static Task<BlogPost[]> SimilarTo(string query, int limit = 5) =>
        Vector<BlogPost>.SearchAsync(query, limit);
}

// AI-enhanced controller
[Route("api/[controller]")]
public class BlogPostsController : EntityController<BlogPost>
{
    private readonly IAi _ai;
    
    public BlogPostsController(IAi ai) => _ai = ai;
    
    [HttpPost("{id}/generate-summary")]
    public async Task<IActionResult> GenerateSummary(string id)
    {
        var post = await BlogPost.ByIdAsync(id);
        if (post == null) return NotFound();
        
        await post.GenerateSummary(_ai);
        return Ok(new { summary = post.Summary });
    }
    
    [HttpPost("{id}/generate-tags")]
    public async Task<IActionResult> GenerateTags(string id)
    {
        var post = await BlogPost.ByIdAsync(id);
        if (post == null) return NotFound();
        
        await post.GenerateTags(_ai);
        return Ok(new { tags = post.Tags });
    }
    
    [HttpGet("similar")]
    public Task<BlogPost[]> FindSimilar([FromQuery] string query, [FromQuery] int limit = 5) =>
        BlogPost.SimilarTo(query, limit);
}
```

### 2. **RAG (Retrieval-Augmented Generation) Pattern**

Combine vector search with AI generation:

```csharp
public class Document : Entity<Document>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Source { get; set; } = "";
    
    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];
    
    public static Task<Document[]> FindRelevant(string query, int limit = 5) =>
        Vector<Document>.SearchAsync(query, limit);
}

[Route("api/[controller]")]
[ApiController]
public class KnowledgeController : ControllerBase
{
    private readonly IAi _ai;
    
    public KnowledgeController(IAi ai) => _ai = ai;
    
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        // 1. Retrieve relevant documents
        var relevantDocs = await Document.FindRelevant(request.Question, 3);
        
        // 2. Build context from retrieved documents
        var context = string.Join("\n\n", relevantDocs.Select(d => 
            $"Source: {d.Source}\nTitle: {d.Title}\nContent: {d.Content}"));
        
        // 3. Generate answer using context
        var chatRequest = new AiChatRequest
        {
            Messages = [
                new() {
                    Role = AiMessageRole.System,
                    Content = "You are a helpful assistant. Answer the user's question based on the provided context. If the context doesn't contain relevant information, say so."
                },
                new() {
                    Role = AiMessageRole.User,
                    Content = $"Context:\n{context}\n\nQuestion: {request.Question}"
                }
            ]
        };
        
        var response = await _ai.ChatAsync(chatRequest);
        var answer = response.Choices?.FirstOrDefault()?.Message?.Content ?? "I couldn't generate an answer.";
        
        return Ok(new AskResponse
        {
            Answer = answer,
            Sources = relevantDocs.Select(d => new SourceReference
            {
                Title = d.Title,
                Source = d.Source,
                Id = d.Id
            }).ToArray()
        });
    }
}

public class AskRequest
{
    public string Question { get; set; } = "";
}

public class AskResponse
{
    public string Answer { get; set; } = "";
    public SourceReference[] Sources { get; set; } = [];
}

public class SourceReference
{
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string Id { get; set; } = "";
}
```

---

## 🌊 Flow Pipeline Patterns

### 1. **Data Ingestion and Transformation**

Use Flow for ETL-style data processing:

```csharp
// Source entities from different systems
[FlowAdapter(system: "erp", adapter: "sap")]
public class ErpProduct : FlowEntity<ErpProduct>
{
    [AggregationKey]
    public string SKU { get; set; } = "";
    
    public string ProductName { get; set; } = "";
    public decimal Cost { get; set; }
    public string CategoryCode { get; set; } = "";
}

[FlowAdapter(system: "ecommerce", adapter: "shopify")]  
public class EcommerceProduct : FlowEntity<EcommerceProduct>
{
    [AggregationKey]
    public string SKU { get; set; } = "";
    
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

// Interceptors for data transformation
public class ProductInterceptor : ISoraAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        FlowInterceptors
            .For<ErpProduct>()
            .BeforeIntake(async product =>
            {
                // Validate ERP data
                if (string.IsNullOrEmpty(product.SKU))
                    return FlowIntakeActions.Drop(product, "Missing SKU");
                    
                // Normalize category codes
                product.CategoryCode = NormalizeCategoryCode(product.CategoryCode);
                return FlowIntakeActions.Continue(product);
            })
            .AfterAssociation(async product =>
            {
                // Notify downstream systems after association
                await NotifyInventorySystem(product);
                return FlowStageActions.Continue(product);
            });
            
        FlowInterceptors
            .For<EcommerceProduct>()
            .BeforeIntake(async product =>
            {
                // Only process active products
                if (!product.IsActive)
                    return FlowIntakeActions.Drop(product, "Inactive product");
                    
                return FlowIntakeActions.Continue(product);
            });
    }
    
    private string NormalizeCategoryCode(string code) => 
        code.ToUpperInvariant().Replace(" ", "_");
        
    private async Task NotifyInventorySystem(ErpProduct product)
    {
        await new InventoryUpdateEvent { SKU = product.SKU, Cost = product.Cost }.Send();
    }
}

// Unified canonical view
public class ProductCanonical : Entity<ProductCanonical>
{
    public string SKU { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Cost { get; set; }  // From ERP
    public decimal? Price { get; set; } // From E-commerce
    public string Category { get; set; } = "";
    
    // External ID tracking
    public Dictionary<string, string> ExternalIds { get; set; } = new();
    
    public static Task<ProductCanonical[]> WithDiscrepancies() =>
        All().Where(p => p.Cost.HasValue && p.Price.HasValue && p.Price < p.Cost);
}
```

### 2. **Multi-Source Identity Resolution**

Correlate entities across systems:

```csharp
[FlowPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate)]
public class Customer : FlowEntity<Customer>
{
    [AggregationKey]
    public string Email { get; set; } = "";
    
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime? BirthDate { get; set; }
}

// Different sources send customer data
[FlowAdapter(system: "crm", adapter: "salesforce")]
public class CrmCustomerAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Simulate CRM data
        await new Customer
        {
            Email = "john.doe@example.com",
            FirstName = "John",
            LastName = "Doe",
            Phone = "+1-555-0123"
        }.Send();
    }
}

[FlowAdapter(system: "support", adapter: "zendesk")]
public class SupportCustomerAdapter : BackgroundService  
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Same customer from support system
        await new Customer
        {
            Email = "john.doe@example.com",
            FirstName = "John",
            LastName = "Doe",
            BirthDate = new DateTime(1985, 5, 15)
        }.Send();
    }
}

// Flow automatically correlates by email and creates unified view:
// {
//   "email": "john.doe@example.com",
//   "firstName": "John", 
//   "lastName": "Doe",
//   "phone": "+1-555-0123",
//   "birthDate": "1985-05-15",
//   "identifier": {
//     "external": {
//       "crm": "john.doe@example.com",
//       "support": "john.doe@example.com"
//     }
//   }
// }
```

---

## 🚀 Deployment Patterns

### 1. **Container-Based Development**

Use Sora CLI for local development:

```yaml
# sora.orchestration.yml
dependencies:
  postgres:
    provider: docker
    image: postgres:15
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: myapp
      POSTGRES_USER: user  
      POSTGRES_PASSWORD: pass
    volumes:
      - postgres-data:/var/lib/postgresql/data
    health:
      test: ["CMD-SHELL", "pg_isready -U user"]
      interval: 30s
      timeout: 10s
      retries: 3
      
  redis:
    provider: docker
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    health:
      test: ["CMD", "redis-cli", "ping"]
      
  rabbitmq:
    provider: docker
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: user
      RABBITMQ_DEFAULT_PASS: pass
    volumes:
      - rabbitmq-data:/var/lib/rabbitmq
    health:
      test: ["CMD", "rabbitmq-diagnostics", "check_port_connectivity"]

volumes:
  postgres-data:
  redis-data:
  rabbitmq-data:
```

```bash
# Development workflow
Sora export compose --profile Local
Sora up --profile Local --timeout 300
Sora status  # Check all services are healthy
dotnet run   # Start your application
```

### 2. **Production Configuration**

Environment-specific settings:

```json
// appsettings.Production.json
{
  "Sora": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Postgres": {
        "ConnectionString": "Host=db.prod.com;Database=myapp;Username=app_user;Password={SECRET}"
      }
    },
    "Web": {
      "EnableSwagger": false,
      "SecureHeaders": {
        "EnableHsts": true,
        "HstsMaxAge": "31536000",
        "ContentSecurityPolicy": "default-src 'self'; script-src 'self' 'unsafe-inline'"
      }
    },
    "AI": {
      "Budget": {
        "MaxTokensPerRequest": 2000,
        "MaxRequestsPerMinute": 100
      }
    },
    "Messaging": {
      "RabbitMq": {
        "ConnectionString": "amqp://user:pass@rabbitmq.prod.com:5672"
      }
    },
    "Observability": {
      "EnableTelemetry": true,
      "OtelEndpoint": "http://jaeger.prod.com:4317"
    }
  }
}
```

### 3. **Health Check Patterns**

Comprehensive health monitoring:

```csharp
public class DatabaseHealthCheck : IHealthContributor
{
    public string Name => "database";
    public bool IsCritical => true;
    
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            // Simple query to test database connectivity
            var count = await Product.All().CountAsync();
            return HealthReport.Healthy($"Database responsive, {count} products");
        }
        catch (Exception ex)
        {
            return HealthReport.Unhealthy("Database connectivity failed", ex);
        }
    }
}

public class ExternalApiHealthCheck : IHealthContributor
{
    private readonly HttpClient _httpClient;
    
    public string Name => "external-api";
    public bool IsCritical => false; // Non-critical dependency
    
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode
                ? HealthReport.Healthy($"External API responsive ({response.StatusCode})")
                : HealthReport.Degraded($"External API degraded ({response.StatusCode})");
        }
        catch (Exception ex)
        {
            return HealthReport.Degraded("External API unreachable", ex);
        }
    }
}
```

---

## 🔐 Security Patterns

### 1. **Authentication Integration**

Built-in auth providers:

```csharp
// Program.cs - Authentication setup
builder.Services.AddSoraWeb(options =>
{
    options.EnableAuthentication = true;
    options.AuthProviders = new[]
    {
        "Google",    // OAuth with Google
        "Microsoft", // OAuth with Microsoft  
        "OIDC"       // Generic OIDC provider
    };
});

// appsettings.json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Google": {
          "ClientId": "{GOOGLE_CLIENT_ID}",
          "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
        },
        "Microsoft": {
          "ClientId": "{MS_CLIENT_ID}", 
          "ClientSecret": "{MS_CLIENT_SECRET}"
        },
        "OIDC": {
          "Authority": "https://auth.mycompany.com",
          "ClientId": "myapp",
          "ClientSecret": "{OIDC_CLIENT_SECRET}"
        }
      }
    }
  }
}
```

### 2. **Authorization Patterns**

Role-based and policy-based authorization:

```csharp
[Route("api/[controller]")]
[Authorize] // Require authentication
public class ProductsController : EntityController<Product>
{
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")] // Role-based
    public override async Task<IActionResult> Delete(string id)
    {
        return await base.Delete(id);
    }
    
    [HttpPost]
    [Authorize(Policy = "CanCreateProducts")] // Policy-based
    public override async Task<IActionResult> Post([FromBody] Product entity)
    {
        return await base.Post(entity);
    }
}

// Startup configuration
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanCreateProducts", policy =>
        policy.RequireRole("Admin", "Editor")
              .RequireClaim("department", "product-management"));
});
```

---

## 📊 Observability Patterns

### 1. **Telemetry Integration**

Built-in OpenTelemetry support:

```csharp
// Program.cs
builder.Services.AddSoraObservability(options =>
{
    options.ServiceName = "MyApp";
    options.ServiceVersion = "1.0.0";
    options.EnableTracing = true;
    options.EnableMetrics = true;
    options.JaegerEndpoint = "http://localhost:14268";
});

// Custom metrics
public class OrderMetrics
{
    private static readonly Counter<int> _ordersCreated = 
        Meter.CreateCounter<int>("orders.created", "count", "Number of orders created");
        
    private static readonly Histogram<double> _orderValue =
        Meter.CreateHistogram<double>("orders.value", "USD", "Order value distribution");
    
    public static void RecordOrderCreated(decimal value)
    {
        _ordersCreated.Add(1);
        _orderValue.Record((double)value);
    }
}

// Custom tracing
public class OrderService
{
    private static readonly ActivitySource _activitySource = new("MyApp.OrderService");
    
    public async Task<Order> ProcessOrder(CreateOrderRequest request)
    {
        using var activity = _activitySource.StartActivity("ProcessOrder");
        activity?.SetTag("order.customer", request.CustomerEmail);
        
        try
        {
            var order = new Order { /* ... */ };
            await order.SaveAsync();
            
            OrderMetrics.RecordOrderCreated(order.Total);
            activity?.SetTag("order.id", order.Id);
            
            return order;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

### 2. **Structured Logging**

Rich, queryable logs:

```csharp
public class ProductService
{
    private readonly ILogger<ProductService> _logger;
    
    public async Task<Product> CreateProduct(CreateProductRequest request)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "CreateProduct",
            ["CorrelationId"] = Guid.NewGuid().ToString()
        });
        
        _logger.LogInformation("Creating product {ProductName} in category {Category}",
            request.Name, request.Category);
            
        try
        {
            var product = new Product
            {
                Name = request.Name,
                Price = request.Price,
                Category = request.Category
            };
            
            await product.SaveAsync();
            
            _logger.LogInformation("Product created successfully {ProductId}", product.Id);
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create product {ProductName}", request.Name);
            throw;
        }
    }
}
```

---

## ⚡ Performance Patterns

### 1. **Efficient Data Access**

Use streaming and paging for large datasets:

```csharp
public class ProductService
{
    // For small, known datasets
    public static Task<Product[]> GetFeaturedProducts() =>
        Product.All().Where(p => p.IsFeatured).Take(10);
    
    // For large datasets - use streaming
    public static async Task ProcessAllProducts(Func<Product, Task> processor)
    {
        await foreach (var product in Product.AllStream())
        {
            await processor(product);
        }
    }
    
    // For user-facing lists - use paging
    public static async Task<PagedResult<Product>> GetProductsPaged(int page = 1, int size = 20)
    {
        return await Product.Page(page, size);
    }
    
    // For complex queries with filtering
    public static async Task<Product[]> SearchProducts(ProductSearchRequest request)
    {
        var query = Product.Query();
        
        if (!string.IsNullOrEmpty(request.Category))
            query = query.Where(p => p.Category == request.Category);
            
        if (!string.IsNullOrEmpty(request.SearchTerm))
            query = query.Where(p => p.Name.Contains(request.SearchTerm));
            
        if (request.MinPrice.HasValue)
            query = query.Where(p => p.Price >= request.MinPrice);
            
        if (request.MaxPrice.HasValue)
            query = query.Where(p => p.Price <= request.MaxPrice);
        
        return await query.OrderBy(p => p.Name)
                         .Take(request.Limit ?? 50);
    }
}
```

### 2. **Caching Patterns**

Use Redis for caching:

```csharp
public class ProductCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    
    public async Task<Product[]> GetPopularProducts()
    {
        const string cacheKey = "popular-products";
        
        // Try memory cache first
        if (_memoryCache.TryGetValue(cacheKey, out Product[] cached))
            return cached;
        
        // Try distributed cache
        var distributedValue = await _distributedCache.GetStringAsync(cacheKey);
        if (distributedValue != null)
        {
            var products = JsonSerializer.Deserialize<Product[]>(distributedValue);
            _memoryCache.Set(cacheKey, products, TimeSpan.FromMinutes(5));
            return products;
        }
        
        // Load from database
        var popular = await Product.All()
            .Where(p => p.ViewCount > 100)
            .OrderByDescending(p => p.ViewCount)
            .Take(20);
        
        // Cache at both levels
        _memoryCache.Set(cacheKey, popular, TimeSpan.FromMinutes(5));
        await _distributedCache.SetStringAsync(cacheKey, 
            JsonSerializer.Serialize(popular),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });
        
        return popular;
    }
}
```

---

This comprehensive guide covers the most important usage patterns in Sora Framework. For specific pillar deep-dives, see the pillar-specific REF documents.