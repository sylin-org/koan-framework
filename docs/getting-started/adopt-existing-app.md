---
type: GUIDE
domain: core
title: "Adopt Koan in an existing application"
audience: [developers, architects, technical-leads, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: reviewed
  scope: incremental ASP.NET Core adoption, coexistence, inspection, and rollback
---

# Adopt Koan in an existing application

Koan does not require an application rewrite. Add one capability at a clear boundary, keep the
existing host and integrations, and expand only after the result earns its place.

## Add one persisted model

In an existing ASP.NET Core application, add the supported SQLite connector:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

Keep the existing host and add one composition call:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(); // existing application services remain
builder.Services.AddKoan();

var app = builder.Build();
// Keep the application's existing middleware and endpoint mappings here.
await app.RunAsync();
```

Introduce Koan at one new or deliberately carved-out domain boundary:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[ApiController, Route("api/todos")]
public sealed class TodosController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<Todo>> Get(CancellationToken ct) => Todo.All(ct);
}
```

Existing controllers, middleware, services, EF Core models, repositories, and provider SDK clients
continue to work. Use `EntityController<T>` only when its projection is the intended boundary.

## What stays ordinary .NET

| Existing concern | Adoption rule |
|---|---|
| Hosting and DI | Keep the current host and registrations; call `AddKoan()` once. |
| Controllers and middleware | Keep them. Add Koan projections only where their conventions are useful. |
| EF Core or repositories | Leave existing aggregates in place; adopt Entity semantics one boundary at a time. |
| Authentication | Existing ASP.NET Core authorization remains valid; add Koan Identity only when its capability is needed. |
| External SDK clients | Keep application-specific integrations; use a Koan connector only for a capability Koan owns. |
| Docker, Aspire, or Kubernetes | Keep the topology unchanged; connectors consume configuration and report readiness. |

## Prove the increment

1. Start the application and read Koan's startup composition.
2. Exercise the new business path while existing paths remain unchanged.
3. Add an Entity conformance test when the boundary is intended to remain.
4. Confirm the selected provider and its limits in the
   [product surface](../reference/product-surface.md).

If the increment does not help, remove the package reference, the new Entity boundary, and
`AddKoan()` when no other Koan capability uses it. Data already written to the selected provider is an
application migration decision; Koan does not silently move or delete it.

Continue with the [capability curriculum](../index.md), or review
[architecture responsibilities](../architecture/index.md) before adding another capability.
