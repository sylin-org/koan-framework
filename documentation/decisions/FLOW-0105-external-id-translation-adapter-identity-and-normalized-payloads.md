---
title: "FLOW-0105: External ID translation, adapter identity, and normalized payload ingestion"
status: Accepted
date: 2025-09-01
related:
  - ARCH-0040-config-and-constants-naming.md
  - FLOW-0101-bindings-canonical-ids-and-vo-ingest.md
  - FLOW-0104-ulid-primary-id-and-canonical-id.md
---

# FLOW-0105 - External ID translation, adapter identity, and normalized payload ingestion

Contract (at a glance)

- Inputs:
  - Messages produced by adapter hosts annotated with `[FlowAdapter(system, adapter)]`.
  - Envelope metadata auto-stamped: `adapter.system`, `adapter.name`.
  - External IDs carried as `identifier.external.<system>` (zero or more).
  - Optional contractless payloads ("normalized bags"): include `model` (canonical key) and `reference.<entityKey>.external.<system>` for links.
- Outputs:
  - Canonical entities with parent references filled via `[ParentKey]`.
  - ExternalId index entries: `(entityKey, system, externalId) -> canonicalId`.
- Error modes:
  - Unknown external ID → defer with retry; eventual DLQ with reason `external-id-not-found`.
  - Duplicate mapping attempt → conflict (reject) or replace per policy.
  - Invalid bag (missing `model`) → hard error.
- Success:
  - Ingested items have canonical IDs resolved, are persisted, and envelope metadata is preserved for lineage/audit.

## Context

Adapters ingest data from external systems and should not depend on the orchestrator’s canonical identifiers (ULIDs). We need a consistent, DX-friendly way to:

- Identify the producing adapter/system.
- Attach external/native IDs without polluting entity models.
- Resolve canonical references (e.g., parent keys) from external IDs.
- Support both strong-typed models and contractless, JSON-path-style payloads.
- Enable bulk sends without split builders or per-item boilerplate.

## Decision

1. Adapter identity via attribute

- Introduce `[FlowAdapter(system, adapter)]` on adapter hosts (publishers/ingesters). The runtime stamps every message with:
  - `adapter.system` - stable identifier for the source system (centralized constant).
  - `adapter.name` - adapter variant/name.

2. External IDs live in the envelope (not the model)

- Carry native IDs as envelope metadata: `identifier.external.<system> = <value>` (0..N entries).
- Models remain transport-agnostic; do not add `NativeId`/`SourceId` to `FlowEntity`/`FlowValueObject`.

3. Parent relationships are declared on the model

- Use `[ParentKey]` on properties (e.g., `Sensor.DeviceId`) to declare canonical links. Adapters never set canonical IDs; the resolver fills them.

4. Contractless ingestion (normalized bag) is first-class

- A normalized bag carries:
  - `model` - the canonical entity key (e.g., `Keys.Device.Key`).
  - Arbitrary field paths (case-insensitive, dotted permitted) with values.
  - Link hints: `reference.<entityKey>.external.<system> = <externalId>`.
- The pipeline maps bag fields to the model and resolves links using the ExternalId index.

5. ExternalId index and resolution

- Maintain an index mapping `(entityKey, system, externalId) -> canonicalId`.
- On entity create/update, when external IDs are present in the envelope, upsert entries into the index for that entity key.
- During ingestion, for each property with `[ParentKey]`:
  - If value is already canonical → keep.
  - Else, look for `reference.<targetKey>.external.<system>` in the bag.
  - Else, consult envelope `identifier.external.*` if sufficient to infer the parent.
  - If resolvable → set canonical; else → defer per policy.

## Alternatives considered

- Adding `NativeId`/`SourceId` to all models:

  - Rejected. Couples models to transport concerns, does not scale to multi-source events.

- Overloading `[ParentKey]` with native-id translation flags:
  - Possible, but less explicit than bag/envelope hints; can be revisited if needed.

## Consequences

Pros

- Clean separation of concerns; models remain canonical-only.
- Adapters stay ignorant of canonical IDs and can send in bulk.
- Deterministic metadata schema (reserved keys), centralized constants (ARCH-0040).
- Works for both strong-typed and contractless ingestion.

Cons

- Requires maintaining an ExternalId index and resolver step.
- Ambiguity when multiple systems provide conflicting IDs; must be disambiguated by explicit system keys and policy.

Edge cases

- Unknown external ID → backoff, park, DLQ with actionable reason.
- Duplicate mappings → conflict or replace per configuration.
- High-volume streams → resolver must batch lookups and cache hot entries.

## Implementation notes

- Constants: centralize system identifiers and reserved keys under `Infrastructure/Constants` (per ARCH-0040). Avoid magic strings.
- Envelope format: do not mutate the entity; attach metadata alongside the payload.
- Discovery: `[FlowAdapter]` supports auto-discovery by the DX toolkit; metadata is displayed in monitors.
- Documentation: Reference and Guides updated to include reserved keys, resolution, and examples.
