---
type: GUIDE
domain: core
title: "Koan Framework documentation"
audience: [developers, architects, ai-agents]
status: current
---

# Koan Framework Documentation

**Model your domain as entities. Reference your intents. Koan composes the rest — and tells you
exactly what it did.**

## Quick navigation

### 🚀 [Getting started](getting-started/overview.md)

The golden path: a running app in 60 seconds, then the framework concept-by-concept.
Short on time? [Quickstart](getting-started/quickstart.md).

### 📖 [Developer guides](guides/README.md)

Task-oriented how-tos:

- [Building APIs](guides/building-apis.md) — REST over entities, hooks, pagination
- [Data modeling](guides/data-modeling.md) — entity-first patterns, relationships
- [Background jobs](guides/jobs-howto.md) — entity-first jobs, scheduling, the capability ladder
- [AI integration](guides/ai-integration.md) — embeddings, semantic search, chat
- [Media recipes](guides/media-recipes-howto.md) — format-preserving image pipeline
- [Authentication](guides/authentication-setup.md) — auth providers and service auth

### 🧭 [Samples ladder](../samples/README.md)

S0 (console, 5 min) → S1 (CRUD) → S10 (multi-provider) → S14 (jobs/benchmarks), then the
dogfood flagships.

### 🚨 [Troubleshooting](support/troubleshooting.md)

Start with the **boot report** — the console output at startup names every discovered module,
adapter election, and boot phase; most registration/connectivity questions are answered there.

### 📚 [Reference](reference/index.md)

Per-pillar reference: [Core](reference/core/index.md) · [Data](reference/data/index.md) ·
[Web](reference/web/index.md) · [AI](reference/ai/index.md) ·
[Cache](reference/data/cache.md) · [API docs](api/index.md)

### 🏗️ Architecture

- **[Framework principles](architecture/principles.md)** — the canon, with enforcement points
- **[Architecture decisions](decisions/index.md)** — 280+ ADRs; supersession is explicit
- **[Framework assessment & maturity model](assessment/00-overview.md)** — the framework's
  published self-audit: what is settled, what is experimental, what is being cut

## The shape of a Koan app

```csharp
// Program.cs — complete.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

```csharp
public sealed class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

[Route("api/[controller]")]
public sealed class ProductController : EntityController<Product> { }
// → REST CRUD with pagination, GUID v7 ids, /api/health, zero-config SQLite
```

```csharp
var products = await Product.All();                 // same code on SQLite, Postgres, Mongo, …
var cheap    = await Product.Query(p => p.Price < 10);
```

Capabilities are negotiated, not assumed:

```csharp
if (Data<Product, string>.Capabilities.Has(DataCaps.Query.Linq)) { /* pushdown active */ }
```

## Current status

- **Version**: pre-1.0, [NBGV](../version.json)-driven (0.17.x); in active consolidation
  ("fewer but more meaningful parts")
- **Settled core**: data pillar + connectors, web, cache, jobs, vector, security/trust —
  see the [maturity model](assessment/03-maturity-model.md) for the full per-pillar grading
- **License**: Apache 2.0 · **Target**: .NET 10
- **Packages**: published as `Sylin.Koan.*`; until 1.0, build from source

## Contributing

ADR-first workflow; keep the green ratchet green (`scripts/green-ratchet.ps1`). See
[engineering docs](engineering/index.md) and [CONTRIBUTING](../CONTRIBUTING.md).

---

**Your entities are the app.**
