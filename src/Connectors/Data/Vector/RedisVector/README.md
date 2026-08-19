# Sylin.Koan.Data.Vector.Connector.RedisVector

Use Redis Search vector indexing through Koan's Entity-first vector API. Applications already operating a
vector-enabled Redis deployment can add semantic search without adopting a second data service.

## Declare the space

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.RedisVector
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

Reference the package, call `AddKoan()` once, and use the ordinary `Vector<TEntity>` operations. The connector
reuses `ConnectionStrings:Redis`, Koan's shared Redis discovery, and its host-owned multiplexer. It adds no provider
registration or second Redis client to application code.

## Usage: save and search

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

Awaited saves and deletes are immediately visible. Cosine, Euclidean, and dot-product results use normalized
`[0,1]` similarity where larger always means closer.

## Runtime prerequisite

The selected Redis endpoint must provide Redis Search with vector indexing. Plain Redis is insufficient and fails
with a corrective health or operation error. Redis Search vector indexes live in logical database 0; a vector source
that requests another database is rejected instead of silently addressing the wrong index. Record and cache sources
may continue using their own logical database while sharing the same Redis process and multiplexer. Readiness uses
the ordinary Search information surface and does not require the administrative `FT._LIST` permission; immutable
vector shape is validated when a declared space is first ensured or used.

Named sources can use `Adapter=redis` or `Adapter=redis-vector`; both resolve through the existing Redis connection
owner. Standard source `Access` and `StorageLifecycle` policy remains in force.

## Guarantees and limits

- Managed sources create and validate an exact `FLAT`/`FLOAT32` index against the declared dimensions, metric,
  space, and model. Reads never create storage.
- Native Redis Search prefilters implement equality, numeric comparison, set, existence, size, boolean, and
  negation semantics over nested metadata paths. Finite numeric metadata that Redis NUMERIC cannot represent
  exactly still round-trips, while relational filters over that path fail closed.
- Neutral metadata remains lossless in an unindexed payload; deterministic TAG/NUMERIC projections own filtering.
- Vector keys and indexes have their own `_vector` namespace, so record and vector representations of one Entity
  coexist in one Redis deployment.
- Batches pipeline native Redis operations, preserve ordered outcomes, and report `BatchAtomicity.NotGuaranteed`.
- Eventual visibility, hybrid search, continuations, streaming export, multiple vectors per Entity, and atomic
  batches are declined explicitly.

`RedisVectorOptions` bounds metadata bytes, batch points, search candidates, per-point projection, and total
dynamic numeric/size schema paths. See
[TECHNICAL.md](./TECHNICAL.md) for native shape and failure semantics.
