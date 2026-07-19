# Sylin.Koan.Data.Connector.ElasticSearch

Elasticsearch vector storage and similarity search for Koan Entities. Reference the package, keep the application's
normal `AddKoan()` bootstrap, and use `Vector<TEntity>`; the connector owns discovery, index provisioning, native
kNN requests, filtering, naming, source isolation, and readiness participation.

- Target framework: net10.0
- License: Apache-2.0

## Install

> **Maturity:** This provider is available below the supported 0.20 boundary. Package presence is not a support
> claim; check the [generated product surface](https://github.com/sylin-org/Koan-framework/blob/main/docs/reference/product-surface.md).

```powershell
dotnet add package Sylin.Koan.Data.Connector.ElasticSearch
```

## Meaningful result

With Elasticsearch reachable, no provider registration or repository scaffold is required:

```csharp
builder.Services.AddKoan();

public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
}

float[] embedding = [0.12f, 0.42f, 0.88f];
await Vector<Article>.Save("article-1", embedding, new { category = "docs" });

var nearest = await Vector<Article>.Search(
    embedding,
    topK: 5,
    filter: Filter.Eq("category", "docs"));
```

When Elasticsearch is the intended vector provider, it participates in the shared automatic election policy. If
more than one durable provider is referenced, pin business-critical placement with `[VectorAdapter("elasticsearch")]`
or `Koan:Data:VectorDefaults:DefaultProvider`.

## Configuration

`auto` is the default and uses Koan's health-checked discovery pipeline, then the local endpoint fallback. Use exact
provider configuration for secured or explicitly placed clusters:

```json
{
  "ConnectionStrings": {
    "ElasticSearch": "https://search.example.net:9200"
  },
  "Koan": {
    "Data": {
      "ElasticSearch": {
        "ApiKey": "use-your-secret-provider",
        "IndexPrefix": "catalog",
        "Dimension": 1536
      }
    }
  }
}
```

`Koan:Data:ElasticSearch:Endpoint` is the exact endpoint alternative to the connection-string entry. HTTP Basic
credentials use `Username` and `Password`. Credentials belong in the platform's secret store.

## Capabilities

- automatic Elasticsearch `dense_vector` mapping and index provisioning;
- single and bulk vector upsert/delete, clear, count, scroll export, and index statistics;
- native Elasticsearch kNN search with caller-owned `topK`;
- metadata filtering pushed into `knn.filter`, with unsupported operators rejected rather than ignored;
- source, partition, container, and tenant-aware index naming;
- selection-aware readiness and redacted startup reporting.

## Boundaries

- This connector stores vector representations and metadata; it is not a general Elasticsearch Entity data adapter.
- Embedding retrieval and hybrid text/vector search are not supported.
- All named sources share the configured cluster endpoint and isolate through generated index names. Per-source
  cluster endpoints are not supported.
- Setting `IndexName` explicitly pins all contexts to one index and may defeat active isolation. Koan reports this
  conflict but honors the explicit name.
- Automatic discovery probes without credentials. Configure secured clusters explicitly.
- Elasticsearch index mappings fix the vector dimension at creation. Changing models normally requires a new index
  or a controlled reindex.

## References

- [Technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Connectors/Data/ElasticSearch/TECHNICAL.md)
- [Vector runtime](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Data.Vector/README.md)
