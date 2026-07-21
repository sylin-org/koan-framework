# Sylin.Koan.Data.Vector.Abstractions — technical contract

## Responsibility

This assembly is the inert boundary shared by the vector runtime, provider packages, projections, and provider-neutral
libraries. It owns no `KoanModule`, service registration, provider catalog, ambient data context, or physical naming
policy. Its only project dependency is `Sylin.Koan.Data.Abstractions`.

## Provider SPI

- `IVectorAdapterFactory` identifies a provider, aliases, reference identities, automatic-floor posture, naming
  capability, and typed repository creation.
- `IVectorSearchRepository<TEntity,TKey>` owns upsert, delete, search, and optional embedding retrieval, index creation,
  flush, and streaming export.
- `IDecoratedVectorRepository` and `IOverlayNamingAware` expose bounded runtime decoration/naming capabilities.
- `IVectorProviderResolver` and `IVectorAdapterParticipation` are infrastructure handoffs implemented by the runtime;
  provider packages consume the interfaces without owning election or host state.
- `VectorAdapterAttribute` requests one provider identity for an entity. The runtime fails when that exact identity is
  unavailable rather than silently substituting another backend.

## Query and result model

`VectorQueryOptions` carries the query vector, top-K, continuation token, typed Filter AST, timeout, vector name,
hybrid search text, and semantic/keyword alpha. `VectorQueryResult<TKey>` contains matches plus continuation and total
posture; `VectorMatch<TKey>` carries identity, score, and metadata. `VectorExportBatch<TKey>` supports bounded streaming
migration/backup workflows.

`VectorCaps` maps repository capabilities into Koan's shared `CapabilitySet`. A capability declaration describes
available mechanics; it does not establish backend readiness or performance.

## Schema boundary

`VectorSchemaAttribute`, `VectorPropertyAttribute`, schema descriptors/properties, and `VectorSchemaRegistry` provide a
provider-neutral metadata schema. Reflection-based schema projection is cached per CLR type. Providers decide how—and
whether—the descriptor maps to their physical schema system.

## Failure boundary

- Optional repository operations default to corrective `NotSupportedException` failures.
- Invalid vector length, unsupported filters or hybrid search, provider availability, rate limits, timeouts, and
  consistency errors originate in the selected provider/runtime.
- Contract methods carry cancellation; implementations must not swallow caller cancellation.
- Metadata is an open dictionary. Providers own serialization limits, supported scalar/collection types, and reserved
  field handling.
