---
id: ARCH-0053
slug: sora-flow-pillar-entity-first-and-auto-registrar
domain: Architecture
status: accepted
date: 2025-08-30
title: Sora.Flow pillar — Entity-first pipeline with per-view sets and AutoRegistrar defaults
---

## Context

We need a neutral ingestion→association→projection pipeline that aligns with Sora principles: first-class entity statics, controllers-only HTTP, centralized constants/options, great DX, and provider neutrality. Existing "Orchestration" in Sora is the DevHost stack; to avoid confusion, the pipeline pillar is introduced as Sora.Flow.

## Decision

- Introduce Sora.Flow as a new pillar consisting of Core (runtime/contracts) and Web (controllers), with auto-registration via ISoraAutoRegistrar.
- Entity-first design: a single Record entity uses sets to represent ETL stages (intake/standardized/keyed). KeyIndex/ReferenceItem, ProjectionTask, ProjectionView<T>, RejectionReport, PolicyBundle are separate entities.
- Projections use per-view sets (ProjectionView<T>.Set(viewName)).
- Provider neutrality: no persistence adapters. Entities use first-class statics from Sora.Data.*
- Defaults via FlowOptions: batch size, stage concurrency, stage TTLs (7 days for intake/standardized/keyed, projection tasks; 30 days for rejections), DLQ enabled, default view name.
- Delivery adapters: default delivery is MQ for resilience; DLQ names are defined as constants.

## Scope

Applies to the new Sora.Flow.Core and Sora.Flow.Web modules. Multi-tenancy is not enabled in v1 but the model reserves fields/options for a future enablement.

## Consequences

- Simplifies persistence by leaning on Entity<> statics and sets. Improves DX and consistency.
- Clear separation from DevHost orchestration naming.
- Requires index/TTL initialization hooks; provided via bootstrap initializer and options.

## Implementation notes

- Constants centralize routes, sets, and DLQ names.
- Options path: Sora:Flow.
- AutoRegistrars wire defaults and surface info in BootReport.
- Workflow provider selection prefers Dapr when present, falls back to in-memory.

## Follow-ups

- Add Dapr workflow provider package and adapter SDK wiring.
- Add PolicyController and DistributionController, LineageController.
- Expand FlowBootstrap to apply indexes where supported.

## References

- DATA-0061, DATA-0030, ARCH-0040, DX-0038
