# Weaviate adapter technical contract

The adapter has four runtime responsibilities:

- `WeaviateRoute` resolves one source endpoint, credential, and immutable source policy.
- `WeaviateClient` owns bounded HTTP/JSON execution and safe status-only provider failures.
- `WeaviateFilter` projects neutral metadata into a constant `text[]` schema and writes exact GraphQL prefilters.
- `WeaviateRepository` realizes the immutable `VectorSpacePlan`, point lifecycle, search, visibility, and isolation.

## Placement and discovery

An explicit HTTP endpoint is authoritative. `auto` uses the service-discovery coordinator and may fall back to the
local default when no healthy candidate wins. A `zen-garden://...` connection intent is different: the options owner
parses it through `Koan.ZenGarden.Contracts`, requests required resolution from the coordinator, and fails before
connector I/O when no matching ready offering exists. Required intent never enters the autonomous fallback branch.

## Physical shape

Every physical collection name is a readable GraphQL-safe prefix plus a SHA-256 suffix over Koan's lossless logical
name. This preserves source and partition isolation without collision-prone character replacement.

Managed creation writes `vectorizer: none`, HNSW, the declared distance, and three fixed properties:

| Property | Purpose |
|---|---|
| `koanId` | Stable provider-neutral logical identity. |
| `koanMetadata` | Base64-encoded lossless neutral metadata JSON. |
| `koanTerms` | Constant-schema tokens used only for native prefiltering. |

The collection description contains a versioned marker for dimensions, metric, logical space, and model because
Weaviate's schema does not expose self-provided vector dimensions. Existing collections must match the complete marker,
vectorizer, distance, and fixed properties. External sources are inspected but never provisioned or repaired.

## Point and query semantics

Physical object UUIDs are deterministic UUIDv5 values over collection, compiled row-scope identity, and logical ID.
That makes direct get/delete scope-safe without exposing scope values. Metadata is stored once losslessly; projection
tokens encode paths and values injectively with type-aware canonical forms. No user metadata property is added to the
Weaviate schema.

Search uses native `nearVector` and GraphQL `where` before ranking. Equal/NotEqual, In/Nin, Has/HasAny/HasAll/HasNone,
Size, Exists, And, Or, and Not translate to exact `ContainsAny`, `ContainsAll`, and `ContainsNone` operations over the
fixed token set. Unsupported operators fail through `VectorFilterUnsupportedException`; there is no full scan or
post-kNN residual filter.

Raw Weaviate distance becomes Koan similarity as follows:

| Metric | Weaviate distance | Koan similarity |
|---|---|---|
| Cosine | `1 - cosine` | `1 - distance / 2` |
| Euclidean | squared L2 | `1 / (1 + sqrt(distance))` |
| Dot product | negative dot | `logistic(-distance)` |

Search reports `Approximate`, an unknown candidate count, no fabricated total, and no continuation. Stable cutoff ties
expand within `MaxSearchCandidates` or fail correctively.

## Mutation and visibility

Creates use object POST; an identity conflict is replaced through object PUT. Batch inputs are fully validated before
the first write, preserve input order, report Inserted/Updated/Deleted/Missing per item, and report
`BatchAtomicity.NotGuaranteed`. Native bulk capability is deliberately not advertised.

Session mutations request consistency `ALL`, then inspect the collection's node/shard vector queue until ready within
`VisibilityTimeoutSeconds`. Clear first retrieves at most `MaxClearPoints + 1` object IDs; overflow fails before any
delete. The default bound is 9,999, reserving one result beneath Weaviate's 10,000 query ceiling for overflow proof.

HTTP responses, metadata, batches, clear, search expansion, and visibility waits all have typed bounds. Provider error
bodies and business values are not included in thrown transport or GraphQL errors.
