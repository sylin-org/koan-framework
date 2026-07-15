---
uid: reference.modules.Koan.mcp
title: Koan.Mcp - Technical Reference
description: Model Context Protocol tools, resources, transports, and runtime inspection for Koan applications.
since: 0.2.x
packages: [Sylin.Koan.Mcp]
source: src/Koan.Mcp/
---

## Relationship expansion

MCP entity reads use the shared Web `IEntityEndpointService`; there is no MCP-specific relationship
loader. Related-type visibility, backend negotiation, result/candidate limits, and corrective
rejections therefore match REST. The selected or rejected child-edge strategy is inspectable through
`koan://facts`. Runtime facts are a latest-state snapshot, not an operation history.

## Contract

- Inputs: discovered MCP entities, custom tools, caller identity/grants, and host runtime facts.
- Outputs: MCP tools, resources, JSON-RPC results, and transport health.
- Error modes: invalid protocol envelopes, unavailable tools, denied grants, and transport failures.
- Success criteria: each caller sees only its permitted tools/resources and receives structured results.

## Runtime fact resource

`RuntimeFactsResourceProvider` owns `koan://facts`. It resolves `IKoanRuntimeFacts` from the same host
and serializes `Current` with `KoanFactJson`; it does not rebuild composition or scrape logs.

The resource contains no arbitrary provider payload, raw exception text, stack trace, or configuration
value. It can expose topology identifiers, so remote MCP transports must retain their authentication
and authorization posture.

Consumers must:

1. check envelope `schema` before binding new fields;
2. treat `complete: false` as unknown;
3. branch on fact `code`, `reasonCode`, `kind`, and `state`;
4. present `summary` and `correction` as guidance, not stable protocol tokens.

## Composition

- Resource providers register through `IMcpResourceProvider` and are projected by the shared MCP RPC
  handler.
- `koan://self` introduces the application and its caller-visible Entity plus custom-workflow surface.
- `koan://entities` describes caller-visible entities and verbs.
- `koan://facts` explains host composition decisions and degraded states.

Custom-tool visibility is calculated once by `CustomToolProjection` and reused by protocol listing,
remote dispatch, Explorer projection, and `koan://self`. A null principal preserves the established
trusted local-STDIO surface; a concrete remote principal applies server authentication, required
scopes, and operational-toolset enablement. Disabled or unauthorized tools leave no trace.

`McpCustomToolRegistry` owns custom input schemas. A tool declared with `IsMutation=true` advertises
the shared `McpDryRun.ArgumentName` control; the handler already accepts that control and returns an
honest non-executing partial rehearsal. Entity schema synthesis treats the CLR/wire property name as
the normal description fallback. Optional prose metadata improves agent guidance but its absence is
not an operational warning.

## Transports

STDIO is enabled by default. When `EnableHttpSseTransport` is true, Koan hosts modern Streamable
HTTP on the single `HttpSseRoute` (`/mcp` by default): client JSON-RPC, including `initialize`, uses
`POST`; an established session may use `GET` for resumable server push and `DELETE` for termination.
The initialize response mints `Mcp-Session-Id`, which the client echoes with the negotiated
`MCP-Protocol-Version` on subsequent requests.

`EnableLegacySseTransport` separately opts into the deprecated `/mcp/sse` plus `/mcp/rpc` shape.
Both HTTP shapes delegate to the same dispatcher, session, authorization, and projection core.

## References

- Runtime facts: `/docs/engineering/runtime-facts.md`
- ARCH-0111: `/docs/decisions/ARCH-0111-unified-runtime-facts.md`
- MCP conformance suite: `/tests/Suites/Mcp/Koan.Mcp.Conformance.Tests/`
