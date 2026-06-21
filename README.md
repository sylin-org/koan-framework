# Koan Framework

**Model your domain as entities. Reference your intents. Koan composes the rest — storage, web,
AI, jobs, caching — and tells you exactly what it did.**

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)

Koan is a .NET 10 meta-framework for **agentic, data-driven web applications** — think
*Rails for .NET*, built for the era where AI capability is part of the domain model and a lot of
your code is written with coding agents. One grammar — `Entity<T>` — covers CRUD, REST, vector
search, background jobs, caching, embeddings, and agent (MCP) tools.

> **Status: pre-1.0, consolidation phase** (version via [NBGV](version.json), currently 0.17.x).
> The core — entity/data pillar, web, cache, jobs, vector — is settled and integration-tested;
> several outer pillars are experimental and being consolidated or cut. We audit ourselves:
> see the [framework assessment](docs/assessment/00-overview.md) and the per-pillar
> [maturity model](docs/assessment/03-maturity-model.md) for exactly what is settled and what
> isn't. **Until 1.0, build from source** — published packages (`Sylin.Koan.*` on NuGet) lag the
> repo.

---

## 60 seconds to a running app

```bash
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/S1.Web
# → browse http://localhost:5044  ·  curl http://localhost:5044/api/health
```

You'll see the framework introduce itself before the first request — what got discovered, which
adapters won, what capabilities they negotiated:

```text
┌─ [KOAN] Application ─────────────────────────────
│  Name: S1.Web · Environment: Development
│  Registry: initializers, auto-registrars, background services
│  Inventory: Koan.Core, Koan.Data.Core, Koan.Web, Koan.Data.Connector.Sqlite, …
└──────────────────────────────────────────────────
[K:PHASE] warmup→registry→data→services→ready
```
*(abridged — run it to see the real thing)*

That boot report is the framework's character in one screen: **the app explains itself.**

## The whole framework in three beats

### 1 · Model an entity → get an application

<!-- validate -->
```csharp
// Program.cs — complete. Nothing else to wire.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

<!-- validate -->
```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

[Route("api/[controller]")]
public sealed class TodoController : EntityController<Todo> { }
```

That's a full REST API: GET/POST/DELETE with pagination and querying, GUID v7 ids generated on
first read, `/api/health`, structured logging, and zero-config SQLite (`./data/app.db`). Schema
is created automatically in development. JSON defaults: camelCase, nulls omitted
(Newtonsoft.Json — chosen for predictable polymorphic serialization).

Working with entities is direct — no repositories, no DbContext:

```csharp
var todo = await Todo.Get(id);                       // null if missing
var open = await Todo.Query(t => !t.IsCompleted);    // pushed down to the store
await new Todo { Title = "Try Koan" }.Save();
await todo.Remove();
await foreach (var t in Todo.AllStream(batchSize: 1000)) { /* streams, no materialization */ }
```

### 2 · Reference an intent → gain a capability

Adding a package **is** the configuration. Each line below is one package reference and (at
most) one attribute:

```csharp
// dotnet add package Sylin.Koan.Data.Connector.Postgres  → same entities, Postgres storage
// dotnet add package Sylin.Koan.Cache                    → transparent L1/L2 caching:
[Cacheable(300)]
public sealed class Todo : Entity<Todo> { /* … */ }

// dotnet add package Sylin.Koan.Messaging.Connector.RabbitMq → cross-process events:
await new TodoCompleted { TodoId = todo.Id }.Send();

// dotnet add package Sylin.Koan.Jobs → durable background work, jobs are entities too:
public sealed class ImportJob : Entity<ImportJob>, IKoanJob<ImportJob>
{
    public string Source { get; set; } = "";
    public static async Task Execute(ImportJob job, JobContext ctx, CancellationToken ct)
    { /* runs on the ledger: at-least-once, retries, scheduling, progress */ }
}

// dotnet add package Sylin.Koan.Data.AI (+ an Ollama + vector connector)
// → entities become semantically searchable:
[Embedding(Properties = new[] { nameof(Title) })]
public sealed class Todo : Entity<Todo> { /* embeddings maintained in the background */ }

var related = await SemanticSearch<Todo>("groceries and meal planning");
// using static Koan.Data.AI.EntityEmbeddingExtensions;

// dotnet add package Sylin.Koan.Mcp → entities become agent tools over HTTP/SSE:
[McpEntity]
public sealed class Todo : Entity<Todo> { /* agents can now query and mutate Todos */ }
```

Backends differ, and Koan refuses to pretend otherwise: every adapter declares its capabilities,
the framework negotiates them, and an unsupported operation **fails loudly** instead of silently
degrading.

```csharp
if (Data<Todo, string>.Capabilities.Has(DataCaps.Query.Linq))
{
    // this query runs inside the database, not in memory
}
```

### 3 · The app explains itself

Beyond the boot report: capability sets are queryable at runtime, well-known endpoints describe
the running service, health contributors aggregate readiness, and `[McpEntity]` exposes your
domain to agents through the same endpoint service the REST controllers use. The framework's
discovery isn't reflection magic at runtime — it's a **Roslyn source-generated registry**
compiled into your assemblies: deterministic, ordered, AOT-friendly.

## What's distinctive (and actually shipped)

- **One grammar everywhere** — the same `Entity<T>` is your table row, your REST resource, your
  cache entry, your job, your embedding source, and your agent tool.
- **Capability-graded multi-provider** — SQLite, Postgres, SQL Server, MongoDB, Couchbase,
  Redis, JSON-file storage; Weaviate, Qdrant, Milvus, Elasticsearch, OpenSearch vectors — behind
  one API that *negotiates* features (pushdown, bulk ops, CAS, TTL) rather than faking parity. A
  cross-adapter convergence oracle tests that every adapter agrees with the reference semantics.
- **Background jobs as entities** ([JOBS-0005](docs/decisions/JOBS-0005-jobs-pillar-rebuild.md))
  — a capability ladder from in-memory to durable-ledger to distributed competing consumers,
  with cron/`@boot` scheduling, idempotency, chains, and contention-free claims.
- **Transparent entity caching** — `[Cacheable]` L1/L2 with cross-node coherence and a
  principled fresh-or-null contract (stale reads are opt-in, never a surprise).
- **AI as a property of your data** — `[Embedding]` keeps vectors in sync in the background;
  semantic search is a query, not a subsystem you build.
- **Decision-first engineering** — 280+ ADRs, integration-tests-as-canon, and a self-assessment
  you can read. The architecture is auditable, not asserted.
- **Composition you can diff** — every build writes a checked-in
  [`koan.lock.json`](docs/guides/composition-lockfile.md) describing the app's modules and elections,
  so PR review *sees* composition drift in a plain `git diff` and the boot report self-checks it.
- **Your app inherits a test suite** — reference
  [`Sylin.Koan.Testing`](docs/guides/testing-your-app.md) and write one method per entity;
  `EntityConformanceSpecs<T>` runs round-trip, pushdown-vs-reference-oracle, paging, partition, cache
  and embedding batteries — gated automatically on what the entity declares.

## Learn it

| Path | Where |
|---|---|
| **First 15 minutes** | [Getting started](docs/getting-started/overview.md) — the golden path, concept by concept |
| **Run real apps** | [samples/](samples/README.md) — the ladder: S0 console → S1 CRUD → S10 multi-provider → S14 jobs/benchmarks, then the dogfood flagships |
| **Do a task** | [Guides](docs/guides/README.md) — APIs, data modeling, auth, AI, media, jobs |
| **Understand why** | [Architecture principles](docs/architecture/principles.md) · [ADRs](docs/decisions/index.md) |
| **Check what's solid** | [Framework assessment & maturity model](docs/assessment/00-overview.md) |
| **When stuck** | [Troubleshooting](docs/support/troubleshooting.md) |
| **Coding agents** | [llms.txt](llms.txt) — the framework in one file: three beats, the 8 concepts, anti-patterns, the canonical way |

Requirements: **.NET 10 SDK**. Docker/Podman only for container-backed samples and integration
tests. Coding agents: start at [CLAUDE.md](CLAUDE.md) and `.claude/skills/`.

## Contributing

Single-maintainer project in active consolidation ("fewer but more meaningful parts").
Issues and discussions welcome; PRs should follow the ADR-first workflow and keep the green
ratchet green (`scripts/green-ratchet.ps1`). See [docs/engineering](docs/engineering/index.md).

Licensed under [Apache 2.0](LICENSE).
