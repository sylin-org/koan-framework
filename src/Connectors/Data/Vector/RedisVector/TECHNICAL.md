# RedisVector adapter technical contract

## Ownership

`VectorSpacePlan` owns source, name, dimensions, metric, visibility, and model. `DataSourcePlan` owns access and
storage lifecycle. `RedisVectorOptions` owns bounded vector work only. `Koan.Redis` remains the single discovery,
connection-pooling, multiplexer, and disposal owner; this connector neither registers nor disposes a Redis client.

`RedisVectorVectorAdapterFactory` resolves a source-aware route through `IRedisConnectionProvider`.
`RedisVectorRepository` executes StackExchange.Redis hash and Redis Search commands directly. `RedisVectorFilter` is
the only metadata projection/query translator. There is no connector-local discovery adapter, service descriptor,
or pass-through client wrapper.

## Index and key shape

The shared naming service composes Entity, source, partition, and segmentation particles with a `_vector` anchor.
Each physical name owns one `koan_vector_*` Search index and one disjoint hash-key prefix. The immutable marker and
`FT.INFO` must agree on HASH storage, prefix, exact FLAT algorithm, FLOAT32 dimensions, metric, space, and model.
Shape disagreement fails before mutation and is never repaired.

Redis Search vector indexes are addressed in logical database 0. This is a provider constraint, not a second Redis
placement: the route and multiplexer still come from the existing shared endpoint. A nonzero vector source fails
correctively. A paired record/cache source may retain its own logical database; that setting does not retarget the
vector plane. Readiness probes the low-privilege `FT.INFO` Search surface and leaves vector-shape validation to the
first declared space boundary instead of requiring the administrative `FT._LIST` command.

## Metadata and filtering

Hashes store the invariant Entity identity, compiled scope, little-endian FLOAT32 embedding, and lossless
`VectorMetadata` JSON. Atomic replacement removes stale projected fields before every upsert.

Four fixed TAG fields carry hashed path-presence, typed scalar, typed array-element, and precision-boundary tokens. Numeric leaves and
array sizes add deterministic per-path NUMERIC fields through distributed-lock-serialized `FT.ALTER` on Managed
sources. Repositories refresh missing dynamic descriptors from `FT.INFO`, so another host's schema addition cannot
leave range/size compilation stale. Hash tokens
avoid query escaping and prevent one user's metadata value from becoming query syntax. Filter compilation implements
the neutral missing/null rules, nested paths, `Eq`, `Ne`, numeric ranges, `In`, `Nin`, `Has*`, `Size`, `Exists`,
`All`, `Any`, and `Not`. Unsupported value/operator combinations throw `VectorFilterUnsupportedException`; there is
no client-side search fallback.

Redis NUMERIC uses binary64 ordering. A finite neutral number that cannot be represented there exactly remains in
the lossless payload and is marked as unordered; a relational filter over that path detects the marker natively and
fails closed rather than omitting or misordering the point. Dynamic numeric/size path admission is bounded across the
native index, not only per request.

## Search and lifecycle

Search executes `FT.SEARCH` with a native scope/filter precondition and exact FLAT KNN clause. Results are ordered by
distance and stable identity before the requested bound is applied. Similarity maps cosine as `1-distance/2`, Redis's
squared L2 result as `1/(1+sqrt(distance))`, and inner product as the logistic of `1-distance`. Execution reports `Exact`, the requested metric,
and no invented candidate count or continuation.

Managed save/ensure may create an index or add numeric schema fields only after `SchemaOrAdmin` policy succeeds.
Reads never create shape. Clear deletes only keys admitted by the active native scope. Awaited mutations are Session
visible; `Sync` is therefore a no-op barrier. External/read-only ceilings are enforced at their owning boundary.

Batch input is fully validated before schema or data mutation. Save uses an atomic per-point Lua hash replacement;
save/delete batches are pipelined through the shared multiplexer and preserve per-item outcomes while truthfully
reporting `NotGuaranteed` whole-batch atomicity. Repository disposal never disposes the shared multiplexer.
