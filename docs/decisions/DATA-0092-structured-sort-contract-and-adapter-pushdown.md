---
id: DATA-0092
slug: DATA-0092-structured-sort-contract-and-adapter-pushdown
domain: DATA
status: Accepted
date: 2026-05-17
---

# ADR 0092: Structured sort contract and adapter pushdown

## Context

Sort handling in the Koan data layer is broken in three independent ways, all silent:

1. **Sort is never pushed down to any adapter.** `DataQueryOptions.Sort` is declared as `string?` but **no adapter reads it**. The field is set in `EntityEndpointService.BuildDataQueryOptions` and consumed nowhere — a write-only field across the entire framework.
2. **Sort is applied after pagination.** `EntityEndpointService.ApplySort` runs on the already-paged repository result ([EntityEndpointService.cs:88-101]), so even root-level sort produces semantically wrong results for datasets larger than one page.
3. **Deep-object sort is silently dropped.** `CreateKeySelector` calls `Type.GetProperty(segment)` on each segment of a dot-path. For collection segments (`Sightings.LastChangedAt` where `Sightings` is `List<Sighting>`), `GetProperty("LastChangedAt")` on `List<Sighting>` returns `null` → selector returns `null` → `ApplySort` silently `continue`s, leaving the result unsorted.

Compounding the above:

- **The string sort grammar is parser-broken.** `?sort=+Title` keeps the `+` prefix as part of the field name (`GetProperty("+Title")` → `null`) and silently drops the sort. Only `-` is handled.
- **`POST /query` body has no sort field.** Asymmetric with `GET ?sort=`.
- **`IPagedRepository` is dead code.** Declared Sep 2025, superseded by `IDataRepositoryWithOptions` + `CountRequest` in Oct 2025, zero implementors. The "fastpath" branch in `Data<T,K>.QueryWithCount` is permanently dark.
- **`QueryCapabilities` flags are advisory only.** `RepositoryFacade` dispatches by interface type-check, not by capability flag. Adding a `Sort` flag would be misleading metadata — capability is conveyed by implementing the interface.

These bugs hide each other: users who report "deep-object sort doesn't work" are also unknowingly receiving incorrectly-sorted page-1-of-natural-order results for root-level sort on any multi-page dataset.

## Decision

Introduce a **structured sort contract** at the data abstractions layer, **pushed down where adapters can translate** and **applied in-memory as a correct fallback** where they cannot. Replace the dead `string Sort` field with `IReadOnlyList<SortSpec>`. Replace the dead `IPagedRepository` interface. Make the `IDataRepositoryWithOptions` family return a richer `RepositoryQueryResult<T>` that declares which sort specs were pushed down — the orchestrator uses this signal to decide whether to fall back to in-memory sort.

### 1. Structured sort representation

```csharp
// Koan.Data.Abstractions
public sealed record SortSpec(MemberPath Path, bool Desc, SortAggregation Aggregation = SortAggregation.None);

public sealed record MemberPath
{
    public Type RootType { get; }
    public IReadOnlyList<MemberInfo> Members { get; }
    public string DotPath { get; }                  // canonical "Foo.Bar.Baz"
    public Type ValueType { get; }                  // the final leaf type
    public bool TraversesCollection { get; }        // any segment was IEnumerable<T>
}

public enum SortAggregation { None, Max, Min, First, Last }
```

`MemberPath` is **resolved at parse time**, never null when present in a `SortSpec`. Adapters translate from this structured form to native syntax (Mongo dot-notation, Postgres JSON path expressions, SQL `ORDER BY`, in-memory `Expression.Property` chains). No adapter parses strings.

### 2. String grammar

The string form is one of three input surfaces; HTTP and tooling use it.

| Input | Direction | Notes |
|---|---|---|
| `Field` | ASC | no prefix |
| `+Field` | ASC | explicit |
| `-Field` | DESC | explicit |
| `Foo.Bar` | ASC | dot-path; resolved against entity type |
| `-Foo.Bar` | DESC | dot-path; resolved against entity type |
| `a,b,c` | multi | comma-separated; applied as `OrderBy(a).ThenBy(b).ThenBy(c)` |

**Aggregation convention for collection paths:** when a segment is `IEnumerable<T>` (excluding `string`), the next segment resolves against the element type and `SortAggregation` defaults to `Max` for `Desc=true` or `Min` for `Desc=false`. This is the only convention that does not surprise users ("sort by latest sighting first" reads naturally for `-Sightings.LastChangedAt`).

### 3. LINQ surface

```csharp
public interface ISortBuilder<T>
{
    ISortBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> selector);
    ISortBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> selector);
    ISortBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> selector);
    ISortBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> selector);
}
```

Both the string parser and the LINQ visitor normalize to the same `SortSpec[]`. Lambdas give compile-time field validation (typos do not compile); strings give URL-grammar convenience.

### 4. Strictness

**Default: strict.** Unresolvable field → `InvalidSortFieldException` (programmatic) or `400 Bad Request` (HTTP). No silent skip.

**Opt-in lenient:** `EndpointOptions.LenientSort = true` (per controller) or `?ignoreUnknownSort=true` (per request). When lenient, the framework still skips the spec but adds a `Koan-Sort-Skipped` response header so the caller can detect it.

### 5. Repository contract

```csharp
public sealed class RepositoryQueryResult<TEntity>
{
    public required IReadOnlyList<TEntity> Items { get; init; }
    public long? TotalCount { get; init; }
    public bool IsEstimate { get; init; }
    public bool PaginationHandled { get; init; }
    public IReadOnlySet<SortSpec> SortHandled { get; init; } = FrozenSet<SortSpec>.Empty;
}

public interface IDataRepositoryWithOptions<TEntity, TKey> : IDataRepository<TEntity, TKey>
{
    Task<RepositoryQueryResult<TEntity>> Query(object? query, DataQueryOptions? options, CancellationToken ct = default);
}
// Same return-type migration for ILinqQueryRepositoryWithOptions and IStringQueryRepositoryWithOptions.
```

Adapters return `SortHandled` listing the specs they actually pushed down. Adapters that cannot push any sort return an empty set; the orchestrator handles the rest.

### 6. Orchestrator rules

`Data<T,K>.QueryWithCount`:

1. If no sort: behaviour unchanged (pagination either pushed down or applied in-memory).
2. If sort + adapter handled all specs: trust the adapter, no in-memory work, paginate as adapter delivered.
3. If sort + adapter handled some specs: fetch unpaginated (subject to `AbsoluteMaxRecords`), apply remaining specs in-memory via `InMemorySorter`, then paginate.
4. If sort + adapter handled no specs: as case 3.

The orchestrator **always emits sort-stable results**. When the adapter doesn't push down, paginate-after-sort, not the inverse.

### 7. Per-adapter pushdown commitments (initial implementation)

| Adapter | Pushdown strategy |
|---|---|
| InMemory | LINQ `OrderBy` chain via expression builder from `MemberPath`. Aggregation via `Max`/`Min` on collections. |
| Json | Same as InMemory (materialize, LINQ sort). |
| Redis | Same as InMemory. |
| Sqlite | `ORDER BY json_extract(Json, '$.Path')` for scalar paths; projected columns when present. Aggregation: not pushed, falls back. |
| Postgres | `ORDER BY "Json" #>> '{Path}'` for scalar paths; generated columns when present. Aggregation: not pushed, falls back. |
| SqlServer | `ORDER BY JSON_VALUE([Json], '$.Path')` for scalar paths; computed columns when present. Aggregation: not pushed. |
| Mongo | `Sort(BsonDocument)` with dot-paths. Aggregation: not pushed in v1 (use `$sortArray` in v2). |
| Couchbase | `ORDER BY doc.path` N1QL. Aggregation: not pushed. |

Adapters that cannot push aggregation return the un-aggregated specs in `SortHandled = empty` for those specs; orchestrator handles them in memory. **Correctness is guaranteed regardless of which adapter pushed how much.**

### 8. Dead code removal

- Delete `IPagedRepository` and `PagedRepositoryResult` (no implementors, replaced by enriched `IDataRepositoryWithOptions`).
- Delete `EntityController.ApplySort(IQueryable, QueryOptions)` virtual hook (never called, no overrides).
- Delete `?dir=` query parameter (subsumed by per-spec `±` prefix).
- Delete `DataQueryOptions.Sort : string?` (replaced by `IReadOnlyList<SortSpec>`).
- Delete `EntityEndpointService.ToSortString` (no stringification at boundary).
- Delete `EntityEndpointService.CreateKeySelector` (replaced by `InMemorySorter` using `MemberPath`).

### 9. Hook compatibility

`QueryOptions.Sort` is `List<SortSpec>` (web-layer alias of the structured type). Hooks can mutate the list. To add a sort by string field name without ceremony, hooks use the extension `opts.Sort.AddByField("-CreatedAt")` which resolves against `TEntity` at call time.

## Consequences

**Positive:**
- All sort failure modes become loud (`InvalidSortFieldException` / `400`) instead of silent drops.
- Sort is correct on multi-page datasets regardless of adapter capability.
- Deep-path and collection-aggregation semantics are explicit.
- Lambda surface gives compile-time field validation.
- One canonical internal representation; eliminates lossy stringification.
- Removes three pieces of dead code.

**Negative / breaking:**
- `DataQueryOptions.Sort` type changes from `string?` to `IReadOnlyList<SortSpec>` (no consumers of the old type exist in-repo, but external integrators must migrate).
- `IDataRepositoryWithOptions.Query` and siblings now return `RepositoryQueryResult<T>` instead of `IReadOnlyList<T>`. Every in-repo adapter is migrated as part of this ADR. External adapters must follow.
- `IPagedRepository` removal is a public API breaking change (zero in-repo impact).
- HTTP behaviour change: `?sort=NonexistentField` now returns 400 instead of 200-with-skipped-sort. Migration path: set `EndpointOptions.LenientSort = true` if existing clients depend on lenient behaviour.

**DX preservation:**
- `Entity<T>.Query(predicate)`, `.All()`, `.Get(id)`, `.Save()` — all unchanged.
- `DataQueryOptions.WithSort(string)` — preserved as a convenience that parses internally.
- `?sort=` query string — same grammar, plus `+` prefix now works correctly.

## Alternatives considered

- **Keep `string Sort`, parse in each adapter.** Rejected: every adapter re-implements identical parsing with identical edge cases; the string can't carry aggregation hints; it's lossy at the type boundary.
- **Add a separate `ISortPushdownCapable` interface.** Rejected: violates the "interface declares capability" pattern already in place; adds dispatch complexity; the return-flag mechanism (`SortHandled` on `RepositoryQueryResult`) covers partial pushdown more cleanly than an all-or-nothing flag.
- **Boolean `SortHandled` instead of `IReadOnlySet<SortSpec>`.** Rejected: adapters that can push some specs but not others (e.g., scalar paths yes, collection aggregation no) need to communicate partial capability. The set form expresses this cleanly.
- **Capability flag on `QueryCapabilities` enum.** Rejected: flags are advisory; adapters declare real capability by implementing the interface. Adding a flag would be misleading metadata.
