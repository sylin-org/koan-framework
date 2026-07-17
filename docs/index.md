---
type: GUIDE
domain: framework
title: "Koan documentation"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: public documentation front door and executable FirstUse path
---

# Koan documentation

Koan is an opinionated .NET 10 meta-framework for agentic, data-driven applications. Its job is to
turn business intent into readable code while centralizing composition, backend negotiation,
lifecycle, and explanation.

## Start with a result

From a new checkout:

```powershell
dotnet run --project samples/FirstUse
```

Then create and inspect one approval:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/approvals `
  -ContentType application/json -Body '{"subject":"Approve supplier invoice"}'
Invoke-RestMethod http://localhost:5000/api/approvals
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

Use the URL printed by the application if it differs. The [quickstart](getting-started/quickstart.md)
explains the result; [FirstUse](../samples/FirstUse/README.md) is the executable contract.

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

Koan is pre-1.0 and source-first. Package versions are owned independently; a coherent candidate wave
has local clean-room evidence, but public-feed installation has not yet been observed. Package presence
does not imply support, production certification, or backend parity. The generated
[product surface](reference/product-surface.md) is the authority for assessed, experimental,
specified, and unassessed capabilities.

Use canonical `/health/live` and `/health/ready` probes. Use `/.well-known/Koan/facts` or
`koan://facts` to understand runtime decisions. Unsupported configured intent should reject with a
correction instead of silently choosing weaker behavior.

## Documentation boundary

This navigation is the current public product curriculum. Initiative ledgers, assessments, plans,
proposals, and archived material remain in the repository as engineering evidence; they are not
alternate usage guidance. Architecture decision records are preserved as dated decisions and may
describe the system at the time they were written.
