# Milvus adapter internals

The implementation has four semantic owners:

- `MilvusRoute` resolves source placement, database, credential, and immutable policy.
- `MilvusClient` owns bounded REST v2 transport and safe provider-error handling.
- `MilvusFilter` translates only exact neutral predicates to native Milvus expressions.
- `MilvusRepository` owns plan validation, collection/index/load state, complete points, search, isolation, and outcomes.

## Physical model

Each framework-resolved vector container becomes one injective Milvus collection name. A nullable, payload-free schema
field carries a versioned contract hash for dimensions, metric, space, model, and fixed wire fields. Dynamic fields are disabled.
The schema contains:

- `koan_id`: deterministic scoped storage identity and VARCHAR primary key;
- `koan_logical_id`: reversible Koan identity;
- `koan_vector`: plan-sized FLOAT_VECTOR with an HNSW index;
- `koan_metadata`: lossless neutral JSON metadata.

The adapter validates the contract field, field roles/types, vector dimensions, and index metric before data I/O. Managed
read-write sources may create and load a collection. External or read-only sources must already expose the correct,
loaded shape.

## Visibility and ranking

Milvus separates ingestion from QueryNode visibility. Every adapter read/search uses Strong consistency; collection
load completion is awaited within a configured deadline. Koan Eventual visibility is declined because the REST adapter
does not own a durable provider session timestamp.

Milvus reports COSINE and IP with higher values closer, while L2 is squared Euclidean distance with lower values closer.
The adapter returns `(cosine + 1) / 2`, `logistic(innerProduct)`, and `1 / (1 + sqrt(l2))`, respectively. HNSW execution
is reported approximate and the provider does not expose candidate count for these requests.

## Bounded operations

Native upsert/delete batches run only after all inputs validate. Existing-point preflight provides ordered per-item
outcomes; atomicity remains not guaranteed. Clear first queries at most `MaxClearPoints + 1` scoped identities and fails
without mutation on overflow. Stable cutoff ties expand within `MaxSearchCandidates`; inability to prove a stable cutoff
fails explicitly.
