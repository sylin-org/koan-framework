# Sylin.Koan.Data.Vector

Vector search facade for Koan: first-class static APIs over vector providers.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Provider-agnostic save/search for embeddings
- Works with Weaviate and other adapters via abstractions

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector
```

## Example

```csharp
// Save and search embeddings for an entity with string key
await Vector<MyEntity>.Save("id-1", embedding);
var res = await Vector<MyEntity>.Search(query: embedding, topK: 5);
```

## Links
- Data access patterns: `~/guides/data/all-query-streaming-and-pager.md`
