# Sylin.Koan.Data.Vector.Connector.Milvus

Use Milvus behind Koan's entity-first vector API. Referencing this package activates the adapter; `AddKoan()` handles
registration, provider election, naming, health participation, and startup reporting.

> **Maturity:** Supported 0.20 extension within the capabilities and boundaries below.

## Install and use

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Milvus
```

```csharp
public sealed class Article : Entity<Article> { }

await Vector<Article>.Save(article.Id, embedding, new { category = "support" });
var matches = await Vector<Article>.Search(embedding, topK: 12);
```

No Milvus-specific registration is required. The zero-configuration endpoint is `http://localhost:19530`. Configure
another deployment with `Koan:Data:Milvus:Endpoint`; use `Token` or `Username`/`Password` when authentication is
enabled.

The first write establishes vector dimension. Set `Koan:Data:Milvus:Dimension` only when an empty collection must be
created before the first write. Koan defaults `topK` to 10; an explicit positive value is sent unchanged.

## Honest capability boundary

Milvus supports KNN search, native metadata-filter pushdown, bulk writes/deletes, collection clear, dynamic collection
naming, and normalized cosine scores. The current adapter does not provide embedding reads, export, hybrid text
search, search continuation tokens, or index statistics. Delete visibility follows the configured Milvus consistency
posture and is not claimed as immediately observable.

Avoid pinning `CollectionName` in partitioned or tenant-isolated applications unless one shared collection is truly
intended. A fixed name bypasses Koan's physical-name folds, and the runtime reports that correction.

See [TECHNICAL.md](./TECHNICAL.md) for configuration, deployment shape, health, and failure behavior.
