# Sylin.Koan.Data.Vector.Connector.Milvus

Milvus adapter for Koan vector data.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities
- Creates collections with a dense vector schema and JSON metadata fields
- Bulk upsert and delete via the Milvus REST API
- Hybrid similarity search with operator-aware metadata filters pushed into the query

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Milvus
```

## Example

```csharp
using Koan.Data.Abstractions.Filtering;

await Vector<MyDoc>.Save("doc-1", embedding, metadata: new { category = "support" });

// Metadata filter pushed into the search (the unified Filter AST).
var results = await Vector<MyDoc>.Search(
    embedding,
    topK: 10,
    filter: Filter.Eq("category", "support"));
```

The `filter` argument also accepts the JSON metadata-filter DSL or a dictionary; all three are parsed
by the one schemaless reader. An operator the adapter cannot push down is a hard error, never a silent
match-all.

## Links
- Vector pathway + filter model: `~/decisions/DATA-0097-vector-pathway-parity.md`
- Milvus REST API reference: https://milvus.io/docs
