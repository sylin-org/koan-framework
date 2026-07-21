# Sylin.Koan.Data.Vector.Abstractions

Provider-neutral contracts for Koan vector search: adapter factories, repositories, capabilities, query and match
shapes, export batches, schema descriptions, and provider-selection annotations. Referencing this package alone does
not register a vector runtime or select a backend.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Abstractions
```

Application developers normally reference `Sylin.Koan.Data.Vector` and a provider. Reference Abstractions directly
when authoring a vector provider, shared query library, or projection that must not activate the runtime.

## Smallest meaningful use

A provider-neutral library can construct the exact query handed to every vector repository:

```csharp
using Koan.Data.Vector.Abstractions;

var query = new VectorQueryOptions(
    Query: embedding,
    TopK: 10,
    SearchText: "annual revenue",
    Alpha: 0.65);
```

Provider authors implement `IVectorAdapterFactory` and return `IVectorSearchRepository<TEntity,TKey>` instances.
`VectorAdapterAttribute` is the explicit entity-level provider request; `VectorCaps` describes optional repository
behavior without pretending every backend has the same guarantees.

## Guarantees and boundaries

- This package depends on entity/filter/naming vocabulary from `Sylin.Koan.Data.Abstractions`; it does not depend on
  Data.Core, dependency injection, the vector runtime, or any provider client.
- Query options express intent. Hybrid text, filtering, continuation, export, flush, and embedding retrieval remain
  provider capabilities and may be unsupported.
- Repository default methods fail with `NotSupportedException` for optional operations rather than simulating them.
- Provider election, memoization, physical collection naming, active-participation health, and entity facades belong
  to `Sylin.Koan.Data.Vector`.
- Score scale, consistency, indexing latency, dimensionality, and distance behavior are backend guarantees; this
  package does not normalize them into false equivalence.

See [TECHNICAL.md](./TECHNICAL.md) for the complete SPI and schema boundary.
