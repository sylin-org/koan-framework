---
type: GUIDE
domain: core
title: "Koan Quickstart"
audience: [developers, ai-agents]
status: current
nav: true
---

# Quickstart

The source checkout is the only currently demonstrated path. The public 0.17.0 `Sylin.Koan.*`
packages have an internal version mismatch and cannot restore as a coherent application; see the
[clean package probe](../initiatives/koan-v1/R02-EVIDENCE.md#clean-package-install-probe).

## Clone and run

```bash
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/S1.Web
```

- Browse the printed URL; try `/api/todo` and `/api/health`.
- Read the console **boot report** — it lists discovered modules, elected adapters, and boot
  phases. It is the framework's primary self-description and debugging surface.
- Next rungs: `samples/S0.ConsoleJsonRepo` (minimal console),
  `samples/S10.DevPortal` (live multi-provider switching),
  `samples/S14.AdapterBench` (jobs + cross-adapter benchmarks).
  Ladder details: [samples/README.md](../../samples/README.md).

To explore the whole framework: open `Koan.sln`.

## Package-first project — currently unavailable

The intended package-first journey is shown below as a product target, not a runnable quickstart.
Do not use it with the current 0.17.0 public package set.

```bash
dotnet new web -n MyApp
cd MyApp
dotnet add package Sylin.Koan.Core
dotnet add package Sylin.Koan.Web
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

> Package IDs are prefixed `Sylin.` on NuGet; code namespaces are plain `Koan.*`.
> `dotnet add package` takes one package per invocation.

<!-- validate -->
```csharp
// Program.cs — complete.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

<!-- validate -->
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

```bash
dotnet run
curl -X POST http://localhost:5000/api/todo -H "Content-Type: application/json" -d '{"title":"hello"}'
curl http://localhost:5000/api/todo
```

You now have REST CRUD with pagination, GUID v7 ids, `/api/health`, structured logging, and a
zero-config SQLite database at `./data/app.db`. JSON defaults: camelCase, nulls omitted.

**Continue**: [the golden path](./overview.md) — the full concept-by-concept tour (database
swap, caching, jobs, messaging, semantic search, agent tools).
