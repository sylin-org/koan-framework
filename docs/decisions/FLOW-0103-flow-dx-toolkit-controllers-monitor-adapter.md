---
id: FLOW-0103
slug: flow-dx-toolkit-controllers-monitor-adapter
domain: flow
status: accepted
date: 2025-09-01
title: Flow DX toolkit — FlowEntityController, mutable OnProjected monitor, adapter metadata, and actions (OOB via AutoRegistrar)
---

## Context

Developers working with Flow need simple, out-of-the-box tools to operate on both sides of a pipeline:

- Orchestrators should expose safe HTTP endpoints for Flow models (roots and views), observe onboarding/projection/parked events, and initiate actions like seed/report.
- Adapters should easily stamp envelopes with consistent metadata (system/adapter/source/policies), handle orchestrator actions, and reply in a uniform way.

Koan guardrails apply:
- Controllers over inline endpoints; reference=intent canon for route design.
- Sane defaults and self-registration via KoanAutoRegistrar.
- First-class model statics for data access.

## Decision

Adopt a Flow DX toolkit with the following pillars:

1) FlowEntityController<TModel> atop EntityController
- Inherit root CRUD/page for `DynamicFlowEntity<TModel>` and add Flow routes for canonical/lineage, admin intents, and actions.
- Attribute-routed under `/api/flow/{model}` by default; can be overridden via options.

2) Flow monitor with mutable OnProjected hook
- `AddFlowMonitor` provides per-model hooks. `OnProjected<TModel>` receives a mutable reference to the materialized root (Model) and policy bag (Policies). Changes are committed atomically to `DynamicFlowEntity<TModel>.Model` and `PolicyState<TModel>.Policies`, then projection flags/tasks are cleared.
- `OnOnboarded`, `OnParked`, and `OnRejected` provide additional lifecycle signals.

3) Adapter metadata via [FlowAdapter]
- A class decorator declared by adapter processes describing `System`, `Adapter`, `DefaultSource`, `Policies[]`, and `Capabilities`.
- Auto-enrichment ensures intake payloads include `system`, `adapter`, and default `source` if missing; action handlers are registered only for declared capabilities.

4) Actions (reference=intent verbs)
- Orchestrators publish typed `FlowAction<TModel>` messages (verbs: `seed`, `report`, `ping` by default). Adapters reply with `FlowAck` (ok|reject|busy|unsupported|error) or `FlowReport` (free-form stats/metrics).
- CorrelationId and Model name are mandatory; ReplyTo is set by orchestrators.

5) OOB sane defaults + KoanAutoRegistrar
- A registrar in Flow Web/Runtime wires: controllers for all discovered `FlowEntity<T>` models, a default monitor, and action client/agent with the default verbs.
- Opt-out or override via `Koan:Flow` options.

## Scope

Applies to Flow Web/Runtime and adapter host processes. No changes required in core data semantics. Controller shape and monitor hooks are additive.

## Consequences

Pros
- Zero/low boilerplate: controllers, monitor, and actions available by default.
- Safer business rule injection point (OnProjected) with atomic commit and policy tracking.
- Consistent envelope metadata from adapters; uniform action/reply protocol.

Cons
- Adds surface area; requires concise documentation to avoid confusion.
- Mutable OnProjected requires guardrails (short execution, error handling) to avoid slow projections.

## Implementation notes

- Controllers: implement `FlowEntityController<TModel>` in Flow Web; register via `AddFlowControllers(o => o.AddForAllModels())` and KoanAutoRegistrar.
- Monitor: implement `ProjectionContext<TModel>` with `Model: IDictionary<string,object?>` and `Policies: IDictionary<string,string>`; commit occurs in the projection worker after hook returns.
- Adapter metadata: add `FlowAdapterAttribute` under `Koan.Flow.Attributes`; runtime reads it at startup and configures enrichment and capabilities.
- Actions: define `IFlowActions`, `FlowAction<TModel>`, `FlowAck`, `FlowReport` in Flow Messaging; map default verbs; wire a responder in adapter hosts.
- Options: enable/disable auto-registration via `Koan:Flow:AutoRegister` and customize route prefix/paging.

## Follow-ups

- Document controller routes and samples; include guidance on protecting admin/action endpoints.
- Add a sample in `samples/S8.Flow` using the controller set, monitor hooks, and adapter metadata.
- Provide unit tests for OnProjected commit semantics and action correlation.

## References

- FLOW-0101 — bindings, canonical IDs, value-object ingest
- FLOW-0102 — identity map, provisional mappings, and VO indexing
- DATA-0061 — data access pagination and streaming
- WEB-0035 — entity controller transformers
