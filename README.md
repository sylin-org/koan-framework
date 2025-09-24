# Koan Framework

**Try it. Be delighted. Build sophisticated apps with simple patterns.**

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/download)
[![Framework Version](https://img.shields.io/badge/Version-v0.2.18-green.svg)](https://github.com/sylin-org/koan-framework/releases)
[![GitHub Stars](https://img.shields.io/github/stars/sylin-org/koan-framework)](https://github.com/sylin-org/koan-framework/stargazers)

> **The .NET framework that makes small teams capable of sophisticated solutions through intelligent automation, AI-native patterns, and elegant scaling.**

---

## From Simple to Sophisticated in Minutes

### **1. Get started quickly**

```bash
# 2 minutes to working API
dotnet new web -n MyApp && cd MyApp
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
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
// dotnet add package Koan.Messaging.RabbitMq

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

### **4. Intelligent Automation**

```csharp
// Small teams, sophisticated solutions

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

// Multi-provider scaling with zero code changes
// dotnet add package Koan.Data.Postgres Koan.Data.Vector

await todo.Save();                                    // → PostgreSQL
var similar = await Todo.SemanticSearch("urgent");   // → Vector DB
```

**Generate sophisticated architectures and deployment artifacts automatically. Functional prototypes in hours, not weeks.**

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

| **What You Want** | **How Koan Delivers** |
|-------------------|----------------------|
| **Fast prototyping** | Functional apps in minutes, not hours |
| **Modern patterns** | AI-native, event-driven, multi-provider by design |
| **Simple scaling** | One pattern (`Entity<>`) from CRUD to enterprise |
| **Team productivity** | Small teams build sophisticated solutions |
| **Low risk adoption** | Works with existing .NET tools and knowledge |

---

## Quick Start Paths

### **For Individual Developers**
```bash
# Try the quickstart
git clone https://github.com/koan-framework/quickstart
cd quickstart && dotnet run

# Create your first app
dotnet new web -n MyApp
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
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

# Get: Chat APIs, semantic search, embedding generation, MCP integration
```

---

## Technology Stack

**70+ integrated modules spanning:**
- **Data**: PostgreSQL, MongoDB, SQLite, Redis, Vector databases
- **AI**: Ollama, OpenAI, Azure OpenAI, semantic search, embeddings
- **Messaging**: RabbitMQ, Azure Service Bus, in-memory patterns
- **Web**: Authentication (Google, Microsoft, Discord), GraphQL, transformers
- **Orchestration**: Docker, Podman, Aspire, CLI automation
- **Enterprise**: Secrets management, observability, backup, health monitoring

**Integrated modules that work together without configuration.**

---

## What Teams Are Building

- **AI-native applications** with chat, embeddings, and semantic search
- **Event-driven architectures** with sophisticated business logic
- **Multi-tenant SaaS** with provider transparency across environments
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

- **.NET 9 SDK** or later
- **Docker** or **Podman** (for container features)
- **5 minutes** to get started

### Install & Run
```bash
# Option 1: Try the quickstart
git clone https://github.com/koan-framework/quickstart
cd quickstart && dotnet run

# Option 2: Start from scratch
dotnet new web -n MyKoanApp
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
# Add your Entity<> models and EntityController<> endpoints
dotnet run
```

**Get a working application with enterprise features in under 2 minutes.**

---

## Enterprise Support & Documentation

- **[Complete Documentation](documentation/README.md)** - Architecture, patterns, and guides
- **[Quickstart Guide](documentation/getting-started/quickstart.md)** - Get running immediately
- **[Enterprise Architecture Guide](documentation/architecture/principles.md)** - Strategic framework adoption
- **[Troubleshooting Guide](documentation/support/troubleshooting.md)** - Solutions to common challenges

**For enterprise adoption support and architecture guidance, explore our comprehensive documentation.**

---

## License

Licensed under the Apache License 2.0. See [LICENSE](LICENSE) for details.

---

**Koan Framework: Build sophisticated apps with simple patterns.**

*A .NET framework that makes small teams capable of sophisticated solutions.*