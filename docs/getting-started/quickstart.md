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

## Clone, run, achieve one business result

```bash
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/FirstUse
```

In another shell:

```bash
curl -X POST http://localhost:5000/api/approvals \
  -H "Content-Type: application/json" \
  -d '{"subject":"Approve supplier invoice"}'
curl http://localhost:5000/api/approvals
curl http://localhost:5000/.well-known/Koan/facts
```

The URL printed by the app is authoritative if it differs from `localhost:5000`. Read the console
**boot report** and the facts response: together they identify discovered modules, the elected
SQLite adapter, and any degraded or corrective state.

Now read [`samples/FirstUse`](../../samples/FirstUse/README.md). The meaningful application code is
one `Approval : Entity<Approval>`, one `EntityController<Approval>`, and the complete four-line
bootstrap. Storage, schema creation, REST mechanics, health, runtime explanation, and MCP hosting
come from referenced intents.

This is an executable contract, not a documentation-only example. CI builds this exact directory
from repository source and exercises REST, facts, MCP discovery, authorization, dry-run, and an
agent write observed through REST. The release compiler copies the same directory into a clean
room and repeats the proof using only staged packages.

Next, run [`samples/GoldenJourney`](../../samples/GoldenJourney/README.md). It is the executable
growth contract: the application gains a real rule, durable background assessment, a bounded agent
recommendation, and an explained configuration failure while its bootstrap remains unchanged.

To explore the whole framework: open `Koan.sln`.

## Package-first project — currently unavailable

The intended package-first journey is shown below as a product target, not a runnable quickstart.
Do not use it until the public release status above changes. The package-shaped source of truth is
[`FirstUse.csproj`](../../samples/FirstUse/FirstUse.csproj); the clean-room release proof consumes
`Sylin.Koan.App`, the SQLite connector, and `Sylin.Koan.Mcp`.

```bash
dotnet new web -n MyApp
cd MyApp
dotnet add package Sylin.Koan.App
dotnet add package Sylin.Koan.Data.Connector.Sqlite
dotnet add package Sylin.Koan.Mcp
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

public sealed class Approval : Entity<Approval>
{
    public string Subject { get; set; } = "";
}

[Route("api/approvals")]
public sealed class ApprovalsController : EntityController<Approval>;
```

```bash
dotnet run
curl -X POST http://localhost:5000/api/approvals -H "Content-Type: application/json" -d '{"subject":"hello"}'
curl http://localhost:5000/api/approvals
```

You now have REST CRUD with pagination, GUID v7 ids, `/health`, structured logging, and a
zero-config SQLite database at `./data/app.db`. JSON defaults: camelCase, nulls omitted.

**Continue**: [the golden path](./overview.md) — the full concept-by-concept tour (database
swap, caching, jobs, messaging, semantic search, agent tools).
