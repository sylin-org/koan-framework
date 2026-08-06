---
uid: reference.modules.Koan.data.connector.elasticsearch
title: Koan.Data.Connector.ElasticSearch - Technical Reference
description: Plan-bound Elasticsearch vector adapter with native kNN, bounded metadata projection, and honest source policy.
packages: [Sylin.Koan.Data.Connector.ElasticSearch]
source: src/Connectors/Data/ElasticSearch/
---

## Ownership

`ElasticSearchVectorAdapterFactory` binds an immutable `VectorSpacePlan` before provider I/O. `DataSourcePlan` supplies
access and lifecycle policy. `ElasticSearchOptions` contains endpoint, authentication, timeout, and bounded-work
settings only. The adapter has no production dependency on `Koan.Data.SearchEngine` and no provider-owned schema or
vector semantics.

Four runtime parts own distinct failures:

- `ElasticSearchRoute` resolves one source-aware endpoint, credential set, and policy;
- `ElasticSearchClient` owns bounded HTTP, cancellation, safe status errors, and narrow read-only transient retries;
- `ElasticSearchFilter` compiles the declared neutral Filter subset into native pre-filter queries;
- `ElasticSearchRepository` owns physical shape, complete points, receipts, score conversion, lifecycle, and disposal.

## Physical contract

Each logical Entity/source/partition container and named space resolves to a readable, lowercase, hash-suffixed write
alias with one generated backing index. Writes set `require_alias=true`, so document APIs cannot create an accidental
index. Managed lifecycle may create the alias and backing index; External validates but never creates; ReadOnly rejects
mutations before dispatch.

The mapping stores:

- logical identity and scope as `keyword`;
- the embedding as `dense_vector` with plan-owned dimensions and metric;
- lossless neutral metadata as a disabled object;
- a constant-cardinality nested path/value projection for native filters;
- contract version, space, model, and metric in mapping metadata.

The nested projection hashes logical property paths and stores canonical typed values. This avoids dynamic mapping
growth while supporting equality, range, set, collection, size, existence, and boolean composition. A filter outside
the declared subset throws `VectorFilterUnsupportedException` before a query is sent.

## Search and score truth

Search uses Elasticsearch top-level kNN with `filter` as a native pre-filter. `num_candidates`, response size, and
stable tie expansion are bounded. Results are sorted by normalized similarity and then logical identity.

| Koan metric | Elasticsearch similarity | Koan normalization |
|---|---|---|
| Cosine | `cosine` | native `(1 + cosine) / 2` score |
| Euclidean | `l2_norm` | invert native squared-distance score, then `1 / (1 + distance)` |
| DotProduct | `max_inner_product` | invert the native piecewise score, then apply Koan logistic normalization |

Accuracy is reported as `Approximate`. Native per-shard candidate counts are not mislabeled as a truthful global
candidate count.

## Mutations and visibility

Single and bulk mutations request an explicit refresh, providing awaited Session visibility without waiting for the
periodic refresh interval. Bulk responses are parsed item by item in request order. HTTP success never becomes
fabricated item success, and `BatchAtomicity.NotGuaranteed` records Elasticsearch partial-application semantics.
Mutation requests are not automatically retried because their outcome may be unknown after transport failure.

Clear uses policy-gated delete-by-query inside the active scope; it does not drop the index. Complete positional
get-many, source/partition/container/row isolation, cancellation, restart recovery, and disposal are exercised against
the pinned Elasticsearch 9.4.3 container.

## Configuration

Endpoint precedence is exact provider/source configuration, `ConnectionStrings:ElasticSearch`, then the configured
default endpoint. Supported provider settings are `Endpoint`, `ApiKey`, `Username`, `Password`, `TimeoutSeconds`,
`MaxMetadataBytesPerPoint`, `MaxBatchPoints`, `MaxRequestBytes`, `MaxSearchCandidates`, `MaxResponseBytes`, readiness,
and discovery control.

Vector dimensions, metric, model, space name, visibility, storage lifecycle, access, index names, and wire fields are
not provider settings.

## Capability boundary

The adapter claims native kNN, the listed Filter subset, bulk upsert/delete, score normalization, and dynamic physical
containers. It explicitly declines Eventual visibility, hybrid search, native continuation, export, atomic batch, and
multi-vector-per-Entity behavior.
