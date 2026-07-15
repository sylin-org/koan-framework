---
type: GUIDE
domain: core
title: "Getting started with Koan"
audience: [developers, architects, ai-agents]
status: current
nav: true
---

# Getting started with Koan

This is the intended golden path: zero to a working, explained application, one concept at a time.
Source and executable checks are authoritative; the V1 capability baseline is re-verifying this path
from clean checkout.

> **Pre-1.0 note:** build from source. The public 0.17.0 `Sylin.Koan.*` packages currently have an
> internal version mismatch and cannot restore as a coherent application. See the
> [clean package probe](../initiatives/koan-v1/R02-EVIDENCE.md#clean-package-install-probe).

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
8. Web defaults — controllers, `/health`, secure headers auto-wired; JSON is **camelCase,
   nulls omitted** (Newtonsoft.Json — chosen for predictable polymorphic serialization)

## Path A — start from the executable contract

```bash
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/FirstUse
```

Create an approval, read it back, and inspect `/.well-known/Koan/facts` using the commands in the
[quickstart](./quickstart.md). Then read the small application in
[`samples/FirstUse`](../../samples/FirstUse/README.md). This exact code is the source-checkout and
package clean-room contract, including its REST, SQLite, startup-report, runtime-facts, and MCP
behavior. Once that mental model is clear, use [samples/README.md](../../samples/README.md) for the
larger learning ladder.

## Path B — from scratch (currently unavailable)

The following is Koan's intended package-first journey. It is retained to make the target concrete,
but it is not runnable against the current public 0.17.0 package set.

```bash
dotnet new web -n MyApp
cd MyApp
dotnet add package Sylin.Koan.App
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
curl http://localhost:5000/health
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
public sealed class Todo : Entity<Todo> { /* exposed to agents over MCP (Streamable HTTP) */ }
```

## When something goes wrong

1. **Read the boot report first.** It names every module discovered, the adapter elections, and
   the boot phases — most "why isn't X registered" questions are answered there.
2. Resolution failures throw messages that name the exact configuration keys to set. The
   [GoldenJourney V5 reproduction](../../samples/GoldenJourney/README.md#reproduce-v5-reject-explain-recover)
   shows a rejected default adapter, its machine-readable correction, and a clean recovery.
3. **Check the composition lockfile.** The checked-in
   [`koan.lock.json`](../guides/composition-lockfile.md) records statically referenced modules; the
   runtime-resolved twin adds adapter elections. The boot report's `lockfile DRIFT(...)` verdict
   points straight at an unexpected difference between those views.
4. [Troubleshooting hub](../support/troubleshooting.md) · [debugging guide patterns](../guides/README.md).

## Where next

- **Samples ladder**: [samples/README.md](../../samples/README.md) — S0 (console, 5 min) →
  S1 (CRUD + relationships) → S10 (live multi-provider switching) → S14 (jobs + benchmarks),
  then the dogfood flagships (S5 recommendations, S16 vision+MCP).
- **Guides**: [building APIs](../guides/building-apis.md) ·
  [data modeling](../guides/data-modeling.md) · [AI integration](../guides/ai-integration.md) ·
  [auth setup](../guides/authentication-setup.md) · [jobs](../guides/jobs-howto.md).
- **Why it's built this way**: [product constitution](../architecture/product-constitution.md) ·
  [architecture principles](../architecture/principles.md) ·
  [ADR index](../decisions/index.md).
- **What's settled vs experimental**: [the framework's own assessment](../assessment/00-overview.md).
- **The vocabulary**: [glossary](../reference/glossary.md) — every term defined and pinned to
  the type that defines it (entity, partition, adapter, pushdown, boot report, lane, coherence).
