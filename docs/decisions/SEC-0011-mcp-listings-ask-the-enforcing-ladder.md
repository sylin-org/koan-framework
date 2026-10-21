---
id: SEC-0011
slug: mcp-listings-ask-the-enforcing-ladder
domain: Security
status: Accepted
date: 2026-10-21
title: MCP listings ask the authorization ladder the call enforces
related:
  - SEC-0002
  - SEC-0004
  - SEC-0005
  - SEC-0010
  - ARCH-0092
---

# SEC-0011: MCP listings ask the authorization ladder the call enforces

## Context

`tools/call` reaches `EntityEndpointService.Gate`, which asks `IAuthorize`: every `IAuthorizationProvider` in order
(the entity floor re-evaluates with active agent grants; applications may add PDP or ReBAC rungs), then
`AuthorizeOptions.DefaultDecision`. `tools/list`, `koan://entities`, `koan://self` and the Explorer map decided
visibility with `McpEntityGate.CoarseAllows`, which evaluates the compiled `[Access]` gate alone. With
`Koan:Web:Authorization:DefaultDecision=Forbid`, an undeclared entity's tools were listed and every call denied; a
subject holding an `AgentGrant` did not see the tool it could call; application providers were invisible to agents
(ledger NK-193). Advertisement was not enforcement.

## Decision

**A verb is listed for a caller exactly when the decision that enforces it would admit the caller.**

1. `EntityAuthorization.Decide` (Koan.Web) is that decision: the `IAuthorize` ladder for one action on one entity type,
   `null` to proceed. `EntityEndpointService.Gate` calls it; so does every MCP listing.
2. Listings run it through `McpVerbVisibility`, one per `tools/list` or resource read: a fresh DI scope, the scoped
   `IAuthorize`, memoized per (entity, action), as the principal the call runs as
   (`EntityRequestContextBuilder.NonHttpPrincipal`, which stamps an unstamped caller `origin:remote`).
3. The compiled gate keeps one job in listings: the `[Door]` `needs` text for a verb the caller cannot invoke.
4. `EntityCatalogResourceProvider`, `SelfResourceProvider` and `McpSurfaceProjector` gain constructors that take an
   `IServiceScopeFactory`, which DI selects. Their former constructors remain, marked obsolete, and list by the compiled
   gate alone.
5. A `null` principal (STDIO) still sees every verb; NK-189 tracks that trust model separately.

## Compatibility

- With a `Forbid` default decision, undeclared entities' tools disappear from `tools/list` and the catalogs for callers
  the default denies.
- A subject with an active agent grant now sees the granted verbs; an application `IAuthorizationProvider` now shapes
  what agents see.
- Listing costs one ladder evaluation per (entity, action) per listing. An external PDP rung is called at list time.
- Code constructing the three projections with the old constructors gets an obsolete warning.

## Guarantee and correction

- Every MCP listing agrees with the call for the same principal, whatever providers the application registers.
- The REST `Koan-Access` header and per-row manifest already came from the same seam; all surfaces now share one
  decision function.

## Evidence

- `ForbidDefaultParitySpec` and `GrantParitySpec` (Koan.Mcp.Conformance.Tests): with a `Forbid` default the undeclared
  collection is denied and absent from `tools/list` and `koan://entities`; an `AgentGrant` for `is:editor` admits the
  read and lists it in both. Both failed before this change at the listing assertion.
