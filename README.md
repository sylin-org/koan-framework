# Koan Framework

**Try it. Be delighted. Build sophisticated apps with simple patterns.**

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://github.com/sylin-org/koan-framework/blob/dev/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![Framework Version](https://img.shields.io/badge/Version-v0.6.3-green.svg)](https://github.com/sylin-org/koan-framework/releases)
[![GitHub Stars](https://img.shields.io/github/stars/sylin-org/koan-framework)](https://github.com/sylin-org/koan-framework/stargazers)

> **The .NET framework that makes small teams capable of sophisticated solutions through intelligent automation, AI-native patterns, and elegant scaling.**

---

## From Simple to Sophisticated in Minutes

**Koan keeps the AR ergonomics developers love** - entities with `Save()`, discoverable CRUD controllers - **while neutralizing the common AR traps** with set-scoped routing, capability-aware queries, batch ops, a shared endpoint service, and first-class event flows. You get AR’s "don't make me think" feel without painting yourself into a single-DB, fat-controller, N+1-ridden corner.

### **1. Get started quickly**

```bash
# 2 minutes to working API
dotnet new web -n MyApp && cd MyApp
dotnet add package Koan.Core Koan.Web Koan.Data.Connector.Sqlite
```

```csharp
// Define your model
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

// Get full REST API
[ApiController, Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

```bash
dotnet run
# Full REST API with health checks
# Auto-generated GUID v7 IDs
# SQLite database (zero config)
# Structured logging and telemetry
```

**Result:** Full REST API with enterprise features in under 2 minutes.

---

### **2. Entity<> scales elegantly**

```csharp
// Same pattern, growing capabilities

// Database operations
var todo = new Todo { Title = "Try Koan Framework" };
await todo.Save();                    // Simple and familiar

// Add messaging - same mental model
public class TodoCompleted : Entity<TodoCompleted>
{
    public string TodoId { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

// Reference = Intent (no configuration ceremony)
// dotnet add package Koan.Messaging.Connector.RabbitMq

await new TodoCompleted { TodoId = todo.Id }.Send();  // Same pattern extends
```

**One pattern that scales from CRUD to enterprise event-driven architecture.**

---

### **3. AI with native feel**

```csharp
// Reference = Intent
// dotnet add package Koan.AI.Ollama

// AI through familiar patterns
var suggestions = await ai.Chat("What should I do after: " + todo.Title);

// Semantic search feels like LINQ
var related = await Todo.SemanticSearch("project planning tasks");

// Vector operations through Entity<> patterns
var similar = await Product.SemanticSearch("eco-friendly laptops");
```

**AI integration that feels natural, not forced. Provisioning, models, contexts - all handled automatically.**

---

### **4. Semantic Streaming Pipelines**

```csharp
// Complex AI workflows made simple - process 10,000 documents in one pipeline

await Document.AllStream()
    .Pipeline()
    .ForEach(doc => {
        doc.ProcessedAt = DateTime.UtcNow;
        doc.Status = "processing";
    })
    .Tokenize(doc => $"{doc.Title} {doc.Content}")     // AI tokenization
    .Embed(new AiEmbedOptions { Model = "all-minilm" }) // Generate embeddings
    .Branch(branch => branch
        .OnSuccess(success => success
            .Save()                                     // Clean, semantic - no type pollution
            .Notify(doc => $"Document '{doc.Title}' processed successfully"))
        .OnFailure(failure => failure
            .Trace(env => $"Failed: {env.Error?.Message}")
            .Notify(doc => $"Processing failed for '{doc.Title}'")))
    .ExecuteAsync();

// What just happened:
// ✓ Streamed 10K+ documents without memory issues
// ✓ Generated AI embeddings with automatic batching
// ✓ Stored documents (PostgreSQL) + vectors (Weaviate) in one .Save()
// ✓ Branched success/failure paths with notifications
// ✓ Full observability and error handling
// ✓ All with clean, readable, semantic code
```

```csharp
// Event-driven architecture through simple patterns
Flow.OnUpdate<Todo>(async (todo, previous) => {
    if (todo.IsCompleted && !previous.IsCompleted) {
        await new TodoCompleted { TodoId = todo.Id }.Send();
        await new NotificationRequested {
            UserId = todo.UserId,
            Message = $"Task '{todo.Title}' completed!"
        }.Send();
    }
    return UpdateResult.Continue();
});
```

**Semantic pipelines turn complex AI + data workflows into readable, maintainable code. Enterprise-grade streaming, embedding generation, and multi-provider storage in natural .NET patterns.**

---

### **5. Works with what you know**

```csharp
// Standard .NET patterns, enhanced
public class TodosController : EntityController<Todo>
{
    // Override when needed, get defaults otherwise
    public override async Task<ActionResult<Todo>> Post(Todo todo)
    {
        // Custom business logic
        todo.CreatedBy = User.Identity.Name;
        return await base.Post(todo);  // Leverage framework automation
    }
}
```

```bash
# Docker Compose generated automatically
koan export compose --profile Local

# Works with Aspire
builder.AddKoan();
builder.Services.AddKoanWeb();

# Standard .NET tooling works perfectly
dotnet build, dotnet test, dotnet publish
```

**Enhances your existing .NET workflow without replacing it.**

---

## Why Choose Koan

Koan gives you **Active Record ergonomics** with the **scalability of a Data Mapper**—and adds polyglot storage, flows, and semantic pipelines without ceremony.

**Legend:** 🟩 Good · 🟨 Mixed/depends · 🟥 Weak

| Capability                          | EF  | AR  | Koan |
| ----------------------------------- | --- | --- | ---- |
| Time to first API                   | 🟨  | 🟩  | 🟩   |
| Polyglot storage (SQL/NoSQL/Vector) | 🟨  | 🟥  | 🟩   |
| Multi-tenant & view routing         | 🟨  | 🟥  | 🟩   |
| Event-driven & projections          | 🟨  | 🟥  | 🟩   |
| Semantic/Vector pipeline            | 🟥  | 🟥  | 🟩   |
| Capability detection / fallback     | 🟨  | 🟥  | 🟩   |
| Migrations & schema                 | 🟩  | 🟨  | 🟨   |

**What this means in practice**

- **Start simple, scale cleanly:** AR‑easy CRUD today; switch on Flow, sets, and vectors when complexity appears.
- **One pattern, many backends:** Swap providers without rewriting your domain or controllers.
- **AI‑native:** Semantic search, embeddings, and streaming pipelines are first‑class, not bolt‑ons.

**Deep comparison:** [See the full, categorized matrix.](https://github.com/sylin-org/koan-framework/blob/dev/docs/architecture/comparison.md)

---

## Quick Start Paths

### **For Individual Developers**

```bash
# Try the quickstart
git clone https://github.com/koan-framework/quickstart
cd quickstart && dotnet run

# Create your first app
dotnet new web -n MyApp
dotnet add package Koan.Core Koan.Web Koan.Data.Connector.Sqlite
```

### **For Teams & Architects**

```bash
# Explore enterprise patterns
git clone https://github.com/koan-framework/enterprise-sample
cd enterprise-sample && ./start.bat

# See: AI integration, event sourcing, multi-provider data, container orchestration
```

### **For AI-First Projects**

```bash
# Start with AI-native patterns
dotnet new web -n AiApp
dotnet add package Koan.Core Koan.Web Koan.AI.Ollama Koan.Data.Vector

# Get: Chat APIs, semantic search, embedding generation, streaming pipelines, MCP integration
```

---

## Technology Stack

**70+ integrated modules spanning:**

- **Data**: PostgreSQL, MongoDB, SQLite, Redis, Vector databases
- **AI**: Ollama, OpenAI, Azure OpenAI, semantic search, embeddings
- **Pipelines**: Semantic streaming, AI tokenization, cross-pillar integration
- **Messaging**: RabbitMQ, Azure Service Bus, in-memory patterns
- **Web**: Authentication (Google, Microsoft, Discord), GraphQL, transformers
- **Orchestration**: Docker, Podman, Aspire, CLI automation
- **Enterprise**: Secrets management, observability, backup, health monitoring

**Integrated modules that work together seamlessly through semantic pipeline patterns.**

---

## What Teams Are Building

- **AI-native applications** with chat, embeddings, and semantic search
- **Streaming data pipelines** processing millions of documents with AI enrichment
- **Event-driven architectures** with sophisticated business logic
- **Multi-tenant SaaS** with provider transparency across environments
- **Content processing workflows** that combine AI, vector search, and notifications
- **Rapid prototypes** that scale to production without rewrites
- **Enterprise applications** with governance-friendly deployment artifacts

---

## Community & Contribution

**Community and contribution:**

- **Star the repository** to show support
- **Report issues** you encounter
- **Suggest features** based on your needs
- **Submit pull requests** for improvements
- **Join discussions** about framework design

Feedback and contributions help improve the framework.

---

## Requirements & Getting Started

- **.NET 10 SDK** or later
- **Docker** or **Podman** (for container features)
- **5 minutes** to get started

### Install & Run

```bash
# Option 1: Try the quickstart
git clone https://github.com/koan-framework/quickstart
cd quickstart && dotnet run

# Option 2: Start from scratch
dotnet new web -n MyKoanApp
dotnet add package Koan.Core Koan.Web Koan.Data.Connector.Sqlite
# Add your Entity<> models and EntityController<> endpoints
dotnet run
```

**Get a working application with enterprise features in under 2 minutes.**

---

## Enterprise Support & Documentation

- **[Complete Documentation](docs/index.md)** - Architecture, patterns, and guides
- **[Quickstart Guide](docs/getting-started/quickstart.md)** - Get running immediately
- **[Enterprise Architecture Guide](docs/architecture/principles.md)** - Strategic framework adoption
- **[Troubleshooting Guide](docs/support/troubleshooting.md)** - Solutions to common challenges

**For enterprise adoption support and architecture guidance, explore our comprehensive documentation.**

---

## License

Licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.

---

**Koan Framework: Build sophisticated apps with simple patterns.**

_A .NET framework that makes small teams capable of sophisticated solutions._

