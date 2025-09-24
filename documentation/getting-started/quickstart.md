---
type: GUIDE
domain: core
title: "5-Minute Koan Experience: From Simple to Sophisticated"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# 5-Minute Koan Experience: From Simple to Sophisticated

**Try it. Be delighted. Watch simple patterns scale to sophisticated solutions.**

**Target Audience**: Developers ready to experience the future of .NET development
**Framework Version**: v0.2.18+

---

## The Progressive Experience

This quickstart demonstrates Koan's core promise: **sophisticated applications through simple patterns**. You'll build from a basic API to AI-native event-driven architecture in minutes, not hours.

## Prerequisites

- **.NET 9 SDK** or later
- **5 minutes** of curiosity

---

## **Step 1: Try it, be delighted** *(2 minutes)*

Create a sophisticated API in under 2 minutes:

```bash
# Create project and add core packages
mkdir my-koan-app && cd my-koan-app
dotnet new web
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
```

```csharp
// Models/Todo.cs - Your domain model
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string Category { get; set; } = "General";
}
```

```csharp
// Controllers/TodosController.cs - Full REST API in one line
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

```csharp
// Program.cs - Zero ceremony startup
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();  // Auto-discovers everything
var app = builder.Build();
app.Run();
```

```bash
dotnet run
```

**Result:** Full REST API with health checks, telemetry, auto-generated GUID v7 IDs, SQLite database - zero configuration.

**Test it:**
```bash
# Create a todo (watch the auto-generated ID)
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Experience Koan Framework", "category": "Learning"}'

# Get all todos
curl http://localhost:5000/api/todos

# Check health (auto-generated)
curl http://localhost:5000/api/health
```

**Feeling:** *"This is how .NET development should feel."*

---

## **Step 2: Entity<> scales elegantly** *(1 minute)*

Add messaging with the same pattern:

```csharp
// Models/TodoCompleted.cs - Events are entities too
public class TodoCompleted : Entity<TodoCompleted>
{
    public string TodoId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
```

```csharp
// Controllers/TodosController.cs - Add business logic
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    public override async Task<ActionResult<Todo>> Put(string id, Todo todo)
    {
        var result = await base.Put(id, todo);

        // If marked complete, send event - same pattern as .Save()
        if (todo.IsCompleted)
        {
            await new TodoCompleted {
                TodoId = todo.Id,
                Title = todo.Title
            }.Send();
        }

        return result;
    }
}
```

**Reference = Intent** (no configuration ceremony):
```bash
# Want messaging? Reference it.
dotnet add package Koan.Messaging.InMemory
```

**Feeling:** *"One pattern that grows with my needs."*

---

## **Step 3: AI feels native** *(1 minute)*

Add AI capabilities through familiar patterns:

```bash
# Want AI? Reference it.
dotnet add package Koan.AI.Ollama
```

```csharp
// Controllers/TodosController.cs - AI through dependency injection
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    private readonly IAiService _ai;
    public TodosController(IAiService ai) => _ai = ai;

    [HttpPost("{id}/suggestions")]
    public async Task<ActionResult<string>> GetSuggestions(string id)
    {
        var todo = await Todo.Get(id);
        if (todo == null) return NotFound();

        // AI feels like any other service call
        var suggestion = await _ai.Chat($"What should I do after completing: {todo.Title}");
        return Ok(suggestion);
    }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IEnumerable<Todo>>> SemanticSearch([FromQuery] string query)
    {
        // Semantic search through familiar patterns
        var results = await Todo.SemanticSearch(query);
        return Ok(results);
    }
}
```

**Test AI integration:**
```bash
# Get AI suggestions
curl "http://localhost:5000/api/todos/[your-todo-id]/suggestions"

# Semantic search (finds related todos by meaning, not keywords)
curl "http://localhost:5000/api/todos/semantic-search?query=work projects"
```

**Feeling:** *"AI without the complexity - it just works."*

---

## **Step 4: Intelligent Automation** *(1 minute)*

Scale to event-driven architecture and multi-provider data:

```csharp
// Program.cs - Add event-driven patterns
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

// Event handlers through simple patterns
Flow.OnUpdate<Todo>(async (todo, previous) => {
    // Sophisticated business logic through simple APIs
    if (todo.IsCompleted && !previous.IsCompleted) {
        await new TodoCompleted {
            TodoId = todo.Id,
            Title = todo.Title
        }.Send();

        // Generate AI-powered completion summary
        var ai = builder.Services.BuildServiceProvider().GetService<IAiService>();
        var summary = await ai.Chat($"Summarize the completion of task: {todo.Title}");

        await new TaskSummaryGenerated {
            TodoId = todo.Id,
            Summary = summary
        }.Send();
    }

    return UpdateResult.Continue();
});

var app = builder.Build();
app.Run();
```

**Scale providers with zero code changes:**
```bash
# Development: SQLite (automatic)
# Production: Add PostgreSQL + Vector search
dotnet add package Koan.Data.Postgres Koan.Data.Vector

# Same code now uses PostgreSQL for data, vector DB for semantic search
# No configuration changes needed
```

**Generate deployment artifacts:**
```bash
# Intelligent automation generates Docker Compose
koan export compose --profile Local

# Generates governance-friendly deployment artifacts
# - Health check configurations
# - Service dependencies
# - Environment-specific settings
```

**Feeling:** *"Small team, sophisticated solutions - the framework does the heavy lifting."*

---

## **Step 5: Works with what you know**

Koan enhances your existing .NET workflow:

```bash
# Standard .NET tooling works perfectly
dotnet build
dotnet test
dotnet publish

# Docker integration (if you use containers)
docker build -t my-koan-app .

# Aspire integration (if you use Aspire)
# builder.Services.AddKoan() works seamlessly with Aspire orchestration
```

**Feeling:** *"It fits perfectly into how I already work."*

---

## **What You Just Built**

In 5 minutes, you created:

- **REST API** with full CRUD operations
- **Event-driven architecture** with business logic
- **AI integration** with chat and semantic search
- **Multi-provider data access** (SQLite → PostgreSQL → Vector)
- **Health checks and telemetry** (automatic)
- **Container deployment** configuration (generated)
- **Enterprise observability** (built-in)

**All through the simple Entity<> pattern.**

---

## The Business Impact

**What this means for your team:**
- **Functional prototypes in hours**, not weeks
- **AI-native applications** without infrastructure complexity
- **Event-driven architecture** through familiar patterns
- **Multi-provider flexibility** with zero vendor lock-in
- **Production-ready observability** from day one

**What this means for your organization:**
- **Small teams capable of sophisticated solutions**
- **Governance-friendly deployment artifacts** generated automatically
- **Rapid iteration and validation** of business ideas
- **Enterprise capabilities** without enterprise complexity

---

## Next Steps

### **For Individual Developers**
- **[Complete Getting Started Guide](getting-started.md)** - Deep dive into patterns
- **[Building APIs Guide](../guides/building-apis.md)** - Advanced API patterns
- **[AI Integration Guide](../guides/ai-integration.md)** - Comprehensive AI patterns

### **For Teams & Architects**
- **[Enterprise Architecture Guide](../architecture/principles.md)** - Strategic framework adoption
- **[Sample Applications](../../samples/)** - Real-world implementations
- **[Container Orchestration](../guides/orchestration.md)** - Production deployment patterns

### **For AI-First Development**
- **[MCP Integration Guide](../guides/mcp-http-sse-howto.md)** - AI-assisted development workflow
- **[Vector Search Patterns](../guides/vector-search.md)** - Semantic search implementation
- **[Chat Integration Patterns](../guides/chat-integration.md)** - Conversational interfaces

---

**You've experienced the future of .NET development. Welcome to Koan.**

*Try it. Be delighted. Build sophisticated apps with simple patterns.*

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+