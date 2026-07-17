---
uid: reference.modules.Koan.data.core
title: Koan.Data.Core - Technical Reference
description: Contracts, options, design and operations for the Koan data core.
since: 0.2.x
packages: [Sylin.Koan.Data.Core]
source: src/Koan.Data.Core/
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: public Data.Core source inventory and current capability contracts; adapter behavior remains suite-owned
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
  - Predictable materialized paging plus explicit capability negotiation for provider-bounded streams

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

- `TEntity.Lifecycle` declares persistence behavior inside `AddKoan(() => ...)` or Koan module
  registration. The builder is static syntax; every plan and handler list belongs to one host.
- The outer `RepositoryFacade` is the single execution boundary. Provider/module decorators sit
  inside it, so cache hits and generated REST/MCP operations cannot bypass Lifecycle, isolation,
  transforms, or storage guards.
- Registering an equal delegate instance repeatedly is idempotent. Distinct handlers retain FIFO
  order. A plan freezes on first inspection or execution and rejects late mutation correctively.
- `UpsertMany` preflights before-handlers before the first write. With Lifecycle configured it lowers
  to truthful point writes; without handlers the adapter's native bulk path remains available.
- Remove Lifecycle is preserved by `Safe` and `Optimized`; explicit `Fast` is a deliberate bypass.
- `IDataDiagnostics.GetLifecyclePlansSnapshot()` and `koan.data.lifecycle.selected` facts expose the
  composed handler inventory without retaining runtime entities or service scopes.

## Aggregate configuration ownership

- One immutable `DataProviderCatalog` per host owns canonical IDs, aliases, direct-reference evidence,
  collision rejection, and memoized priority metadata. Provider factories no longer implement a second
  `CanHandle` identity authority.
- `DataDefaultProviderPlan` compiles the default choice once. Repository construction, aggregate metadata,
  vector role correlation, diagnostics, startup facts, and the resolved lock consume that same provider ID
  or receipt; they do not independently rank DI registrations.
- Context/source/Entity/default precedence remains Data-owned. A named choice is required and fails closed;
  only record-to-vector correlation is preferred and may continue through Vector's own automatic policy.
- Known build provenance admits directly referenced connectors plus the deliberate JSON floor. A transitive
  factory cannot become persistence accidentally. Low-level hosts without a generated manifest use a
  deterministic priority fallback explicitly reported as `unknown-provenance-priority`.

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
- `IDataDiagnostics.GetAdapterParticipationsSnapshot()` is a separate host-owned execution fact. Merely
  describing an Entity or its route does not activate an adapter; successful repository construction or
  a Direct connection request records the canonical provider and logical source idempotently.
- `AggregateConfigs.Reset()` remains available for test-matrix discovery cleanup, but repeated-host
  correctness does not depend on calling it.

## Data-adapter health participation

- `DataAdapterHealthContributorBase` distinguishes connector availability from application
  dependency. A provider participates when it wins default election, owns a configured source, or
  is selected by a runtime repository or Direct request in that host.
- An available but inactive provider returns `Unknown`, remains non-critical, and must not open a
  connection or mutate backing infrastructure.
- An active provider probes every participating source and is critical. Provider implementations
  retain ownership of the physical probe and may provision only what their normal repository
  contract already provisions.
- Selection uses `AdapterResolver`, `DataSourceRegistry`, and `IDataDiagnostics`; connector health
  implementations must not introduce a parallel configuration hierarchy or fallback election.

## Direct physical routing

- Source- and adapter-routed Direct sessions ask the selected provider factory to resolve the
  physical connection, so normal provider configuration and autonomous discovery remain provider-owned.
- `WithConnectionString(...)` is the explicit escape hatch: its value is used literally and never
  reinterpreted as a source or replaced by a configured default. Blank values and `auto` reject before
  provider I/O; use source or adapter routing when Koan should resolve intent.

## Configuration

- Prefer typed Options for tunables; avoid magic values
- Centralize constants per project (see ARCH-0040)
- Structured query planning follows DATA-0096; bounded Entity streaming follows DATA-0107

## Synchronous console-host ownership

- `IServiceCollection.StartKoan()` is the short synchronous facade over the standard .NET Generic
  Host. It composes Koan when needed, starts every hosted capability, and returns a provider facade
  without blocking for shutdown.
- The returned provider is caller-owned and implements `IDisposable` and `IAsyncDisposable`.
  Disposing it releases its process-default `AppHost` lease before owned services are torn down.
- If discovery or startup throws, `StartKoan()` releases the host lease and disposes the provider
  before rethrowing. A failed start does not leave a selectable ambient host.
- Overlapping providers are safe at the binding boundary: disposing an older provider cannot clear a
  newer owner. Use `AppHost.PushScope(provider)` when concurrent flows must select different providers.
- `StartKoan()` supplies the same `IHostEnvironment`, `IHostApplicationLifetime`, configuration,
  validation, and `IHostedService` lifecycle as other Generic Host applications. Web apps and workers
  use their native builders with `AddKoan()` rather than nesting a second host.

## Usage guidance

- In application models, expose first-class statics:
  - `Item.All(ct)`, `Item.Query(...)`, `Item.FirstPage(...)`, `Item.Page(...)`, `Item.QueryStream(...)`
- Reserve `Data<TEntity, TKey>` for cases where no first-class static exists
- Establish the runtime with a Koan generic host or `StartKoan()` before calling static Entity/Data
  operations; use the typed host-context failure to diagnose lifecycle versus composition errors
- For large sets, prefer explicit pages or a capability-qualified stream; avoid unbounded `All()`

## Explicit numbered paging

- `FirstPage(size)` and `Page(pageNumber, pageSize)` return one materialized page.
- Iterate page numbers until a returned page contains fewer than `pageSize` items.
- Supply an explicit stable sort when repeated or concurrent writes could otherwise change page
  membership. No provider-agnostic cursor/resume-token API exists today.
- Numbered paging limits the result returned to the caller. Some adapters may still perform
  in-memory fallback work; Koan does not yet promise universally bounded provider execution.

## Streaming semantics

- `AllStream/QueryStream` use one Data.Core coordinator to request numbered candidate pages lazily.
  They never invoke `QueryWithCount`; totals are not requested.
- A repository must advertise `DataCaps.Query.ProviderBoundedPaging`, enforce the requested page and
  complete total order, and report both honestly. Otherwise enumeration throws a corrective
  `QueryStreamRejectedException` before yielding instead of materializing the complete source.
- Qualified adapters are SQLite, PostgreSQL, SQL Server, CockroachDB, MongoDB, and Couchbase.
  InMemory, JSON, and Redis currently reject.
- `batchSize` bounds the Koan-visible candidate page, not opaque driver buffers. Consumer pace controls
  later page requests; cancellation and early disposal stop later work.
- Every user stream sort component must be a single-member, top-level, non-nullable `bool`, `byte`,
  `sbyte`, `short`, `ushort`, or `int`. Nullable, enum, string/char, wide numeric, floating/decimal,
  temporal, `Guid`, binary, nested, complex, collection, and explicit Entity-identifier sorts reject
  before provider I/O.
- After validating caller ordering, Koan appends the actual Entity identifier. The usual string key is
  an opaque provider-stable tie-break, not a CLR/cross-provider collation promise. A
  different business member named `id` does not suppress it, but models declaring both `Id` and `id`
  are not portable persistence models and are outside the qualified-adapter contract.
- The current `Int32` provider-offset contract rejects a page before I/O when
  `(pageNumber - 1) * pageSize` exceeds `Int32.MaxValue`. Numbered paging is not snapshot-consistent,
  mutation-safe, resumable, or cursor-based.

## Edge cases and limits

- Large result sets require explicit paging or a stream on a provider that earns
  `DataCaps.Query.ProviderBoundedPaging`
- Concurrency and batches → follow transactional batch semantics (see DATA-0007)
- Adapter capabilities vary. `DataCaps.Query.Filter` describes operator semantics;
  `DataCaps.Query.FilterExecution` carries `FilterExecutionProfile` (`Native`, `InMemory`, `Scan`, or
  `Unknown`). Do not infer backend pushdown from `DataCaps.Query.Linq` or `FilterSupport.Full`.

## Relationship negotiation

- `RelationshipGraphLoader` is the single scalar/set/stream graph-loading owner. It consumes
  `EntityCardinality`, preserves source order and multiplicity, batches parent keys through `GetMany`,
  and sends each child edge to `IRelationshipQueryExecutor` once per bounded source batch.
- `IRelationshipQueryExecutor` remains the child-edge execution owner used by Entity, graph, Web,
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
- This contract covers direct edges. Recursive graph planning, depth budgets, cross-key-type edges,
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

- Prefer Entity streams for consumer-paced large-set work on a qualified adapter; use explicit
  materialization or numbered pages when the selected adapter does not provide bounded streaming
- Batch operations according to adapter guarantees

## Compatibility and migrations

- Target frameworks: net10.0
- Adapter compatibility is capability-specific. Use the adapter matrix and runtime facts instead of
  treating package presence as proof of every Data.Core feature.

## References

- ADR ARCH-0040 - config and constants naming: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- ADR DATA-0096 - unified filter pipeline: `/docs/decisions/DATA-0096-unified-filter-pipeline.md`
- ADR DATA-0107 - provider-bounded Entity streams: `/docs/decisions/DATA-0107-provider-bounded-entity-streams.md`
- ADR ARCH-0084 - unified capability model: `/docs/decisions/ARCH-0084-unified-capability-model.md`
- Engineering guardrails: `/docs/engineering/index.md`

