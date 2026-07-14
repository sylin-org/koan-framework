---
uid: reference.modules.Koan.data.core
title: Koan.Data.Core - Technical Reference
description: Contracts, options, design and operations for the Koan data core.
since: 0.2.x
packages: [Sylin.Koan.Data.Core]
source: src/Koan.Data.Core/
---

## Contract

- Inputs/Outputs
  - Core interfaces and helpers for aggregates (IEntity<TKey>, conventions, helpers)
  - Generic facades as second-class helpers: `Data<TEntity, TKey>`
- Options (selected)
  - Paging defaults and limits (via typed Options)
  - Naming and constants policy (see ADR ARCH-0040)
- Error modes
  - Validation and adapter errors surface as exceptions; adapters may wrap provider errors
  - Common host-backed Entity/Data operations use `KoanHostContextException` when no host is active,
    the selected provider is disposed, or a required Data service is absent
- Success criteria
  - Consistent paging/streaming semantics across adapters; predictable options binding

## Key types and surfaces

- Primary abstractions: `IEntity<TKey>`, helpers and extensions for data operations
- Extension points: adapter/provider implementations consume these primitives

## Entity lifecycle ownership

- `TEntity.Events` declares process-stable, host-independent behavior for an Entity type.
- Registering an equal delegate repeatedly is idempotent. Distinct handlers retain FIFO order.
- Handlers must not capture a host service provider, scoped service, configuration snapshot, or other
  disposable runtime state. Runtime dependencies resolve through the active operation's ambient host.
- Repeated `AddKoan()` composition may rediscover the same static module hook without multiplying it.
- Distinct closure instances remain distinct handlers; idempotence is not a substitute for correct
  host-independent handler design.

## Aggregate configuration ownership

- `AggregateConfigs.Get<TEntity,TKey>(services)` memoizes configuration and its lazy repository per
  `IServiceProvider`. Sequential or simultaneous hosts using the same closed Entity type do not share
  adapter factories, guards, read contributors, configurations, or repositories.
- Per-provider caches use weak provider keys, so the cache does not extend a host's lifetime. Values
  may safely close over their owning provider because the entire entry releases with that provider.
- `AggregateConfigs.GetRegisteredTypes()` exposes process-wide entity/key discovery facts only. It
  never exposes or retains a provider, repository, configuration snapshot, or service instance.
- `AggregateConfigs.Reset()` remains available for test-matrix discovery cleanup, but repeated-host
  correctness does not depend on calling it.

## Configuration

- Prefer typed Options for tunables; avoid magic values
- Centralize constants per project (see ARCH-0040)
- Paging and streaming follow DATA-0061

## Non-hosted startup ownership

- `IServiceCollection.StartKoan()` is the short synchronous path for processes that do not need a
  generic-host lifecycle. It composes Koan when needed, builds a provider, runs runtime discovery and
  startup, and returns that provider.
- The returned provider is caller-owned and implements `IDisposable` and `IAsyncDisposable`.
  Disposing it releases its process-default `AppHost` lease before owned services are torn down.
- If discovery or startup throws, `StartKoan()` releases the host lease and disposes the provider
  before rethrowing. A failed start does not leave a selectable ambient host.
- Overlapping providers are safe at the binding boundary: disposing an older provider cannot clear a
  newer owner. Use `AppHost.PushScope(provider)` when concurrent flows must select different providers.
- `StartKoan()` does not run the generic host or its `IHostedService` lifecycle. Use `AddKoan()` with a
  generic host for web apps, workers, graceful stop, and hosted background services.

## Usage guidance

- In application models, expose first-class statics:
  - `Item.All(ct)`, `Item.Query(...)`, `Item.FirstPage(...)`, `Item.Page(...)`, `Item.QueryStream(...)`
- Reserve `Data<TEntity, TKey>` for cases where no first-class static exists
- Establish the runtime with a Koan generic host or `StartKoan()` before calling static Entity/Data
  operations; use the typed host-context failure to diagnose lifecycle versus composition errors
- For large sets, prefer paging/streaming; avoid unbounded `All()`

## Pager and EntityCursor

- Stable iteration order is by `Id ASC` across adapters.
- `EntityCursor` captures position and optional includeTotal; treat it as an opaque resume token.
- `Pager` consumes a cursor and yields pages with `Items`, `HasMore/End`, and a continuation `Cursor`.
- Typical flow: `FirstPage(size)` → render/process → `Page(cursor)` until `HasMore=false`.
- For long-running jobs and resumability, use `EntityCursor` directly with `Pager`.

## Streaming semantics

- `AllStream/QueryStream` iterate lazily in batches; honor `batchSize` and `CancellationToken`.
- Backpressure: awaiting inside the loop throttles production naturally.
- Error handling: throw on first error; callers should checkpoint between batches if needed.

## Edge cases and limits

- Large result sets → require paging/streaming
- Concurrency and batches → follow transactional batch semantics (see DATA-0007)
- Adapter capabilities vary; use capability flags when present

## Observability and security

- Integrate with platform logging/tracing; propagate operation context
- Data exposure is adapter-specific; enforce auth/permission at the Web or service layer

## Design and composition

- Data.Core provides shared building blocks used by relational/document/vector adapters
- Composes with Web controllers via application models exposing first-class statics

## Deployment and topology

- No standalone runtime; shipped as a library used by apps and adapters

## Performance guidance

- Prefer paging/streaming over materializing large sets
- Batch operations according to adapter guarantees

## Compatibility and migrations

- Target frameworks: net10.0
- Works with: Koan.Data.Connector.Sqlite, SqlServer, Postgres, Redis, Mongo, Vector providers

## References

- ADR ARCH-0040 - config and constants naming: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- ADR DATA-0061 - paging/streaming semantics: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Engineering guardrails: `/docs/engineering/index.md`

