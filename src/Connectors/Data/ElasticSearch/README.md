# Sylin.Koan.Data.Connector.ElasticSearch

Elasticsearch adapter for Koan vector data.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities
- Automatic index provisioning with dense vector mappings
- kNN vector search with bulk operations
- Operator-aware metadata filtering pushed into the kNN query — the `Filter` → query-DSL translation
  is shared with OpenSearch via `Koan.Data.SearchEngine`
- Health checks and orchestration-aware configuration

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.ElasticSearch
```

## Example

```csharp
using Koan.Data.Abstractions.Filtering;

await Vector<MyDocument>.Save("doc-1", embedding, metadata: new { category = "docs" });

// Metadata filter pushed into the kNN query (the unified Filter AST).
var results = await Vector<MyDocument>.Search(
    embedding,
    topK: 5,
    filter: Filter.Eq("category", "docs"));
```

The `filter` argument also accepts the JSON metadata-filter DSL (`{ "category": "docs" }`) or a
dictionary; all three are parsed by the one schemaless reader. An operator the adapter cannot push
down is a hard error, never a silent match-all.

## Links
- Vector pathway + filter model: `~/decisions/DATA-0097-vector-pathway-parity.md`
- Embedding↔vector seam: `~/decisions/AI-0036-embedding-vector-seam.md`
