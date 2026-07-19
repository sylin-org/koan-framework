---
name: koan-entity-first
description: Entity<T> patterns, GUID v7 auto-generation, static Get/Query/All + instance Save/Remove, batch and streaming facades, static methods vs manual repositories
pillar: data
card: docs/reference/cards/data.md
status: current
last_validated: 2026-06-18
---

# Koan Entity-First Development

## Trigger this skill when you see

- A type deriving `Entity<T>` / `Entity<T, TKey>`, or `Todo.Get(id)` / `todo.Save()` / `todo.Remove()`
- Manual `IRepository<T>` / `ITodoRepository` interfaces, or a repository injected into a service
- `Todo.Query(...)`, `Todo.All(...)`, `Todo.Page(...)`, `Todo.AllStream(...)`, `Todo.QueryWithCount(...)`
- `Todo.Batch()` / `list.Save(...)` / `Todo.UpsertMany(...)` bulk writes
- `[Timestamp]`, `[Parent(typeof(...))]`, `[Identifier]`, `[DataAdapter(...)]` annotations
- References to `Koan.Data.Core`, `Koan.Data.Abstractions`, `QueryDefinition`
- "CRUD", "repository", "data access", "N+1", "batch load", "GUID v7", "provider transparency"

## Core principle

**`Entity<T>` replaces manual repositories.** Every entity is self-persisting: GUID v7 ids are generated on first `Id` access, and the same static/instance verbs run on any provider (the adapter is chosen by package reference + config — Reference = Intent). Canonical verbs are **Save / Remove / Query** ([principles.md §5](../../../docs/architecture/principles.md)). Timestamps are **not** inherited — declare a `DateTimeOffset` with `[Timestamp]`.

<!-- validate -->
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }

    [Timestamp] public DateTimeOffset CreatedAt { get; set; }              // set-once on first save
    [Timestamp(OnSave = true)] public DateTimeOffset UpdatedAt { get; set; } // every save
}

public sealed class TodoService
{
    public async Task<Todo> Complete(string id, CancellationToken ct = default)
    {
        var todo = await Todo.Get(id, ct);                 // Task<Todo?>
        if (todo is null) throw new InvalidOperationException($"Todo {id} not found");
        todo.Done = true;
        return await todo.Save(ct);                        // create or update; returns the entity
    }

    public async Task<IReadOnlyList<Todo>> Open(CancellationToken ct = default)
        => await Todo.Query(t => !t.Done, ct);             // pushed down when the adapter can

    public async Task<(IReadOnlyList<Todo> Items, long Total)> Page(int page, int size, CancellationToken ct = default)
    {
        var q = QueryDefinition.All.WithPagination(page, size); // DATA-0096: replaces DataQueryOptions
        var result = await Todo.QueryWithCount(t => !t.Done, q, ct);
        return (result.Items, result.TotalCount);
    }

    public async Task SeedAndStream(CancellationToken ct = default)
    {
        var batch = new List<Todo> { new() { Title = "a" }, new() { Title = "b" } };
        await batch.Save(ct);                              // bulk upsert (IEnumerable extension)
        await foreach (var t in Todo.AllStream(batchSize: 500, ct)) { _ = t.Title; }
    }
}
```

`AllStream`/`QueryStream` are provider-bounded on SQLite, PostgreSQL, SQL Server, CockroachDB,
MongoDB, and Couchbase. InMemory, JSON, and Redis reject before query/yield; do not describe Entity
streaming as universal provider parity.

## Reference = Intent activation

| Add this | Effect |
|---|---|
| `Koan.Data.Core` + any entity | `Entity<T>` facade is live; an in-memory adapter backs it with zero config. |
| `+ Koan.Data.Sqlite` / `.Postgres` / `.Mongo` / `.SqlServer` / `.Json` | That provider backs every entity — same code, no rewrite (the adapter auto-registers). |
| `[DataAdapter("mongo")]` on an entity | Routes just that entity to a named provider when several are referenced. |
| `Koan.Web` + `EntityController<Todo>` (`Koan.Web.Controllers`) | Full REST CRUD + query + patch over the entity — no hand-written actions. |

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `interface ITodoRepository { Task<Todo> GetAsync(...); }` | Delete it — `Entity<T>` already is the repository. |
| A repository injected into a service (`TodoService(ITodoRepository repo)`) | Call `Todo.Get/Query/Save` directly; no DI plumbing. |
| `Todo.Batch()....SaveAsync(ct)` | `.Save(BatchOptions?, ct)` — the batch commit verb is `Save`, returning `BatchResult`. |
| `new DataQueryOptions { OrderBy = ... }` | `QueryDefinition` with `.WithSort(...)` / `.WithPagination(...)` (DATA-0096 removed `DataQueryOptions`). |
| `List<Todo>` as the declared return of a facade call | `IReadOnlyList<Todo>` — the facade returns read-only lists. |
| "Created/Updated are inherited automatically" | They are not — declare a `DateTimeOffset` with `[Timestamp]` / `[Timestamp(OnSave=true)]`. |
| `foreach (var id in ids) await Todo.Get(id)` (N round-trips) | `await Todo.Get(ids, ct)` → `IReadOnlyList<Todo?>`, order-preserving, single query. |
| `Todo.QueryStream("t.Done == false")` (C# expression text) | The `string` overload is the **JSON filter DSL** (`{"Done":false}`); pass an `Expression<Func<Todo,bool>>` for LINQ. |
| Hand-rolled `Capabilities.HasFlag(QueryCaps...)` | `Data<Todo,string>.Capabilities.Has(DataCaps.Query.Linq)` (ARCH-0084 unified `CapabilitySet`). |

## Escape hatches

- **Custom keys**: `Entity<NumericEntity, int>` with `public override int Id { get; set; }` — manage the key yourself; GUID v7 auto-gen applies only to the string-keyed default.
- **Filter DSL / raw provider query**: `Todo.Query(string filterJson, ct)` and `Todo.QueryStream(string filterJson, ...)` accept the JSON filter DSL; `Todo.QueryRaw(providerQuery, parameters, ct)` passes a native provider query through.
- **Bulk write shapes**: `list.Save(ct)` (upsert) and `Todo.UpsertMany(items, ct)` for set writes; `Todo.Batch().Add/.Update/.Delete` then `.Save(new BatchOptions(RequireAtomic: true), ct)` for mixed atomic operations.
- **Relationships**: `[Parent(typeof(Category))]` (`Koan.Data.Core.Relationships`) enables `await todo.GetRelatives(ct)` and the batch `entities.Relatives(ct)` enrichment (DATA-0072).
- **Raw SQL inside Koan**: `IDataService.Direct(...)` (registered by default, ARCH-0090) — parameterized, source XOR adapter, transactions via `.Begin()`.

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — one-screen pillar map
- [Entity capabilities how-to](../../../docs/guides/entity-capabilities-howto.md) — the authoritative walkthrough (queries, batch, streaming, counts)
- [Data modeling guide](../../../docs/guides/data-modeling.md) — aggregates, keys, relationships
- [TaskGraph](../../../samples/fundamentals/TaskGraph/README.md) — `Entity<Todo>` CRUD + relationship navigation over a web UI
- [DATA-0059 — entity-first facade & save semantics](../../../docs/decisions/DATA-0059-entity-first-facade-and-save-semantics.md)
- [DATA-0096 — unified filter pipeline (`QueryDefinition`)](../../../docs/decisions/DATA-0096-unified-filter-pipeline.md) · [ARCH-0084 — unified capability model](../../../docs/decisions/ARCH-0084-unified-capability-model.md)
