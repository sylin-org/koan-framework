# FLOW-0104 - ULID as Primary Id with CanonicalId (Business Key)

Status: Approved

## Contract

- Scope: Koan.Flow entity identity and all derived artifacts (stages, projections, links, APIs)
- Inputs: Aggregation tags → CanonicalId (business key); new entity creation events
- Outputs: A minted ULID as the primary Id; CanonicalId retained and indexed; both propagated end-to-end
- Error modes: Duplicate CanonicalId races, merges/splits, backfill gaps; must be handled deterministically
- Success criteria:
  - ULID is URL-friendly and stable for transport/storage
  - CanonicalId remains the stable business key for domain semantics and joins
  - Both identifiers available across stages, views, and APIs

## Decision

Adopt a dual-identifier model for Flow entities:

- Id: ULID string - primary identifier for transport, URLs, and storage (aligns with Entity<>)
- CanonicalId: string - stable business key produced by aggregation keys and association rules

Mint the ULID during the Associate step when a new canonical entity is first created. Preserve and propagate both identifiers across the Flow stack (Stage → Keyed → Projections/Views → APIs). Add bidirectional lookups and indexes (ULID ↔ CanonicalId). Expose dual API routes (by ULID and by CanonicalId).

## Rationale

- Developer ergonomics: ULID is compact, sortable, and URL-friendly; ideal as the outward-facing id.
- Domain integrity: CanonicalId is derived from business keys and drives association; it must remain separate and visible.
- Consistency: Minting at Associate is the earliest point we can atomically decide “new vs existing,” preventing double-minting.

References: ARCH-0052 (Core IDs) and FLOW-0101/0102 (bindings, identity map, aliasing).

## Details

### Minting and Uniqueness

- Mint point: Associate step, on first materialization of a new canonical entity.
- Uniqueness: Enforce a unique index on CanonicalId in the canonical registry (ReferenceItem or equivalent). On conflict, retry and reuse the existing entity’s ULID.
- Backfill: For pre-existing canonicals without ULID, run a one-time backfill to assign ULIDs and populate the cross-index.

### Propagation

- StageRecord<T> (after Associate): enrich with ReferenceUlid (EntityUlid) alongside CorrelationId (often the CanonicalId or business key). Do not overload CorrelationId.
- Keyed stage: carry both CanonicalId and ReferenceUlid in the envelope/metadata.
- Projections (ProjectionView<TModel,TView>):
  - Use ULID as the document key for new writes (e.g., latest::{ULID}).
  - Include CanonicalId as a view field for filtering/joining; add an index where relevant.
- IdentityLink/KeyIndex: include both ULID and CanonicalId; maintain bidirectional lookups and indexes.

### API Surface

- Default route by ULID: /{model}/{id} (id=ULID)
- Secondary route by business key: /{model}/by-cid/{canonicalId}
- Responses include both identifiers. Hypermedia links prefer ULID.
- Controllers accept either key shape where appropriate; for lists, support ?cid=… filters.

### Storage and Indexing

- Canonical registry:
  - Primary key: Id (ULID)
  - Unique index: CanonicalId
  - Secondary index: CreatedAt (for audit), optional UpdatedAt
- Projections:
  - Primary key: ULID-based
  - Optional index: CanonicalId for frequent filters/joins

### Merges and Splits

- Merge: choose a winner ULID (deterministically: oldest createdAt or highest confidence). Create redirect/link docs from losing ULIDs → winner; update KeyIndex/IdentityLink; reproject.
- Split: mint a new ULID for the split-off entity; adjust keys; reproject.
- Emit admin events and provide small admin endpoints for cache invalidation.

### Edge Cases and Guardrails

- Race on CanonicalId during Associate: rely on unique index and retry loser.
- CanonicalId reassignment: treat as a merge/split operation; never overwrite silently.
- Time ordering: do not rely on ULID timestamp for business time; prefer OccurredAt.

## Impact

- Improves URL ergonomics and cross-system references without sacrificing domain identity semantics.
- Requires minimal code delta: add ULID field to canonical registry, unique index on CanonicalId, enrichment after Associate, dual routes, backfill.

## Migration Plan

1. Add ULID (Id) to canonical registry and unique index on CanonicalId; deploy.
2. Backfill existing canonicals lacking ULID; build ULID↔CanonicalId map.
3. Switch projections to write with ULID keys; add read-compat for CanonicalId during deprecation window.
4. Expose dual routes; update docs and Swagger.
5. Remove CanonicalId-only doc keys after deprecation window if desired.

## References

- ARCH-0052-core-ids-and-json-merge-policy.md
- FLOW-0101-bindings-canonical-ids-and-vo-ingest.md
- FLOW-0102-identity-map-provisional-and-aliasing.md
- DATA-0061-data-access-pagination-and-streaming.md
