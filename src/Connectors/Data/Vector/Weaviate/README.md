# Sylin.Koan.Data.Vector.Connector.Weaviate

Weaviate adapter for Koan vector data.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Weaviate
```

## Capabilities
- Save/search embeddings for entities via the `Koan.Data.Vector` facade
- Weaviate client options and class mapping helpers
- Operator-aware metadata filtering pushed into the query. Weaviate is the intentionally **reduced**
  reference adapter: it declares a smaller operator set (notably no `In`), and an operator outside that
  set is a hard error rather than a silent match-all.

## Example

```csharp
using Koan.Data.Abstractions.Filtering;

await Vector<MyDoc>.Save("doc-1", embedding, metadata: new { category = "support" });

var results = await Vector<MyDoc>.Search(
    embedding,
    topK: 10,
    filter: Filter.Eq("category", "support"));
```

## Links
- Vector pathway + filter model: `~/decisions/DATA-0097-vector-pathway-parity.md`
- Data access patterns: `~/guides/data/all-query-streaming-and-pager.md`
