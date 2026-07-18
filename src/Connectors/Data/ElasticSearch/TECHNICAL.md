---
uid: reference.modules.Koan.data.connector.elasticsearch
title: Koan.Data.Connector.ElasticSearch - Technical Reference
description: Elasticsearch vector provider for Koan with native kNN, shared search-engine mechanics, and selection-aware readiness.
packages: [Sylin.Koan.Data.Connector.ElasticSearch]
source: src/Connectors/Data/ElasticSearch/
---

## Role

This package makes the `elasticsearch` vector provider available by reference. Its thin provider assembly owns the
Elasticsearch identity, aliases, orchestration metadata, discovery vocabulary, and native `dense_vector`/top-level
`knn` dialect. `Sylin.Koan.Data.SearchEngine` owns the common repository, configuration, health, naming, and REST
mechanics shared with OpenSearch.

No Elasticsearch-specific service registration is required in application code.

## Election and lifecycle

`ElasticSearchVectorAdapterFactory` is a provider candidate with priority 20 and alias `elastic`. An exact
`[VectorAdapter("elasticsearch")]` request or `Koan:Data:VectorDefaults:DefaultProvider` pin wins over automatic
selection and fails if unavailable; Koan does not substitute a different provider.

Reference makes the connector available but not critical. Before a runtime Entity/source selects it, health is
`Unknown`, non-critical, and connection-free. Selection records provider/source participation; subsequent health
checks probe `/_cluster/health` with the same endpoint and authentication used by repository operations.

## Configuration precedence

Endpoint resolution uses the first exact value:

1. `ConnectionStrings:ElasticSearch`
2. `Koan:Data:ElasticSearch:ConnectionString`
3. `Koan:Data:ElasticSearch:Endpoint`
4. autonomous discovery and then `http://localhost:9200`

There is no generic Data connection-string alias. Other keys under `Koan:Data:ElasticSearch` are `IndexPrefix`,
`IndexName`, `VectorField`, `MetadataField`, `IdField`, `SimilarityMetric`, `RefreshMode`, `TimeoutSeconds`,
`Dimension`, `ApiKey`, `Username`, `Password`, `DisableIndexAutoCreate`, and `DisableAutoDetection`.

Startup facts show the effective de-identified endpoint and common vector/index choices. Credentials are never
projected.

## Repository behavior

The shared repository provides automatic index ensure, upsert/bulk upsert, delete/bulk delete, clear, count, scroll
export, statistics, and kNN search. The Elasticsearch dialect emits a `dense_vector` mapping and Elasticsearch 8.x
top-level `knn` request. The caller's `topK` is passed through; the adapter does not invent or reduce it.

The unified `Filter` AST is translated once in `Sylin.Koan.Data.SearchEngine` and placed in `knn.filter`. Supported
operators are `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `StartsWith`, `EndsWith`, `Contains`, `Has`,
`HasAny`, `HasAll`, `HasNone`, and `Exists`. Unsupported operators fail before a misleading match-all result.

## Naming and isolation

The factory passes the provider selected by Vector into Koan's central naming chokepoint; the repository does not
re-elect naming during an operation. Generated index names include active source, partition, container, and tenant
contributors. All sources use the one configured cluster endpoint.

`IndexName` is an explicit physical-name pin. Koan honors it and reports when it defeats active isolation. Per-source
cluster endpoints, embedding retrieval, hybrid text/vector search, and snapshot-consistent scroll guarantees are not
part of this provider contract.

## Validation surface

The provider-owned matrix exercises boot/configuration, pre-use readiness, index ensure, CRUD/bulk behavior, search,
filtering, partition isolation, export, clear, and semantic shapes against Elasticsearch 8.13.4. Capability-gated
tests record embedding retrieval and hybrid search as unsupported.

## References

- Shared implementation: `~/reference/modules/Koan/data/searchengine`
- DATA-0097 Vector Pathway Parity: `~/decisions/DATA-0097-vector-pathway-parity.md`
