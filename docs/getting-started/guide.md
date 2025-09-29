---
type: GUIDE
domain: core
title: "Complete Getting Started Guide"
audience: [developers, architects, ai-agents]
last_updated: 2025-02-14
framework_version: v0.2.18
status: current
validation:
  date_last_tested: 2025-02-14
nav: true
---

# Master Koan from First Request to AI-Native Service

## Contract

- **Inputs**: .NET 9 SDK+, a terminal or IDE, and basic familiarity with C# web projects.
- **Outputs**: A Koan service featuring REST CRUD, messaging events, AI-assisted endpoints, and automation hooks.
- **Error Modes**: Missing Koan packages, disabled container runtime (for optional steps), or providers lacking advertised capabilities.
- **Success Criteria**: `dotnet run` serves `/api/todos`, event handlers log activity, and AI endpoints respond when a provider is configured.

### Edge Cases

- Windows PowerShell needs `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser` before running Koan CLI scripts.
- Messaging and AI sections require matching Koan packages; skip them when offline or when providers are unavailable.
- Vector search depends on adapter capabilities—confirm semantic search is supported before calling `Todo.SemanticSearch`.
- Flow automation should run inside long-lived processes; use `dotnet watch run` or hosted services for production deployments.

---

## 1. Understand the Koan Philosophy

Koan targets five experiences that shape every decision:

1. **Get started quickly** – meaningful results in minutes.
2. **Entity<T> scales elegantly** – the same pattern spans CRUD, messaging, and automation.
3. **AI feels native** – intelligent features look like standard DI code.
4. **Intelligent automation** – small teams ship sophisticated workflows.
5. **Works with what you know** – standard .NET tooling remains the default.

Keep these pillars in mind; the rest of the guide puts them into practice.

---

## 2. Prepare Your Environment

### System Checklist

- .NET 9 SDK or newer
- An IDE (VS 2022, VS Code, or Rider)
- Docker (optional, for adapters that expect local infrastructure)
- Git (for samples and version control)

### Quick Verification

```bash
# Confirm .NET version (≥ 9.0)
dotnet --version

# Optional: confirm Docker is running
docker info
```

If Docker isn’t installed, you can still complete every step aside from containerized adapters.

---

## 3. Build the Two-Minute API

Create a new project and add the Koan pillars you need:

```bash
mkdir TodoApp
cd TodoApp
dotnet new web
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
```

Define an entity—Koan takes over from there:

```csharp
// Models/Todo.cs
using Koan.Data.Abstractions;

namespace TodoApp.Models;

public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public string Category { get; set; } = "General";
}
```

One-line REST controller:

```csharp
// Controllers/TodosController.cs
using Microsoft.AspNetCore.Mvc;
using Koan.Web;
using TodoApp.Models;

[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

Minimal hosting model:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

Run and test:

```bash
dotnet run

curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title":"Learn Koan","category":"Learning"}'

curl http://localhost:5000/api/todos
curl http://localhost:5000/api/health
```

You now have REST CRUD, health checks, telemetry, and a SQLite datastore without touching configuration files.

---

## 4. Scale the Pattern with Messaging

Events are entities just like models:

```csharp
// Models/TodoCompleted.cs
public class TodoCompleted : Entity<TodoCompleted>
{
    public string TodoId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
```

Add business logic to your controller while keeping `EntityController<T>` in play:

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    public override async Task<ActionResult<Todo>> Put(string id, Todo todo)
    {
        var existing = await Todo.Get(id);
        if (existing is null) return NotFound();

        var result = await base.Put(id, todo);

        if (todo.IsCompleted && !existing.IsCompleted)
        {
            await new TodoCompleted
            {
                TodoId = todo.Id,
                Title = todo.Title
            }.Send();
        }

        return result;
    }
}
```

Add messaging by intent:

```bash
dotnet add package Koan.Messaging.InMemory
```

Handle the events with Koan Flow:

```csharp
// Program.cs (excerpt)
Flow.OnCreate<TodoCompleted>(async completed =>
{
    Console.WriteLine($"Todo '{completed.Title}' completed at {completed.CompletedAt:o}");
    return UpdateResult.Continue();
});
```

Re-run `dotnet run` and toggle a todo to completed—the console logs confirm the event pipeline is alive.

---

## 5. Layer in AI Features

Add the AI provider you want—Koan discovers it automatically:

```bash
dotnet add package Koan.AI.Ollama
```

Inject `IAiService` for suggestions and semantic search:

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    private readonly IAiService _ai;
    public TodosController(IAiService ai) => _ai = ai;

    [HttpPost("{id}/suggestions")]
    public async Task<ActionResult<string>> GetSuggestions(string id)
    {
        var todo = await Todo.Get(id);
        if (todo is null) return NotFound();

        var suggestion = await _ai.Chat($"What should I do after completing: {todo.Title}?");
        return Ok(suggestion);
    }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IEnumerable<Todo>>> SemanticSearch([FromQuery] string query)
        => Ok(await Todo.SemanticSearch(query));
}
```

Test the endpoints once your AI provider is configured:

```bash
curl "http://localhost:5000/api/todos/{todoId}/suggestions"

curl "http://localhost:5000/api/todos/semantic-search?query=work projects"
```

Skip this section when an AI provider isn’t available; the rest of the service continues to operate normally.

---

## 6. Automate with Flow

Keep expanding the same project—Flow hooks enable rich domain automation without new infrastructure:

```csharp
Flow.OnUpdate<Todo>(async (todo, previous) =>
{
    if (todo.IsCompleted && !previous.IsCompleted)
    {
        await new TodoCompleted
        {
            TodoId = todo.Id,
            Title = todo.Title
        }.Send();
    }

    return UpdateResult.Continue();
});
```

Swap providers when you need more power:

```bash
dotnet add package Koan.Data.Postgres Koan.Data.Vector
```

The same entity code now targets PostgreSQL and a vector database without any controller changes.

Generate deployment assets via the Koan CLI when you are ready to ship:

```bash
koan export compose --profile Local
```

---

## 7. Work the Way You Already Do

Koan augments the standard .NET toolchain—you keep your workflows:

- `dotnet build`, `dotnet test`, and `dotnet publish` behave exactly as expected.
- `dotnet watch run` pairs perfectly with Flow automation for iterative development.
- Dockerfiles can be generated or hand-authored; Koan’s Compose export simply saves time.
- Aspire integration works out-of-the-box—`builder.Services.AddKoan()` participates inside Aspire orchestrations.

---

## 8. Where to Go Next

- Start with the high-level context in the [framework overview](./overview.md).
- Compare Koan to adjacent stacks using the [architecture comparison](../architecture/comparison.md).
- Review the docs [index](../index.md) to see current migration status and discover more guides.
- Explore distributed patterns in the [ASPIRE integration guide](../ASPIRE-INTEGRATION.md).

---

**You now have a clear path from the first entity to AI-native automation using consistent, minimal patterns. Keep iterating—the pillars stay the same even as your architecture grows.**
