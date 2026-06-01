---
uid: reference.modules.Koan.data.connector.elasticsearch
title: Koan.Data.Connector.ElasticSearch - Technical Reference
description: Elasticsearch vector adapter for Koan — index provisioning, kNN search, and metadata filtering.
packages: [Sylin.Koan.Data.Connector.ElasticSearch]
source: src/Connectors/Data/ElasticSearch/
---

## Summary
- Adapter integrating Elasticsearch with the `Koan.Data.Vector` facade.
- Index provisioning, dense vector mappings, kNN queries, and metadata-filter translation.
- Health contributor and orchestration-aware option binding.

## Capabilities
- `VectorEnsureCreated`: creates `dense_vector` index mappings.
- Upsert/UpsertMany: bulk-friendly ingestion via `_bulk`.
- Delete/DeleteMany: immediate deletion with refresh control.
- Search: kNN query with a pushed-down metadata filter, optional timeout.
- Instructions: `data.ensureCreated`, `data.clear`.
- Health: `_cluster/health`.

## Metadata filtering

The `Filter` → Elasticsearch query-DSL translation lives in `SearchEngineFilterTranslator`
(`Koan.Data.SearchEngine`), shared with OpenSearch — both engines speak the same Apache Lucene query
DSL. The adapter exposes that one `VectorFilterCapabilities` constant and pushes the filter **into the
kNN query** (`knn.filter`), not as a sibling query, so the filter actually narrows the neighbour set.

- **Supported operators:** `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `StartsWith`,
  `EndsWith`, `Contains`, `Has`, `HasAny`, `HasAll`, `HasNone`, `Exists`.
- **Field targeting:** caller metadata is nested under the configured metadata field. String
  exact-match (`term`/`terms`) and wildcard target the dynamic `.keyword` sub-field; numeric range and
  `exists` target the bare field.
- **Null-inclusive negation:** `Ne`/`Nin`/`HasNone` use Lucene's null-inclusive `bool/must_not`, so
  rows missing the key are included — matching the convergence oracle.
- **Fail-loud:** an operator outside the declared set throws rather than degrading to match-all.

## Configuration Keys
- `Koan:Data:ElasticSearch:Endpoint`
- `Koan:Data:ElasticSearch:IndexPrefix`
- `Koan:Data:ElasticSearch:IndexName`
- `Koan:Data:ElasticSearch:Dimension`
- `Koan:Data:ElasticSearch:ApiKey` / `Username` / `Password`
- `Koan:Data:ElasticSearch:Similarity`
- `Koan:Data:ElasticSearch:Refresh`

## References
- `~/decisions/DATA-0097-vector-pathway-parity.md` — vector pathway + filter model
- `~/decisions/AI-0036-embedding-vector-seam.md` — §10.4 the ES/OS shared base
