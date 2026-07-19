---
type: REF
domain: data
title: "Vector — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: docs/reference/cards/vector.md
---

# Vector — pillar map

> One-screen map of the Vector pillar — semantic / KNN search over entity embeddings. Full detail: [ai-vector-howto.md](../../guides/ai-vector-howto.md).

**What it does** — Stores and searches embedding vectors behind one facade, `Vector<TEntity>`. The same `Vector<Media>.Search(...)` code runs on any vector store; the adapter is chosen by **package reference** — add `Koan.Data.Vector.Connector.Weaviate` (or `…Milvus` / `…Qdrant`; ElasticSearch / OpenSearch ship vector support in their general `Koan.Data.Connector.ElasticSearch` / `Koan.Data.Connector.OpenSearch` packages) and it activates (Reference = Intent). Pair an entity with `[Embedding]` so the text-to-vector step is automatic, then search by vector. `Vector<T>` is the user-facing facade; `VectorData<T>` is the persistence engine it delegates into — distinct layers, not twins.

## The one canonical pattern

Mark the entity with `[Embedding]` (the text that gets vectorized), then search by a query vector. The facade verbs are **Save / Search** (`Search` is the canonical read; metadata filters push down to the store when the adapter can).

```csharp
[Embedding(Properties = new[] { nameof(Title), nameof(Synopsis) })]
public sealed class Media : Entity<Media>
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
}

await media.Save();                                  // entity + embedding lifecycle; Async=true defers the vector write

var hits = await Vector<Media>.Search(
    vector: queryVector,                             // float[] — the query embedding
    text: "cozy sci-fi",                             // optional → hybrid BM25 + vector
    topK: 20,                                        // exact positive request; omitted defaults to 10
    filter: vectorFilter);                           // optional metadata filter (pushed down)

foreach (var m in hits.Matches) { /* m.Id, m.Score */ }
```

`Vector<T>` is pinned to `IEntity<string>` keys. Scope a query to a partition with `Vector<Media>.WithPartition("tenant-123")`; inspect adapter support with `Vector<Media>.GetCapabilities()`.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[Embedding(Properties=…)]` · `[Embedding(Template="{Title}\n{Body}")]` | Mark the text that gets embedded; `Properties`, `Template`, or `Policy` chooses the source (`Koan.Data.AI`). |
| `[EmbeddingIgnore]` | Exclude a property from `Policy`-based auto-discovery embedding. |
| `[EmbedStorage(Partition=…, Source=…)]` | Override where async `EmbedJob` work-items are stored. |
| `[VectorAdapter("weaviate")]` | Route this entity to a specific vector provider when more than one is referenced. |

## The escape hatch

Drop to the raw repository — `IVectorSearchRepository<TEntity, TKey>` — for direct upsert/search/export without the facade or the embedding pipeline:

```csharp
var repo = sp.GetRequiredService<IVectorService>()
             .TryGetRepository<Media, string>();
await repo!.Upsert(id, embedding, metadata);
var result = await repo.Search(new VectorQueryOptions(Query: queryVector, TopK: 20));
await foreach (var batch in repo.ExportAll()) { /* provider migration */ }
```

The repository exposes `Upsert` / `Delete` / `Search` / `Flush` / `ExportAll` and the `TKey`-generic key the facade hides. The connector is present only when you reference that adapter (Reference = Intent). The shared Lucene-DSL translator behind the ElasticSearch / OpenSearch connectors is documented in [DATA-0103](../../decisions/DATA-0103-search-engine-shared-core.md).

Referencing a connector makes it available for election; it does not by itself make the external service a readiness dependency. Once repository resolution selects a provider for an entity/source route, that connector becomes critical and `/health/ready` probes it. Startup and health facts therefore distinguish optional capability presence from an active application dependency.

`VectorQueryOptions` owns the one `TopK` policy: omitted means 10 and non-positive values fail immediately. Adapters
receive an explicit positive value unchanged; they never substitute a preferred page size or silently cap application
intent. A provider may still reject a value outside its native deployment limits.

## The sample that shows it

[`GardenCoop Chapter 2`](../../../samples/journeys/GardenCoop/02-LocalDiscovery/) — `[Embedding]` on `Produce` plus local ONNX and sqlite-vec ranks a natural-language query without an external service.
