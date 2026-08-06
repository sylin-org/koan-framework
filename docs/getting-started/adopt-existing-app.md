---
type: GUIDE
domain: core
title: "Adopt Koan in an existing application"
audience: [developers, architects, technical-leads, ai-agents]
status: current
last_updated: 2026-07-23
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: reviewed
  scope: incremental ASP.NET Core adoption, coexistence, inspection, and rollback
---

# Bring Koan into the app you already have

Keep your application. Pick one place where plumbing is louder than intent. Let Koan own just that
slice, then decide whether it has earned another.

## Start with one Entity

Add Koan's supported SQLite provider:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

Keep the host you have and add one Koan line:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(); // your existing services stay
builder.Services.AddKoan();

var app = builder.Build();
// Keep your existing middleware and endpoint mappings.
await app.RunAsync();
```

Now declare one new—or deliberately carved-out—piece of the domain:

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
```

Koan gives that Entity durable SQLite persistence. How much more it owns is your choice.

## Keep your controller

Use Entity operations inside an ordinary ASP.NET Core controller:

```csharp
[ApiController, Route("api/todos")]
public sealed class TodosController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<Todo>> Get(CancellationToken ct) => Todo.All(ct);
}
```

Your routes, actions, filters, and response design remain yours.

## Or declare the whole API

When Koan's standard Entity API is exactly what you want, the controller becomes the intent:

```powershell
dotnet add package Sylin.Koan.App
```

```csharp
[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

**Entity. Controller. Done—even inside the application you already have.**

The App bundle adds Koan's ASP.NET Core integration; the explicit SQLite reference remains the
provider for this Entity.

## Nothing else has to move

Existing controllers, middleware, services, EF Core models, repositories, authentication, and SDK
clients continue to work. Your Docker, Aspire, or Kubernetes topology stays where it is. Koan
connectors consume configuration and report readiness; they do not take over deployment.

Adopt Entity semantics only at boundaries where they make the code clearer. Reach for ordinary .NET
everywhere else.

## Let the result earn the next step

Run the application and exercise both the new path and an unchanged one. Koan's startup report shows
what it added; `/.well-known/Koan/facts` shows the same choices in a machine-readable form.

Before expanding the boundary:

1. Add an Entity conformance test if this behavior should remain.
2. Check the selected provider and its limits in [what works today](../reference/what-works.md).
3. Add the next capability only when the application asks for it.

If the experiment is not helping, remove the package and the Entity boundary. Remove `AddKoan()` too
when no other Koan feature uses it. Data already written to SQLite remains an application migration
decision; Koan will never silently move or delete it.

Ready for another slice? [Choose what to add next](../index.md), or read
[where Koan fits—and where it stops](../architecture/index.md).
