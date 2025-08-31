## Flow bindings, canonical IDs, and value-object ingest

This page defines how Sora Flow models declare binding hints, how OEM/source identifiers are resolved to canonical IDs, and how value objects (VOs) ingest independently while keeping lineage. It applies across modules and samples (e.g., S8).

### contract

- Inputs
  - Domain aggregates (perennial): e.g., `Device`, `Sensor`.
  - Value objects (append-only): e.g., `SensorReading`.
  - Binding hints on models (attributes or registry) that identify association keys: `tenant.id`, `device.id`, `sensor.id`, `event.time`, etc.
- Outputs
  - Canonical aggregates with stable IDs ("canonical::…").
  - `KeyIndex<T>` entries mapping external keys → canonical IDs.
  - VO rows persisted with canonical IDs and lineage metadata.
- Error modes
  - Missing/invalid binding → rejection with diagnostics.
  - Out-of-order/late data → accepted and ordered by event time in views.
- Success criteria
  - Models stay clean and separately partitioned; queries target canonical IDs; lineage is preserved; storage remains provider-agnostic.

### model partitioning

- Keep aggregates and VOs in separate collections:
  - Aggregates: `S8.Flow.Shared.Device#flow.*`, `S8.Flow.Shared.Sensor#flow.*` (long-lived).
  - VOs: `S8.Flow.Shared.SensorReading#flow.*` (append-only, time-partitioned, TTL).
- Do not embed high-volume VOs inside aggregates.

### bindings and keys

- Use a neutral key taxonomy via constants (no literals): `FlowBindingKeys.DeviceId`, `FlowBindingKeys.SensorId`, `FlowBindingKeys.EventTime`, `FlowBindingKeys.TenantId`, `FlowBindingKeys.PartitionKey`, `FlowBindingKeys.CorrelationId`.
- Bindings are hints for:
  - Association (ReferenceKey): resolve parent/target canonical ID.
  - Partitioning (PartitionKey): sharding/time routing; provider decides how to map.
  - Metadata: lineage/audit propagation.

### association and canonical ID mapping

- On aggregate upsert (e.g., `Sensor`), write `KeyIndex<T>` rows mapping `(namespace, sourceId[, composite]) → canonicalId`.
- On VO ingest (e.g., `SensorReading`):
  1) Persist `StageRecord<VO>` to `#flow.intake`.
  2) Association worker resolves canonical ID using `KeyIndex` and VO bindings.
  3) On hit, store VO with `SensorCanonicalId` (and denormalize `DeviceCanonicalId`). On miss, write `RejectionReport`.
  4) Enqueue `ProjectionTask<Aggregate>` to update canonical views.

### lineage

- Preserve `{ sourceSystem, sourceId }` on both aggregates and VOs; do not rely solely on canonical IDs.
- Diagnostics record the exact bindings used and any transforms applied.

### examples (production-safe)

- Ingest a VO (typed statics shown):
  - `SensorReading.AllStream(ct)` → stream readings for batch jobs.
  - `SensorReading.QueryStream(x => x.SensorCanonicalId == id && x.EventTime >= from && x.EventTime < to, ct)` → range stream.
  - `Sensor.FirstPage(pageSize, ct)`; `Sensor.Page(cursor, pageSize, ct)` for aggregates.

- Controllers (attribute-routed; no inline endpoints):
  - `GET /models/sensor/views/canonical/{sensorId}` → canonical.
  - `GET /models/sensorreading?sensorId&from&to` → stream/paged readings.

### edge cases

- Out-of-order events: order windows by event time; projections must tolerate reordering.
- Duplicates: enforce idempotency (composite of canonicalId, event time, and channel/sequence).
- Unknown parents: reject or park until aggregate appears; support admin replay.
- Rekey: update `KeyIndex` and reproject views as needed; VO TTL limits backfill scope.

### references

- Engineering front door: ../engineering/index.md
- Architecture principles: ../architecture/principles.md
- Data access patterns: ../guides/data/all-query-streaming-and-pager.md, ../decisions/DATA-0061-data-access-pagination-and-streaming.md
- Web API conventions: ../api/web-http-api.md, ../decisions/WEB-0035-entitycontroller-transformers.md
