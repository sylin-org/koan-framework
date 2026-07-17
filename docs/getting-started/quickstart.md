---
type: GUIDE
domain: core
title: "Koan quickstart"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: samples/FirstUse source-checkout contract
---

# Koan quickstart

The supported first-use path is the source checkout. Public package installation is not yet a
certified coherent path.

## Clone, run, achieve one business result

```powershell
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/FirstUse
```

In another shell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/approvals `
  -ContentType application/json -Body '{"subject":"Approve supplier invoice"}'
Invoke-RestMethod http://localhost:5000/api/approvals
$filter = [uri]::EscapeDataString('{"subject":"Approve supplier invoice"}')
Invoke-RestMethod "http://localhost:5000/api/approvals?filter=$filter"
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The URL printed by ASP.NET Core is authoritative if it differs. The POST persists an approval; the
two reads prove ordinary and filtered Entity access; the facts response explains the modules and
SQLite election that produced the result.

## Read the whole application

Read [`samples/FirstUse`](../../samples/FirstUse/README.md) in this order:

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

The release tooling also rebuilds the same application from a locally staged package closure. That
proves a candidate; it does not claim that the coherent wave is already available from public feeds.

## Continue

Run [`samples/GoldenJourney`](../../samples/GoldenJourney/README.md) next. It grows the same small host
with a business rule, durable assessment job, bounded agent recommendation, and explained
configuration failure. Then use the [golden path](overview.md) and only the
[graduated sample portfolio](../../samples/README.md).
