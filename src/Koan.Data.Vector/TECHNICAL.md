# Sylin.Koan.Data.Vector — technical contract

## Responsibility

This package owns the functional vector runtime: module activation, provider catalog/election, per-host memoization,
typed repository decoration, isolation-aware physical naming, query gating, active-provider health participation, and
the `Vector<TEntity>` / `VectorData<TEntity>` facades. Provider-neutral types remain in
`Sylin.Koan.Data.Vector.Abstractions`.

## Activation and provider election

`VectorDataModule` calls `AddKoanDataVector()` when the package is referenced. The registration compiles one
`VectorProviderCatalog` from referenced `IVectorAdapterFactory` implementations and exposes the narrow
`IVectorProviderResolver` contract.

Election follows one policy:

1. `[VectorAdapter("id")]` requests that exact provider and fails if unavailable;
2. otherwise, directly referenced provider candidates participate in priority election;
3. if there is no direct candidate, an `IsAutomaticFloor` provider such as InMemory may supply the local floor;
4. with no eligible provider, the entity has no vector repository.

The automatic choice and entity/key configuration are memoized per service provider. `ConditionalWeakTable` keeps
host caches bounded by host lifetime rather than global application lifetime.

## Repository and isolation pipeline

`VectorService.TryGetRepository<TEntity,TKey>()` resolves the elected factory, source, and repository once for the
operation shape, then applies `ScopedVectorRepository` where segmentation requires it. Metadata stamps and read
filters enforce row-scoped dimensions; unsupported required filtering fails closed.

`VectorAdapterNaming` is the single connector-facing naming chokepoint. It folds ambient partition, container-axis
particles, and routed database source through the shared `StorageNameGenerator` and provider naming capability. A
static provider option that pins a collection/index name bypasses those folds; the runtime emits one corrective
warning per entity type when that pin defeats active isolation.

## Operations and coordination

- `Vector<TEntity>.Save` and `VectorData<TEntity>.Save` write vector state only.
- `SaveWithVector` coordinates entity then vector persistence. An active Koan transaction defers both operations to
  its coordinator; otherwise the operations are sequential and a partial success becomes `VectorCoordinationException`.
- `Search` normalizes external filter input into the shared Filter AST and `VectorFilterCoordinator` gates provider
  pushdown before the repository executes.
- `VectorQueryOptions` is the single TopK policy owner: default 10, positive-only validation, and exact pass-through to
  providers. Connector options do not carry competing defaults or caps.
- destructive/optional operations (`Flush`, `Clear`, `Rebuild`, `Stats`, export, embedding retrieval) preserve provider
  capability failures rather than returning fabricated success.

## Health and inspectability

Connector reference makes a provider available, not critical. `IVectorAdapterParticipation` records provider/source
pairs only after repository resolution selects them. Connector health contributors use that compiled participation
snapshot so an unused composed provider does not make an otherwise healthy application unready. Boot provenance
reports the configured default-provider posture and connector modules report their availability/configuration.

## Failure boundaries

- Missing or explicitly unavailable providers fail with the entity and requested/available identities.
- Provider-specific availability, authentication, schema, dimensionality, consistency, throttling, timeout, and
  indexing failures pass through their functional owner.
- Cancellation is carried through facade, coordination, query-gating, and repository calls.
- Runtime memoization assumes provider registration/configuration is immutable after host composition freezes.
