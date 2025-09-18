---
id: FLOW-0102
slug: identity-map-provisional-and-aliasing
domain: flow
status: accepted
date: 2025-08-31
title: Identity map, provisional mappings, aliasing (cap=1), and value‑object indexing
---

## Context

The Flow runtime must stay type‑agnostic while supporting high‑volume value objects (VOs) that reference canonical aggregates via external identifiers. Adapters emit system/adapter‑scoped IDs and may not know canonical IDs. The platform needs:

- A durable identity map from external IDs to canonical IDs (ULID) with resilient ingest when parents are missing.
- Clear merge semantics when two external references later unify to the same real‑world entity.
- Lightweight VO ingest without strict dedup or event keys.
- Generic, attribute‑driven indexing for VO properties that reference entities, applicable across domains.

## Decision

1. Identity map (type‑agnostic)

   - Key: (entityType, system, adapter, externalId) → canonicalId (ULID).
   - Status: provisional | confirmed.
   - Policy: provisional‑on‑miss for value objects; GC stale provisional entries after 2 days (configurable).

2. Aliasing (merge semantics)

   - Support alias records to unify canonical IDs when references converge.
   - Hard cap alias chain length to 1. On merge, rewrite existing aliases to point directly to the terminal canonicalId; resolvers must always return the terminal ID.

3. Value‑object ingest

   - Treat VOs as append‑only; duplicates are acceptable; source eventId not required.
   - On mapping failures beyond provisional creation (e.g., policy/validation failure), park entries in a parallel set and sweep periodically to promote when resolvable.

4. Attribute‑driven, default‑on indexing

   - Introduce an attribute under `Koan.Flow.Attributes` to mark VO properties that link to entities:
     - Deprecated: `EntityLinkAttribute(Type flowEntityType, LinkKind kind)` where `kind ∈ { CanonicalId, ExternalId }`.
       Use reserved keys `identifier.external.{source}` in intake payloads instead; mapping uses `system|adapter|externalId` composites.
   - Enable indexing by default for any VO property decorated with this attribute:
     - If `CanonicalId`: index (property, capturedAt DESC).
     - If `ExternalId`: index (system, adapter, property, capturedAt DESC).

5. Sensor → Device relationship (sample baseline)

   - Add `deviceId` (ULID) FK on `Sensor` to reference `Device`. Keep business keys optional for diagnostics.

6. Minimal VO envelope (type‑agnostic)
   - Envelope fields: `system`, `adapter`, `capturedAt`, optional `source` string, and the VO payload.
   - VO payload includes one or more entries under reserved `identifier.external.*` keys to drive mapping and indexing.

## Scope

Applies to the Flow pillar (runtime, workers, and samples such as S8). Type‑agnostic across domains. No multi‑tenant field in the identity tuple; tenancy handled via database segmentation.

## Consequences

- Pros: Resilient ingest, simple adapters, predictable resolution, domain‑agnostic indexing, fast queries by canonical or external IDs.
- Cons: Requires background GC of provisional mappings, alias maintenance, and care to limit index bloat for many external link properties.

## Implementation notes

- IdentityMap entity: `{ entityType, system, adapter, externalId, canonicalId, status, createdAt, updatedAt, provenance }` with a unique index on the key tuple.
- Provisional GC: background worker deletes or flags entries older than TTL (default 2 days) when still provisional.
- Aliases: `{ fromCanonicalId, toCanonicalId, reason, at }`; enforce chain length ≤ 1 by rewriting on each merge; cache terminal resolution with a short TTL.
- Park‑and‑sweep: parallel `<EntityType>#flow.park` set; sweeper retries mapping and promotes on success; emit metrics (parked, promoted, expired).
- Index registration: deprecated. External-id values are read from reserved keys at runtime; explicit registration not required.
- Sensor→Device: introduce `deviceId` on Sensor; populate during upsert via resolver.
- TelemetryEvent (sample): slim to external sensor identifier + unit, value, capturedAt, system, adapter, source.

## Follow‑ups

- Do not implement `EntityLinkAttribute`; prefer contractless `identifier.external.*` keys.
- Add options for IdentityMap TTL and park sweeper cadence; expose minimal health/metrics.
- Implement alias resolver with terminal ID caching and unit tests for merge/chains.
- Update S8: `Sensor` gains `deviceId` (ULID); `TelemetryEvent` contract slimmed; docs and examples align.
- Add docs for VO envelopes and reserved key usage under `docs/reference/`.

## References

- FLOW‑0101 - Flow bindings, canonical IDs, and value‑object ingest
- DATA‑0061 - Data access semantics (All/Query; streaming; pager)
- DATA‑0030 - Entity sets routing and storage suffixing
- ARCH‑0052 - Core IDs (ShortId + ULID) and JSON merge policy
- WEB‑0035 - EntityController transformers
