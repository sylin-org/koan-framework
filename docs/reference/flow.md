# Sora.Flow — Model-typed pipeline (ingest → standardize → key → associate → project)

Contract (at a glance)
- Inputs: Normalized deltas (patch-like) per model over HTTP/MQ; options under Sora:Flow.
- Outputs: Per-model canonical projections and lineage; processed hot-stage records; diagnostics.
- Error modes: Rejections with reason/evidence; DLQs; readiness/health.
- Success: Deltas accepted, keyed and associated; projection tasks drained; canonical and lineage are queryable.

Core types (first-class statics)
- FlowEntity<TModel> : Entity<TModel> — TModel is the canonical shape (e.g., Device)
- DynamicFlowEntity<TModel> : Entity<DynamicFlowEntity<TModel>> — normalized transport with Id + JObject Data
- StageRecord<TModel> : intake | standardized | keyed | processed
- KeyIndex<TModel> : AggregationKey → ReferenceId
- ReferenceItem<TModel> : ReferenceId, Version, RequiresProjection
- ProjectionTask<TModel>
- ProjectionView<TModel, TView> with per-view sets; CanonicalProjection<TModel>, LineageProjection<TModel>

Sets and naming (per model)
- flow.{model}.intake | standardized | keyed | processed
- flow.{model}.tasks
- flow.{model}.views.{view} (e.g., canonical, lineage)
- Helpers: FlowSets.Stage<TModel>(Stage.Intake), FlowSets.View<TModel>("canonical")

Aggregation (keys)
- Use attributes on the model to declare aggregation tag paths for association:
  - [AggregationTag("person.identifier.username")]
  - [AggregationTag("person.employee.email")]
- Optionally override/extend via options. Values are extracted from DynamicFlowEntity<TModel>.Data (dotted path, case-insensitive), normalized, and used to update KeyIndex<TModel>.

Normalized transport (patch deltas)
- DynamicFlowEntity<TModel> carries Data as JObject (or JsonNode) with dotted-path properties from adapters. Deltas merge into TModel via deterministic policy (source priority, timestamp, or custom resolvers). Lineage tracks per-path provenance.

Hot → processed
- After successful projection, move records out of hot sets (intake/standardized/keyed) to processed (copy+delete) and/or apply TTLs. This reduces seek time on hot collections.

Messaging (per-model isolation)
- Exchanges/routing keys per model, e.g., sora.flow.device (or routing keys flow.device.intake). Queues are provisioned per group+model. DLQs per stage remain: flow.{stage}.dlq.{model}.

Routes (controllers)
- GET /models/{model}/views/{view}
- GET /models/{model}/views/{view}/{referenceId}
- Legacy /views/* may be absent in new apps. Controllers resolve {model} to the registered type and query CanonicalProjection<TModel>/LineageProjection<TModel> against FlowSets.View<TModel>(view).

Options (Sora:Flow)
- Concurrency per stage; BatchSize (default 500)
- TTLs: Hot=short; Processed=longer/archival
- PurgeEnabled=true; PurgeInterval=6h (provider-neutral TTL purge)
- DeadLetterEnabled=true
- Discovery: Auto-discover FlowEntity<T> models via reflection (no source generator). Allow [FlowModel("device")] rename and [FlowIgnore] opt-out; constrain scan scope via options.

Indexing and search
- KeyIndex<TModel> remains the source of truth for tag→owner lookups.
- Optional denormalized search terms on Keyed<TModel> (e.g., AggregationTerms: ["person.identifier.username=jdoe"]) can accelerate ad-hoc filters (multikey index in Mongo). Provider adapters may opt-in to additional indexes.

Edge cases
- Missing aggregation values: skip; do not assign keys.
- Conflicts across sources: resolve via policy; log provenance.
- High fan-out of tags: cap per record and DLQ excess.
- Trimming/AOT: reflection-based discovery requires linker hints or a config to limit scan scope.

Examples (snippets)
- Define a canonical model
  - public sealed class Device : FlowEntity<Device> { [AggregationTag("inventory.serial")] public string Serial { get; set; } = default!; }
- Ingest a normalized delta
  - await new DynamicFlowEntity<Device> { Id = "dev:123", Data = fromAdapter }.Save(FlowSets.Stage<Device>("intake"), ct);
- Page canonical view
  - using (DataSetContext.With(FlowSets.View<Device>("canonical"))) { var page = await CanonicalProjection<Device>.FirstPage(50, ct); }

See also
- Decisions: ARCH-0053, DATA-0061, DATA-0030, ARCH-0040, DX-0038

## Running with Dapr (notes)
- Reference `Sora.Flow.Runtime.Dapr` to prefer the Dapr runtime.
- Run with a Dapr sidecar (DAPR_HTTP_PORT/GRPC). Flow doesn’t create components.
- Replay enqueues ProjectionTask<TModel> for references requiring projection.

## Dapr runtime provider

When `Sora.Flow.Runtime.Dapr` is referenced, the Dapr-backed runtime replaces the default provider automatically via AutoRegistrar.

Minimal configuration hints
- DAPR_HTTP_PORT / DAPR_GRPC_PORT
- State components/workflows are provided by the app.
- Flow uses Entity statics for data and enqueues ProjectionTask<TModel> as needed.

## Minimal E2E sample

1) Post a normalized delta
  - POST /models/device/intake with JSON: { "id": "dev-1", "data": { "inventory.serial": "INV-1001" } }
2) Trigger replay
  - POST /admin/replay
3) Query canonical view
  - GET /models/device/views/canonical?page=1&size=50

Notes
- Prefer first-class model statics (All/Query/FirstPage/Page/Stream). Use per-model sets via FlowSets.
