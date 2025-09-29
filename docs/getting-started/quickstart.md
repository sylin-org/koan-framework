---
type: GUIDE
domain: core
title: "Koan Quickstart: From Simple to Sophisticated"
audience: [developers, architects, ai-agents]
last_updated: 2025-02-14
framework_version: v0.2.18
status: current
validation:
  date_last_tested: 2025-02-14
nav: true
---

# From Simple to Sophisticated

## Contract

- **Inputs**: .NET 9 SDK+, a terminal session, and five focused minutes.
- **Outputs**: Running Koan service with REST CRUD, messaging hook, and AI-ready endpoints.
- **Error Modes**: Missing Koan packages, blocked HTTP ports, or unsupported data/AI providers in the local environment.
- **Success Criteria**: `dotnet run` serves `/api/todos`, messaging triggers on completion, and AI endpoints respond when an AI provider is configured.

### Edge Cases

- Project folders created outside the SDK path may miss implicit `global.json`; run `dotnet --list-sdks` if builds fail.
- Messaging and AI require the matching Koan packages—omit those steps when working offline.
- Docker or Aspire integration commands assume their CLIs are installed; skip the automation section otherwise.
- Vector search relies on provider capabilities; confirm adapters advertise semantic search before calling `Todo.SemanticSearch`.

---

## Step&nbsp;1 – Launch an API (2 minutes)

Create the project and add the Koan pillars you need:

```bash
mkdir my-koan-app
cd my-koan-app
dotnet new web
dotnet add package Koan.Core Koan.Web Koan.Data.Sqlite
```

Add your first entity:

```csharp
// Models/Todo.cs
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string Category { get; set; } = "General";
}
```

Expose a full REST controller in one line:

```csharp
// Controllers/TodosController.cs
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }
```

Keep `Program.cs` minimal—the framework bootstraps everything:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

Run the service:

```bash
dotnet run
```

Verify it works:

```bash
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Experience Koan Framework", "category": "Learning"}'

curl http://localhost:5000/api/todos

curl http://localhost:5000/api/health
```

You now have REST, health checks, GUID v7 IDs, and SQLite storage without configuration.

---

## Step&nbsp;2 – Messaging in the Same Pattern (1 minute)

Events are entities too:

```csharp
// Models/TodoCompleted.cs
public class TodoCompleted : Entity<TodoCompleted>
{
    public string TodoId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
```

Hook business logic inside the controller:

```csharp
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    public override async Task<ActionResult<Todo>> Put(string id, Todo todo)
    {
        var result = await base.Put(id, todo);

        if (todo.IsCompleted)
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

Bring in messaging by intent:

```bash
dotnet add package Koan.Messaging.InMemory
```

Completion now emits `TodoCompleted` messages with no extra plumbing.

---

## Step&nbsp;3 – Add AI When You Need It (1 minute)

Reference the AI provider you want:

```bash
dotnet add package Koan.AI.Ollama
```

Add intelligent endpoints that feel like typical DI code:

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

        var suggestion = await _ai.Chat($"What should I do after completing: {todo.Title}");
        return Ok(suggestion);
    }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IEnumerable<Todo>>> SemanticSearch([FromQuery] string query)
        => Ok(await Todo.SemanticSearch(query));
}
```

Exercise the endpoints:

```bash
curl "http://localhost:5000/api/todos/{todoId}/suggestions"

curl "http://localhost:5000/api/todos/semantic-search?query=work projects"
```

If an AI provider isn’t available, skip this step—the rest of the service still runs.

---

## Step&nbsp;4 – Automate with Flow (1 minute)

Promote the app into event-driven automation:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

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

var app = builder.Build();
app.Run();
```

Scale providers by intent:

```bash
dotnet add package Koan.Data.Postgres Koan.Data.Vector
```

Same code, different providers—no config rewrites.

Add AI summarization inside Flow by injecting `IAiService` into a custom worker or using the same patterns from Step&nbsp;3 when the provider is available.

Generate deployment artifacts when you’re ready:

```bash
koan export compose --profile Local
```

---

## What You Built

- REST CRUD with validation, health, and telemetry
- Messaging hooks that follow entity patterns
- AI-assisted endpoints ready for semantic search
- Event-driven automation using Flow
- Provider swaps (SQLite → Postgres, in-memory → vector DB) without touching controllers
- Deployment assets via the Koan CLI

---

## Next Steps

- Continue in the [getting started overview](./overview.md) for architecture context.
- Graduate to the [complete getting started guide](./guide.md) for automation and AI walkthroughs.
- Compare Koan to adjacent stacks in the [architecture comparison](../architecture/comparison.md).
- Review the docs [index](../index.md) to track migration progress and locate guides as they land in `/docs`.
- Walk through the [ASPIRE integration guide](../ASPIRE-INTEGRATION.md) when you're ready for distributed setups.

---

**You just moved from concept to AI-ready service in minutes—without leaving familiar .NET patterns.**
