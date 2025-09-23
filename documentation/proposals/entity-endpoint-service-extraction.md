---
type: PROPOSAL
domain: web
title: "Entity Endpoint Service Extraction"
audience: [architects, developers, ai-agents]
date: 2025-02-14
status: delivered
last_updated: 2025-02-17
---

# Entity Endpoint Service Extraction (Post-Implementation Review)

Koan v0.2.18 delivered the Entity Endpoint Service initiative that was originally captured in this proposal. This document now serves as the authoritative record of what was shipped, why the change matters, and how future protocols should build on the shared abstractions.

## Executive Summary

- **Problem we solved**: `EntityController<TEntity, TKey>` bundled query parsing, hook orchestration, repository access, pagination, and response metadata directly into ASP.NET MVC actions. That tight coupling made it impossible for GraphQL, MCP, CLI agents, or background jobs to reuse the behaviour without duplicating logic.
- **What we shipped**: A protocol-neutral orchestration layer (`IEntityEndpointService<TEntity, TKey>` and supporting contracts) that owns entity CRUD flows. HTTP controllers, GraphQL adapters, and future surfaces now delegate to the same service while hooks continue to run in a deterministic order.
- **Impact**: REST behaviour stayed unchanged, hook authors gained a consistent context model, and new protocol adapters can expose entities without reverse engineering MVC internals. This extraction is the foundation for upcoming MCP integration and other AI-facing surfaces.

## Delivered Architecture

### Core Abstractions

| Component | Responsibility | Location |
| --- | --- | --- |
| `EntityEndpointService<TEntity, TKey>` | Implements collection/query/get/upsert/delete/patch flows, including pagination, shape/relationship handling, dataset routing, and hook execution. | `src/Koan.Web/Endpoints/EntityEndpointService.cs` |
| `IEntityEndpointService<TEntity, TKey>` | Protocol-neutral contract consumed by all front-ends. | `src/Koan.Web/Endpoints/IEntityEndpointService.cs` |
| `EntityRequestContext` + `EntityRequestContextBuilder` | Capture per-request services, query options, cancellation token, optional `HttpContext`, caller principal, headers, and warnings. | `src/Koan.Web/Endpoints/EntityRequestContext*.cs` |
| `EntityEndpointRequests/Results` | Strongly typed request/response envelopes for every verb. | `src/Koan.Web/Endpoints/EntityEndpointRequests.cs`, `EntityEndpointResults.cs` |
| `IEntityHookPipeline<TEntity>` | Wrapper around `HookRunner<TEntity>` that accepts an `EntityRequestContext`. | `src/Koan.Web/Endpoints/IEntityHookPipeline.cs` |
| `IEntityEndpointDescriptorProvider` | Exposes metadata (operations, defaults, shapes, relationships) for discovery-based protocols. | `src/Koan.Web/Endpoints/DefaultEntityEndpointDescriptorProvider.cs` |

All abstractions live in `Koan.Web.Endpoints` so non-MVC packages can reference them without inheriting HTTP dependencies.

### HTTP Controller Adapter

`EntityController<TEntity, TKey>` now acts as a thin translator:

1. Build `QueryOptions` from the current request, honoring `EntityEndpointOptions` and any `KoanDataBehaviorAttribute` overrides.
2. Use `EntityRequestContextBuilder` to create an `EntityRequestContext` seeded with services, the current principal, cancellation token, and (when available) `HttpContext`.
3. Populate a request DTO (`EntityCollectionRequest`, `EntityUpsertRequest<TEntity>`, etc.).
4. Call `IEntityEndpointService<TEntity, TKey>`.
5. Apply returned headers/warnings via existing `PrepareResponse` helpers and resolve hook short-circuits.

The observable HTTP surface (routes, payloads, headers) remains unchanged from pre-extraction behaviour.

### Hook Pipeline Behaviour

- Hooks now receive `HookContext<TEntity>` that wraps the shared `EntityRequestContext`.
- `HookContext.Http` remains available when invoked from HTTP, but hooks can no longer assume it is non-null.
- Short-circuit helpers (`ShortCircuit(IActionResult)` / `ShortCircuit(object)`) work across protocols.
- Warning collection and response headers flow through `EntityRequestContext` and are emitted by all adapters that surface them.

## Migration Recap

The extraction followed the phased plan outlined in the original proposal. Key highlights:

- **Phase 0** — Verified baseline behaviour with snapshot tests across collection, query, mutation, delete, and patch endpoints.
- **Phase 1** — Introduced `EntityRequestContext`, request/result envelopes, and the hook pipeline abstraction without modifying controller behaviour.
- **Phase 2** — Ported logic from `EntityController` into `EntityEndpointService` with parity tests covering pagination headers, relationship expansion, and shape transforms.
- **Phase 3** — Flipped controllers to call the service, removed duplicated logic, and reused the service inside the GraphQL adapter.
- **Phase 4** — Updated documentation, samples, and options. Exposed descriptor APIs for future adapters (MCP, DevHost automation).

## Testing & Validation

- Added deterministic unit tests across the service layer for every verb, verifying hook sequencing, capability headers, dataset routing, shape/relationship handling, and in-memory pagination fallbacks.
- Captured golden HTTP responses pre/post extraction to guard against regressions during the flip.
- Validated GraphQL operations against the same fixtures to ensure behavioural parity.

## Known Limitations & Follow-Up Work

1. **Pagination Heuristics**: `EntityEndpointService` still inspects `HttpContext` to infer pagination switches when adapters do not set `ForcePagination`. Non-HTTP callers should explicitly populate `EntityCollectionRequest.ForcePagination`.
2. **Hook Expectations**: Some custom hooks continue to rely on `HttpContext` for ancillary services. We documented best practices and will audit official packages to remove remaining dependencies.
3. **Descriptor Enrichment**: Metadata currently mirrors `EntityEndpointOptions`. Future work can augment descriptors with authorization requirements and examples to power richer tooling.

## Guidance for New Adapters

- Resolve `EntityRequestContextBuilder` from the active DI scope and build contexts with the correct principal and cancellation token.
- Surface headers and warnings returned by the service to keep clients aligned with REST semantics (e.g., GraphQL extensions, MCP diagnostics, CLI output).
- Inspect `IEntityEndpointDescriptorProvider` to drive schema generation, tool registration, or discovery flows.
- Prefer extending the hook pipeline (e.g., emitter hooks) rather than duplicating controller-specific logic.

## Changelog Snapshot

- Introduced `Koan.Web.Endpoints` namespace with the shared contracts and default implementations.
- Updated `EntityController<TEntity, TKey>` and derivative types to delegate to `IEntityEndpointService`.
- Added options wiring (`EntityEndpointOptions`) to expose pagination, shape, and relationship defaults.
- Documented the service in `documentation/reference/web/entity-endpoint-service.md` and linked migration guidance for contributors.

## References

- `src/Koan.Web/Endpoints/`
- `documentation/reference/web/entity-endpoint-service.md`
- `documentation/decisions/AI-0005-protocol-surfaces.md`
- Upcoming proposal: `documentation/proposals/koan-mcp-integration.md`

---

**Status:** Delivered in Koan v0.2.18. Future revisions should capture enhancements to the descriptor model, paging heuristics, and cross-protocol diagnostics.
