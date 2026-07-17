# Sylin.Koan.Data.Core

Data access core for Koan: common primitives, options, and helpers used by relational/document/vector providers and apps.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities

- Entity contracts and helpers for aggregate storage
- Options and conventions shared across data adapters
- Data-owned adapter option binding for readiness and default paging
- Schema recovery composed around Core's generic readiness gate
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
  - `Item.FirstPage(size, ct)` and `Item.Page(pageNumber, pageSize, ct)`
  - `Item.QueryStream(predicate, ct)`
- `AllStream`/`QueryStream` lazily compose provider-bounded numbered pages on SQLite, PostgreSQL,
  SQL Server, CockroachDB, MongoDB, and Couchbase. InMemory, JSON, and Redis reject correctively
  before query/yield; there is no materializing fallback. `batchSize` bounds Koan-visible candidates,
  not opaque driver buffers. No public cursor or resume-token API exists.
- If a first-class static isn’t available, you can fall back to the generic facade (second-class): `Data<TEntity, TKey>.Query(...)`.

Host-owned persistence policy composes beside the normal zero-configuration bootstrap:

```csharp
services.AddKoan(() =>
    Item.Lifecycle.BeforeUpsert(context =>
        context.Current.Price < 0
            ? context.Cancel("Price cannot be negative.", "item.price")
            : context.Proceed()));
```

The same Lifecycle boundary governs Entity/Data calls and generated REST/MCP entity operations. See
`/docs/reference/data/entity-lifecycle.md` for phases, bulk/transaction semantics, and deliberate
bypasses.

Child relationships are strict by default. Native and in-memory providers execute directly; a
scan-backed provider fails with a corrective `RelationshipQueryRejectedException` unless the call
chooses a finite budget:

```csharp
var children = await todo.GetChildren<TodoItem>(
    RelationshipQueryPolicy.Bounded(maxCandidates: 1_000, maxResults: 200), ct);
```

This policy bounds candidates before rows escape and never returns a partial relationship.

Load every declared direct edge with the same `Relatives` operation for one Entity, a finite
selection, or a provider-bounded stream. Model and key types are inferred:

```csharp
var graph = await todo.Relatives(ct);
var graphs = await todos.Where(todo => !todo.IsCompleted).Relatives(ct);

await foreach (var current in Todo.QueryStream(todo => !todo.IsCompleted).Relatives(ct))
{
    // current.Entity, current.Parents, current.Children
}
```

Finite and stream forms preserve source order and multiplicity. Parent edges use batched keyed reads;
child edges retain the same strict or explicitly bounded negotiation and runtime facts as
`GetChildren<TChild>`.

Required Entity/Data operations without a usable Koan host throw `KoanHostContextException`. Its
`Failure`, `Operation`, and `RequiredService` properties distinguish an absent host, a disposed host,
and a host where the Data module was not composed.

`EntityContext` is deliberately Data-specific: it scopes source, adapter, partition, cache, and
transaction routing. It stores that state in Core's logical-flow `KoanContext`, but it is not the
generic API for tenancy, subjects, or other module-owned axes. Those modules own their business-facing
facades and register durable carriage independently through `Koan.Core.Context`.

For a synchronous console process, `new ServiceCollection().StartKoan()` starts a standard .NET
Generic Host and returns its active provider facade. The caller owns it; use
`using var app = (IDisposable)services.StartKoan()` so disposal stops hosted capabilities and releases
the ambient Koan host binding. ASP.NET Core and workers continue to use their native host builder with
`AddKoan()`.

## Boundaries and failures

- Data Core is the Entity runtime and provider-election owner; it does not provide storage by itself. Reference a
  connector or use the JSON floor carried by a Koan entry bundle.
- Its dependency on Cache Abstractions is inert contract vocabulary. Referencing Data Core does not activate caching.
- Stream-shaped Entity APIs run only when the elected provider proves bounded paging. Unsupported providers reject
  before yielding instead of hiding whole-source materialization.
- Relationship expansion is direct-edge and budgeted. It is not recursive graph traversal, snapshot isolation, or a
  promise that scan-backed providers can execute without an explicit candidate limit.
- `EntityContext` owns Data routing dimensions only; tenancy, subject, and other semantic axes are contributed and
  enforced by their owning modules.
- Canonical `PatchPayload` is provider-neutral. Web/MCP projections own protocol parsing and normalization before
  the operation reaches Data.
- Required Entity operations without a live composed host throw `KoanHostContextException` with the missing
  operation/service correction.

## Customization

- Configuration and advanced usage are documented in
  [TECHNICAL.md](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Data.Core/TECHNICAL.md).

## References

- [Data access patterns](https://github.com/sylin-org/Koan-framework/blob/main/docs/guides/data/entity-access-and-streaming.md)
- [Provider-bounded Entity streams](https://github.com/sylin-org/Koan-framework/blob/main/docs/decisions/DATA-0107-provider-bounded-entity-streams.md)
- [Engineering guardrails](https://github.com/sylin-org/Koan-framework/blob/main/docs/engineering/index.md)
- Repo: https://github.com/sylin-org/Koan-framework
