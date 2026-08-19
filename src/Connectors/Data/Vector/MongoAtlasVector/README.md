# Sylin.Koan.Data.Vector.Connector.MongoAtlasVector

`Sylin.Koan.Data.Vector.Connector.MongoAtlasVector` makes an existing Atlas deployment the vector
plane for ordinary Koan `Vector<TEntity>` calls. Referencing the package is the composition step;
the application keeps declaring spaces and using the shared vector API.

## Usage: declare and search

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.MongoAtlasVector
```

```csharp
builder.Services.AddKoan(koan =>
    koan.Data.Source("Default").Vector<Article>(space => space
        .Name("articles")
        .Dimensions(768)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));
```

```csharp
await Vector<Article>.Save(article.Id, embedding, new { article.Category });

var related = await Vector<Article>.Search(embedding, query => query
    .Top(12)
    .Where(Filter.Eq("Category", article.Category)));
```

If the application already uses `Sylin.Koan.Data.Connector.Mongo`, the vector connector reuses that
Mongo endpoint. It writes to the separate `KoanVectors` database and `_vector` collections, so a
record and its vector can safely have the same Entity type and identity.

## Configuration

```json
{
  "Koan": {
    "Data": {
      "MongoAtlasVector": {
        "ConnectionString": "mongodb://atlas-host:27017",
        "Database": "KoanVectors"
      }
    }
  }
}
```

`ConnectionStrings:MongoAtlasVector` is also accepted. When neither vector setting is concrete, the
connector reuses `Koan:Data:Mongo:ConnectionString`, `ConnectionStrings:Mongo`, or the selected
Mongo source. A source may override vector placement under
`Koan:Data:Sources:{source}:mongo-atlas-vector`. The vector database never inherits the record
connector database; set it explicitly when `KoanVectors` is not the desired boundary.

## Guarantees and boundaries

- Search is native Atlas Search with exhaustive `exact: true` execution and scores normalized to
  `[0,1]`.
- Awaited writes and deletes poll Atlas Search until the accepted revision is visible, providing the
  declared Session visibility with a bounded timeout.
- Metadata equality, comparison, set, existence, size, and boolean filters execute as native Atlas
  prefilters. Unsupported intent fails before returning a partial client-filtered answer.
- Batch work is native Mongo bulk I/O with ordered per-item outcomes; whole-batch atomicity is not
  claimed.
- Managed sources may create and validate collections and search indexes. Read-only and external
  sources never create schema and fail correctively when required shape is absent.

This package requires **MongoDB Atlas with Search/Vector Search enabled**. Ordinary MongoDB does not
provide the required search-index commands or execution stage and is rejected; the connector never
falls back to an in-memory approximation. Atlas Local is suitable for development and conformance,
not a production topology recommendation.
