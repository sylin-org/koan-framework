---
type: GUIDE
domain: core
title: "Getting started with Koan"
audience: [developers, architects, ai-agents]
status: current
nav: true
---

# Getting started with Koan

This is the golden path: zero to a working, explained application, one concept at a time.
Every code block on this page compiles against the current source — if one doesn't, that is a
bug; please report it.

> **Pre-1.0 note**: until 1.0, the recommended path is **building from source** (Path A below).
> Published NuGet packages use the `Sylin.Koan.*` prefix and may lag the repo.

## The concept budget

Hello-CRUD needs exactly **eight Koan concepts**. This page introduces them in order; nothing
else is required:

1. `Entity<T>` — your model, with a string `Id` (GUID v7, generated on first read)
2. Entity statics & instance verbs — `Get` / `All` / `Query` / `Save()` / `Remove()`
3. `EntityController<T>` — the REST surface over an entity
4. `AddKoan()` — the single bootstrap call ("Reference = Intent": referenced packages register themselves)
5. Provider election — the referenced data connector wins; zero-config defaults (SQLite → `./data/app.db`)
6. The boot report — what the app prints at startup is the primary debugging surface
7. Auto schema ("magic") — created in Development; gated by `Koan:AllowMagicInProduction` elsewhere
8. Web defaults — controllers, `/api/health`, secure headers auto-wired; JSON is **camelCase,
   nulls omitted** (Newtonsoft.Json — chosen for predictable polymorphic serialization)

## Path A — run it from the repo (60 seconds)

```bash
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/S1.Web
```

Browse the printed URL; hit `/api/health`; read the boot report in the console — it lists every
discovered module, the elected adapters, and the boot phases. Then read
[samples/README.md](../../samples/README.md) for the learning ladder
(S0 → S1 → S10 → S14).

## Path B — from scratch

```bash
dotnet new web -n MyApp
cd MyApp
dotnet add package Sylin.Koan.Core
dotnet add package Sylin.Koan.Web
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

**Program.cs — this is complete.** Resist the urge to add more; the web pipeline, controllers,
health endpoints, and discovery are auto-wired when `Koan.Web` is referenced:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

Model + API:

```csharp
using Koan.Data.Core.Model;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

[Route("api/[controller]")]
public sealed class TodoController : EntityController<Todo> { }
```

Run and verify:

```bash
dotnet run
curl -X POST http://localhost:5000/api/todo -H "Content-Type: application/json" -d '{"title":"Experience Koan"}'
curl http://localhost:5000/api/todo
curl http://localhost:5000/api/health
```

Working with data (the verbs — note: `Query`, not `Where`; `Remove`, not `Delete`):

```csharp
var todo  = await Todo.Get(id);                      // null when missing
var open  = await Todo.Query(t => !t.IsCompleted);   // pushed down when the adapter supports it
var page  = await Todo.FirstPage(20);
await new Todo { Title = "Ship it" }.Save();
await todo.Remove();
await foreach (var t in Todo.AllStream(batchSize: 1000)) { /* streaming, constant memory */ }
```

## Growing by intent (one package + one attribute each)

Each step below is independent. New concepts per step are listed — that's the price you pay.

### Swap the database (+1 concept: connection configuration)

```bash
dotnet add package Sylin.Koan.Data.Connector.Postgres
```

```json
{ "Koan": { "Data": { "Postgres": { "ConnectionString": "Host=localhost;Database=myapp" } } } }
```

Same entities, same controllers. Capability differences are negotiated, not hidden:

```csharp
if (Data<Todo, string>.Capabilities.Has(DataCaps.Query.Linq)) { /* pushdown active */ }
```

### Cache entities (+1 concept: `[Cacheable]`)

```bash
dotnet add package Sylin.Koan.Cache
```

```csharp
[Cacheable(300)]   // 5-minute TTL; L1/L2 layering and cross-node coherence by reference
public sealed class Todo : Entity<Todo> { /* … */ }
```

Reads past TTL return fresh-or-null — stale data is opt-in, never a surprise.

### Background jobs (+2 concepts: `IKoanJob<T>`, `JobContext`)

```bash
dotnet add package Sylin.Koan.Jobs
```

```csharp
public sealed class ImportJob : Entity<ImportJob>, IKoanJob<ImportJob>
{
    public string Source { get; set; } = "";

    public static async Task Execute(ImportJob job, JobContext ctx, CancellationToken ct)
    {
        // at-least-once, retried, schedulable (interval / cron / @boot), progress-reporting
    }
}
```

Jobs ride the data pillar: in-memory by default, durable + distributed when a database connector
is present. See the [jobs guide](../guides/jobs-howto.md).

### Messaging (+1 concept: `.Send()`)

```bash
dotnet add package Sylin.Koan.Messaging.Connector.RabbitMq
```

```csharp
await new TodoCompleted { TodoId = todo.Id }.Send();
```

### Semantic search (+2 concepts: `[Embedding]`, vector adapters)

```bash
dotnet add package Sylin.Koan.Data.AI
dotnet add package Sylin.Koan.AI.Connector.Ollama
dotnet add package Sylin.Koan.Data.Vector.Connector.Weaviate
```

```csharp
[Embedding(Properties = new[] { nameof(Title) })]
public sealed class Todo : Entity<Todo> { /* embeddings maintained by a background worker */ }
```

```csharp
using static Koan.Data.AI.EntityEmbeddingExtensions;

var related = await SemanticSearch<Todo>("groceries and meal planning", limit: 10);
```

### Agent tools (+1 concept: `[McpEntity]`)

```bash
dotnet add package Sylin.Koan.Mcp
```

```csharp
[McpEntity]
public sealed class Todo : Entity<Todo> { /* exposed to agents over MCP (HTTP/SSE) */ }
```

## When something goes wrong

1. **Read the boot report first.** It names every module discovered, the adapter elections, and
   the boot phases — most "why isn't X registered" questions are answered there.
2. Resolution failures throw messages that name the exact configuration keys to set.
3. [Troubleshooting hub](../support/troubleshooting.md) · [debugging guide patterns](../guides/README.md).

## Where next

- **Samples ladder**: [samples/README.md](../../samples/README.md) — S0 (console, 5 min) →
  S1 (CRUD + relationships) → S10 (live multi-provider switching) → S14 (jobs + benchmarks),
  then the dogfood flagships (S5 recommendations, S16 vision+MCP).
- **Guides**: [building APIs](../guides/building-apis.md) ·
  [data modeling](../guides/data-modeling.md) · [AI integration](../guides/ai-integration.md) ·
  [auth setup](../guides/authentication-setup.md) · [jobs](../guides/jobs-howto.md).
- **Why it's built this way**: [architecture principles](../architecture/principles.md) ·
  [ADR index](../decisions/index.md).
- **What's settled vs experimental**: [the framework's own assessment](../assessment/00-overview.md).
