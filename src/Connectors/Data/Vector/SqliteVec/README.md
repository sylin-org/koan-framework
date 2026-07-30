# Sylin.Koan.Data.Vector.Connector.SqliteVec

Durable, exact vector search in one local SQLite file, with no vector server or provider ceremony.

- Target framework: net10.0
- Native engine: sqlite-vec v0.1.9
- License: Apache-2.0

## Smallest meaningful result

```csharp
builder.Services.AddKoan(koan =>
    koan.Data.Source("Semantic").Vector<Article>(space => space
        .Name("articles")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));

await Vector<Article>.Save(article.Id, embedding, new { article.Category }, ct);

var nearest = await Vector<Article>.Search(
    embedding,
    query => query.Top(12).AtLeast(.82),
    ct);
```

The package discovers itself through `AddKoan`. By default it pairs with the selected SQLite record source; configure
`Koan:Data:Sources:<name>:SqliteVec:ConnectionString` when vectors belong in a separate file.

## Guarantees

- exact native cosine or Euclidean kNN with finite `[0,1]` similarity and stable identity ties;
- atomic complete-point upsert and atomic ordered batches;
- immediate Session visibility and file-backed restart durability;
- positional get-many, truthful delete outcomes, and lossless neutral metadata;
- source, partition, and Koan hard-scope isolation;
- ReadOnly and External policy enforcement before file or shape mutation;
- pinned native RID payloads verified by SHA-256 and `vec_version()` before use.

SqliteVec deliberately declines dot product, arbitrary metadata `Where(...)`, hybrid search, Eventual visibility,
continuations, streaming export, and multiple vectors per Entity. Unsupported intent fails with a corrective exception;
the adapter never substitutes a managed scan or a weaker result.

## Supported native platforms

- Windows x64
- Linux x64
- Linux arm64

An unsupported RID fails before a database operation. Native library paths and extension-loading controls are not
application configuration.

## Bounded options

`Koan:Data:SqliteVec:MaxMetadataBytesPerPoint` and `MaxSearchCandidates` bound per-point metadata and stable-tie
expansion. Dimensions, metric, visibility, placement policy, and space name remain shared Koan decisions.

## References

- [Technical reference](./TECHNICAL.md)
- [Vector reference](../../../../../docs/reference/ai/vector.md)
- [Data adapter development primer](../../../../../docs/architecture/data-adapter-development-primer.md)
