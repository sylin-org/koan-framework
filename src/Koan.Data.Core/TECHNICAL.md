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

## Context ownership

- `EntityContext` is the Data facade for source, adapter, partition, cache, and transaction routing.
- Its `ContextState` is one typed value in `Koan.Core.Context.KoanContext`, so nested Data scopes share
  Core's logical-flow restoration without making Data the owner of cross-pillar context.
- Tenancy, access subjects, and other modules use their own business-facing facades over
  `KoanContext`; `EntityContext` exposes no generic slice API.
- Durable context carriage is independently registered through `IKoanContextCarrier` and the
  host-owned `KoanContextCarrierRegistry`. The Data-axis DSL describes Data realization only; it does
  not register context carriers.

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
- `IDataDiagnostics.GetEntityConfigsSnapshot()` is host-owned and contains only configurations
  observed by that host. Aggregate resolution records into it directly; diagnostics do not reflect
  over private cache implementation details.
- `AggregateConfigs.Reset()` remains available for test-matrix discovery cleanup, but repeated-host
  correctness does not depend on calling it.

## Data-adapter health participation

- `DataAdapterHealthContributorBase` distinguishes connector availability from application
  dependency. A provider participates when it wins default election, owns a configured source, or
  appears in the host-owned entity diagnostics snapshot.
- An available but inactive provider returns `Unknown`, remains non-critical, and must not open a
  connection or mutate backing infrastructure.
- An active provider probes every participating source and is critical. Provider implementations
  retain ownership of the physical probe and may provision only what their normal repository
  contract already provisions.
- Selection uses `AdapterResolver`, `DataSourceRegistry`, and `IDataDiagnostics`; connector health
  implementations must not introduce a parallel configuration hierarchy or fallback election.

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

## Explicit numbered paging

- `FirstPage(size)` and `Page(pageNumber, pageSize)` return one materialized page.
- Iterate page numbers until a returned page contains fewer than `pageSize` items.
- Supply an explicit stable sort when repeated or concurrent writes could otherwise change page
  membership. No provider-agnostic cursor/resume-token API exists today.
- Numbered paging limits the result returned to the caller. Some adapters may still perform
  in-memory fallback work; Koan does not yet promise universally bounded provider execution.

## Streaming semantics

- `AllStream/QueryStream` currently materialize `QueryWithCount` before the first yield. The
  `batchSize` parameter is accepted but not yet applied, so these methods do not provide bounded
  memory or provider backpressure today.
- Cancellation reaches the materialized query. Prefer explicit numbered pages to limit each returned
  result, but do not infer a provider-enforced memory bound until the R07 Data-semantic streaming slice
  replaces this implementation.

## Edge cases and limits

- Large result sets → require paging/streaming
- Concurrency and batches → follow transactional batch semantics (see DATA-0007)
- Adapter capabilities vary. `DataCaps.Query.Filter` describes operator semantics;
  `DataCaps.Query.FilterExecution` carries `FilterExecutionProfile` (`Native`, `InMemory`, `Scan`, or
  `Unknown`). Do not infer backend pushdown from `DataCaps.Query.Linq` or `FilterSupport.Full`.

## Relationship negotiation

- `IRelationshipQueryExecutor` is the single child-edge execution owner used by Entity, batch, Web,
  and MCP paths. A batch of roots becomes one `Filter.In` query per edge.
- `RelationshipQueryPolicy.Strict` accepts native or already-resident InMemory execution and rejects
  scans or residual fallback. It is the default for existing Entity method shapes.
- `RelationshipQueryPolicy.Bounded(maxCandidates, maxResults)` explicitly accepts finite scan or
  residual work. Providers implementing `IBoundedQueryRepository` refuse before returning partial
  data when the candidate limit is exceeded.
- `RepositoryFacade` applies storage guards and managed/read-scope filters to bounded reads exactly as
  it does ordinary queries; field transforms are reversed before results leave the facade.
- `RelationshipQueryRejectedException` carries safe relationship/provider/reason/correction fields.
  The latest selected or rejected mode is recorded as `koan.data.relationship.execution` in the
  shared runtime-fact snapshot.
- This contract covers direct child edges. Parent batching, recursive graph planning, depth budgets,
  index verification, and fleet-wide performance certification are not included.

## Observability and security

- Integrate with platform logging/tracing; propagate operation context
- Data exposure is adapter-specific; enforce auth/permission at the Web or service layer

## Design and composition

- Data.Core provides shared building blocks used by relational/document/vector adapters
- Composes with Web controllers via application models exposing first-class statics

## Deployment and topology

- No standalone runtime; shipped as a library used by apps and adapters

## Performance guidance

- Prefer explicit numbered paging for large sets; current stream-shaped methods are materialized
  compatibility surfaces, and a universal provider-enforced bound is not yet available
- Batch operations according to adapter guarantees

## Compatibility and migrations

- Target frameworks: net10.0
- Works with: Koan.Data.Connector.Sqlite, SqlServer, Postgres, Redis, Mongo, Vector providers

## References

- ADR ARCH-0040 - config and constants naming: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- ADR DATA-0061 - paging/streaming semantics: `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Engineering guardrails: `/docs/engineering/index.md`

