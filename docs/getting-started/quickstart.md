---
type: GUIDE
domain: core
title: "Build your first Koan application"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: public template install, SQLite-backed REST create/read, and runtime facts
---

# Build your first Koan application

## Result

You will create a .NET 10 web application, persist a Todo through SQLite, expose it through HTTP, and
inspect why Koan selected that runtime shape.

## Create and run

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

Use the URL printed by ASP.NET Core. In another shell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The POST and GET prove the application result. The facts response explains the referenced modules,
selected data provider, configuration provenance, and readiness posture that produced it.

## Read the application

The host is the normal .NET host plus one Koan composition call:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The business model and HTTP surface are:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

Referenced capabilities contribute persistence, schema readiness, controller conventions, startup
reporting, health, and runtime facts. The application contains no repository, `DbContext`, schema
script, provider registration, or controller CRUD implementation.

## Guarantee and correction

The template references the supported SQLite provider and uses compatible `0.20.*` package ranges.
SQLite provides durable embedded storage for a local or single-node application; it does not promise
remote-database behavior or multi-node writes.

If a configured provider was not referenced or cannot satisfy the requested capability, Koan rejects
the intent and names the missing package, configuration, or provider guarantee. It does not silently
move the Entity to another backend.

## Inspect before changing configuration

1. Read the startup report.
2. Use `/health/live` for process liveness.
3. Use `/health/ready` for selected dependency readiness.
4. Read `/.well-known/Koan/facts` for the redacted composition decisions.
5. Review `koan.lock.json` when referenced modules change.

## Add the next capability

Choose by business need:

- [Data](../reference/data/index.md) for queries, relationships, paging, streaming, and providers.
- [Web](../reference/web/index.md) for HTTP conventions and projections.
- [Identity and isolation](../reference/identity/index.md) for authentication, access, and tenancy.
- [Work and communication](../reference/communication/index.md) for Jobs, Events, and Transport.
- [State and content](../reference/state-content/index.md) for Cache, Storage, and Media.
- [Intelligence](../reference/ai/index.md) for AI, vector, and search capabilities.
- [Agents](../reference/agents/index.md) for governed MCP exposure.
- [Testing and operations](../reference/operations/index.md) for conformance and diagnostics.

For a complete repository-owned application, run [FirstUse](../../samples/FirstUse/README.md). Use
only the [graduated sample portfolio](../../samples/README.md) as current application curriculum.

Already have an ASP.NET Core application? [Adopt one capability incrementally](adopt-existing-app.md)
without replacing its controllers, services, data access, or deployment topology.
