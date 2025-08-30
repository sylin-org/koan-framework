# Sora.Flow — Entity-first pipeline (ingest → associate → project)

Contract (at a glance)
- Inputs: Intake records over HTTP or MQ; options at Sora:Flow.
- Outputs: Projection views (per-view sets), lineage via ReferenceItem, diagnostics.
- Error modes: Reject with reason/evidence; DLQs; readiness via health endpoints.
- Success: Records accepted at /intake/records; projection tasks created and views materialized; lineage and policies queryable.

Key entities and first-class statics
- Record (sets: intake|standardized|keyed): Save(set), All/Query/AllStream/FirstPage/Page
- KeyIndex (AggregationKey → ReferenceId): Get/Save/TryAssignOwner
- ReferenceItem (ReferenceId, Version, RequiresProjection): Get/Query/FirstPage/QueryStream
- ProjectionTask: EnqueueIfAbsent/Query
- ProjectionView<T>.Set(viewName): Save/Get/Query/AllStream/FirstPage/Page
- RejectionReport: Save/Query
- PolicyBundle: GetActive/Put

Routes (controllers only)
- POST /intake/records
- GET /views/{view}/{referenceId}
- GET /views/{view}?q=...
- GET /lineage/{referenceId}
- GET/PUT /policies
- POST /admin/replay, /admin/reproject

Options (Sora:Flow)
- Concurrency (Standardize/Key/Associate/Project)
- BatchSize (default 500)
- TTLs: Intake/Standardized/Keyed=7d; ProjectionTask=7d; RejectionReport=30d
- PurgeEnabled=true; PurgeInterval=6h (background TTL purge of stage records, tasks, and rejections)
- DeadLetterEnabled=true
- DefaultViewName="canonical"

Messaging
- Default delivery is MQ (resilient), using Sora.Messaging.* — do not implement bespoke MQ adapters in Flow code.
- DLQ names: flow.intake.dlq, flow.standardized.dlq, flow.keyed.dlq, flow.association.dlq, flow.projection.dlq
 - Runtime provider: InMemory by default; when the Dapr runtime package (Sora.Flow.Runtime.Dapr) is referenced, it replaces the default provider automatically via AutoRegistrar.

Bootstrap
- On startup, Flow ensures schemas exist for key entities and sets via data.ensureCreated (idempotent). This covers stage sets (intake/standardized/keyed), associations, tasks, rejections, and policies.
- Indexes are declared via [Index] attributes on commonly queried fields (SourceId, OccurredAt, CorrelationId, ReferenceId, ViewName, Version). Adapters apply them when supported.
- TTLs are option-driven defaults and provider-conditional. Flow runs a lightweight, provider-neutral purge loop (configurable) that deletes expired items via Entity statics. For stores with native TTL, prefer configuring TTL at the store; for others, this loop provides a safe baseline.

Examples (snippets)
- Save an intake record
  - await new Record { RecordId = Guid.NewGuid().ToString("n"), SourceId = "crm", OccurredAt = DateTimeOffset.UtcNow, StagePayload = payload }.Save("intake", ct)
- Page projection views
  - var page = await ProjectionView<UserCanonical>.FirstPage(50, ct); // use Set(viewName) for per-view set operations

See also
- Decisions: ARCH-0053, DATA-0061, DATA-0030, ARCH-0040, DX-0038

## Dapr runtime provider

When you add a reference to `Sora.Flow.Runtime.Dapr`, the DI container prefers the Dapr-backed runtime over the in-memory default. No additional wiring is required.

Minimal configuration hints (subject to change as the runtime evolves):
- DAPR_HTTP_PORT / DAPR_GRPC_PORT: provide when running sidecar.
- State components and workflow configuration are owned by the app; Flow does not create them.
- Flow uses standard Entity statics for data. The runtime enqueues ProjectionTask entities as needed.

## Minimal E2E sample

1) Post an intake record
  - POST /intake/records with JSON: { "sourceId": "crm", "occurredAt": "2025-08-30T12:00:00Z", "payload": { "id": "u1" } }
2) Trigger a replay (enqueue projection tasks for references that need projection)
  - POST /admin/replay
3) Query a view page
  - GET /views/canonical?page=1&size=50

Notes
- View data shape is domain-specific; examples use `object` for simplicity. Use `ProjectionView<T>` with per-view sets for typed projections.
