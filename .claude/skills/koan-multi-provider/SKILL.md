---
name: koan-multi-provider
description: Provider transparency, capability detection (CapabilitySet / DataCaps), context routing — partition, source, adapter (DATA-0077)
pillar: data
card: docs/reference/cards/data.md
status: current
last_validated: 2026-06-18
---

# Koan Multi-Provider Transparency

## Trigger this skill when you see

- The same `Entity<T>` code targeting more than one store (SQL / document / JSON / vector)
- `EntityContext.Partition(...)` / `.Source(...)` / `.Adapter(...)`, or `[DataAdapter("...")]`
- `Data<T, K>.Capabilities`, `CapabilitySet.Has(...)`, `DataCaps.Query.Linq` / `DataCaps.Write.FastRemove`
- `Todo.SupportsFastRemove` / `RemoveAll(RemoveStrategy.Fast)`
- A vector entity with `[Embedding]` routed to Weaviate / Qdrant / Milvus
- "provider transparency", "capability detection", "multi-tenant partition", "read replica", "switch databases"

## Core principle

**Write entity code once; the provider is a deployment detail.** The adapter is chosen by package reference + config (Reference = Intent), and the [ARCH-0084](../../../docs/decisions/ARCH-0084-unified-capability-model.md) capability model decides *per call* whether a query is pushed down or evaluated in-memory — no adapter carries fallback logic. Routing is **ambient and scoped** via `EntityContext`; `Source` and `Adapter` are **mutually exclusive** ([DATA-0077](../../../docs/decisions/DATA-0077-entity-context-source-adapter-partition-routing.md)) — passing both is a hard `InvalidOperationException`, not a silent override.

<!-- validate -->
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;
using Koan.Data.Core.Model;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[DataAdapter("weaviate")]                                  // pin one entity to a provider
[Embedding(Template = "{Title}")]                          // class-level; routed to the vector store
public sealed class Memo : Entity<Memo>
{
    public string Title { get; set; } = "";
}

public sealed class TodoService
{
    // Capability detection — same code, store-aware behaviour (ARCH-0084 CapabilitySet)
    public async Task<IReadOnlyList<Todo>> Open(CancellationToken ct = default)
    {
        var caps = Data<Todo, string>.Capabilities;        // CapabilitySet for the elected adapter
        if (caps.Has(DataCaps.Query.Linq))
            return await Todo.Query(t => !t.Done, ct);      // pushed down to the store
        return await Todo.Query("{\"Done\":false}", ct);   // JSON filter DSL ($in/$gt/... Mongo-flavoured)
    }

    public async Task<IReadOnlyList<Todo>> FromReplica(CancellationToken ct = default)
    {
        using (EntityContext.Source("analytics"))           // named config in appsettings
            return await Todo.Query(t => !t.Done, ct);
    }

    public async Task Migrate(CancellationToken ct = default)
    {
        using (EntityContext.Adapter("mongo"))              // explicit provider override (NOT with Source)
        using (EntityContext.Partition($"tenant-42"))       // logical isolation within the provider
            await new Todo { Title = "seed" }.Save(ct);
    }

    public async Task<long> Purge(CancellationToken ct = default)
        => Todo.SupportsFastRemove                           // capability probe
            ? await Todo.RemoveAll(RemoveStrategy.Fast, ct)  // TRUNCATE/drop path when supported
            : await Todo.RemoveAll(ct);                      // safe per-row fallback
}
```

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Data.Core` (alone) | In-memory adapter backs every entity — zero config. |
| `+ Koan.Data.Sqlite` / `.Postgres` / `.Mongo` / `.SqlServer` / `.Json` | That provider backs every entity; same code, no rewrite. |
| `+ a vector connector` (`Koan.Data.Vector.Connector.Weaviate` / `.Qdrant` / `.Milvus`) | Vector store available for `[Embedding]` entities. |
| `[DataAdapter("name")]` on an entity | Routes just that entity when several providers are referenced. |

## Context routing (DATA-0077)

| Scope | Means | Use for |
|---|---|---|
| `EntityContext.Partition("p")` | Logical suffix within one provider (e.g. `Todo#p`) | Multi-tenant isolation, archival, test data separation. |
| `EntityContext.Source("name")` | A **named configuration** (its own adapter + connection string) | Read replicas, analytics DBs, regional stores. |
| `EntityContext.Adapter("mongo")` | Explicit provider override | Migration, provider comparison, dev overrides. |

Scopes nest and replace inner-most-wins; dispose restores the outer value. `Partition` composes with **either** `Source` or `Adapter`. **`Source` + `Adapter` together throws** — a `Source` already names its adapter, so specifying both is contradictory (DATA-0077).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `Data<Todo,string>.QueryCaps` / `QueryCapabilities` / `EntityCaps` | `Data<Todo,string>.Capabilities` → `CapabilitySet` (ARCH-0084 removed the enums). |
| `caps.Capabilities.HasFlag(QueryCapabilities.LinqQueries)` | `caps.Has(DataCaps.Query.Linq)` — token negotiation, no `[Flags]` enum. |
| `caps.Capabilities.HasFlag(QueryCapabilities.FullTextSearch)` | No such token. Use `DataCaps.Query.Linq` + the JSON filter DSL, or a vector store for semantic search. |
| `Todo.Query("CONTAINS(Title, '...')")` (SQL text in the string overload) | The `string` overload is the **JSON filter DSL** (`{"Tags":{"$in":["x"]}}`); use `Todo.QueryRaw(...)` for provider-native. |
| `[VectorField] float[] Embedding` on a property | Class-level `[Embedding]` (`Koan.Data.AI.Attributes`) — Koan composes & stores the vector; no hand-held float array. |
| `EntityContext.Source("x")` nested inside `EntityContext.Adapter("y")` | Pick one. Both set is a hard `InvalidOperationException` (DATA-0077), never "undefined behaviour". |
| `[DataAdapter("pgvector")]` | pgvector is removed — route to `weaviate` / `qdrant` / `milvus`. |

## Escape hatches

- **Probe a typed capability**: `Data<T,K>.As<IConditionalWriteRepository<T,K>>()` returns `null` when the adapter lacks it — the cast *is* the probe; branch on null to a fallback.
- **Raw provider query**: `Todo.QueryRaw(providerQuery, parameters, ct)` for native SQL/N1QL; `IDataService.Direct(adapter: "sqlite")` for raw SQL with source-XOR-adapter + transactions (`.Begin()`).
- **Pin durability per entity**: `[DataAdapter("name")]` overrides the elected provider for one entity even when several adapters are referenced.
- **Inspect the election**: the boot report lists each adapter, its priority, and the elected default — the source of truth when "wrong provider chosen" surfaces.

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — one-screen pillar map
- [Entity capabilities how-to](../../../docs/guides/entity-capabilities-howto.md) — capability tokens, query pushdown, counts
- [DATA-0077 — context routing (partition / source / adapter)](../../../docs/decisions/DATA-0077-entity-context-source-adapter-partition-routing.md)
- [ARCH-0084 — unified capability model (`CapabilitySet` / `DataCaps`)](../../../docs/decisions/ARCH-0084-unified-capability-model.md)
- [`samples/S1.Web`](../../../samples/S1.Web/README.md) — `Entity<Todo>` over a single provider
- [`samples/S10.DevPortal`](../../../samples/S10.DevPortal/README.md) — multi-provider showcase
