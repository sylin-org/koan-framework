# PgVector adapter technical contract

## Ownership

`VectorSpacePlan` owns source, name, dimensions, metric, visibility, and model. `DataSourcePlan` owns access and storage
lifecycle. `PgVectorOptions` owns PostgreSQL placement and bounded operational work. The adapter does not alter the
Postgres record connector.

`PgVectorVectorAdapterFactory` validates visibility and resolves one source-aware `PgVectorRoute`.
`PgVectorRepository` executes direct, parameterized Npgsql commands; it owns native shape, identity, lifecycle, and
vector operations. `PgVectorFilter` is the only JSONB metadata translator. There is no alternate client or legacy path.

## Tables and shape

The factory gives every vector anchor a `_vector` suffix, then the shared naming service composes source, ambient
partition, and segmentation particles into a quoted PostgreSQL table identifier. The suffix lets record and vector
storage for the same Entity safely share one database. A table stores `(scope, id)` as its primary key, a dimensioned
`vector(n)`, lossless neutral metadata in PostgreSQL `json`, and a separate `jsonb` filter projection. The table comment
holds a versioned, encoded immutable marker for space name, metric, and model.

Managed save and ensure operations take database-wide extension and table-specific transaction advisory locks in a
fixed order, enable `vector` when necessary, and create a missing table. Existing tables are inspected through
PostgreSQL catalogs; type, nullability, primary-key, extra-required-column, dimension, or marker disagreement fails
correctively and is never repaired. Reads do not create tables or enable extensions. External and read-only policy is
enforced before schema or mutation work.

## Identity, scope, and metadata

Entity IDs use invariant text. The compiled vector scope contributes a stable hashed storage scope so equal Entity IDs
can coexist across isolated row scopes. Provider reads, deletes, searches, and clears also apply the compiled native
predicate. Routed sources and partitions additionally receive distinct physical table names from shared naming.

`VectorMetadata` owns the lossless payload representation. The filter projection keeps native scalar and array shapes
so JSONB can compare them without interpreting the tagged neutral encoding. Values and JSON paths are parameters;
only naming-service identifiers are quoted into SQL.

## Search and filtering

Search orders by the plan's pgvector operator and then by identity using PostgreSQL's `C` collation. The search
transaction enables sequential scan and disables PostgreSQL index scan paths, so execution remains `Exact` even when
an externally provisioned ANN index exists; the connector itself creates no ANN index. No candidate count or
continuation is invented.

| Metric | Operator | Portable similarity |
|---|---|---|
| Cosine | `<=>` | `1 - distance / 2` |
| Euclidean | `<->` | `1 / (1 + distance)` |
| Dot product | `<#>` | logistic of `-distance` |

Native filters support nested paths plus `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `Has`, `HasAny`,
`HasAll`, `HasNone`, `Size`, and `Exists`, including `All`, `Any`, and `Not`. Missing/null behavior matches the neutral
dictionary oracle. Case-insensitive and undeclared operators throw `VectorFilterUnsupportedException`; there is no
client-side fallback.

## Failure and capability truth

Cancellation reaches connection open, schema work, commands, and readers. Command, metadata, batch, and result bounds
are explicit. Provider errors do not include connection strings. Disposed repositories reject new work. Health becomes
material only after runtime selection and verifies that the selected PostgreSQL server offers pgvector.

Batch save and delete prevalidate the complete request and execute as one set-based PostgreSQL statement while
preserving ordered per-item outcomes. The adapter declares KNN, filters, bulk upsert/delete, score normalization, and dynamic collections. It does not claim
hybrid search, native continuation, streaming export, multi-vector-per-Entity behavior, or atomic batches. Awaited
single mutations provide Session visibility; batch atomicity is deliberately `NotGuaranteed`.
