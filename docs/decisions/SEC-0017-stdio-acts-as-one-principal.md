---
id: SEC-0017
slug: stdio-acts-as-one-principal
domain: Security
status: Accepted
date: 2026-10-21
title: STDIO acts as one principal for listing, resources and calls
related:
  - SEC-0004
  - SEC-0011
  - AI-0037
---

# SEC-0017: STDIO acts as one principal for listing, resources and calls

## Context

The STDIO transport binds the raw `McpRpcHandler`. Its `tools/call` ran as an anonymous principal stamped
`origin:local`, but its `tools/list` was unfiltered and its `resources/*` projected for a `null` principal, which the
entity projection treats as full trust. Over STDIO, `tools/list` and `koan://entities` advertised every verb, while every
call on an entity gated `authenticated` (or by a scope or role) was challenged unless the gate also granted
`origin:local`. Agents planned against tools that always failed (ledger NK-189).

## Decision

**The raw JSON-RPC channel acts as `McpRpcHandler.StdioPrincipal()`, an anonymous principal stamped `origin:local`,
for `tools/list`, `resources/list`, `resources/read` and `tools/call`.**

1. `tools/list` lists the exposed tools and keeps an entity tool only when that principal may call it, through the
   SEC-0011 decision. Custom verbs keep their exposure-only listing, matching their call path.
2. The resource methods project for that principal, so `koan://entities` and `koan://self` agree with the calls and
   disclose `[Door]` needs as they would to any anonymous caller.
3. HTTP transports filter the same exposed list for their own request principal.
4. A `null` principal passed directly to a resource provider still means full trust, for privileged in-process tooling.

## Compatibility

- STDIO clients no longer see entity tools they cannot call: entities gated `authenticated`, by scope or by role
  disappear from STDIO `tools/list` and catalogs. `origin:local` and open entities stay.
- STDIO catalogs now show `[Door]` doors for scope-gated verbs.
- `McpRpcHandler.StdioPrincipal` is added; no signature changes.

## Evidence

- `StdioTrustSpec` (Koan.Mcp.Conformance.Tests): a scope-gated entity's call is denied and the entity is absent from
  `tools/list` and `koan://entities`; an `origin:local` entity is listed and callable. The listing cell failed before.
- Three conformance cells that pinned full STDIO trust (`ResourcesConformanceSpec`, `DoorProjectionSpec`,
  `EdgeTraversalSpec`) now read as the STDIO principal or as a scoped caller.
