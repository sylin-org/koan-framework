---
uid: reference.modules.Koan.data.connector.opensearch
title: Koan.Data.Connector.OpenSearch - Technical Reference
description: Plan-bound OpenSearch vector adapter with native kNN, bounded metadata projection, and honest source policy.
packages: [Sylin.Koan.Data.Connector.OpenSearch]
source: src/Connectors/Data/OpenSearch/
---

## Ownership

`OpenSearchVectorAdapterFactory` binds an immutable `VectorSpacePlan` before provider I/O. `DataSourcePlan` supplies
access and lifecycle policy. `OpenSearchOptions` contains endpoint, authentication, timeout, and bounded-work settings
only. Provider configuration does not own vector shape, names, metric, visibility, or source policy.

Four runtime parts own distinct failures:

- `OpenSearchRoute` resolves one source-aware endpoint, credential set, and policy;
- `OpenSearchClient` owns bounded HTTP, cancellation, safe status errors, and narrow read-only transient retries;
- `OpenSearchFilter` compiles the declared neutral Filter subset into native pre-filter queries;
- `OpenSearchRepository` owns physical shape, complete points, receipts, score conversion, lifecycle, and disposal.

## Physical contract

Each logical Entity/source/partition container and named space resolves to a readable, lowercase, hash-suffixed write
alias with one generated backing index. Writes set `require_alias=true`, so document APIs cannot create an accidental
index. Managed lifecycle may create the alias and backing index; External validates but never creates; ReadOnly
rejects mutations before dispatch.

The mapping stores:

- logical identity and scope as `keyword`;
- the embedding as `knn_vector` with plan-owned dimensions and metric;
- lossless neutral metadata as a disabled object;
- a constant-cardinality nested path/value projection for native filters;
- contract version, space, model, metric, and engine in mapping metadata.

The nested projection hashes logical property paths and stores canonical typed values. This avoids dynamic mapping
growth while supporting equality, range, set, collection, size, existence, and boolean composition. A filter outside
the declared subset throws `VectorFilterUnsupportedException` before a query is sent.

## Search and score truth

Search uses `query.knn.<field>` with a native `filter`. The Lucene HNSW engine realizes all three Koan metrics.
Response size and stable tie expansion are bounded. Results are sorted by normalized similarity and then logical
identity.

| Koan metric | OpenSearch space | Koan normalization |
|---|---|---|
| Cosine | `cosinesimil` | native `(1 + cosine) / 2` score |
| Euclidean | `l2` | invert native squared-distance score, then `1 / (1 + distance)` |
| DotProduct | `innerproduct` | invert the Lucene piecewise score, then apply Koan logistic normalization |

Accuracy is reported as `Approximate`. OpenSearch does not expose a truthful global candidate count for this query,
so the adapter does not invent one.

## Mutations and visibility

Single and bulk mutations request an explicit refresh, providing awaited Session visibility without waiting for the
periodic refresh interval. Bulk responses are parsed item by item in request order. HTTP success never becomes
fabricated item success, and `BatchAtomicity.NotGuaranteed` records partial-application semantics. Mutation requests
are not automatically retried because their outcome may be unknown after transport failure.

Clear uses policy-gated delete-by-query inside the active scope; it does not drop the index. Complete positional
get-many, source/partition/container/row isolation, cancellation, restart recovery, and disposal are exercised against
the pinned OpenSearch 3.7.0 container.

## Configuration

Endpoint precedence is exact provider/source configuration, `ConnectionStrings:OpenSearch`, then discovery and the
configured default endpoint. Supported provider settings are `Endpoint`, `ApiKey`, `Username`, `Password`,
`TimeoutSeconds`, `MaxMetadataBytesPerPoint`, `MaxBatchPoints`, `MaxRequestBytes`, `MaxSearchCandidates`,
`MaxResponseBytes`, readiness, and discovery control.

Vector dimensions, metric, model, space name, visibility, storage lifecycle, access, index names, and wire fields are
not provider settings.

## Capability boundary

The adapter claims native kNN, the listed Filter subset, bulk upsert/delete, score normalization, and dynamic physical
containers. It explicitly declines Eventual visibility, hybrid search, native continuation, export, atomic batch, and
multi-vector-per-Entity behavior.
