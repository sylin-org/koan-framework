---
id: DATA-0128
slug: analytics-reads-carry-the-read-scope
domain: Data
status: Accepted
date: 2026-10-21
title: An analytics answer is a read and carries the caller's read scope
related:
  - DATA-0106
  - DATA-0123
  - ARCH-0101
  - DATA-0124
---

# DATA-0128: An analytics answer is a read and carries the caller's read scope

## Context

Declared analytics (DATA-0123) composes an aggregate in the entity's own store and runs it. Composition went through
the repository facade's `IAnalyticsQueryComposer`, which forwarded to the adapter with no guard and no read scope
("the facade has no words of its own"). Execution went through the generic instruction gate as an opaque
`SqlScalar`/`SqlQuery` labeled `Read`, and that gate refused only compiled segmentation. So an aggregate saw rows a
`Query` would never return (ledger NK-041):

- a `[SoftDelete]` entity counted its soft-deleted rows;
- a DataAxis predicate or a Web request row filter did not narrow the aggregate, and a grouped answer disclosed the
  group values of hidden rows, including over the HTTP analytics doors.

Measured before this change on SQLite: three rows, one soft-deleted, `count` answered 3 while `Query` returned 2.

Materialized projections (NK-042) were worse. A projection is one table in the engine's sink. It was refreshed
under whatever ambient the refresher had and served to every caller from the sink, and the rows, facets, delta and
history doors read the sink directly, with no source-policy demand, no operation horizon and no scope. Measured
before this change: a projection of a soft-delete entity refreshed under the default scope and was served without
complaint.

## Decision

**An analytics answer crosses the repository facade as a read, with the same folded read scope as `Query`.**

1. **Live answers are scoped in the store.** The facade folds the caller's read scope (segmentation, managed
   equality, every read-filter contributor: soft delete, data axes, Web row filters) and passes it to the
   adapter's composer, which ANDs it into the question's own predicate with the same `SqlFilterTranslator` a scoped
   query uses. Composition and execution are one facade operation, `IAnalyticsQueryExecutor.ExecuteAnalytics`, under
   one guard (source policy, horizon lease, segmentation bind, pushability proof), so the SQL that runs is the SQL
   composed for that scope. Analytics no longer calls the instruction gate. The explain door composes the same scoped
   SQL.
2. **A composer that cannot place the scope refuses.** `IAnalyticsQueryComposer` gains a scope-taking `TryCompose`
   overload whose default implementation refuses whenever a scope is supplied. SQLite and DuckDB implement it.
3. **A projection is entered only where nothing scopes the caller.** `TryEnterProjection` admits a projection read
   or refresh as a read (source policy and horizon) and returns nothing while any read scope applies, because the
   table was not computed for that scope. Then:
   - `Run` on a materialized question computes live under the caller's scope, labeled live, and never backfills;
   - refresh (programmatic, loop, HTTP trigger, backfill) refuses with a corrective;
   - the rows, facets, delta and history doors, and the HTTP rows door with its CSV and Parquet formats, refuse;
   - explain reports `compute` with the reason.

Tenancy is a read scope like the others. A tenant-segmented entity used to fail closed at the instruction gate;
its live answers now aggregate the tenant's rows, and its projections are entered only under an explicit host scope.

## Compatibility

- Live answers over soft-delete, DataAxis, Web-filtered or tenant entities change: they aggregate visible rows only.
  This is the fix.
- A materialized question over an entity whose reads are scoped by default (soft delete's hidden rows) can no longer
  be refreshed or read through the doors under that default. Refresh and read it under a scope-free ambient (for
  soft delete, inside `T.WithDeleted()`, which includes deleted rows), or ask it on demand. The background refresh
  loop logs the refusal and serving falls back to live answers.
- HTTP: the rows, refresh, facets, delta and history doors answer `400` with the corrective under a scope.
- Additive public surface: `IAnalyticsQueryComposer.TryCompose(question, values, Filter? scope, …)` (default member)
  and `IAnalyticsQueryExecutor<TEntity>`. No signature changes; no stored format changes. `Koan.Data.Analytics`
  grants `InternalsVisibleTo` to `Koan.Data.Analytics.Web` so the HTTP rows door uses the same admission gate.

## Evidence

`AnalyticsReadScopeSpec` (Koan.Data.Analytics.Tests, SQLite record store, DuckDB sink, Koan.Data.SoftDelete):
live count and grouped sum exclude a soft-deleted row and include it under `WithDeleted()`; a materialized
projection refuses refresh under the default scope, refreshes under `WithDeleted()`, is answered live (not served)
for the scoped caller, refuses the rows, facets and history doors under the scope, and serves under `WithDeleted()`.
Both specs failed before this change (count 3; refresh did not throw).
