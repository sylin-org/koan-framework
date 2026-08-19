# MongoAtlasVector technical notes

## Ownership

The package owns vector mechanics only. It reuses Mongo endpoint intent but deliberately has no
`KoanService` or discovery adapter, so it cannot provision a second Mongo process or compete with
the record connector's discovery owner. A small package-local client cache exists because the record
connector's manager is intentionally internal; both clients target the same physical service.

Vector data defaults to `KoanVectors`, independently of the record database. Physical collection
names flow through Koan naming (entity, source, partition) and receive `_vector`, preventing
record/vector collision even when a caller chooses one database deliberately.

## Native shape

Each physical collection has one Atlas Search index named `koan_vector`. Its standard Search mapping
is dynamic for filter projections and explicitly declares the embedding vector. A reserved marker
document binds dimensions, metric, model, and schema version. Existing mismatches are rejected at
the schema boundary before mutation.

Documents carry the stable identity and compiled scope, embedding, a unique mutation generation,
lossless neutral metadata JSON, and a separate native filter projection. Equality and collection
operators use typed hashed tokens; numeric ranges and collection sizes use path-hashed native numeric
fields. User metadata beginning with `__koan` is reserved unless it is an exact managed-scope value
supplied by Koan.

## Search and visibility

Queries use Atlas `$search.vectorSearch` with `exact: true`; filter intent is part of that first native
stage. Stable score ties are ordered by invariant identity after bounded native retrieval. Receipts
therefore report `Exact`, the declared metric, no approximate-candidate count, and no continuation.

Atlas Search indexing is asynchronous. A successful mutation is not returned until a generation
probe is search-visible (or a deleted identity is absent), bounded by
`MutationVisibilityTimeoutSeconds`. Index creation is likewise polled to `READY` and `queryable`
within `IndexReadyTimeoutSeconds`. Timeout errors state that Mongo accepted the primary mutation but
the Search visibility guarantee was not established.

## Capability boundary

The connector claims exact KNN, the locked metadata-filter floor, bulk upsert/delete, normalized
scores, and dynamic collections. It does not claim hybrid queries, native continuation, streaming
export, multiple vectors per Entity, atomic batches, or cross-store transaction atomicity. Eventual
visibility is rejected instead of being simulated.
