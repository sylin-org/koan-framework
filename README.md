# Koan Framework

**Smart Development Made Simple**

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download)
[![Framework Version](https://img.shields.io/badge/Version-v0.2.18-green.svg)](https://github.com/sylin-org/koan-framework/releases)
[![GitHub Stars](https://img.shields.io/github/stars/sylin-org/koan-framework)](https://github.com/sylin-org/koan-framework/stargazers)

> **Early-stage framework eliminating .NET boilerplate and configuration chaos. Join us in building the developer experience we wish we had.**

Koan Framework is developing intelligent entity-first patterns for .NET developers. Our goal: eliminate the hours of setup that every project requires while maintaining the power and flexibility .NET developers expect.

## Early Stage, High Impact

**We're building Koan in public** with a small but growing community. Version 0.2.18 represents the foundation we're building on:

**Core entity patterns** - Define models, get working APIs
**Multi-provider data access** - Same code, different storage backends
**Auto-registration system** - Add packages, get functionality
**AI integration** - Vector stores and embeddings (in development)
**Performance optimization** - Production benchmarks (planned)

**Your feedback shapes what we build next.**

## Quick Start: Todo API in 3 Steps

**1. Create project and add Koan packages:**

```bash
dotnet new web -n TodoApi && cd TodoApi
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
```

**2. Define your domain model:**

```csharp
// Models/Todo.cs
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**3. Add controller and run:**

```csharp
// Controllers/TodosController.cs
[ApiController]
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

```bash
dotnet run
```

**What you get automatically:**

- REST endpoints: GET, POST, PUT, DELETE `/api/todos`
- Auto-generated GUID v7 IDs for new todos
- Basic validation and error handling
- Health check endpoint at `/api/health`
- SQLite database (zero configuration)

**Try it:**
```bash
# Get all todos
curl http://localhost:5000/api/todos

# Create a todo
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Try Koan Framework", "isCompleted": false}'
```

## Provider Transparency in Action

**Define your model once, run anywhere:**

```csharp
// Your domain model - inherits from Entity<T> for automatic capabilities
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
}
```

**The same code, different storage backends:**

```csharp
// Development: SQLite (zero config, file-based)
var products = await Product.All();

// Staging: PostgreSQL (just add connection string)
var products = await Product.All();

// Production: MongoDB (same API, different capabilities)
var products = await Product.All();

// Analytics: Vector DB (semantic search enabled)
var similar = await Product.Query("eco-friendly laptop");
```

**Intelligent provider selection:** Install `Koan.Data.Postgres`, and your app automatically uses PostgreSQL when available, gracefully falls back to SQLite in development. Your entity methods work identically across all providers.

## Vision: Smart Patterns for Common Scenarios

### Service-to-Service Authentication (Planned)

```csharp
[KoanService("inventory-service", ProvidedScopes = new[] { "inventory:read" })]
[ApiController]
public class InventoryController : ControllerBase
{
    [CallsService("pricing-service", RequiredScopes = new[] { "pricing:read" })]
    public async Task<IActionResult> GetInventoryWithPricing([FromService] IKoanServiceClient client)
    {
        // Vision: Automatic JWT token acquisition, service discovery, and secure communication
        var pricing = await client.GetAsync<PricingData>("pricing-service", "/api/pricing");
        return Ok(new { inventory = GetInventory(), pricing });
    }
}
```

**Development goal:** Eliminate manual OAuth setup, JWT configuration, and service discovery complexity.

### AI Integration (In Development)

```csharp
// Vision: Simple AI integration with provider auto-selection
[HttpPost("recommend")]
public async Task<IActionResult> GetRecommendations([FromServices] IAiService ai, [FromBody] ProductQuery query)
{
    var embedding = await ai.GetEmbeddingAsync(query.Description);
    var similar = await Data<Product, string>.VectorQuery(embedding, limit: 5);

    var prompt = $"Recommend products based on: {query.Description}";
    var recommendation = await ai.ChatAsync(prompt);

    return Ok(new { similar, recommendation });
}
```

**Current status:** Vector storage foundation in place, AI provider abstraction in development.

### Event-Driven Architecture

```csharp
// Dynamic entity processing with sophisticated materialization
public class OrderEvent : DynamicFlowEntity<OrderEvent>
{
    // Flexible JSON-based schema that evolves with your business
}

// Business logic that scales
Flow.OnUpdate<OrderEvent>(async (ref orderEvent, current, metadata) =>
{
    if (orderEvent.GetPathValue<string>("status") == "completed")
    {
        // Automatically publishes to message queues, triggers workflows
        await new OrderCompletedNotification(orderEvent.Id).Send();
    }
    return UpdateResult.Continue("Order processed");
});
```

## Container-Native by Design

### Intelligent Container Orchestration

```bash
# Automatic Docker Compose generation with health checks
Koan export compose --profile Local

# Smart dependency resolution and conflict avoidance
Koan up --profile Local --wait-for-healthy

# Real-time status across all services
Koan status --json
```

**Profiles for every environment:**

- **Local:** Bind mounts, exposed ports, development conveniences
- **CI:** Named volumes, isolated testing, parallel execution
- **Staging:** Production-like with debugging enabled
- **Production:** Minimal surface area, maximum security

### Multi-Runtime Support

Koan automatically detects and works with:

- **Docker:** Full Compose support with JSON parsing
- **Podman:** Native support with format compatibility
- **Kubernetes:** Service mesh integration and health checks

## Framework Capabilities at Scale

| Component          | Technologies Supported                                   | Zero-Config |
| ------------------ | -------------------------------------------------------- | ----------- |
| **Data Storage**   | PostgreSQL, MongoDB, SQLite, Redis, Weaviate, JSON Files | Yes         |
| **Authentication** | Google, Microsoft, Discord, OIDC, Custom JWT             | Yes         |
| **Messaging**      | RabbitMQ, Azure Service Bus, In-Memory                   | Yes         |
| **Storage**        | Local Files, AWS S3, Azure Blob Storage                  | Yes         |
| **AI Providers**   | Ollama, OpenAI, Azure OpenAI (extensible)                | Yes         |
| **Containers**     | Docker, Podman (auto-detection)                          | Yes         |

**63 modules across 8 functional categories**-use what you need, when you need it.

## Architecture That Scales

### Multi-Provider Applications

```csharp
[SourceAdapter("postgres")]   // User data in PostgreSQL
public class User : Entity<User> { /* ... */ }

[SourceAdapter("mongo")]      // Flexible catalog in MongoDB
public class ProductCatalog : Entity<ProductCatalog> { /* ... */ }

[SourceAdapter("redis")]      // Fast sessions in Redis
public class UserSession : Entity<UserSession> { /* ... */ }

[SourceAdapter("vector")]     // AI features with vector search
public class ProductEmbedding : Entity<ProductEmbedding> { /* ... */ }
```

### Enterprise Observability

```csharp
// Built-in structured logging with business context
using var scope = _logger.BeginScope(new Dictionary<string, object>
{
    ["OrderId"] = order.Id,
    ["CustomerId"] = order.CustomerId,
    ["ProcessingStartTime"] = DateTimeOffset.UtcNow
});

_logger.LogInformation("Processing order {OrderValue}", order.Total);

// Automatic health monitoring across all providers
public class DatabaseHealthContributor : IHealthContributor
{
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        // Framework provides rich health context and failure classification
    }
}
```

## Current Capabilities & Development Goals

**What's working today:**
- Auto-discovery eliminates manual service registration
- Entity-first patterns reduce boilerplate
- Multi-provider data access (SQLite, PostgreSQL, MongoDB)
- Zero-configuration project setup

**In active development:**
- Performance benchmarking and optimization
- AI provider abstractions and vector storage
- Advanced caching strategies
- Production monitoring and health checks

## When to Try Koan

**Koan is great for experimentation if you:**

- Want to eliminate .NET project setup boilerplate
- Are interested in entity-first development patterns
- Need multi-provider data access in your applications
- Want to contribute to framework development
- Are curious about "smart defaults" approaches to .NET

**Consider waiting if you:**

- Need production-ready features for critical applications
- Require extensive documentation and community support
- Want stable APIs without breaking changes
- Prefer mature frameworks with proven track records

## Join Our Development Community

**We're looking for early adopters who want to:**

- **Shape the framework** - Your use cases directly influence our roadmap
- **Contribute code** - Help build the patterns you wish existed
- **Provide feedback** - Real-world usage guides our priorities
- **Be recognized** - Early contributors get lasting credit as framework builders

**Ways to get involved:**
- **Star the repository** to show support
- **Report issues** you encounter
- **Suggest features** based on your needs
- **Submit pull requests** for improvements
- **Join discussions** about framework design

## Learn More

### Quick Start

- **[5-Minute Quickstart](documentation/getting-started/quickstart.md)** - Get running immediately
- **[Getting Started Guide](documentation/getting-started/getting-started.md)** - Complete walkthrough
- **[Framework Overview](documentation/getting-started/overview.md)** - Architecture deep-dive

### Reference Documentation

- **[Core Reference](documentation/reference/core/index.md)** - Auto-registration, health checks, configuration
- **[Data Reference](documentation/reference/data/index.md)** - Multi-provider data access patterns
- **[Web Reference](documentation/reference/web/index.md)** - Controllers, authentication, transformers
- **[AI Reference](documentation/reference/ai/index.md)** - AI integration and vector search
- **[Flow Reference](documentation/reference/flow/index.md)** - Data pipeline and ingestion patterns
- **[Messaging Reference](documentation/reference/messaging/index.md)** - Commands, announcements, flow events
- **[Storage Reference](documentation/reference/storage/index.md)** - File/blob storage and routing
- **[Orchestration Reference](documentation/reference/orchestration/index.md)** - Container orchestration and CLI
- **[Architecture Principles](documentation/architecture/principles.md)** - Framework design philosophy
- **[Troubleshooting Guide](documentation/support/troubleshooting.md)** - Comprehensive problem-solving guide

### Implementation Guides

- **[Building APIs](documentation/guides/building-apis.md)** - REST endpoints and business logic
- **[Data Modeling](documentation/guides/data-modeling.md)** - Entity design and relationships
- **[AI Integration](documentation/guides/ai-integration.md)** - Chat, embeddings, RAG patterns
- **[Authentication Setup](documentation/guides/authentication-setup.md)** - Multi-provider authentication
- **[Performance Guide](documentation/guides/performance.md)** - Optimization and monitoring

## Requirements

- **.NET 9 SDK** or later
- **Docker** or **Podman** (for container orchestration)
- **Optional:** Koan CLI for enhanced development experience

## Getting Started

1. **Install the CLI** (recommended):

   ```bash
   ./scripts/cli-all.ps1
   ```

2. **Or use NuGet packages directly**:

   ```bash
   dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
   ```

3. **Follow the quickstart** above or visit our [5-minute guide](documentation/getting-started/quickstart.md)

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

**Key contribution areas:**

- New data providers and adapters
- Additional authentication providers
- Container orchestration improvements
- Documentation and examples

## Enterprise Support

For enterprise adoption, architecture guidance, and production deployment support:

- Review our [Architecture Principles](documentation/architecture/principles.md) for technical framework philosophy
- Consult the [Complete Documentation](documentation/README.md) for adoption planning
- Explore [troubleshooting guide](documentation/support/troubleshooting.md) for operational patterns

## License

Licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.

---

**Koan Framework: Smart Development Made Simple**

_Early-stage .NET framework eliminating boilerplate and configuration chaos. Join us in building the developer experience we wish we had._
