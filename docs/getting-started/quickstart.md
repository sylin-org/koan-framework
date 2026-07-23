---
type: GUIDE
domain: core
title: "Build your first Koan application"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-23
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: public template install, SQLite-backed REST create/read, and runtime facts
---

# Build your first Koan application

Five minutes. One Entity. One Controller. A real API that keeps its data.

This is the application you are about to run:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

That is the intent. Koan brings the persisted, queryable HTTP API.

## Make it real

You need the .NET 10 SDK.

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run -- --urls http://localhost:5000
```

In another shell, create a Todo and read it back:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
```

Stop the app, start it again, and repeat the GET. Your Todo is still there.

**Entity. Controller. Done.**

## Where did the plumbing go?

The generated host is ordinary ASP.NET Core with one Koan call:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The template references Koan's SQLite provider. From that reference and your declarations, Koan
sets up persistence, prepares the schema, maps the controller, and reports whether the app is ready.

There is no repository, `DbContext`, schema script, provider registration, CRUD service, or endpoint
mapping to maintain.

Want to look behind the magic?

```powershell
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The response shows what Koan found, what it selected, and why. For quick operational checks, use
`/health/live` and `/health/ready`.

## Let the idea grow

Keep the Entity. Add only what the application needs:

- [Change or expand its data](../reference/data/index.md).
- [Give it identity and tenant boundaries](../reference/identity/index.md).
- [Add jobs, events, or messaging](../reference/work/index.md).
- [Add AI and semantic search](../reference/ai/index.md).
- [Let an agent discover and work with it](../reference/agents/index.md).

Or open a complete, runnable story in the [sample portfolio](../../samples/README.md). The
[FirstUse application](../../samples/FirstUse/README.md) is the smallest complete example.

Already have an ASP.NET Core application? [Bring Koan into one boundary](adopt-existing-app.md)
without replacing what already works.

## Start small. Know the edges.

Koan 0.20 is a .NET 10 preview. The template uses compatible `0.20.*` packages and durable embedded
SQLite storage, which is a great fit for local and single-node applications—not a promise of
remote-database or multi-node-write behavior.

When you choose another provider, Koan will not quietly fall back to a different one. If its package
or configuration is missing, startup tells you what to add or change.
