---
name: koan-performance
description: Capability-qualified Entity<T> streaming, explicit paging/count strategies, and batch-shaped reads/writes without overclaiming provider commands or guarantees
pillar: data
card: docs/reference/data/index.md
status: current
last_validated: 2026-07-15
---

# Koan Performance

## Trigger this skill when you see

- `All()` or a client-side loop over a result that can grow without a known bound
- `AllStream` / `QueryStream`, paging, totals, or count accuracy questions
- N+1 key reads or one-at-a-time save/delete loops
- `Count.Fast`, `Count.Exact`, `Count.Optimized`, or `RemoveStrategy`
- performance claims such as "one round-trip", "constant time", or "works on every adapter"

## Core principle

Pick the Entity verb that matches the result shape, then qualify physical guarantees through the
elected adapter's capabilities. A single facade call is not necessarily one provider command.
Materialized collection facades return `IReadOnlyList<T>`; query composition uses predicates, the JSON
filter DSL, typed sort builders, or `QueryDefinition`—not the removed `DataQueryOptions` and not an
`IQueryable` model chain ([DATA-0096](../../../docs/decisions/DATA-0096-unified-filter-pipeline.md)).

<!-- validate -->
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TodoReports
{
    // Valid only when the elected adapter earns ProviderBoundedPaging.
    public async Task ExportOpen(CancellationToken ct = default)
    {
        await foreach (var todo in Todo.QueryStream(t => !t.Done, batchSize: 1000, ct: ct))
        {
            _ = todo.Title;
        }
    }

    // One coordinated result; the adapter may execute separate data and count commands.
    public async Task<(IReadOnlyList<Todo> Items, long Total, bool IsEstimate)> Page(
        int page,
        int size,
        CancellationToken ct = default)
    {
        var query = QueryDefinition.All.WithPagination(page, size);
        var result = await Todo.QueryWithCount(t => !t.Done, query, ct);
        return (result.Items, result.TotalCount, result.IsEstimate);
    }

    public Task<IReadOnlyList<Todo>> Latest(int size, CancellationToken ct = default)
        => Todo.FirstPage(size, sort => sort.OrderByDescending(t => t.CreatedAt), ct);

    public async Task<(long Fast, long Exact, long Optimized)> Counts(CancellationToken ct = default)
        => (await Todo.Count.Fast(ct),       // estimate is permitted; adapter may fall back
            await Todo.Count.Exact(ct),      // exact result requested
            await Todo.Count.Optimized(ct)); // adapter-preferred path; may be estimated

    public async Task Reseed(IReadOnlyList<Todo> todos, CancellationToken ct = default)
    {
        await Todo.RemoveAll(RemoveStrategy.Fast, ct); // provider-defined fast path; count may be -1
        await todos.Save(ct);                          // one bulk-shaped facade call
    }

    public Task<IReadOnlyList<Todo?>> Load(IEnumerable<string> ids, CancellationToken ct = default)
        => Todo.Get(ids, ct);
}
```

## Reference = Intent

| Surface | Current contract |
|---|---|
| `AllStream` / `QueryStream` | Consumer-paced `IAsyncEnumerable<T>` on a qualified provider; unsupported providers reject before query/yield. |
| `FirstPage` / `Page` | One materialized numbered page; no cursor/resume token. |
| `QueryWithCount` | One `QueryResult<T>` carrying items, total, and `IsEstimate`; provider command count is adapter-owned. |
| `Count.Fast` | Requests a fast path where estimates are allowed; an adapter may use an exact fallback. |
| `Count.Exact` | Requests an exact result; it does not prescribe scan versus native count mechanics. |
| `Count.Optimized` / `await Entity.Count` | Lets the adapter choose its preferred path and can return an estimate internally. Use `Exact` when correctness depends on the value. |
| `items.Save` / `UpsertMany` | One batch-shaped framework call; native bulk, round-trips, and atomicity are capability/provider-specific. |
| `RemoveAll(strategy)` | Provider-defined bulk removal; `Fast` can bypass lifecycle behavior and may return `-1` when count is unknown. |
| `Get(ids)` | Batch-shaped key read with input-position results; physical request count remains adapter-owned. |

## Provider-bounded stream boundary

Qualified today: SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, and Couchbase. InMemory, JSON,
and Redis reject.

Koan requests one numbered candidate page lazily, requests no total, and verifies provider paging and
complete ordering before yielding. Each caller-requested sort component must be a top-level,
non-nullable `bool`, `byte`, `sbyte`, `short`, `ushort`, or `int` member. Every other caller sort,
including an explicit Entity identifier sort, rejects before provider I/O. Koan appends the usual
string Entity identifier only as an opaque provider-stable tie-breaker, not a CLR or cross-provider
collation promise. Streams are not
snapshot-consistent, mutation-safe, resumable, or cursor-based ([DATA-0107](../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)).

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `await Todo.All()` over an unbounded source | A qualified stream, or explicit `FirstPage`/`Page` on an unsupported adapter. |
| `Query` plus a separate `Count` for one response | `QueryWithCount` for a coordinated result; do not promise one provider command. |
| `QueryDefinition.All.WithSort(b => ...)` | Use a typed facade overload such as `Todo.Page(page, size, b => b.OrderByDescending(...), ct)`. |
| `Todo.Query().Where(...).Take(...)` | `Todo.Query(predicate, ...)`, `FirstPage`, `Page`, or `QueryDefinition`; there is no model `IQueryable` chain. |
| "Optimized is exact" | Use `Count.Exact` when exactness is required; current optimized adapter paths may estimate. |
| N key reads in a loop | `Todo.Get(ids, ct)`; retain no claim about physical round-trips. |
| One save per item | `items.Save(ct)` or `Todo.UpsertMany(items, ct)`; verify atomicity separately. |
| `RemoveAll(Fast)` in business logic without review | Probe `DataCaps.Write.FastRemove` and account for bypassed lifecycle/count semantics. |

## Escape hatches

- **Count a subset:** `Todo.Count.Where(t => !t.Done, CountStrategy.Exact, ct)` or
  `Todo.Count.Query(queryDefinition, ct)`.
- **Sorted stream:** `Todo.AllStream(sort => sort.OrderBy(t => t.Priority), ct: ct)` or the
  corresponding `QueryStream` overload, subject to DATA-0107's portable ordering floor.
- **Probe the elected adapter:** `Data<Todo, string>.Capabilities.Has(...)` with `DataCaps` tokens.
- **Provider-native query:** `Todo.QueryRaw(providerQuery, parameters, ct)` when portability is
  intentionally traded for provider behavior.
- **Direct provider session:** resolve `IDataService` and use `Direct(source, adapter)` for an
  advanced provider-specific path; keep it at an infrastructure boundary.

## See also

- [Data capability](../../../docs/reference/data/index.md)
- [Performance guide](../../../docs/guides/performance.md)
- [Entity capabilities how-to](../../../docs/guides/entity-capabilities-howto.md)
- [Entity data reference](../../../docs/reference/data/index.md)
- [DATA-0096 — unified filter pipeline](../../../docs/decisions/DATA-0096-unified-filter-pipeline.md)
- [DATA-0107 — provider-bounded Entity streams](../../../docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [ARCH-0084 — unified capability model](../../../docs/decisions/ARCH-0084-unified-capability-model.md)
