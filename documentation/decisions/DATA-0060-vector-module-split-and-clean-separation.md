# DATA-0060 — Vector module split and clean separation (Koan.Data.Vector)

Status: Accepted

## Context

Vector features (search repos, defaults, and facades) currently live partly in Koan.Data.Core. Non-vector apps see vector types and helpers despite not using them. We want strict separation so vector functionality is opt-in by package reference.

## Decision

Introduce a dedicated module/package Koan.Data.Vector and move all vector-facing implementations there. Also introduce a slim contracts-only package Koan.Data.Vector.Abstractions and relocate vector contracts there (providers depend only on abstractions).

- In Koan.Data.Vector.Abstractions (new): IVectorSearchRepository, IVectorAdapterFactory, VectorCapabilities, VectorQueryOptions/Result/Match, VectorEmbeddingAttribute, [VectorAdapter].
- In Koan.Data.Vector (new): IVectorService (resolution and caching), AddKoanDataVector(IServiceCollection) with options binding, VectorDefaultsOptions, facades VectorData<TEntity,TKey>/VectorData<TEntity> (Upsert/Delete/Search + SaveWithVector/SaveManyWithVector), extensions for IServiceProvider and health.
	- Developer-facing facade: Vector<TEntity> provides terse ergonomics without touching Core. Methods: Save((id, vector, metadata)), Save(IEnumerable<...>), Search(options). Example: await Vector<MyDoc>.Save(items, ct).
- In Koan.Data.Core (remove): Data<TEntity,TKey>.Vector nested facade, SaveWithVector helpers in AggregateExtensions, IDataService vector helpers (TryGetVectorRepository/GetRequiredVectorRepository), VectorDefaultsOptions.
- Resolution precedence: [VectorAdapter] attribute → VectorDefaultsOptions.DefaultProvider → Source provider (from [SourceAdapter]/[DataAdapter]) → first available IVectorAdapterFactory → fail fast.

This is a greenfield split; no shims or deprecations required.

## Packages and dependencies

One-way dependency flow:

Koan.Data.Vector → Koan.Data.Vector.Abstractions → (no further deps)

Providers depend on Koan.Data.Vector.Abstractions (not on Koan.Data.Vector). Core and non-vector apps remain free of vector dependencies by default.

## Consequences

- Non-vector apps do not reference Koan.Data.Vector and won’t see vector APIs or options.
- Samples/tests that use vector features must reference Koan.Data.Vector and update using statements.
- Providers (e.g., Weaviate) register IVectorAdapterFactory from Koan.Data.Vector.Abstractions; Koan.Data.Vector discovers them via DI.

## Migration (repo-wide)

- Add projects Koan.Data.Vector and Koan.Data.Vector.Abstractions; wire AddKoanDataVector().
- Move contracts from Koan.Data.Abstractions → Koan.Data.Vector.Abstractions.
- Move implementation code listed above from Core to Vector module; update namespaces.
- Update S5.Recs to reference Koan.Data.Vector and use Vector<TEntity> (or VectorData<TEntity>) facade for vector operations.
- Update docs and guides to reference the new module.

## Alternatives considered

- Keep vector helpers in Core with conditional DI: rejected; still exposes APIs to non-vector apps and couples Core to vector defaults.
- Partial static extensions across assemblies: not feasible in C#; would fragment UX.

## Notes

- Follow Koan’s core engineering concerns: centralize constants, avoid stubs, and keep module-level boundaries crisp.
- Keep provider packages depending on abstractions only to avoid implementation coupling and ease versioning.
