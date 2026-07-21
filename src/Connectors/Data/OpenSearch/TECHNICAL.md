---
uid: reference.modules.Koan.data.connector.opensearch
title: Koan.Data.Connector.OpenSearch - Technical Reference
description: OpenSearch vector provider for Koan with native kNN, shared search-engine mechanics, and selection-aware readiness.
packages: [Sylin.Koan.Data.Connector.OpenSearch]
source: src/Connectors/Data/OpenSearch/
---

## Role

This package makes the `opensearch` vector provider available by reference. Its thin provider assembly owns the
OpenSearch identity, orchestration metadata, discovery vocabulary, and native `knn_vector`/`query.knn` dialect.
`Sylin.Koan.Data.SearchEngine` owns the common repository, configuration, health, naming, and REST mechanics shared
with Elasticsearch.

No OpenSearch-specific service registration is required in application code.

## Election and lifecycle

`OpenSearchVectorAdapterFactory` is a provider candidate with priority 20. An exact
`[VectorAdapter("opensearch")]` request or `Koan:Data:VectorDefaults:DefaultProvider` pin wins over automatic
selection and fails if unavailable; Koan does not substitute a different provider.

Reference makes the connector available but not critical. Before a runtime Entity/source selects it, health is
`Unknown`, non-critical, and connection-free. Selection records provider/source participation; subsequent health
checks probe `/_cluster/health` with the same endpoint and authentication used by repository operations.

## Configuration precedence

Endpoint resolution uses the first exact value:

1. `ConnectionStrings:OpenSearch`
2. `Koan:Data:OpenSearch:ConnectionString`
3. `Koan:Data:OpenSearch:Endpoint`
4. autonomous discovery and then `http://localhost:9200`

There is no generic Data connection-string alias. Other keys under `Koan:Data:OpenSearch` are `IndexPrefix`,
`IndexName`, `VectorField`, `MetadataField`, `IdField`, `SimilarityMetric`, `RefreshMode`, `TimeoutSeconds`,
`Dimension`, `ApiKey`, `Username`, `Password`, `DisableIndexAutoCreate`, and `DisableAutoDetection`.

Startup facts show the effective de-identified endpoint and common vector/index choices. Credentials are never
projected.

## Repository behavior

The shared repository provides automatic index ensure, upsert/bulk upsert, delete/bulk delete, clear, count, scroll
export, statistics, and kNN search. The OpenSearch dialect emits an index-level kNN setting, `knn_vector` mapping,
and OpenSearch 2.x `query.knn.<field>` request. The caller's `topK` is passed through; the adapter does not invent or
reduce it.

The unified `Filter` AST is translated once in `Sylin.Koan.Data.SearchEngine` and placed in the native kNN filter.
Supported operators are `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `StartsWith`, `EndsWith`, `Contains`,
`Has`, `HasAny`, `HasAll`, `HasNone`, and `Exists`. Unsupported operators fail before a misleading match-all result.

## Naming and isolation

The factory passes the provider selected by Vector into Koan's central naming chokepoint; the repository does not
re-elect naming during an operation. Generated index names include active source, partition, container, and tenant
contributors. All sources use the one configured cluster endpoint.

`IndexName` is an explicit physical-name pin. Koan honors it and reports when it defeats active isolation. Per-source
cluster endpoints, embedding retrieval, hybrid text/vector search, and snapshot-consistent scroll guarantees are not
part of this provider contract.

## Validation surface

The provider-owned matrix exercises boot/configuration, pre-use readiness, index ensure, CRUD/bulk behavior, search,
filtering, partition isolation, export, clear, and semantic shapes against OpenSearch 2.13.0. Capability-gated tests
record embedding retrieval and hybrid search as unsupported.

## References

- Shared implementation: `~/reference/modules/Koan/data/searchengine`
- DATA-0097 Vector Pathway Parity: `~/decisions/DATA-0097-vector-pathway-parity.md`
