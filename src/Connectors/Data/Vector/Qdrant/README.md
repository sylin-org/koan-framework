# Sylin.Koan.Data.Vector.Connector.Qdrant

Use Qdrant behind Koan's entity-first vector API. Referencing this package is the activation step; `AddKoan()`
discovers it, elects it when appropriate, and reports the effective route at startup.

> **Maturity:** Supported 0.20 extension within the capabilities and boundaries below.

## Install and use

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Qdrant
```

```csharp
public sealed class Article : Entity<Article> { }

await Vector<Article>.Save(article.Id, embedding, new { category = "support" });
var matches = await Vector<Article>.Search(embedding, topK: 12);
```

No Qdrant-specific registration is required. With Qdrant reachable at `http://localhost:6333`, no configuration is
required either. Set an endpoint only when the deployment differs:

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

The first write establishes vector dimension. Set `Koan:Data:Qdrant:Dimension` only when the application must create
an empty collection before any write. Koan defaults `topK` to 10; an explicit positive value is sent unchanged.

## Honest capability boundary

Qdrant supports KNN search, metadata filters, bulk writes/deletes, embedding reads, collection clear, export through
scroll, dynamic collection naming, and normalized cosine scores. This adapter does not claim hybrid text search,
search continuation tokens, or index statistics.

Writes wait for visibility by default. Set `WaitForResult` to `false` only when ingestion throughput is more important
than immediate read-after-write behavior. The default scalar-quantized, on-disk profile favors lower memory use; tune
`Quantization` and `OnDisk` when recall and memory requirements differ.

Avoid pinning `CollectionName` in partitioned or tenant-isolated applications unless one shared collection is truly
intended. A fixed name bypasses Koan's physical-name folds, and the runtime reports that correction.

See [TECHNICAL.md](./TECHNICAL.md) for configuration, naming, health, and failure behavior.
