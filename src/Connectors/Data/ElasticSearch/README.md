# Sylin.Koan.Data.Connector.ElasticSearch

Elasticsearch vector storage for Koan Entities. The application declares one vector space; the connector realizes that
plan through Elasticsearch `dense_vector`, native kNN search, and native metadata pre-filtering.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.ElasticSearch
```

## Use

```csharp
builder.Services.AddKoan(koan => koan.Data
    .Source("Search")
    .Vector<Article>(space => space
        .Name("content")
        .Dimensions(1536)
        .Metric(VectorMetric.Cosine)
        .Visibility(VectorVisibility.Session)));

await Vector<Article>.Save("article-1", embedding, new { category = "docs" });

var nearest = await Vector<Article>.Search(
    embedding,
    query => query.Top(5).Where(Filter.Eq("category", "docs")));
```

The space declaration owns its name, dimensions, metric, model, source, and visibility. Standard source declarations
own access and storage lifecycle. Elasticsearch configuration owns only placement, credentials, timeouts, and bounded
work limits.

## Configuration

```json
{
  "Koan": {
    "Data": {
      "ElasticSearch": {
        "Endpoint": "https://search.example.net:9200",
        "ApiKey": "use-your-secret-provider",
        "TimeoutSeconds": 30,
        "MaxSearchCandidates": 10000
      }
    }
  }
}
```

HTTP Basic authentication uses `Username` and `Password`. `ConnectionStrings:ElasticSearch` remains an endpoint
alternative. Standard named-source routing can select a different endpoint and credentials without changing Entity
code. Keep credentials in the platform secret store.

## Guarantees

- complete point reads return logical identity, embedding, and neutral metadata;
- Cosine, Euclidean, and unrestricted DotProduct spaces have explicit native mappings and normalized higher-is-closer
  scores;
- awaited mutations use explicit refresh and are visible to subsequent Session reads;
- `Eq`, `Ne`, ranges, `In`, `Nin`, `Has`, `HasAny`, `HasAll`, `HasNone`, `Size`, and `Exists` run as native kNN
  pre-filters; unsupported filters fail closed;
- single and bulk upsert/delete preserve ordered outcomes; bulk atomicity is reported as not guaranteed;
- Managed sources may create storage, External sources validate only, and ReadOnly sources reject mutations before I/O;
- source, partition, container, and row scopes isolate every read and mutation path;
- requests, responses, metadata, batches, candidates, retries, and tie expansion are bounded.

## Deliberate limits

- This is a Vector adapter, not a general Elasticsearch Entity adapter.
- Search accuracy is approximate because Elasticsearch uses indexed approximate kNN.
- Eventual visibility, hybrid text search, continuation snapshots, streaming export, and atomic batches are not claimed.
- The adapter generates and validates its write alias and backing index. There is no ordinary index-name, field-name,
  dimension, metric, refresh, or auto-create option.
- Existing external mappings must carry the Koan contract marker and match the declared space exactly.

## Reference

- [Technical reference](https://github.com/sylin-org/Koan-framework/blob/main/src/Connectors/Data/ElasticSearch/TECHNICAL.md)
- [Vector runtime](https://github.com/sylin-org/Koan-framework/blob/main/src/Koan.Data.Vector/README.md)
