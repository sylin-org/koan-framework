---
title: Sylin.Koan.Data.Vector.Connector.SqliteVec - Technical Reference
description: Durable embedded exact Vector provider for Koan.
packages: [Sylin.Koan.Data.Vector.Connector.SqliteVec]
source: src/Connectors/Data/Vector/SqliteVec/
---

## Composition

`SqliteVectorModule` registers typed options, one host-owned native loader, one factory, and one non-provisioning health
probe. Vector Core elects the provider, compiles `DataSourcePlan` and `VectorSpacePlan`, enforces operation policy, and
caches the scoped repository. The adapter receives those decisions; it does not repeat provider election or invent a
second metric, visibility, or source model.

The implementation has five runtime responsibilities:

1. `SqliteVecRoute` compiles placement without I/O.
2. `SqliteVecNative` selects one exact RID payload and verifies its hash, entry point, and reported version.
3. `SqliteVecRepository` owns schema validation, float32 encoding, transactions, and native SQL.
4. `VectorMetadata` encodes the closed neutral algebra without reflection or provider objects.
5. `SqliteVecHealthContributor` observes an existing source without creating it.

## Physical model

Each compiled Entity/source/partition/space name resolves to one deterministic `vec0` virtual table:

```sql
CREATE VIRTUAL TABLE <physical-name> USING vec0(
  id TEXT PRIMARY KEY,
  embedding float[N] distance_metric=cosine|L2,
  scope TEXT PARTITION KEY,
  +metadata TEXT
)
```

Physical names use a short readable prefix plus a SHA-256-derived suffix. Ambient partition and routed source are
resolved on every operation through `VectorAdapterNaming`, so a host-cached repository remains isolation-correct.
Koan hard scopes use vec0's partition key. Arbitrary metadata predicates are not accepted because the auxiliary JSON
value cannot provide native filter-before-rank semantics.

Existing tables are validated for dimensions, metric, identity, scope, and metadata shape. Incompatible External
shape rejects; it is never repaired. Managed shape creation is guarded by `DataSourcePlan.Demand`. Missing reads return
empty without creating a directory, file, table, or native extraction.

## Mutation and search

Single save and batch save prevalidate every embedding and metadata value, then execute delete-plus-insert inside one
SQLite transaction. Delete batches also use one transaction and report each input position. `Clear` deletes only the
current physical container/scope; it does not drop shape.

Search sends a float32 query blob to native vec0 exact KNN. Results normalize cosine distance with `1 - distance / 2`
and Euclidean distance with `1 / (1 + distance)`. Native results are stabilized by distance then ordinal identity.
When a cutoff tie is incomplete, the adapter expands `k` within `MaxSearchCandidates`; it rejects if the configured
bound cannot establish a stable cutoff. There is no managed ranking fallback.

Each file operation uses a short-lived pooled provider connection. Shared in-memory placement keeps one host-owned
keeper connection. No global connection, repository semaphore, process-static native decision, or public reset path
exists.

## Native supply chain

The package embeds sqlite-vec v0.1.9 for win-x64, linux-x64, and linux-arm64. Extraction targets a versioned temporary
path and uses race-safe replacement. The loader accepts only `sqlite3_vec_init`, verifies the embedded SHA-256 before
loading, and requires `SELECT vec_version()` to return `v0.1.9`. Platform, integrity, load, entry-point, and version
failures are distinct corrective errors.

## Capability truth

The adapter declares exact kNN, normalized scores, dynamic collections, native hard-scope isolation, bulk upsert,
bulk delete, and atomic batch. Session visibility is immediate and `Sync` is a completed barrier. DotProduct and
Eventual plans reject at repository creation. Filters, hybrid search, native continuation, streaming results, and
multi-vector points remain unclaimed and fail closed at the shared Vector boundary.

## Adapter-author checklist

Use this connector as a reference for the execution boundary, not as a template for SQLite-specific mechanics:

- accept an immutable source and space decision;
- keep provider-specific configuration to irreducible placement or bounded work;
- validate complete input before mutation;
- implement isolation on every read and mutation surface;
- make capability declarations executable;
- preserve source policy before any implicit provisioning;
- report exact/approximate, score, visibility, and batch facts honestly;
- keep native/client lifetime host-owned and the warm path free of election, reflection, or fallback branches.

The complete cross-provider contract is the [data adapter development primer](../../../../../docs/architecture/data-adapter-development-primer.md).
