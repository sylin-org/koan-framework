title: Koan.Data.OpenSearch - Technical Reference
description: OpenSearch adapter for Koan vector data.
packages: [Sylin.Koan.Data.OpenSearch]
source: src/Koan.Data.OpenSearch/

## Summary
- Adapter integrating OpenSearch with Koan.Data.Vector facade
- Index provisioning, dense vector mappings, kNN queries, filter translation
- Health contributor and orchestration-aware option binding

## Capabilities
- VectorEnsureCreated: creates dense_vector index mappings
- Upsert/UpsertMany: bulk friendly ingestion via `_bulk`
- Delete/DeleteMany: immediate deletion with refresh control
- Search: kNN query + bool filter support, optional timeout
- Instructions: `data.ensureCreated`, `data.clear`
- Health: `_cluster/health`

## Configuration Keys
- `Koan:Data:OpenSearch:Endpoint`
- `Koan:Data:OpenSearch:IndexPrefix`
- `Koan:Data:OpenSearch:IndexName`
- `Koan:Data:OpenSearch:Dimension`
- `Koan:Data:OpenSearch:ApiKey` / `Username` / `Password`
- `Koan:Data:OpenSearch:Similarity`
- `Koan:Data:OpenSearch:Refresh`
