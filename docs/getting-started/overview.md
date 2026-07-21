---
type: GUIDE
domain: core
title: "Getting started with Koan"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: passed
  scope: source-built 0.20 Entity-first grammar and graduated capability examples
---

# Getting started with Koan

Koan's golden path is V0 to V1 in meaningful small steps. Each new line should express a business
decision or a deliberate capability; framework and adapter mechanics stay behind stable semantic
chokepoints.

> **0.20 preview:** run `dotnet run --project samples/FirstUse` from the source checkout today. After the first
> public wave is visible on NuGet, `dotnet new install Sylin.Koan.Templates` becomes the canonical entry. The exact
> candidate has local package evidence, but local evidence is not public availability.

## Step 1 — make one Entity useful

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
```

```csharp
var todo = await new Todo { Title = "Ship the meaningful step" }.Save();
var same = await Todo.Get(todo.Id);
var open = await Todo.Query(item => !item.Done);
await todo.Remove();
```

The string `Id` is generated lazily as a GUID v7. Provider-bounded paging and streaming use the same
Entity grammar; optional physical guarantees depend on the elected adapter.

## Step 2 — expose the same Entity through HTTP

```csharp
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

The complete host remains four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Referenced modules contribute the web pipeline, controller discovery, selected data provider,
health, and facts. The application does not enumerate them.

## Step 3 — make infrastructure intent explicit

A reference makes a provider available; the owning pillar elects among eligible providers. If the
business requires a stable choice as references grow, pin that intent on the Entity or a named source:

```csharp
[DataAdapter("sqlite")]
public sealed class Approval : Entity<Approval>
{
    public string Subject { get; set; } = "";
}
```

Configured intent is fail-loud. An unavailable or incapable provider produces a correction instead
of silently falling back. Startup and `/.well-known/Koan/facts` report the resolved decision.

## Step 4 — add only the capability the business needs

### Durable work

```csharp
public sealed class ReviewRequest : Entity<ReviewRequest>, IKoanJob<ReviewRequest>
{
    public string Title { get; set; } = "";

    public static async Task Execute(ReviewRequest request, JobContext context, CancellationToken ct)
    {
        request.Assess();
        await context.Progress(1, "Ready for recommendation");
    }
}

await request.Job.Submit(ct: ct);
```

Jobs own orchestration, persistence, retries, schedules, and progress. The Entity owns business work.

### Entity communication

```csharp
await order.Events.Raise<OrderApproved>(ct);
await order.Transport.Send(ct);
```

Events mean something happened to the Entity. Transport means distribute an isolated copy of its
current state. Both work locally with no external adapter and lift pointwise over finite Entity
collections and lazy streams. Referenced connectors can extend the mesh only when their declared
guarantees satisfy the channel.

### Semantic search

```csharp
[Embedding(Template = "{Name}. {Description}", Model = "all-MiniLM-L6-v2")]
public sealed class Produce : Entity<Produce>
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
```

The Entity keeps the semantic intent. Referenced AI and vector providers own model execution and
index mechanics. This combined surface remains experimental outside the specifically exercised
provider paths.

### Agent access

```csharp
[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
public sealed class Todo : Entity<Todo>;
```

Referencing `Koan.Mcp` activates its module through `AddKoan()`. No `AddKoanMcp()` or endpoint mapping
belongs in ordinary application code. Access policy governs REST and MCP projections of the same Entity.

## Step 5 — inspect before guessing

When behavior surprises you:

1. Read the startup report.
2. Check `/health/live` for process liveness and `/health/ready` for dependency readiness.
3. Read `/.well-known/Koan/facts` or `koan://facts` for the same redacted runtime decisions.
4. Review `koan.lock.json` for referenced-module drift.
5. Check the selected provider's declared capabilities before assuming parity.

## Executable curriculum

- [FirstUse](../../samples/FirstUse/README.md) — the shortest persisted, inspectable REST/MCP result.
- [GoldenJourney](../../samples/GoldenJourney/README.md) — cumulative business rule, job, agent boundary, and recovery.
- [Graduated samples](../../samples/README.md) — only examples with a focused meaningful proof.
- [Product surface](../reference/product-surface.md) — current maturity and package evidence.
