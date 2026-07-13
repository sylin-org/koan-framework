---
type: ARCHITECTURE
domain: framework
title: "Koan Framework Architecture Principles"
audience: [architects, developers, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: alignment with product constitution and current bootstrap implementation
---

# Koan Framework Architecture Principles

The [product constitution](product-constitution.md) defines Koan's durable product rules, and the
[Entity Semantics Contract](entity-semantics-contract.md) governs how modules grow its first-class
application language. This page describes current implementation principles and representative APIs.
Source and tests remain the authority for shipped behavior; the capability evidence ledger determines
support maturity.

## Core philosophy

### Entity-first development

Entities own their common-path persistence and genuinely entity-centered surfaces. No repository,
`DbContext`, or service layer is required for CRUD. Repositories remain an advanced escape hatch, and
multi-entity/external-system workflows remain plain business-named workflows rather than unrelated
Entity extensions.

```csharp
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

var todo = await Todo.Get(id);                     // null when missing
var open = await Todo.Query(t => !t.IsCompleted);  // pushed down when supported
await new Todo { Title = "Ship" }.Save();
await todo.Remove();
```

The same entity can be the subject across multiple pillars: REST (`EntityController<T>`), caching
(`[Cacheable]`), jobs (`IKoanJob<T>`), embeddings (`[Embedding]`), agent tools (`[McpEntity]`).
One grammar, many capabilities — this is the framework's center of gravity. New module vocabulary
must pass the Entity admission test; type-wide capability grows through small module-contributed
facets rather than permanent Data.Core placeholders.

### Reference = Intent

Adding a package reference *is* the configuration. Each referenced module registers itself; the
app's `Program.cs` stays at four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

The primary discovery path uses a Roslyn-generated per-assembly registry (`KoanRegistry`) and
deterministic topological ordering (`[Before]`/`[After]`). Embedded intent manifests, runtime manifest
loading, assembly-closure loading, and loose-module fallback preserve participation across deployment
shapes. These mechanisms are tactical; predictable activation and explanation are the contract.

### Capability-graded provider transparency **(consolidation era — ARCH-0084)**

Same entity code across SQLite, Postgres, SQL Server, MongoDB, Couchbase, Redis, JSON-file, and
vector stores — but Koan does not pretend backends are identical. Every adapter **declares**
capabilities, the framework **negotiates** them, and consumers can **query** them:

```csharp
var caps = Data<Todo, string>.Capabilities;        // CapabilitySet
if (caps.Has(DataCaps.Query.Linq)) { /* pushdown active */ }
```

Where a capability is absent, behavior is graded honestly: native TTL retention only where the
store supports it (with a universal purge backstop), compare-and-set claims only on adapters
that implement them, pushdown or fail-loud — never silent emulation of guarantees the store
cannot give.

### Fail-loud is canon **(consolidation era — ARCH-0084 / DATA-0097)**

An operation the provider cannot honor is a **hard, descriptive error**, never silent narrowing.
A filter that cannot be pushed down to a vector store throws; an unsupported capability throws
`CapabilityNotSupportedException`; resolution failures name the exact configuration keys to fix.
Explicit degraded boot may continue after a failure, but the failed module and missing guarantees remain
visible. The capability baseline will identify paths that do not yet meet this rule.

### Self-reporting infrastructure

The application should explain itself. Current startup reporting includes registry, module, phase,
health, and composition facts; several collection paths are intentionally best-effort or become known
after startup. Capability sets and well-known endpoints add runtime inspection, and `[McpEntity]`
projects applicable domain surfaces to agents. R02 determines the verified coverage of each claim.

```csharp
KoanEnv.DumpSnapshot(logger);   // environment snapshot on demand
if (KoanEnv.InContainer) { /* container-aware behavior */ }
```

## Design principles

### 1 · Fewer, more meaningful parts **(consolidation era)**

The framework is measured in **developer-facing concepts**, not projects. A part earns its place
only if a dogfood application reaches for it naturally, or removing it forces ceremony back into
an app. Overlap is collapsed where ≥2 real usages prove it; speculative abstraction is resisted;
scaffolding from the feasibility phase is cut, not lovingly refactored.

### 2 · Deterministic configuration, explicit hierarchy

Provider defaults < `appsettings.json` < environment variables < code. Typed options via
`AddKoanOptions<T>` are the runtime surface; `Configuration.Read(cfg, "Koan:Some:Key", default)`
is sanctioned for boot-time/provenance paths where options aren't bound yet.

```json
{ "Koan": { "Data": { "Postgres": { "ConnectionString": "Host=postgres;Database=app" } } } }
```

Environment override: `Koan__Data__Postgres__ConnectionString=…`

### 3 · Progressive complexity

Level 1 is a four-line app with SQLite. Every additional capability is one package and at most
one attribute. Concept count is budgeted and documented per step (see
[getting started](../getting-started/overview.md)) — cognitive load is a tracked cost, not an
accident.

### 4 · Escape hatches preserve the grammar

Custom controllers coexist with `EntityController<T>` (override the virtual actions or add
routes); raw provider access exists via `Data<TEntity, TKey>.Execute<TResult>(sql)` and
instruction execution; per-request behavior is scoped via `EntityContext`
(e.g. `using (EntityContext.Partition("tenant-42")) { … }`). Hatches are explicit and scoped —
they never replace the canonical path in documentation.

### 5 · One canonical way per intent **(consolidation era)**

Where history produced two ways, one is canon and the other is retired (visibly, via `[Obsolete]`
and ADR supersession). Canonical picks of record: `KoanModule` for module authoring; `Save`
(alias of upsert) and `Remove` as the entity verbs; `EntityContext` scoping over per-call
partition parameters; Jobs (`[JobAction(Schedule=…)]`) for scheduling; the Jobs ledger for
outbox semantics; SSE for server push; **Newtonsoft.Json as the application serializer**
(predictable polymorphic handling; defaults: camelCase, nulls omitted).

### 6 · Integration tests are canon (ARCH-0079)

Every adapter, connector, coherence channel, and pillar core ships at least one integration spec
that goes through real `AddKoan()` discovery (`KoanIntegrationHost`). Unit tests with fakes are
insufficient: they structurally cannot reveal composition or shared-resource bugs. Cross-adapter
*convergence oracles* (a shared spec battery run against every adapter, checked against a
reference evaluator) guard provider parity.

### 7 · Decisions are written, and supersession is explicit

Architecture lives in `docs/decisions/` (280+ ADRs). A decision that replaces another marks its
predecessor `Superseded`. Recent ADRs carry empirical probes and staged implementation ledgers —
follow that bar. The framework also maintains a published self-assessment
([docs/assessment](../assessment/00-overview.md)) grading each pillar's maturity; claims about
the framework defer to it.

## Anti-patterns (enforced in review and, increasingly, by analyzers)

```csharp
// ❌ Manual repository/service ceremony around entities
public class TodoService { private readonly IRepository<Todo> _repo; /* … */ }

// ❌ Manual registration of framework services (auto-registration owns this)
services.AddScoped<IRelationshipMetadata, …>();

// ❌ Provider-specific behavior without a capability check
//    (use Data<T,K>.Capabilities.Has(...) and fail loud or branch)

// ❌ Inventing APIs from memory — verify against source; docs snippets must compile
```

## Strategic direction

Koan positions as **the framework for agentic, data-driven .NET applications**: a small senior
team (and its coding agents) ships sophisticated systems without scaffolding time.

- **Agent-native by design**: one canonical way per intent, loud failures, shipped agent
  knowledge (`.claude/skills/`, CLAUDE.md), and self-description of the running app (boot
  report, capabilities, MCP).
- **AI as a property of your data**: `[Embedding]` → background sync → semantic search as a
  query. The flagship AI story is the data→AI seam, not model operations.
- **Container-native, Aspire-friendly**: environment detection, health probes, and orchestration
  via the .NET ecosystem's own tooling rather than a bespoke layer.

---

**References**: [Product constitution](product-constitution.md) ·
[Entity Semantics Contract](entity-semantics-contract.md) ·
[ADR index](../decisions/index.md) ·
[Framework assessment & maturity model](../assessment/00-overview.md) ·
[Getting started](../getting-started/overview.md) ·
[Framework utilities catalog](../guides/framework-utilities.md)
