# Sylin.Koan.Data.Core

Data access core for Koan: common primitives, options, and helpers used by relational/document/vector providers and apps.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Entity contracts and helpers for aggregate storage
- Options and conventions shared across data adapters
- Selection-aware health semantics for data connectors
- Support for paging, streaming, and batching semantics (see references)

## Install (minimal setup)

```powershell
dotnet add package Sylin.Koan.Data.Core
```

## Usage - quick examples

- Prefer first-class model statics for top-level data access in your app models:
  - `Item.All(ct)`
  - `Item.Query(predicate, ct)`
  - `Item.FirstPage(pageSize, ct)` and `Item.Page(cursor, ct)`
  - `Item.QueryStream(predicate, ct)`
- For large sets, use paging or streaming; don’t materialize unbounded results.
- If a first-class static isn’t available, you can fall back to the generic facade (second-class): `Data<TEntity, TKey>.Query(...)`.

Child relationships are strict by default. Native and in-memory providers execute directly; a
scan-backed provider fails with a corrective `RelationshipQueryRejectedException` unless the call
chooses a finite budget:

```csharp
var children = await todo.GetChildren<TodoItem>(
    RelationshipQueryPolicy.Bounded(maxCandidates: 1_000, maxResults: 200), ct);
```

This policy bounds candidates before rows escape and never returns a partial relationship.

Required Entity/Data operations without a usable Koan host throw `KoanHostContextException`. Its
`Failure`, `Operation`, and `RequiredService` properties distinguish an absent host, a disposed host,
and a host where the Data module was not composed.

For a synchronous, non-hosted process, `new ServiceCollection().StartKoan()` returns the active
provider. The caller owns it; use `using var app = (IDisposable)services.StartKoan()` so disposal also
releases the ambient Koan host binding. ASP.NET Core, workers, and applications that need
hosted-service lifecycles should use the generic host with `AddKoan()` instead.

See TECHNICAL.md for contracts, options, and extension points.

## Customization

- Configuration and advanced usage are documented in [`TECHNICAL.md`](./TECHNICAL.md).

## References

- Data access patterns: `/docs/guides/data/all-query-streaming-and-pager.md`
- Decision: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Engineering guardrails: `/docs/engineering/index.md`
- Repo: https://github.com/sylin-org/Koan-framework
