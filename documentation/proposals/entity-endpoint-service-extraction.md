---
type: PROPOSAL
domain: web
title: "Entity Endpoint Service Extraction"
audience: [architects, developers, ai-agents]
date: 2025-02-14
status: draft
---

# EntityController Service Extraction Proposal

**Document Type**: PROPOSAL  
**Target Audience**: Framework Architects, Runtime Maintainers, AI/Protocol Integrators  
**Date**: 2025-02-14  
**Status**: Draft for Review

---

## Executive Summary

Koan’s `EntityController<TEntity, TKey>` delivers a rich HTTP-first CRUD surface, but all behaviours—query shaping, hook execution, relationship enrichment, pagination, set routing, and response metadata—are hardwired to ASP.NET (`HttpContext`, MVC model binding, response headers). This tight coupling blocks reuse by other protocol layers (GraphQL, planned MCP, prospective CLI/DevHost surfaces) and forces every adapter to re‑implement the same orchestration logic.

This proposal extracts the controller’s core orchestration into a reusable service layer (`EntityEndpointService`) backed by explicit request/response contracts. HTTP controllers become thin adapters that call the service, while other front-ends (GraphQL, MCP, automation agents) share the same behaviour, hook pipeline, and policy enforcement. The design maintains backward compatibility for existing applications and opens a path for protocol-agnostic entity operations across Koan.

---

## Current State Analysis

### Monolithic Controller Responsibilities

`EntityController<TEntity, TKey>` currently owns:

- Query option construction and validation; direct access to `HttpContext.Request.Query` (`src/Koan.Web/Controllers/EntityController.cs:52`).
- Hook orchestration via `HookRunner<TEntity>` instantiated per request using `HttpContext.RequestServices` (`src/Koan.Web/Controllers/EntityController.cs:104-115`).
- Repository capability discovery and data access (`IDataService` lookups, `Data<TEntity, TKey>` static helpers) (`src/Koan.Web/Controllers/EntityController.cs:131-200`, `452-518`).
- Set routing with ambient `DataSetContext` for tenancy/partitioning across all verbs (`src/Koan.Web/Controllers/EntityController.cs:149-161`, `321-337`, `462-472`).
- Response shaping: pagination headers, RFC5988 links, `Koan-View`, payload transforms for `shape=map|dict` (`src/Koan.Web/Controllers/EntityController.cs:204-281`).
- Relationship graph enrichment behind the `with=all` switch (`src/Koan.Web/Controllers/EntityController.cs:245-261`, `360-379`, `434-442`).
- Mutation + bulk mutation flows, including validation, hook dispatch, capability headers, and dataset awareness (`src/Koan.Web/Controllers/EntityController.cs:450-570`).
- PATCH orchestration with JSON Patch application, id consistency checks, and re-use of save hooks (`src/Koan.Web/Controllers/EntityController.cs:626-664`).

All of these responsibilities hinge on `HttpContext`, making it impossible to invoke the controller logic outside MVC.

### Hook System Coupling

`HookContext<TEntity>` requires a live `HttpContext` and derives the current user, services, and response headers from it (`src/Koan.Web/Hooks/HookContext.cs:11-32`). Every controller action builds a `HookContext` with HTTP state and passes it to `HookRunner`. GraphQL already imitates this pattern with ad‑hoc instantiation (`src/Koan.Web.GraphQl/AddKoanGraphQlExtensions.cs:501-605`), highlighting duplication and limited reuse.

### Data Access Pattern

Data operations mix repository interfaces (`IDataRepository<TEntity, TKey>`, `ILinqQueryRepository`, `IStringQueryRepository`) with the static `Data<TEntity, TKey>` façade for saves and deletes. The controller decides when to call each API and is responsible for ambient `DataSetContext` scoping and pagination fallbacks.

### Response Semantics

HTTP specifics—header emission, Accept/view negotiation, `PrepareResponse` returning an `ObjectResult` (`src/Koan.Web/Controllers/EntityController.cs:94-101`)—are embedded in the controller. Other surfaces must replicate this behaviour (e.g., GraphQL copies pagination meta and hook emission) or forfeit consistency.

### Pain Points

- **No cross-protocol reuse**: other endpoints cannot call into `EntityController` without manufacturing a fake `HttpContext`.
- **Duplication**: GraphQL already mirrors logic; future MCP adapter would do the same.
- **Testing burden**: behaviour validation requires full HTTP integration tests; there’s no service layer to unit test in isolation.
- **Feature lag**: improvements (e.g., new hook stages, pagination metadata) must be reimplemented per surface.

---

## Goals

1. Introduce a protocol-agnostic service layer encapsulating entity CRUD orchestration, hook dispatch, dataset routing, and response metadata.
2. Preserve existing HTTP behaviour and public controller surface with zero breaking changes for consumers.
3. Enable other adapters (GraphQL, MCP, automation agents) to reuse the same service contracts and hook execution pipeline.
4. Keep hook extensibility intact while relaxing the hard dependency on `HttpContext`.
5. Improve testability by allowing direct unit tests against the new service contracts.

## Non-Goals

- Changing existing routing, authorization attributes, or controller inheritance model.
- Replacing the static `Entity<TEntity, TKey>`/`Data<TEntity, TKey>` façade or repository abstractions immediately (though the service may wrap them).
- Overhauling hook interfaces; only context plumbing changes are proposed.
- Delivering the MCP adapter itself (that will build on this work).

---

## Proposed Architecture

### 1. New Entity Endpoint Abstractions

Create a reusable package (initially inside `Koan.Web`, extracted later if needed) providing:

- `IEntityEndpointService<TEntity, TKey>`: orchestrates read, query, mutation, bulk, patch, and delete flows.
- Request contracts per operation (`EntityCollectionRequest`, `EntityQueryRequest`, `EntityMutationRequest`, `EntityDeleteRequest`, `EntityPatchRequest`). Each carries:
  - `EntityRequestContext` (see below).
  - Operation-specific payload (filters, ids, models, patch documents).
- Response envelopes (`EntityCollectionResult`, `EntityModelResult`, `EntityMutationResult`, etc.) containing payload, pagination metadata, warnings, and response headers.

```
Task<EntityCollectionResult<TEntity>> QueryAsync(EntityCollectionRequest request);
Task<EntityModelResult<TEntity>> GetAsync(EntityModelRequest<TKey> request);
Task<EntityMutationResult<TEntity>> UpsertAsync(EntityMutationRequest<TEntity> request);
// …
```

### 2. Protocol-Agnostic Request Context

Introduce `EntityRequestContext` (mutable builder `EntityRequestContextBuilder` for HTTP/GraphQL) capturing:

- Ambient services: `IServiceProvider`, optional `HttpContext`, `ClaimsPrincipal`, `CultureInfo`, `RequestItems`.
- Query options (`QueryOptions`, shape/view flags, `with` selection, set routing, ignoreCase).
- Pagination hints (page, size) and ability to register warnings.
- Cancellation token and diagnostics correlation ids.

`HookContext<TEntity>` evolves to depend on `EntityRequestContext` instead of raw `HttpContext`. HTTP adapter constructs the context from `ControllerContext.HttpContext`; GraphQL / MCP build it from their execution environment. For backward compatibility, `HookContext.Http` remains available but becomes nullable; hooks use helper methods to access `HttpContext` safely.

### 3. Hook Pipeline Service

Expose an `IEntityHookPipeline<TEntity>` that wraps `HookRunner<TEntity>`. It receives `EntityRequestContext` and handles ordering/short-circuit semantics. The pipeline becomes injectable, so both HTTP controllers and other adapters get a ready-to-use orchestrator without constructing runners manually.

### 4. Default Service Implementation

`DefaultEntityEndpointService<TEntity, TKey>` (internal) implements the interface by moving logic out of the controller:

- Uses injected dependencies: `IDataService`, `IEntityHookPipeline<TEntity>`, `ILogger<…>`, `IOptions<EntityEndpointOptions>`.
- Centralises dataset routing, repository capability checks, filter building (`JsonFilterBuilder`), string query execution, and relationship enrichment.
- Produces `EntityCollectionResult`/`EntityModelResult` that carry both the transformed payload and metadata (headers, view, pagination, warnings).
- Encapsulates Accept/view negotiation and shape transforms, returning those as metadata instead of writing headers directly.

### 5. Adapter Layer

- **HTTP Controller Adapter**: `EntityController<TEntity, TKey>` becomes a thin wrapper
  1. Build `EntityRequestContext` from `HttpContext` + query/body.
  2. Call the relevant service method.
  3. Apply response headers from the result envelope using existing `PrepareResponse` logic.

- **GraphQL Adapter**: Replace current duplicated logic (`src/Koan.Web.GraphQl/AddKoanGraphQlExtensions.cs:501-752`) with calls into the same service, reusing request builders tailored for GraphQL input.

- **Future MCP Adapter**: Provide an `EntityEndpointInvoker` that maps MCP tool calls to service requests, ensuring identical behaviour (hooks, validation, warnings) without touching MVC.

### 6. Response Metadata & Headers

The service returns metadata objects:

```
record EntityResponseMetadata
{
    IReadOnlyDictionary<string, string> Headers { get; }
    PaginationInfo? Pagination { get; }
    string? View { get; }
    IReadOnlyList<string> Warnings { get; }
}
```

HTTP adapter copies headers to `HttpResponse`. Non-HTTP adapters can surface metadata in their own format (e.g., GraphQL extensions payload, MCP diagnostics).

### 7. Configuration & Options

Introduce `EntityEndpointOptions` controlling defaults (page size, allowed shapes, relationship enrichment default, Accept negotiation). Controllers bind existing attributes (e.g., `KoanDataBehaviorAttribute`) into these options during context construction so behaviour stays identical.

### 8. Testing Strategy

- Unit tests for `DefaultEntityEndpointService` covering all verbs, hook behaviour, pagination, dataset routing, and shape transformations using mocked repositories and hook pipelines.
- Adapter-level integration tests to ensure HTTP responses remain unchanged compared to current behaviour (golden-file or snapshot approach).
- Regression tests for GraphQL and other adapters once migrated.

---

## Migration Plan

### Phase 0 – Baseline & Instrumentation
- Snapshot current integration behaviour (collection, query, mutation, delete, patch) for representative entities.
- Document hook expectations and identify hooks relying on direct `HttpContext` access.

### Phase 1 – Introduce Abstractions (No Behaviour Change)
- Add `EntityRequestContext`, response envelopes, and hook pipeline interfaces inside `Koan.Web`.
- Update `HookContext<TEntity>` to wrap the new context while preserving existing properties (`Http` becomes nullable but still populated for HTTP).
- Provide helpers for building contexts from `HttpContext` to keep controller code readable.

### Phase 2 – Implement `DefaultEntityEndpointService`
- Port logic from `EntityController` into the service class method by method (list/query/get/upsert/bulk/delete/patch).
- Ensure service produces identical headers and payloads via unit tests.
- Keep controller methods delegating to both old and new paths behind a feature flag for verification.

### Phase 3 – Flip Controller to the Service
- Replace in-method logic with calls to the service, using translation helpers to map request data to service requests and apply returned metadata.
- Remove the old inline logic once regression tests pass.
- Update GraphQL adapter to use the service instead of custom duplication.

### Phase 4 – Stabilisation & Documentation
- Update developer docs, samples, and guides to highlight the service layer and how other adapters should consume it.
- Expose the service for other packages (MCP, DevHost automation).
- Consider moving the abstractions to a new assembly (e.g., `Koan.Web.Endpoints`) if modularity demands it.

---

## Impact Assessment

- **HTTP APIs**: No route or payload changes. Response headers remain identical; behaviour validated via regression tests.
- **Hooks**: Continue to function; those accessing `HttpContext` still can, but the new context allows non-HTTP surfaces to provide alternatives (e.g., supply a fake principal). Documentation will advise avoiding direct `HttpContext` reliance when possible.
- **GraphQL**: Gains parity with REST behaviour, removing bespoke code and reducing bug surface.
- **Future Protocols**: MCP, gRPC, or CLI adapters can share the same service, reducing implementation time and ensuring consistent guardrails.
- **Testing**: Service-level tests reduce need for extensive controller integration suites.

---

## Risks & Mitigations

| Risk | Description | Mitigation |
| --- | --- | --- |
| Hidden `HttpContext` dependencies | Hooks or downstream services may expect non-null `HttpContext`. | Provide compatibility shims, audit existing hooks, and document best practices. |
| Behaviour drift | Porting logic into the service may alter edge cases (pagination headers, relationship enrichment). | Use golden tests comparing old/new responses, migrate incrementally per endpoint. |
| Performance regression | Additional abstraction layers could add allocations. | Benchmark current vs new service; design contracts to be struct-friendly/pooled where necessary. |
| DX confusion | Developers may be unsure whether to override controller methods or extend the service. | Document clear guidance: override controller only for HTTP-specific behaviour; provide extension points on the service (pipeline decorators, options). |

---

## Open Questions

1. Should the service live in `Koan.Web` or a new package (`Koan.EntityEndpoints`) to avoid MVC dependency for non-HTTP consumers?
2. How should we surface response metadata for GraphQL (e.g., GraphQL extensions vs. per-field)?
3. Do we need an abstraction over `Data<TEntity, TKey>` to simplify unit testing (mock-friendly repository gateway)?
4. Should hook interfaces evolve to accept a protocol identifier so hooks can tailor behaviour per surface?

---

## Next Steps

1. Approve the extraction blueprint and scope for Phase 1.
2. Create detailed technical tasks for introducing the request/response contracts and updating `HookContext`.
3. Schedule pairing sessions to port collection and single-model flows into the new service with regression coverage.
4. Draft updated documentation for contributors describing the new abstraction layer and migration guidelines.

---

**Outcome**: Establishing `EntityEndpointService` unlocks a shared execution surface for REST, GraphQL, MCP, and future protocols while preserving Koan’s hook model and reducing duplication. This enables the broader roadmap (MCP integration, AI-first surfaces) without compromising current developer experience.
