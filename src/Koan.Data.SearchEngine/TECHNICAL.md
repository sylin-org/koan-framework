---
uid: reference.modules.Koan.data.searchengine
title: Koan.Data.SearchEngine - Technical Reference
description: Shared runtime mechanics for the Elasticsearch and OpenSearch vector connectors.
packages: [Sylin.Koan.Data.SearchEngine]
source: src/Koan.Data.SearchEngine/
---

## Responsibility

`Sylin.Koan.Data.SearchEngine` is the mechanism package behind two independently selectable providers. It owns
the mechanics that must not drift: options binding, endpoint discovery, authentication, selection-aware health,
startup projection, naming, REST operations, filter translation, hit parsing, and index lifecycle.

It is not an application-facing provider. `Sylin.Koan.Data.Connector.ElasticSearch` and
`Sylin.Koan.Data.Connector.OpenSearch` retain reference-is-intent registration and the native request dialect.

## Runtime path

1. The provider module delegates registration to `AddSearchEngineConnector<TOptions,TFactory>`.
2. The shared configurator resolves, in order, `ConnectionStrings:<Provider>`, the provider's exact
   `ConnectionString`, then its exact `Endpoint`; otherwise discovery supplies an endpoint or the local fallback.
3. `VectorService` elects a provider globally or from `[VectorAdapter("...")]` and records participation.
4. The selected factory receives the already-selected naming provider and creates the shared repository.
5. The repository uses `ISearchEngineDialect` only for native kNN query, index mapping, and similarity-token
   differences. All other operations follow the common path.

There is no generic `Koan:Data:ConnectionString` alias and no provider-side re-election during an operation.

## Health and startup reporting

Reference makes a provider available; it does not make that provider critical. Before selection its health is
`Unknown`, connection-free, and non-critical. Once an Entity/source operation selects it, its health contributor
probes `/_cluster/health` using the same endpoint and credentials as the repository.

Startup reporting emits the effective, de-identified endpoint and the common index/vector settings once. It also
states that readiness begins only after Vector selects the provider.

## Repository contract

The shared repository provides index ensure, single and bulk upsert/delete, flush, count, scroll export, kNN
search, metadata-filter pushdown, and index statistics. `GetEmbedding` and hybrid text/vector search are not
implemented. Search `topK` is caller-owned; the connector executes the requested value and does not invent a
smaller default or cap.

Source, partition, container, and tenant contributors flow through the selected Koan naming provider. Explicit
`IndexName` is intentionally honored but reported when it defeats active isolation. Per-source cluster endpoints
are outside the current contract.

## Filter contract

`SearchEngineFilterTranslator` renders `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `StartsWith`,
`EndsWith`, `Contains`, `Has`, `HasAny`, `HasAll`, `HasNone`, and `Exists`. Unsupported operators fail loudly.
Exact string and wildcard operations target the dynamic `.keyword` sub-field; numeric ranges and existence checks
target the bare field. Negation uses null-inclusive `bool/must_not` semantics.

## Security boundary

Repository and readiness requests support API-key or HTTP Basic authentication. Credentials are not written to
startup facts. Autonomous discovery does not currently authenticate its health probe, so secured deployments must
provide an exact endpoint through provider configuration.

## References

- DATA-0097 Vector Pathway Parity: `~/decisions/DATA-0097-vector-pathway-parity.md`
- AI-0036 Embedding/vector seam: `~/decisions/AI-0036-embedding-vector-seam.md`
