# Koan.Flow — Model-typed pipeline (ingest → standardize → key → associate → project)

Contract (at a glance) — see also: [Bindings and canonical IDs](./flow-bindings-and-canonical-ids.md) and ADR [FLOW-0105](../decisions/FLOW-0105-external-id-translation-adapter-identity-and-normalized-payloads.md)
- Inputs: Normalized deltas (patch-like) per model over HTTP/MQ; options under Koan:Flow.
- Outputs: Per-model canonical projections and lineage; processed hot-stage records; diagnostics.
- Error modes: Rejections with reason/evidence; DLQs; readiness/health.
- Success: Deltas accepted, keyed and associated; projection tasks drained; canonical and lineage are queryable.

Minimal boot and auto-registration
- Generic hosts (adapters): `builder.Services.AddKoan();` — the Core host binder sets `AppHost`/`KoanEnv` automatically and Flow auto-registers `[FlowAdapter]` `BackgroundService`s. See ADR [FLOW-0106](../decisions/FLOW-0106-adapter-auto-scan-and-minimal-boot.md).
- Web hosts (APIs): Turnkey is ON by default — referencing `Koan.Flow.Web` auto-adds `AddKoanFlow()` unless disabled via `Koan:Flow:AutoRegister=false`. It's idempotent, so explicit calls are safe. Typical: `builder.Services.AddKoan(); app.UseKoan();`

Core types (first-class statics)
- FlowEntity<TModel> : Entity<TModel> — TModel is the canonical shape (e.g., Device)
- DynamicFlowEntity<TModel> : Entity<DynamicFlowEntity<TModel>> — normalized transport with Id + Model (ExpandoObject: nested JSON, provider-neutral)
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
- Optionally override/extend via options. Values are extracted from DynamicFlowEntity<TModel>.Model (dotted path, case-insensitive), normalized, and used to update KeyIndex<TModel>.

Normalized transport (patch deltas)
- DynamicFlowEntity<TModel> carries Model as a provider-neutral nested JSON shape (ExpandoObject/primitives/arrays). Adapters may send dotted-path properties; the projection worker expands them into nested objects before upserting the root snapshot. Deltas merge via deterministic policy (source priority, timestamp, or custom resolvers). Lineage tracks per-path provenance.

Hot → processed
- After successful projection, move records out of hot sets (intake/standardized/keyed) to processed (copy+delete) and/or apply TTLs. This reduces seek time on hot collections.

Messaging (per-model isolation)
- Exchanges/routing keys per model, e.g., Koan.flow.device (or routing keys flow.device.intake). Queues are provisioned per group+model. DLQs per stage remain: flow.{stage}.dlq.{model}.

Routes (controllers)
- GET /models/{model}/views/{view}
- GET /models/{model}/views/{view}/{referenceId}
- Legacy /views/* may be absent in new apps. Controllers resolve {model} to the registered type and query CanonicalProjection<TModel>/LineageProjection<TModel> against FlowSets.View<TModel>(view).

Options (Koan:Flow)

Adapter auto-start configuration (Koan:Flow:Adapters)
- AutoStart (bool): default true when running in containers; false otherwise.
- Include (string[]): optional whitelist of adapters by `"system:adapter"`.
- Exclude (string[]): optional blacklist of adapters by `"system:adapter"`.

Example
{
  "Koan": { "Flow": { "Adapters": { "AutoStart": true, "Include": ["oem:publisher"], "Exclude": [] } } }
}

Turnkey Flow runtime (web) — opt-out gate
- Key: `Koan:Flow:AutoRegister` (bool). Default: `true`.
- Behavior: When true, `Koan.Flow.Web` auto-calls `AddKoanFlow()` during module registration if it hasn't already been added. When false, nothing is added; call `AddKoanFlow()` explicitly in `Program.cs`.

Example (disable turnkey)
{
  "Koan": { "Flow": { "AutoRegister": false } }
}

Indexing and search

Edge cases

Examples (snippets)
  - public sealed class Device : FlowEntity<Device> { [AggregationTag("inventory.serial")] public string Serial { get; set; } = default!; }
  - await new DynamicFlowEntity<Device> { Id = "dev:123", Model = fromAdapter }.Save(FlowSets.Stage<Device>("intake"), ct);
  - using (DataSetContext.With(FlowSets.View<Device>("canonical"))) { var page = await CanonicalProjection<Device>.FirstPage(50, ct); }
  - Build normalized events consistently for entities and VOs: `FlowEvent.For<Device>()` or `FlowEvent.For<Reading>()`

See also

Direct send ergonomics and normalized ingestion
- Send entities directly: `await entity.Send(sourceId, occurredAt)` or `await entities.Send(sourceId, occurredAt)`; this builds a normalized bag internally and the server stamps adapter identity.
- If the publisher class is annotated with `[FlowAdapter(System, Adapter, DefaultSource = "...")]`, you may omit `sourceId`; the sender infers it from `DefaultSource` and passes the host type for stamping.
- Plain-bag ingestion: `FlowSendPlainItem.Of<TModel>(bag, sourceId, occurredAt)` with reserved prefixes (`identifier.external.*`, `reference.*`, `model.*`). Avoid client-side stamping.

## Reserved envelope and bag keys (adapter identity and external IDs)

Per FLOW-0105, adapters do not set canonical IDs. Instead, the sender/runtime stamps adapter identity and accepts external IDs in envelopes and normalized bags:

- Envelope metadata keys
  - `adapter.system` — stamped from `[FlowAdapter(system, adapter)]` on the publisher.
  - `adapter.name` — adapter variant.
  - `identifier.external.<system>` — one or more external/native IDs attached to the message.

- Normalized bag reserved keys
  - `model` — canonical entity key (e.g., `Keys.Device.Key`).
  - `reference.<entityKey>.external.<system>` — external ID for a referenced canonical entity (used to resolve `[ParentKey]`).

The ingestion resolver maintains an ExternalId index `(entityKey, system, externalId) -> canonicalId` and fills `[ParentKey]` properties before persistence. See FLOW-0105 for error modes and policies.

## Running with Dapr (notes)

- Canonical: Nested range → values object aligned to root snapshot. Each leaf path expands from dotted tags into nested objects and stores value arrays preserving insertion order (diagnostics-first; not a materialized single value).
- Lineage: tag → value → [sources] map; null values are skipped.

- Reference `Koan.Flow.Runtime.Dapr` to prefer the Dapr runtime.
## Dapr runtime provider

When `Koan.Flow.Runtime.Dapr` is referenced, the Dapr-backed runtime replaces the default provider automatically via AutoRegistrar.

Minimal configuration hints
- DAPR_HTTP_PORT / DAPR_GRPC_PORT
## Minimal E2E sample

1) Post a normalized delta
  - POST /models/device/intake with JSON: { "id": "dev-1", "data": { "inventory.serial": "INV-1001" } }
2) Trigger replay
  - POST /admin/replay
3) Query canonical view
  - GET /models/device/views/canonical?page=1&size=50

Notes
- Prefer first-class model statics (All/Query/FirstPage/Page/Stream). Use per-model sets via FlowSets.

## DX toolkit (controllers, monitor, adapter metadata, actions)

Contract (at a glance)
- Inputs: discovered `FlowEntity<T>` models; optional adapter metadata via `[FlowAdapter]`.
- Outputs: generic HTTP controllers for roots and views; monitor hooks; action sender/receiver.
- Defaults: auto-registered via KoanAutoRegistrar; route prefix `/api/flow`; verbs: seed/report/ping.

Controllers
- `FlowEntityController<TModel>` extends the base Entity controller for `DynamicFlowEntity<TModel>` and adds:
  - GET `/api/flow/{model}` and `/{id}` (roots)
  - GET `/api/flow/{model}/views/canonical/{id}`
  - GET `/api/flow/{model}/views/lineage/{id}`
  - POST `/api/flow/{model}/admin/reproject/{id}`
  - GET `/api/flow/{model}/admin/parked` and `/admin/rejections`
  - POST `/api/flow/{model}/actions/{verb}` (seed/report/ping)

Monitor (business rules)
- `AddFlowMonitor` exposes `OnProjected<TModel>(ctx)` with:
  - `ctx.Model: IDictionary<string,object?>` (mutable)
  - `ctx.Policies: IDictionary<string,string>` (mutable)
- The framework commits the changes atomically to `DynamicFlowEntity<TModel>.Model` and `PolicyState<TModel>.Policies` before clearing projection flags.

Adapter metadata
- `[FlowAdapter(System, Adapter, DefaultSource, Policies[], Capabilities)]` decorates adapter hosts.
- Auto-enriches intake payloads with `system`, `adapter` and enables default `source` inference for `Send()` calls.

Actions
- `IFlowActions` to send; adapters reply with `FlowAck`/`FlowReport`.
- Correlated by `CorrelationId`, model-qualified.

Options
- Toggle auto-registration: `Koan:Flow:AutoRegister`.
- Override route prefix/paging; extend verbs.

