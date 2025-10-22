# Sylin.Koan.Data.Vector

Vector search facade for Koan: workflow-driven APIs over vector providers.

- Target framework: net10.0
- License: Apache-2.0

## Capabilities
- Provider-agnostic save/search for embeddings
- Workflow profiles with declarative defaults (topK, alpha, metadata enrichers)
- Works with Weaviate and other adapters via abstractions

## Install

```powershell
dotnet add package Sylin.Koan.Data.Vector
```

## Example

```csharp
// Register a profile once during startup
VectorProfiles.Register(builder => builder
	.For<MyEntity>("recs.default")
		.TopK(12)
		.Alpha(0.45)
		.WithMetadata(meta => meta["pipeline"] = "recommendations"));

// Save embeddings using the workflow (document + vector persistence)
var entity = new MyEntity { Id = "id-1", Name = "Demo" };
await VectorWorkflow<MyEntity>.For("recs.default")
	.Save(entity, embedding);

// Execute a hybrid query with profile defaults
var results = await VectorWorkflow<MyEntity>.For("recs.default")
	.Query(vector: embedding, text: "annual revenue");
```

## Links
- Data access patterns: `~/guides/data/all-query-streaming-and-pager.md`
