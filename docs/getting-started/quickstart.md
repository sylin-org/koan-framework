---
type: GUIDE
domain: core
title: "Koan quickstart"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: public 0.20 template install, clean restore/build, SQLite-backed REST create/read, and runtime facts
---

# Koan quickstart

Install the public template and create the application:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

In another shell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The URL printed by ASP.NET Core is authoritative if it differs. The POST persists a Todo; the read proves
ordinary Entity access; the facts response explains the modules and SQLite election that produced the result.
The [template guide](../../templates/README.md) contains the exact generated shape.

## Read the whole application

Repository contributors can read [`samples/FirstUse`](../../samples/FirstUse/README.md) in this order:

1. `Domain/Approval.cs` — business state and access policy.
2. `Web/ApprovalsController.cs` — the governed HTTP surface.
3. `Program.cs` — the complete host.

The host is exactly:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Storage, schema readiness, controller mechanics, health, startup reporting, runtime facts, and MCP
hosting come from referenced capabilities. The application contains no repository, `DbContext`,
database registration, schema script, MCP tool handler, or health plumbing.

## Know what is proved

FirstUse is exercised through its real host. Focused evidence covers REST, SQLite persistence,
filtered query, readiness, composition facts, MCP discovery, access policy, dry-run, and an agent
write observed through REST. Its checked-in `koan.lock.json` records referenced composition.

The public-feed journey has been observed independently: template installation, generation, clean restore/build,
SQLite-backed REST create/read, and runtime facts all pass without repository package sources.

## Continue

Run [`samples/GoldenJourney`](../../samples/GoldenJourney/README.md) next. It grows the same small host
with a business rule, durable assessment job, bounded agent recommendation, and explained
configuration failure. Then use the [golden path](overview.md) and only the
[graduated sample portfolio](../../samples/README.md).
