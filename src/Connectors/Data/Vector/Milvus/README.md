# Sylin.Koan.Data.Vector.Connector.Milvus

Milvus adapter for Koan vector data.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Creates collections with dense vector schema and JSON metadata fields
- Bulk upsert and delete support using Milvus REST API
- Hybrid similarity search with expression-based metadata filters

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector.Connector.Milvus
```

## Example

```csharp
await Vector<MyDoc>.Save(Guid.NewGuid(), embedding, metadata: new { category = "support" });
var results = await Vector<MyDoc>.Search(
    query: embedding,
    topK: 10,
    filter: VectorFilterExpression.From<MyDoc>(d => d.Category == "support"));
```

## Links
- Milvus REST API reference: https://milvus.io/docs

