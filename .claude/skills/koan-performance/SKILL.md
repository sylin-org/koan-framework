---
name: koan-performance
description: Streaming, pagination, count strategies, bulk operations for Entity<T> at scale — AllStream, FirstPage/Page/QueryWithCount, Count.Fast/Exact/Optimized, RemoveAll(RemoveStrategy.Fast), batch Save
pillar: data
card: docs/reference/cards/data.md
status: current
last_validated: 2026-06-18
---

# Koan Performance

## Trigger this skill when you see

- Large-dataset reads — `Todo.All()` materializing millions of rows, OOM, "load everything"
- `Todo.AllStream(...)` / `Todo.QueryStream(...)` streaming facades
- Pagination — `Todo.FirstPage(...)`, `Todo.Page(...)`, `Todo.QueryWithCount(...)`, `QueryDefinition.WithPagination(...)`, `X-Total-Count` headers
- Counts — `Todo.Count`, `Todo.Count.Fast(ct)`, `Todo.Count.Exact(ct)`, `CountStrategy`
- Bulk writes / deletes — `list.Save(ct)`, `Todo.UpsertMany(...)`, `Todo.RemoveAll(RemoveStrategy.Fast)`
- Batch reads — `Todo.Get(ids, ct)` vs an N+1 `foreach (var id in ids) await Todo.Get(id)`
- "performance tuning", "large dataset", "N+1", "streaming", "bulk", "fast count", "TRUNCATE"

## Core principle

**Pick the verb that matches the scale.** The facade gives a verb for each access shape — stream instead of materialize, paginate with one round-trip via `QueryWithCount`, count with an explicit strategy, and bulk-write/delete in a single operation. Every helper returns `IReadOnlyList<T>` (not `List<T>`); sort is a typed `ISortBuilder<T>` or a `QueryDefinition`, never the removed `DataQueryOptions` ([DATA-0096](../../../docs/decisions/DATA-0096-unified-filter-pipeline.md)).

<!-- validate -->
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public DateTimeOffset Created { get; set; }
}

public sealed class TodoReports
{
    // Stream large sets — never materialize the whole table.
    public async Task ExportOpen(CancellationToken ct = default)
    {
        await foreach (var t in Todo.AllStream(batchSize: 1000, ct)) { _ = t.Title; }
    }

    // One round-trip page + total, sorted via the typed builder (no DataQueryOptions).
    public async Task<(IReadOnlyList<Todo> Items, long Total)> Page(
        int page, int size, CancellationToken ct = default)
    {
        var q = QueryDefinition.All.WithPagination(page, size);   // DATA-0096 (sort via the typed Page/FirstPage overloads below)
        var result = await Todo.QueryWithCount(t => !t.Done, q, ct);
        return (result.Items, result.TotalCount);
    }

    // First page only, when you don't need a total.
    public Task<IReadOnlyList<Todo>> Latest(int size, CancellationToken ct = default)
        => Todo.FirstPage(size, b => b.OrderByDescending(t => t.Created), ct);

    // Count strategies are explicit, not magic.
    public async Task<(long Fast, long Exact, long Optimized)> Counts(CancellationToken ct = default)
        => (await Todo.Count.Fast(ct),         // metadata estimate, ~constant time
            await Todo.Count.Exact(ct),        // guaranteed-accurate scan
            await Todo.Count.Optimized(ct));   // adapter's best exact strategy (its OWN path)

    // Bulk write + bulk delete in single operations.
    public async Task Reseed(IReadOnlyList<Todo> todos, CancellationToken ct = default)
    {
        await Todo.RemoveAll(RemoveStrategy.Fast, ct);   // TRUNCATE/DROP where supported
        await todos.Save(ct);                            // one bulk upsert (IEnumerable extension)
    }

    // Batch read — one query, order-preserving — instead of N round-trips.
    public Task<IReadOnlyList<Todo?>> Load(IEnumerable<string> ids, CancellationToken ct = default)
        => Todo.Get(ids, ct);
}
```

## Reference = Intent: what scales the access path

| Add / use this | Effect |
|---|---|
| `Todo.AllStream(batchSize, ct)` / `Todo.QueryStream(predicate, ct)` | `IAsyncEnumerable<Todo>` — paged off the adapter, never the whole set in memory. |
| `Todo.QueryWithCount(predicate, query, ct)` | `QueryResult<Todo>` with `.Items` + `.TotalCount` — page and total in one round-trip. |
| `Todo.FirstPage(size, sort, ct)` / `Todo.Page(page, size, sort, ct)` | Materialized page; `sort` is `Action<ISortBuilder<Todo>>` or a sort string. |
| `Todo.Count.Fast(ct)` | `CountStrategy.Fast` — approximate, from table stats (`query.fastCount` capability). |
| `Todo.Count.Exact(ct)` / `await Todo.Count` | Exact / default-Optimized. `Optimized` is its **own** strategy, not an alias of Fast. |
| `list.Save(ct)` / `Todo.UpsertMany(items, ct)` | Single bulk upsert instead of a write per row. |
| `Todo.RemoveAll(RemoveStrategy.Fast, ct)` | `write.fastRemove` path (TRUNCATE/DROP); falls back to safe DELETE where unsupported. |
| `Todo.Get(ids, ct)` | One order-preserving query for a set of keys (kills the N+1). |

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `var all = await Todo.All();` then a loop over millions | `await foreach (var t in Todo.AllStream(batchSize: …, ct))` — bounded memory. |
| Two calls (`Query` then a separate `Count`) for one page | `Todo.QueryWithCount(predicate, query, ct)` → `.Items` + `.TotalCount` in one trip. |
| `new DataQueryOptions { OrderBy = …, Descending = true }` | `QueryDefinition.All.WithSort(b => b.OrderByDescending(...)).WithPagination(page, size)` (DATA-0096 removed `DataQueryOptions`). |
| `Todo.Query(...).Where(...).OrderBy(...).Take(...).ToArrayAsync()` (LINQ chain) | There is no `IQueryable` chain — use `Todo.Query(predicate, sort, ct)` / `FirstPage(size, sort)` / `All(QueryDefinition.WithPagination(...))`, each returning `IReadOnlyList<Todo>`. |
| "Optimized just uses Fast" | `Optimized` is a distinct exact strategy (the adapter's best exact path); `Fast` is the approximate one. |
| `List<Todo>` as the declared return of a page/query helper | `IReadOnlyList<Todo>` — facade results are read-only. |
| `foreach (var id in ids) await Todo.Get(id)` (N round-trips) | `await Todo.Get(ids, ct)` — single, order-preserving. |
| A DELETE loop / `foreach (... ) await t.Remove()` to clear a table | `Todo.RemoveAll(RemoveStrategy.Fast, ct)` — set-based truncate where the adapter supports it. |
| `foreach (var t in items) await t.Save();` to seed | `await items.Save(ct)` / `Todo.UpsertMany(items, ct)` — one bulk write. |

## Escape hatches

- **Count a subset**: `Todo.Count.Where(t => !t.Done, ct)` and `Todo.Count.Query(queryDefinition, ct)` count with the same strategy machinery as the bare accessor.
- **Sorted stream**: `Todo.AllStream(b => b.OrderBy(t => t.Created), ct)` and `Todo.QueryStream(predicate, sort, ct)` — note a sorted stream materializes the full result before yielding the first item ([ADR-0093]).
- **Capability probe before a fast path**: `Data<Todo, string>.Capabilities.Has(DataCaps.Query.FastCount)` / `.Has(DataCaps.Write.FastRemove)` — branch on the unified `CapabilitySet` (ARCH-0084) when you need to know whether the optimization is real on the elected adapter.
- **Provider-native query**: `Todo.QueryRaw(providerQuery, parameters, ct)` for store-specific tuning; `IDataService.Direct(...)` for raw SQL without leaving Koan (parameterized, registered by default — ARCH-0090 §1).

## See also

- [Reference card: data.md](../../../docs/reference/cards/data.md) — one-screen pillar map
- [Performance guide](../../../docs/guides/performance.md) — streaming, counts, bulk benchmarks
- [Entity capabilities how-to](../../../docs/guides/entity-capabilities-howto.md) — queries, paging, streaming, counts in depth
- [`samples/S14.AdapterBench`](../../../samples/S14.AdapterBench/README.md) — cross-adapter performance benchmarks
- [DATA-0096 — unified filter pipeline (`QueryDefinition`)](../../../docs/decisions/DATA-0096-unified-filter-pipeline.md) · [ARCH-0084 — unified capability model](../../../docs/decisions/ARCH-0084-unified-capability-model.md)
