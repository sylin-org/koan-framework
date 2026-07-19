---
name: koan-vector
description: Semantic / KNN search over entity embeddings — [Embedding] class attribute, Vector<T>.Search(float[]) over Weaviate/Qdrant/Milvus, precompute query vectors with Koan.AI.Client.Embed, and zero-AI-cost provider migration via IVectorSearchRepository.ExportAll/Upsert
pillar: vector
card: docs/reference/cards/vector.md
status: current
last_validated: 2026-07-16
---

# Koan Vector

## Trigger this skill when you see

- `[Embedding]` on an entity, `[EmbeddingIgnore]`, `[DataAdapter("weaviate")]` vector routing
- `Vector<T>.Search(...)`, `Vector<T>.Save(...)`, `Vector<T>.WithPartition(...)`, `Vector<T>.GetCapabilities()`
- `Koan.AI.Client.Embed(text)` to turn raw text into a query vector
- `VectorQueryResult<string>` / `VectorMatch<string>` (`.Matches`, `.Id`, `.Score`)
- `IVectorSearchRepository<T,K>` — `Upsert` / `ExportAll` / `Search` / `Flush`, `IVectorService.TryGetRepository<T,K>()`
- References to `Koan.Data.Vector` / `Koan.Data.Vector.Connector.Weaviate` / `…Qdrant` / `…Milvus`
- "semantic search", "KNN", "vector store", "hybrid BM25 + vector", "embedding migration", "re-embed everything"

## Core principle

**The entity owns the embedding; the package reference chooses the store.** Mark a class with `[Embedding]` to declare *which text gets vectorized* — `Save` then indexes automatically, inline by default or through the durable worker when `Async = true`. Search takes a **precomputed query vector** (`float[]`), not a string: embed the query yourself with `Koan.AI.Client.Embed(text)`, then call `Vector<T>.Search(vector, ...)`. The same code runs on Weaviate, Qdrant, or Milvus — the adapter activates by **package reference** (Reference = Intent), so swapping stores is a reference change, not a code change. For provider migration, the raw repository's `ExportAll` / `Upsert` move vectors store-to-store with **zero AI re-embedding calls**.

<!-- validate -->
```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI;
using Koan.Data.Abstractions;
using Koan.Data.AI.Attributes;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;

[Embedding(Properties = new[] { nameof(Media.Title), nameof(Media.Synopsis) })]  // text that gets vectorized
[DataAdapter("weaviate")]                                                         // route this entity to a vector store
public sealed class Media : Entity<Media>
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
}

public sealed class SemanticSearch
{
    // Embed the raw query text, then search by the precomputed vector (Search takes float[], never a string).
    public async Task<IReadOnlyList<VectorMatch<string>>> Find(string query, CancellationToken ct = default)
    {
        float[] queryVector = await Client.Embed(query, ct);          // Koan.AI raw-text → embedding

        VectorQueryResult<string> hits = await Vector<Media>.Search(
            vector: queryVector,                                       // the query embedding
            text: query,                                              // optional → hybrid BM25 + vector
            alpha: 0.5,                                               // semantic vs keyword weight
            topK: 20,                                                 // how many matches
            ct: ct);

        return hits.Matches;                                          // each: .Id, .Score, .Metadata
    }

    // Save persists the entity AND runs its [Embedding] lifecycle — no manual vector wiring.
    public Task Index(Media media, CancellationToken ct = default) => media.Save(ct);

    // Provider migration: pull vectors straight from the store, re-upsert — zero AI re-embedding.
    public async Task Migrate(IVectorService vectors, CancellationToken ct = default)
    {
        IVectorSearchRepository<Media, string>? source = vectors.TryGetRepository<Media, string>();
        if (source is null) return;

        using (EntityContext.Adapter("qdrant"))                       // scope the destination store
        {
            IVectorSearchRepository<Media, string>? dest = vectors.TryGetRepository<Media, string>();
            if (dest is null) return;

            await foreach (VectorExportBatch<string> batch in source.ExportAll(batchSize: 200, ct))
                await dest.Upsert(batch.Id, batch.Embedding, batch.Metadata, ct);   // reuse stored vector
        }
    }
}
```

## Reference = Intent activation table

| Add this reference | Effect |
|---|---|
| `Koan.Data.Vector` | The `Vector<T>` facade + `IVectorService` contract (no store yet). |
| `+ Koan.Data.AI` | `[Embedding]` pipeline — `Save` auto-vectorizes the declared text. |
| `+ Koan.Data.Vector.Connector.Weaviate` | Weaviate vector store activates (GraphQL, hybrid search). |
| `+ Koan.Data.Vector.Connector.Qdrant` | Qdrant vector store activates. |
| `+ Koan.Data.Vector.Connector.Milvus` | Milvus vector store activates. |
| `+ Koan.Data.Connector.ElasticSearch` / `…OpenSearch` | Vector support ships inside the general search-engine connector (shared Lucene-DSL core, DATA-0103). |

## The embedding surface

| Declare this | Effect |
|---|---|
| `[Embedding(Properties = new[] { ... })]` | Explicit list of properties whose text is embedded (`Koan.Data.AI.Attributes`). |
| `[Embedding(Template = "{Title}\n{Synopsis}")]` | Compose embedded text from a template (precedence: `Template` > `Properties` > `Policy`). |
| `[Embedding(Async = true)]` | Defer vectorization to a background worker instead of blocking `Save`. |
| `[EmbeddingIgnore]` on a property | Exclude that property from `Policy`-based auto-discovery (`AttributeTargets.Property`). |
| `[DataAdapter("weaviate")]` on the entity | Route this entity to a named vector provider when more than one is referenced. |

`[Embedding]` is a **class-level** attribute — there is no per-property `[VectorField]`. `Vector<T>` is pinned to `IEntity<string>` keys; scope a query with `Vector<Media>.WithPartition("tenant-123")` and inspect adapter support with `Vector<Media>.GetCapabilities()`.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| `[VectorField]` on a property | `[Embedding(Properties = new[] { ... })]` on the **class** — there is no per-property attribute. |
| `Vector<Media>.SearchAsync("cozy sci-fi")` | `Vector<Media>.Search(float[] vector, ...)` — embed the text first via `Client.Embed`. No string overload, no `…Async` suffix. |
| `Vector<Media>.Search(text: "...")` with no `vector:` | `vector` is required; pass the precomputed embedding. `text` is only the optional hybrid-BM25 keyword channel. |
| `repo.ExportAllAsync(...)` | `repo.ExportAll(int? batchSize, ct)` → `IAsyncEnumerable<VectorExportBatch<TKey>>` (no `…Async` suffix). |
| Re-running `Client.Embed` on every row during a store-to-store migration | Reuse the stored vector: `ExportAll` → `Upsert(batch.Id, batch.Embedding, ...)`. Migration costs zero AI calls. |
| `EmbeddingCache` referenced as a framework type | It is a **sample-only** type (S7.Meridian) — not part of the framework surface. |
| `pgvector` / `Pinecone` connector | Live vector connectors are **Weaviate / Qdrant / Milvus** (ElasticSearch / OpenSearch via their general connectors). |
| `Task<List<VectorMatch<string>>>` return type | `IReadOnlyList<VectorMatch<string>>` — `VectorQueryResult.Matches` is read-only. |
| `new Vector<Media>()` / injecting `IVectorSearchRepository<T,K>` | `Vector<T>` is a static facade; drop to the repo only via `IVectorService.TryGetRepository<T,K>()`. |

## Escape hatches

- **Raw repository** — drop past the facade for direct upsert/search/export: `sp.GetRequiredService<IVectorService>().TryGetRepository<Media, string>()` exposes `Upsert` / `UpsertMany` / `Delete` / `Search(VectorQueryOptions)` / `Flush` / `ExportAll`. Returns `null` when no vector connector is referenced.
- **Destination routing for migration** — wrap the import in `using (EntityContext.Adapter("qdrant"))` (or `.Source(...)` / `.Partition(...)`, DATA-0077). **Source XOR Adapter** — supplying both throws.
- **Hybrid search** — pass `text:` alongside `vector:` and tune `alpha` (0.0 keyword-only … 1.0 semantic-only); the metadata `filter:` (Koan JSON filter DSL) pushes down to the store when the adapter supports it.
- **Agentic / RAG orchestration** — the higher-level RAG + agent surface that *consumes* `Vector<T>` is real but **migrating out of Koan to Agyo** ([ARCH-0089](../../../docs/decisions/ARCH-0089-ai-pillar-dissolution.md)). Build new RAG pipelines against Agyo; Koan keeps the entity-AI core (`[Embedding]`, `Vector<T>`, `EntityAi`, `Client`).

## See also

- [Reference card: vector.md](../../../docs/reference/cards/vector.md) — one-screen pillar map
- [AI & vector how-to](../../../docs/guides/ai-vector-howto.md) — embeddings, search, migration walkthrough
- [GardenCoop Local Discovery](../../../samples/journeys/GardenCoop/02-LocalDiscovery/README.md) — `[Embedding]` plus `Vector<Produce>.Search` for local semantic discovery
- [DATA-0078 — vector export capabilities](../../../docs/decisions/DATA-0078-vector-export-capabilities.md)
- [DATA-0103 — search-engine shared core (ES/OS vector)](../../../docs/decisions/DATA-0103-search-engine-shared-core.md) · [ARCH-0089 — AI pillar dissolution](../../../docs/decisions/ARCH-0089-ai-pillar-dissolution.md)
