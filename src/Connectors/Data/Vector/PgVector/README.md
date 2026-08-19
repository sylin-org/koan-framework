# Sylin.Koan.Data.Vector.Connector.PgVector

Use PostgreSQL's `vector` extension through Koan's Entity-first vector API. An application that already operates
PostgreSQL can add semantic search without adopting another data service.

## Declare the space

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.PgVector
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

Reference the package, call `AddKoan()` once, and use the ordinary `Vector<TEntity>` operations. No PostgreSQL client,
table bootstrap, or provider registration belongs in application code.

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

Awaited saves and deletes are visible immediately in `Session` mode. Similarity is normalized to `[0,1]`, where larger
always means closer, for cosine, Euclidean, and dot-product spaces.

## Configure placement

PgVector honors source-specific placement. A source declared as `postgres`/`postgresql`/`npgsql` reuses that PostgreSQL
route directly; a PgVector-owned source honors its own connection before falling back to the selected PostgreSQL
record placement:

```json
{
  "Koan": {
    "Data": {
      "PgVector": {
        "ConnectionString": "Host=localhost;Port=5432;Database=Koan;Username=postgres;Password=use-a-secret-provider"
      }
    }
  }
}
```

The PostgreSQL server must make the `vector` extension available. Managed sources enable it idempotently; a role that
cannot do so receives a corrective error naming the administrator command. Source-specific placement and standard
`Access` or `StorageLifecycle` policy use `Koan:Data:Sources:{name}`.

## Guarantees and limits

- Managed writes create a missing table and validate every existing table against the immutable space dimensions,
  metric, model, and name. Reads never create storage.
- Exact SQL search uses pgvector's native distance operators and native JSONB metadata prefilters.
- Neutral metadata round-trips losslessly; a separate JSONB projection preserves filter semantics.
- Vector tables use a deterministic `_vector` anchor suffix, so the record and vector planes can share one PostgreSQL
  database for the same Entity without colliding.
- Source, partition, and row scopes protect reads and mutations. Clear removes only the active scope and keeps shape.
- Batches use one set-based PostgreSQL statement, preserve ordered per-item outcomes, and conservatively report
  `BatchAtomicity.NotGuaranteed`.
- Eventual visibility, hybrid search, continuations, streaming export, multiple vectors per Entity, and atomic batches
  are declined explicitly.

`PgVectorOptions` bounds command duration, metadata bytes, batch points, and search rows. See
[TECHNICAL.md](./TECHNICAL.md) for native shape and failure semantics.
