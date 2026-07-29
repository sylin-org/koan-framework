# Qdrant adapter technical contract

## Ownership

`VectorSpacePlan` is the only owner of logical space name, dimensions, metric, model identity, source, and visibility.
`DataSourcePlan` owns access and storage lifecycle. `QdrantOptions` owns endpoint, API key, and bounded operational
budgets only.

The runtime has four execution parts:

- `QdrantVectorAdapterFactory` validates the plan, resolves one source-aware route, and creates the repository;
- `QdrantRoute` holds immutable endpoint, credential, source, and policy;
- `QdrantClient` owns bounded REST transport, status handling, and transient retries;
- `QdrantRepository` realizes shape, identity, payload, lifecycle, and vector operations.

`QdrantFilter` is the single native payload-filter writer. There is no generic HTTP-vector superclass or alternate
legacy execution path.

## Collections and shape

Koan's naming service composes Entity, routed source, ambient partition, and segmentation particles into a
case-preserving Qdrant collection name. Only identifier-safe, injective particles are accepted. The logical space name
becomes the Qdrant named-vector slot.

Managed save and ensure operations create a missing collection. Reads do not create. External and read-only source
policies are enforced before schema or mutation work. Existing collections are validated for named-vector presence,
dimensions, metric, and declared model metadata. Shape disagreement is a corrective error; the adapter does not repair
or reinterpret the collection.

Clear uses Qdrant's delete-by-filter operation and retains collection shape. All mutations use `wait=true`, which is
the adapter's `Session` visibility realization. `Eventual` is not simulated.

## Identity and payload

Qdrant accepts UUID or unsigned 64-bit point IDs. Native non-negative numeric keys remain numeric; GUIDs remain GUIDs;
other keys use deterministic UUIDv5 projection. Negative signed values have a distinct typed UUID projection and never
collapse to zero. The original invariant Entity ID is stored in `__koan_id` for exact round-trip.

Scoped identity participates in the physical point ID and `__koan_scope` payload. This lets equal Entity IDs coexist
across isolated row scopes. Provider reads and mutations also apply the compiled scope predicate.

Neutral metadata is stored under `__koan_metadata` with CLR scalar kinds preserved. `__koan_index` is a separate
JSON-native projection used only for Qdrant filtering. Reserved `__koan_*` user keys are rejected. Cosine collections
normalize vectors on upload, so `__koan_norm` retains only the original magnitude and read reconstructs the caller's
embedding without storing a duplicate vector.

## Search and filtering

The adapter uses Qdrant v1.18's `POST /collections/{collection}/points/query` API with the declared named vector.
Dense HNSW execution is reported as `Approximate`; the adapter never infers exactness from a small result set.

Provider scores become portable higher-is-closer similarities:

| Metric | Portable similarity |
|---|---|
| Cosine | `(score + 1) / 2` |
| Euclidean | `1 / (1 + distance)` |
| Dot product | logistic `(score)` |

Minimum similarity is converted back to the provider threshold with the inverse transform. Stable score ties are
expanded until identity ordering can be proven or `MaxSearchCandidates` is reached; the latter fails correctively.

Native filters support nested paths plus `Eq`, `Ne`, `Gt`, `Gte`, `Lt`, `Lte`, `In`, `Nin`, `Has`, `HasAny`,
`HasAll`, `HasNone`, `Size`, and `Exists`, including `All`, `Any`, and `Not` composition. Case-insensitive and undeclared
operators throw `VectorFilterUnsupportedException`; there is no client-side fallback.

## Transport and failure behavior

Responses are bounded before JSON parsing. Batches, metadata, search candidates, and request time have explicit limits.
Only HTTP 429, 502, 503, and 504 receive three short bounded attempts. Cancellation reaches every wait and HTTP call.
Provider failures expose operation and HTTP status, not response bodies or credentials. Disposed repositories reject
new work.

Health participation is source-aware: selecting Qdrant makes readiness material; merely referencing the package does
not. Discovery probes `/readyz` and falls back to `http://localhost:6333` when no authoritative endpoint is supplied.

## Capability truth

The adapter declares KNN, native filters, bulk upsert/delete, score normalization, and dynamic collections. It does not
declare portable hybrid search, native continuation, streaming export, multi-vector-per-Entity behavior, or atomic
batches. Bulk results preserve per-item outcomes and state `BatchAtomicity.NotGuaranteed`.
