---
type: REFERENCE
domain: ai
title: "Store and search Entity vectors"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: Entity embedding lifecycle, vector provider election, bounded search, and inspection
---

# Store and search Entity vectors

Use Koan Vector when an application must find Entities by semantic or nearest-neighbor similarity.
`Vector<TEntity>` keeps the query independent of the chosen store; the referenced connector owns its
schema, wire protocol, filtering, consistency, and operating limits.

## Smallest useful result

Mark which Entity text should be embedded, then save and search:

```csharp
[Embedding(Properties = new[] { nameof(Title), nameof(Synopsis) })]
public sealed class Media : Entity<Media>
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
}

await media.Save(ct);

var queryVector = await Client.Embed("cozy science fiction", ct);
var hits = await Vector<Media>.Search(queryVector, topK: 20, ct: ct);
```

Reference `Sylin.Koan.Data.Vector` plus one connector. In-memory provides an ephemeral local floor;
`SqliteVec` adds embedded durability; Qdrant, Milvus, and Weaviate add external-service choices.
The [product surface](../product-surface.md) is the authority for current package maturity.

## Selection and cost

- `[VectorAdapter("weaviate")]` pins an Entity when more than one vector connector is available.
- `Vector<T>.WithPartition(...)` scopes a query to one logical partition.
- `topK` is an exact positive caller request; omitted means 10. Connectors do not silently substitute
  a preferred page size, though a provider may reject values outside its native limits.
- Metadata-filter pushdown and hybrid text/vector search are provider capabilities, not universal
  behavior.
- Connector availability alone is non-critical. Resolution of an Entity/source route makes that
  provider participating and therefore part of readiness.

Use `Vector<T>.GetCapabilities()`, startup facts, `/health/ready`, and runtime facts to inspect the
selected provider and supported operations. An unavailable configured provider or unsupported
operation rejects with a correction rather than falling back to another store.

## Embedding lifecycle

`[Embedding]` opts ordinary Entity saves into vector synchronization. `Async = true` delegates the
work to a captured-context job; it does not mutate a `float[]` property on the Entity. For an explicit
one-off transformation, use `EntityAi.Embed(entity)`. For migrations or rebuilds, use the supported
embedding migrator rather than inventing a second indexing pipeline.

The [AI reference](index.md) owns provider-neutral model operations. The
[AI and vector guide](../../guides/ai-vector-howto.md) owns multi-step embedding and migration tasks.
