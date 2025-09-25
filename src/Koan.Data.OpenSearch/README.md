# Sylin.Koan.Data.OpenSearch

OpenSearch adapter for Koan vector data.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Automatic index provisioning with dense vector mappings
- kNN vector search with metadata filtering and bulk operations
- Health checks and orchestration-aware configuration

## Install

```powershell
dotnet add package Sylin.Koan.Data.OpenSearch
```

## Example

```csharp
await Vector<MyDocument>.Save("doc-1", embedding, metadata: new { category = "docs" });
var results = await Vector<MyDocument>.Search(
    query: embedding,
    topK: 5,
    filter: VectorFilterExpression.From<MyDocument>(d => d.Category == "docs"));
```

## Links
- Vector filtering expressions: `~/guides/data/vector-filtering.md`
