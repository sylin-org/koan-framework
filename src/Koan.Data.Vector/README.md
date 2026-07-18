# Sylin.Koan.Data.Vector

Entity-first vector persistence and search for Koan. The runtime elects one referenced provider per entity, memoizes
repository resolution, applies Koan data isolation to vector operations and physical names, and reports which
providers actually became runtime dependencies.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.InMemory
```

Provider packages bring this runtime. The in-memory provider is the zero-infrastructure development floor; replace or
complement it with a durable provider package when guarantees grow. `AddKoan()` discovers the runtime and referenced
providers; no vector-specific startup code is required. Reference this runtime directly only when authoring a provider
or intentionally composing Vector without a provider.

## Smallest meaningful use

```csharp
using Koan.Data.Core.Model;
using Koan.Data.Vector;

public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
}

var article = new Article { Title = "Koan package quality" };
float[] embedding = [0.12f, 0.42f, 0.88f];

await Vector<Article>.SaveWithVector(article, embedding);
var nearest = await Vector<Article>.Search(embedding, topK: 5);
```

`SaveWithVector` persists the entity through the active data provider and its vector through the elected vector
provider. Use `Vector<Article>.Save(...)` when only the vector representation should change.

Decorate an entity only when automatic provider election is not the intended policy:

```csharp
using Koan.Data.Vector.Abstractions;

[VectorAdapter("qdrant")]
public sealed class Article : Entity<Article> { }
```

The exact provider must be referenced and available; Koan does not silently fall back from an explicit request.

## Guarantees and boundaries

- Referencing the runtime without any vector provider leaves `Vector<TEntity>.IsAvailable` false; operations fail
  correctively instead of inventing storage.
- A directly referenced provider wins automatic election over the low-priority in-memory floor. Multiple candidates
  use the shared provider-priority policy; an explicit `VectorAdapter` request is exact.
- `Save` writes only the vector store. `SaveWithVector` coordinates entity and vector persistence; outside a Koan data
  transaction, an entity success followed by vector failure raises `VectorCoordinationException` and requires
  re-embedding/reconciliation.
- Filter, hybrid search, continuation, export, flush, and embedding retrieval depend on selected-provider capability.
- The in-memory provider is process-local and ephemeral. It is not a durability, distribution, or production-scale
  similarity guarantee.
- Vector dimension, distance metric, score interpretation, consistency, and indexing latency remain provider/model
  responsibilities.

See [TECHNICAL.md](./TECHNICAL.md) for election, caching, naming, isolation, and health behavior.
