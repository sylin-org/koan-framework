# Sylin.Koan.Data.Vector.Connector.Weaviate

Use Weaviate as a durable Koan vector source without exposing collection, schema, vectorizer, GraphQL, or settling
ceremony to application code.

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Weaviate
```

## Usage

```csharp
services.AddKoan(koan => koan.Data
    .Source("Search")
    .Vector<Article>(space => space
        .Name("content")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));

await Vector<Article>.Save(article.Id, embedding, new { article.Category });
var matches = await Vector<Article>.Search(embedding, query => query
    .Top(12)
    .Where(Filter.Eq("Category", "support")));
```

The local default is `http://localhost:8080`. Configure placement or authentication only when needed:

```json
{
  "Koan": {
    "Data": {
      "Weaviate": {
        "Endpoint": "https://cluster.example.weaviate.network",
        "ApiKey": "use-a-secret-provider"
      }
    }
  }
}
```

Source-specific endpoint, `StorageLifecycle`, and `Access` belong under `Koan:Data:Sources:{name}` like every Koan
adapter. The declared vector plan—not provider options—owns dimensions, metric, model, space, source, and visibility.

## Guarantees

- Self-provided vectors; Weaviate never vectorizes Entity data implicitly.
- Fixed schema and immutable plan marker, validated before use.
- Complete point reads: logical ID, original vector, and lossless neutral metadata.
- Native bounded metadata prefilters for equality, set, collection membership, size, existence, and boolean composition.
- Cosine, Euclidean, and dot-product distance normalization to finite Koan similarity in `[0,1]`.
- Awaited Session visibility through `ALL` mutations and a bounded HNSW queue barrier.
- Source, partition, and row-scope isolation across reads and mutations.
- Read-only and External source policies fail before prohibited mutation.

## Explicit limits

Weaviate is not presented as a full ORM or a generic GraphQL client. The adapter declines portable hybrid search,
Eventual visibility, search continuation, streaming export, atomic batch, and multiple vectors per Entity. Range and
text-pattern metadata predicates are not claimed because the fixed lossless token projection cannot realize them
exactly without dynamic user schema. Ordered batch methods remain available with per-item outcomes, but the adapter
does not label serial create/replace behavior as native bulk.
