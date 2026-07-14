# Koan Framework

**Model your domain as entities. Reference your intents. Koan composes the rest — storage, web,
AI, jobs, caching — and reports the major choices it made.**

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)

Koan is a .NET 10 meta-framework for **agentic, data-driven web applications** — think
*Rails for .NET*, built for the era where AI capability is part of the domain model and a lot of
your code is written with coding agents. One grammar — `Entity<T>` — covers CRUD, REST, vector
search, background jobs, caching, embeddings, and agent (MCP) tools.

The [product constitution](docs/architecture/product-constitution.md) defines what that promise means:
V0 to V1 in meaningful, small steps, business-readable application code, and infrastructure complexity
that remains inspectable.

> **Status: pre-1.0, consolidation phase** (version via [NBGV](version.json), currently 0.17.x).
> Capability maturity is being re-baselined from current code and executable evidence by the
> [Koan V1 initiative](docs/initiatives/koan-v1/README.md); do not infer support from package or sample
> existence. **Build from source today.** The public 0.17.0 packages are not a coherent install set:
> an internal dependency requires an unpublished Core patch. See the
> [current capability evidence](docs/initiatives/koan-v1/R02-EVIDENCE.md#clean-package-install-probe).

---

## Shortest path to a running app

```bash
git clone https://github.com/sylin-org/koan-framework
cd koan-framework
dotnet run --project samples/FirstUse
# in another shell:
curl -X POST http://localhost:5000/api/approvals -H "Content-Type: application/json" -d '{"subject":"Approve supplier invoice"}'
curl http://localhost:5000/api/approvals
curl http://localhost:5000/.well-known/Koan/facts
```

The printed URL can differ when a launch profile or `--urls` is used. The application code is in
[`samples/FirstUse`](samples/FirstUse/README.md): one business entity, one controller, and the
four-line bootstrap. Koan elects SQLite, creates the development schema, exposes the governed REST
surface, and projects the same runtime facts to operators and MCP clients. Its boot report is the
framework's character in one screen: **the app explains itself.**

When that first result makes sense, continue with
[`samples/GoldenJourney`](samples/GoldenJourney/README.md). It grows one cumulative application
through business rules, durable work, bounded agent collaboration, and explained adapter recovery
without replacing the four-line bootstrap or introducing application infrastructure plumbing.

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
public sealed class Approval : Entity<Approval>
{
    public string Subject { get; set; } = "";
    public ApprovalState State { get; set; } = ApprovalState.Pending;
}

public enum ApprovalState { Pending, Approved, Rejected }

[Route("api/approvals")]
public sealed class ApprovalsController : EntityController<Approval>;
```

That's a full REST API: GET/POST/DELETE with pagination and querying, GUID v7 ids generated on
first read, `/health`, structured logging, and zero-config SQLite (`./data/app.db`). Schema
is created automatically in development. JSON defaults: camelCase, nulls omitted
(Newtonsoft.Json — chosen for predictable polymorphic serialization).

Working with entities is direct — no repositories, no DbContext:

```csharp
var approval = await Approval.Get(id);                    // null if missing
var pending = await Approval.Query(a => a.State == ApprovalState.Pending);
await new Approval { Subject = "Approve invoice" }.Save();
await approval.Remove();
await foreach (var item in Approval.AllStream(batchSize: 1000)) { /* streaming */ }
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
var profile = Data<Todo, string>.Capabilities
    .Detail<FilterExecutionProfile>(DataCaps.Query.FilterExecution);

if (profile?.Kind is FilterExecutionKind.Native)
{
    // normalized filters execute in the backend
}
```

`DataCaps.Query.Filter` describes which operators are correct; `FilterExecutionProfile` describes
their physical cost posture. Relationship traversal uses both and rejects implicit backend scans.

### 3 · The app explains itself

Beyond the boot report: capability sets are queryable at runtime, well-known endpoints describe
the running service, health contributors aggregate readiness, and `[McpEntity]` exposes applicable
domain surfaces to agents through the same endpoint service the REST controllers use. Discovery is
**source-generated first**, with embedded-manifest and runtime fallbacks for deployment shapes that
need them; activation is deterministically ordered and reported.

> **See all three beats run for real** — [the wedge demo](docs/case-studies/agent-wedge-demo/README.md): a
> live, captured transcript building a multi-provider AI app from one entity to an agent-operable MCP
> server in one session (REST → Postgres → cache → jobs → semantic search → an agent mutating over
> `koan://entities`), every command and output run against the framework.

## What's implemented today

- **One grammar everywhere** — the same `Entity<T>` is your table row, your REST resource, your
  cache entry, your job, your embedding source, and your agent tool.
- **Capability-graded multi-provider** — SQLite, Postgres, SQL Server, MongoDB, Couchbase,
  Redis, JSON-file storage; Weaviate, Qdrant, Milvus, Elasticsearch, OpenSearch vectors — behind
  one API that *negotiates* features (pushdown, bulk ops, CAS, TTL) rather than faking parity. A
  shared convergence-test model lets provider suites compare their behavior with reference semantics;
  current verification remains provider- and suite-specific.
- **Background jobs as entities** ([JOBS-0005](docs/decisions/JOBS-0005-job-orchestrator-rebuild.md))
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
| **First meaningful result** | [FirstUse](samples/FirstUse/README.md) — the executable source and package contract |
| **First 15 minutes** | [Getting started](docs/getting-started/overview.md) — grow from that result, concept by concept |
| **Run real apps** | [samples/](samples/README.md) — the ladder: S0 console → S1 CRUD → S10 multi-provider → S14 jobs/benchmarks, then the dogfood flagships |
| **Do a task** | [Guides](docs/guides/README.md) — APIs, data modeling, auth, AI, media, jobs |
| **Understand why** | [Product constitution](docs/architecture/product-constitution.md) · [Entity Semantics Contract](docs/architecture/entity-semantics-contract.md) · [Architecture principles](docs/architecture/principles.md) · [ADRs](docs/decisions/index.md) |
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
