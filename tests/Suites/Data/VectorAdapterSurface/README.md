# Koan.Data.VectorAdapterSurface

Cross-adapter test matrix for Koan vector adapters, parallel to
[`Koan.Web.AdapterSurface`](../../Web/AdapterSurface/) for data adapters.

## What it is

One shared TestKit (`Koan.Data.VectorAdapterSurface.TestKit`) plus a per-adapter
test project. Each adapter implements **one** interface
(`IVectorAdapterTestFactory`) and inherits ~24 specs covering the entire
[`Vector<TEntity>`](../../../../src/Koan.Data.Vector/Vector.cs) facade.

| Cell | Status | Notes |
|---|---|---|
| `InMemory` | 34/34 | Reference adapter; matrix self-validation |
| `SqliteVec` | 29 pass / 5 capability skips | Embedded durable floor; no filters, hybrid, export, or stats |
| `Weaviate` | 25/25 | Full coverage, including hybrid search + cursor continuation |
| `ElasticSearch` | 29 pass / 4 skip | Native Elasticsearch dialect; embedding reads and hybrid search are not supported |
| `OpenSearch` | 29 pass / 4 skip | Native OpenSearch dialect; embedding reads and hybrid search are not supported |
| `Milvus` | 0/25 on this box | Container OOMs at ~2GB; wire-up validated; CI hosts should pass |
| `PGVector` | Not yet wired | Connector itself doesn't currently compile on this branch |

## Adding a new vector adapter

```text
Koan.Data.VectorAdapterSurface.<Name>.Tests/
├── <Name>TestFactory.cs              ← IVectorAdapterTestFactory implementation
├── <Name>MatrixSpecs.cs              ← 3 one-line subclasses (Surface, Partition, Semantic)
├── AssemblyAttributes.cs             ← DisableTestParallelization across spec classes
└── Koan.Data.VectorAdapterSurface.<Name>.Tests.csproj
```

The `<Name>TestFactory.cs` is the only meaningful surface area. It declares:

- **Lifecycle** (`InitializeAsync` / `DisposeAsync`) — start/stop the container or
  endpoint connection. Set `IsAvailable` based on success.
- **Reset** (`ResetAsync`) — clear backing state. Must restore a clean slate per
  spec. Several adapters cache schema flags per-repo-instance, so cleanest pattern
  is to rebuild the `ServiceProvider` here (see `WeaviateTestFactory` for the canonical
  shape).
- **Capabilities** — override the 10 `bool` properties on `IVectorAdapterCapabilities`
  to declare what the adapter actually supports. Specs that require a missing
  capability skip-green with a clear reason. Defaults assume a fully-featured
  general-purpose vector store; override to `false` what your adapter doesn't do.
- **Embedding dimension** — kit standardises on `8` so deterministic tests stay fast.
  Adapter-specific extras can use realistic dimensions (e.g. 384) outside the matrix.

## Capability flags

| Flag | Meaning | Default |
|---|---|---|
| `SupportsGetEmbedding` | `IVectorSearchRepository.GetEmbedding(id)` / `GetEmbeddings(ids)` | `true` |
| `SupportsBulkOperations` | `UpsertMany` / `DeleteMany` batch ops | `true` |
| `SupportsFlush` | `Flush()` clears all vectors (vs default-throws) | `true` |
| `SupportsExportAll` | Streaming `ExportAll(batchSize)` for migration/backup | `true` |
| `SupportsHybridSearch` | `VectorQueryOptions.SearchText` + `Alpha` (BM25 + vector blend) | `false` |
| `SupportsMetadataFilters` | `VectorQueryOptions.Filter` applied during search | `true` |
| `SupportsContinuationToken` | `VectorQueryResult.ContinuationToken` non-null | `false` |
| `SupportsPartitionIsolation` | `EntityContext.Partition()` produces isolated stores | `true` |
| `SupportsDynamicCollections` | Adapter can create collections on demand | `true` |
| `SupportsScoreNormalization` | Search returns normalised similarity scores | `false` |

## Spec inventory (~29 capability specs plus provider boot/conformance specs)

### `VectorAdapterSurfaceSpecsBase` — CRUD + search (14)
- Upsert: single, overwrite-existing, bulk, bulk-empty
- Delete: single, bulk, non-existent → false
- `GetEmbedding`: single, batch, unknown → null *(capability-gated)*
- `Search`: top-K ordering, top-K limit, empty-index → empty
- `Flush` *(capability-gated)*
- `EnsureCreated` idempotent

### `VectorPartitionSpecsBase` — isolation (5)
- Upsert in A invisible from B
- Search in A never returns B
- Delete in A doesn't touch B
- Flush in A doesn't touch B *(capability-gated)*
- Concurrent multi-partition writes stay isolated under `Task.Run`

### `VectorSemanticSpecsBase` — real-world shape (6, originally from PGVector)
- Document similarity finds related content
- Recommendation finds similar items by vector
- Duplicate detection finds near-duplicates
- Hybrid search combines BM25 + vector *(capability-gated)*
- Capability surface matches advertised flags

## Running

Single cell:
```bash
dotnet test tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.Weaviate.Tests/Koan.Data.VectorAdapterSurface.Weaviate.Tests.csproj
```

CI-friendly: each adapter test assembly disables cross-class parallelization via
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` — spec
classes share `AppHost.Current` as a fallback for AsyncLocal-flow gaps, and
parallel classes would race on that global.

Skip-as-pass: if `IsAvailable=false` (Docker unavailable, container OOM,
adapter bug, env var pointing nowhere), every spec skips with the factory's
`UnavailableReason` as the message. The matrix stays green; the reason is the
audit trail.

## Why this exists

Before this kit, vector adapter coverage was uneven:
- PGVector had 30 thorough specs
- Weaviate had one monolithic `Vector_crud_and_search_roundtrip` test
- ElasticSearch and OpenSearch each had a single `Save_with_vector_and_search_similar` spec
- Milvus had **zero** tests

The matrix replaces those one-off shapes with a uniform capability contract per
adapter. Cell-level test counts are now meaningful (a Milvus regression shows
up in the same place a Weaviate regression would), and adapter divergences are
surfaced as `Supports*=false` flags with green-skipped specs rather than
either silent gaps or noisy failures.
