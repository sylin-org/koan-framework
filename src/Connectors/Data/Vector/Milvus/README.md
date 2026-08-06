# Sylin.Koan.Data.Vector.Connector.Milvus

Milvus realizes Koan vector spaces over the REST v2 API. Applications declare the space once; the adapter owns the
fixed collection schema, HNSW index, load lifecycle, native metadata filters, and score conversion.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Milvus
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

The local default is `http://localhost:19530`. Configure only placement or authentication when needed:

```json
{
  "Koan": {
    "Data": {
      "Milvus": {
        "Endpoint": "https://milvus.example.net",
        "Database": "default",
        "Token": "use-a-secret-provider"
      }
    }
  }
}
```

Milvus v2.6.20 is the reference runtime. Standalone deployments require the Milvus service plus its etcd and object
storage dependencies.

## Guarantees

- Dimensions, metric, model, logical space, source, and visibility come only from `VectorSpacePlan`.
- Managed storage uses a fixed VARCHAR/FLOAT_VECTOR/JSON schema, disabled dynamic fields, and an explicit HNSW index.
- Existing collections are validated before use; External and read-only sources cannot create, load, repair, or clear.
- Awaited mutations use Strong reads to satisfy Koan Session visibility over the stateless REST boundary.
- Complete points preserve identity, vector, and neutral metadata.
- COSINE, L2, and IP results become finite `[0,1]` similarities where higher means closer.
- Supported filters execute as native Milvus prefilters; unsupported operators fail before provider I/O.
- Search, metadata, batches, clear, response bodies, load waits, and tie expansion are bounded.

The adapter does not claim Eventual visibility, hybrid text semantics, continuation snapshots, streaming export, atomic
batches, or multiple vector fields per Entity.
