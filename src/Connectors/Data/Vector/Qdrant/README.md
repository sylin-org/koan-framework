# Sylin.Koan.Data.Vector.Connector.Qdrant

Use Qdrant through Koan's Entity-first vector API. The application declares a vector space once; the adapter realizes
its shape, source policy, isolation, visibility, filtering, and similarity semantics over Qdrant.

## Declare the space

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Qdrant
```

```csharp
public sealed class Article : Entity<Article>;

services.AddKoan(koan => koan.Data
    .Source("Search")
    .Vector<Article>(space => space
        .Name("content")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));
```

`Name`, `Dimensions`, `Metric`, and `Visibility` are application decisions. They are not repeated in Qdrant options.
Referencing the package activates the adapter; no provider client or collection bootstrap belongs in application code.

## Save and search

```csharp
await Vector<Article>.Save(article.Id, embedding, new
{
    article.Category,
    article.Language
});

var matches = await Vector<Article>.Search(embedding, query => query
    .Top(12)
    .Where(Filter.All(
        Filter.Eq("Category", "support"),
        Filter.In("Language", ["en", "fr"]))));
```

Awaited saves and deletes are visible to subsequent operations in `Session` mode. Search results expose a normalized
`Similarity` in `[0,1]`, where larger always means closer, for cosine, Euclidean, and dot-product spaces.

## Configure placement

Qdrant at `http://localhost:6333` needs no configuration. Set placement or authentication only when they differ:

```json
{
  "Koan": {
    "Data": {
      "Qdrant": {
        "Endpoint": "https://cluster.example.qdrant.io",
        "ApiKey": "use-a-secret-provider"
      }
    }
  }
}
```

Use `Koan:Data:Sources:{name}` for source-specific endpoints and standard `Access` or `StorageLifecycle` policy. A
read-only source rejects mutations before provider I/O. An external source validates existing storage and never creates
or repairs it.

## Guarantees and limits

- Managed writes create a missing collection from the immutable vector-space plan; every existing collection is
  validated for the declared named-vector dimensions and metric.
- Arbitrary Entity IDs round-trip without exposing Qdrant's UUID/u64 restriction. Source, partition, and row scopes
  participate in physical isolation.
- Neutral metadata round-trips losslessly. A separate provider-native projection supports declared payload filters.
- Batch outcomes preserve input order and report `BatchAtomicity.NotGuaranteed`.
- Clear removes only points in the active scope; it does not drop the collection.
- Dense search is reported as approximate. Candidate expansion is bounded and fails when a stable cutoff cannot be
  proven within the configured limit.
- Eventual visibility, hybrid text search, search continuations, streaming export, and atomic batches are declined
  explicitly. Unsupported filter operators fail closed.

Operator budgets are available through `QdrantOptions`: request timeout, per-point metadata bytes, batch points,
search candidates, and response bytes. They bound work; they do not redefine vector semantics.

See [TECHNICAL.md](./TECHNICAL.md) for the provider realization and failure contract.

## What it adds

Qdrant vector provider for Koan: plan-bound Entity vector search, native payload filtering, and session-visible mutations over REST.
