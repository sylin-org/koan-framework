---
type: GUIDE
domain: framework
title: "Koan documentation"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: public template install, generation, clean restore/build, SQLite Entity result, and runtime facts
---

# Koan documentation

Koan is an opinionated .NET 10 meta-framework for agentic, data-driven applications. Its job is to
turn business intent into readable code while centralizing composition, backend negotiation,
lifecycle, and explanation.

## Start with a result

Install the public template and create a persisted web API:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

Then create and inspect one Todo:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/todos `
  -ContentType application/json -Body '{"title":"buy milk"}'
Invoke-RestMethod http://localhost:5000/api/todos
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

Use the URL printed by the application if it differs. The [quickstart](getting-started/quickstart.md)
explains the result; [FirstUse](../samples/FirstUse/README.md) is the richer repository-owned executable contract.

## Read business, not plumbing

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

References make capabilities available. `AddKoan()` compiles the referenced module set. Entity and
pillar semantics express application intent; elected providers own mechanics. Startup, health,
runtime facts, and agent resources explain the same resolved composition.

## The current learning path

1. [Quickstart](getting-started/quickstart.md) — run one meaningful approval workflow.
2. [Golden path](getting-started/overview.md) — learn the minimal application grammar and grow it deliberately.
3. [Graduated samples](../samples/README.md) — use only examples with focused executable evidence.
4. [Developer guides](guides/README.md) — add a capability when the business needs it.
5. [Product surface](reference/product-surface.md) — check maturity, package shape, and evidence before relying on a claim.
6. [Architecture](architecture/product-constitution.md) — understand the laws behind the conventions.

## Current support boundary

Koan 0.20 is a preview, not a 1.0 compatibility promise. Package patches remain independently owned,
and package presence does not imply support, production certification, or backend parity. The generated
[product surface](reference/product-surface.md) names the supported foundation, supported extensions,
demonstrations, experiments, specifications, and unassessed packages.

Use canonical `/health/live` and `/health/ready` probes. Use `/.well-known/Koan/facts` or
`koan://facts` to understand runtime decisions. Unsupported configured intent should reject with a
correction instead of silently choosing weaker behavior.

## Documentation boundary

This navigation is the current public product curriculum. Initiative ledgers, assessments, plans,
proposals, and archived material remain in the repository as engineering evidence; they are not
alternate usage guidance. Architecture decision records are preserved as dated decisions and may
describe the system at the time they were written.
