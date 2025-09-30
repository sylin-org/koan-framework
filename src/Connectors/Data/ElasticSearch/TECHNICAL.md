title: Koan.Data.Connector.ElasticSearch - Technical Reference
description: Elasticsearch adapter for Koan vector data.
packages: [Sylin.Koan.Data.Connector.ElasticSearch]
source: src/Koan.Data.Connector.ElasticSearch/

## Summary
- Adapter integrating Elasticsearch with Koan.Data.Vector facade
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
- `Koan:Data:ElasticSearch:Endpoint`
- `Koan:Data:ElasticSearch:IndexPrefix`
- `Koan:Data:ElasticSearch:IndexName`
- `Koan:Data:ElasticSearch:Dimension`
- `Koan:Data:ElasticSearch:ApiKey` / `Username` / `Password`
- `Koan:Data:ElasticSearch:Similarity`
- `Koan:Data:ElasticSearch:Refresh`

