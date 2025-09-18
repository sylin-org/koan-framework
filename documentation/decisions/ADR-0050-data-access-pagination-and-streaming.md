# ADR-0050: Data access semantics — All/Query materialize fully; add streaming and cursor/pager APIs

Status: Proposed

## Context

Current behavior surprises users: calling `Entity<T>.All()` (or `Data<T>.All`) may return only a default page (e.g., 50 in Mongo), due to adapter-level guardrails. Semantically, `All()` and `Query(...)` with no paging options should return the full set. We also need first-class, provider-agnostic ways to iterate large datasets safely without materializing everything.

## Decision

1) Semantics for no-options calls
- `All(ct)` and `Query(query|predicate, ct)` MUST return the complete result set (fully materialized) when no paging options are provided.
- Adapter default-page limits must not apply to the no-options overloads; limits apply only when options are explicitly supplied.

2) Facade enforcement (uniform behavior across adapters)
- Implement facade helpers that page internally to guarantee consistency even if an adapter lags:
  - `Data<TEntity,TKey>.AllUnbounded(ct)` — loops pages and aggregates the full set.
  - `Entity<TEntity>.All(ct)` delegates to `AllUnbounded(ct)`.
  - `Data<TEntity,TKey>.QueryAll(query|predicate, ct)` — loops pages and aggregates the full set.

3) Streaming APIs (non-materializing)
- Add streaming counterparts for pipelines and long-running jobs:
  - `AllStream(int? batchSize = null, CancellationToken ct = default) : IAsyncEnumerable<TEntity>`
  - `QueryStream(string|predicate, int? batchSize = null, CancellationToken ct = default) : IAsyncEnumerable<TEntity>`
- Streams yield in stable Id-ascending order and internally page with `min(batchSize, MaxPageSize)`.

4) Cursor + Pager (imperative iteration)
- Introduce a provider-agnostic cursor primitive and a user-facing pager:
  - `IEntityCursor<TEntity,TKey>`: `Token`, `NextAsync(size, ct) => CursorPage<TEntity>`
  - `CursorPage<T>`: `Items`, `NextToken`
  - `IPager<TEntity>`: `Items`, `PageSize`, `TotalCount?`, `End`, `NextAsync(ct)`, `Cursor`
  - Factories: `EntityCursor.ForAll`, `EntityCursor.ForQuery`, and `Pager.From(cursor, pageSize, includeTotal)`
- Token is opaque and resume-friendly; adapters may supply native cursors (e.g., Mongo). Facade falls back to last-Id tokens.

5) Explicit paging APIs (materializing pages)
- Keep/standardize: `Page(page, size, ct)` and `FirstPage(size, ct)` in the facade. Options paths (`DataQueryOptions`) are the only ones that apply server-side limits.

6) Cross-adapter rules
- Stable order by Id ascending for all paged/streamed/cursor operations.
- Adapters should implement `IDataRepositoryWithOptions`/`ILinqQueryRepositoryWithOptions` and honor `DataQueryOptions` caps.
- Remove implicit default limits from no-options overloads (Mongo/Sqlite/Redis), keeping guardrails in options paths.

## Consequences

- No silent truncation: `All()` and no-options `Query(...)` return complete sets consistently across providers.
- Clear modalities:
  - Materialize all: `All()` / `QueryAll()`
  - Stream: `AllStream()` / `QueryStream()`
  - Imperative page/next: `EntityCursor` + `Pager`
- Large-set guidance: prefer streaming or pager in jobs to avoid OOM; materialization remains explicit and intentional.

## Follow-ups

- Implement facade methods and interfaces in `Koan.Data.Core`.
- Update adapters:
  - Mongo/Sqlite/Redis: stop default limiting in no-options queries; ensure options overloads cap sizes.
  - Ensure ORDER BY Id for relational adapters in paged paths.
- Add tests: facade loops, streaming, cursor/pager; adapter conformance for options vs. no-options.
- Update docs/guides with examples and migration notes.
