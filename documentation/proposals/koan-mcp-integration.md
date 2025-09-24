# Koan MCP Integration Proposal

**Status:** Draft (Prerequisite Delivered)
**Author:** Koan Framework Team
**Date:** February 2025
**Version:** 1.2

## Abstract

`Koan.Mcp` is an optional module that exposes Koan entity endpoints to Large Language Models via the Model Context Protocol (MCP). With the Entity Endpoint Service extraction delivered in Koan v0.2.18, the framework now offers a protocol-neutral execution surface; MCP can reuse that surface to provide behaviour parity with REST and GraphQL while remaining configuration-light. This document redefines the proposal around the new baseline, outlines the transport architecture, and documents the work required to ship an initial release.

## Motivation

- **LLM-ready interactions**: Koan customers increasingly want agents (Claude Desktop, Cursor, VS Code agents) to call runtime endpoints using the MCP standard.
- **Avoid re-implementing controllers**: Prior integrations cloned controller logic, leading to drift. `IEntityEndpointService` lets MCP map calls to shared orchestration instead.
- **Reference = Intent**: Installing `Koan.Mcp` and decorating entities should be enough to expose tools; no bespoke servers or manual JSON-RPC wiring.
- **Future protocol family**: The descriptor metadata introduced with the extraction enables a unified discovery story for REST, GraphQL, MCP, jobs, and other surfaces.

## Prerequisite Status

- Entity Endpoint Service extraction: **Delivered** (Koan v0.2.18) — request/response contracts, context builder, hook pipeline abstraction, descriptor provider.
- Documentation for non-HTTP reuse: **Delivered** (`documentation/reference/web/entity-endpoint-service.md`).

## Solution Overview

`Koan.Mcp` consists of three pillars: discovery, execution, and transport hosting.

1. **Discovery**
   - `McpEntityAttribute` marks entities or descriptor types for exposure.
   - `McpEntityRegistry` queries `IEntityEndpointDescriptorProvider` to assemble tool metadata: operation kinds, supported shapes, dataset routing, pagination defaults.
   - Optional configuration (`Koan:Mcp`) provides allow/deny lists, transport settings, and schema overrides.

2. **Execution**
   - `EndpointToolExecutor` resolves `IEntityEndpointService<TEntity, TKey>`, maps MCP requests to the appropriate request DTO, and forwards the call.
   - Responses (success, validation errors, hook short circuits) are normalized into MCP tool responses with structured diagnostics and warnings.
   - Streaming hooks are future-compatible: the executor captures emitter output for transport layers that support incremental delivery.

3. **Transport Hosting**
   - `StdioTransport` supplies the default local experience (DevHost, dotnet run, CLI shells).
   - `HttpSseTransport` offers a lightweight remote option for IDEs that connect over HTTP + Server-Sent Events.
   - `WebSocketTransport` remains optional for long-lived sessions and streaming once protocol support stabilizes.
   - All transports reuse a shared session manager, capability handshake, and logging hooks.

```
+----------------+      JSON-RPC 2.0      +----------------------+      +----------------------------+
| MCP Clients    | <--------------------> | Koan.Mcp Transport   | -->  | IEntityEndpointService(...) |
| - Claude       |                        | - STDIO / HTTP / WS  |      | - Hook pipeline            |
| - Agent IDEs   |                        | - Tool Registry      |      | - Dataset routing          |
+----------------+                        +----------------------+      +----------------------------+
```

## Module Layout (proposed)

```
src/Koan.Mcp/
  McpEntityAttribute.cs
  McpEntityRegistry.cs
  DescriptorMapper.cs
  Schema/
    SchemaBuilder.cs
    SchemaExtensions.cs
  Execution/
    EndpointToolExecutor.cs
    RequestTranslator.cs
    ResponseTranslator.cs
  Hosting/
    McpServer.cs
    StdioTransport.cs
    HttpSseTransport.cs
    WebSocketTransport.cs
  Options/
    McpServerOptions.cs
    TransportOptions.cs
  Initialization/
    KoanMcpAutoRegistrar.cs
```

## Tool Definition Strategy

- `DescriptorMapper` converts `EntityEndpointDescriptor` data into MCP tool definitions.
- JSON Schema payloads originate from entity metadata, augmented by optional `McpDescriptionAttribute` hints for human-readable descriptions.
- Operations map as follows:
  - `Collection` / `Query` -> list tools (with filter, paging parameters).
  - `GetById` / `GetNew` -> read tools.
  - `Upsert`, `UpsertMany`, `Patch` -> mutation tools with request body schema.
  - `Delete*` operations -> destructive tools surfaced only when `AllowMutations` is true.
- Schema builder respects allowed shapes, relationship toggles, and dataset routing flags to guide LLM request construction.

### Schema Developer Experience

- Standard CRUD templates provide first-run schemas (e.g., `Collection`, `GetById`, `Upsert`) so teams can expose tools without authoring JSON manually.
- Field descriptions pull from data annotations when present; fallback labels use the property name and emit warnings in diagnostics so missing metadata is obvious.
- Per-entity schema artifacts are cached via `AggregateBags` with operation-specific bag keys, ensuring parity across transports while avoiding repeated reflection.
- Optional overrides (`McpEntityAttribute.SchemaOverride`) and future metadata providers keep the escape hatch open for bespoke schemas or external documentation sources.

## Transport Behaviour

### STDIO (default)
- Runs as a hosted service alongside the application (enabled via configuration or `[McpEntity(EnableStdio = true)]`).
- Streams JSON-RPC messages via standard input/output for local agents.
- Intended for development scenarios; can be disabled in production by configuration.

### HTTP + SSE
- Exposes `/mcp/sse` endpoint guarded by Koan authentication middleware.
- Keeps a long-lived SSE channel to deliver responses and streaming updates.
- Ideal for remote IDEs or managed MCP clients that cannot spawn STDIO child processes.
- Hands-on walkthrough: [Expose MCP over HTTP + SSE](../guides/mcp-http-sse-howto.md).

### WebSocket (optional)
- Provides bidirectional messaging for future streaming scenarios (chunked results, patch previews).
- Requires explicit opt-in and TLS when exposed beyond localhost.

### Logging & Diagnostics Defaults
- When STDIO transport is enabled, Koan logs default to STDERR or another sink to keep the JSON-RPC stream clean; configuration helpers document the switch.
- Remote transports emit structured events (`Koan.Transport.Mcp`) so existing observers capture session lifecycle, rate limiting hits, and warning propagation alongside REST traffic.
- Heartbeat and session-metric hooks reuse Koan health reporting, enabling dashboards to show parity with HTTP endpoints out of the box.

## Security & Governance

- Authentication piggybacks on existing Koan identity providers; transports construct `EntityRequestContext` with the authenticated principal.
- Authorization scopes can be declared via `[McpEntity(RequiredScopes = ...)]` and enforced before the executor runs.
- Rate limiting hooks into Koan middleware; transports publish diagnostic events compatible with Koan observability (Serilog, OpenTelemetry).
- Audit logging: each tool invocation records entity type, operation, dataset, and caller identity.

## Migration Plan

| Phase | Scope | Status |
| --- | --- | --- |
| 0. Foundation | Entity Endpoint Service extraction; documentation for non-HTTP contexts. | ✅ Koan v0.2.18 |
| 1. Core Runtime | Attribute, registry, descriptor mapper, executor, STDIO transport, minimal configuration, unit tests. | ⬜ Pending |
| 2. Metadata & Remote Access | Schema builder enhancements, HTTP+SSE transport, sample project, docs. | ⬜ Pending |
| 3. Advanced Capabilities | Streaming/WebSocket support, flow/event surfaces, diagnostics dashboard, deployment guidance. | ⬜ Pending |

### Detailed Tasks (Phase 1)

- [ ] Implement `McpEntityAttribute` with overrides (custom name, description, required scopes, disable mutations).
- [ ] Build `McpEntityRegistry` that caches descriptors and watches for configuration changes.
- [ ] Implement `DescriptorMapper` + `SchemaBuilder` (initial schema support: IDs, paging, filtering, body payloads).
- [ ] Create `EndpointToolExecutor` covering all CRUD verbs; include parity tests comparing REST vs MCP results.
- [ ] Deliver `StdioTransport` with JSON-RPC plumbing, heartbeats, and graceful shutdown.
- [ ] Add unit tests and integration harness exercising a sample entity end-to-end.

### Phase 2 Highlights

- [ ] Enrich schema descriptions with hook-provided summaries and examples.
- [ ] Add `HttpSseTransport` secured by bearer tokens and Koan middleware.
- [ ] Produce documentation: quickstart, sample configuration, CLI walkthrough.
- [ ] Deliver sample MCP client scripts for validation.

### Phase 3 Highlights

- [ ] Implement optional WebSocket transport with streaming emit hook support.
- [ ] Integrate Koan Flow events for notification-style MCP resources.
- [ ] Provide diagnostics endpoints (current sessions, tool metrics).
- [ ] Author deployment guide (reverse proxies, TLS, scaling considerations).

## JSON-RPC Runtime

- `Koan.Mcp` standardizes on `StreamJsonRpc` for JSON-RPC 2.0 handling across STDIO, HTTP+SSE, and WebSocket transports.
- A thin `IMcpTransportDispatcher` abstraction wraps the library so future transports or protocol upgrades can swap implementations without touching entity orchestration.
- Serialization defaults align with Koan''s JSON configuration (System.Text.Json, camelCase, relaxed escaping) to keep MCP payloads consistent with REST and GraphQL.
- Contract tests validate message framing, cancellation, and error propagation for each transport to ensure behaviour parity with REST controllers.

## Risks & Mitigations

| Risk | Description | Mitigation |
| --- | --- | --- |
| Tool overload | Exposing every operation can overwhelm LLMs. | Allow opt-out per operation, provide curated descriptions/examples. |
| Schema drift | Entities may add fields without updating descriptors. | Leverage descriptor provider cache invalidation and validation tests. |
| Security gaps | Misconfigured transports could bypass existing auth flows. | Default to disabled transports; require explicit configuration and Koan auth integration. |
| Streaming complexity | WebSocket streaming ties into hook emission semantics. | Ship streaming behind feature flag; reuse emitter hooks for consistency. |

## Open Questions

1. Should MCP expose query builder helpers (saved filters) as separate tools?
2. How do we communicate pagination metadata to LLMs (headers vs response payload)?
3. Do we need per-tenant throttling when MCP is exposed outside the cluster?
4. What metrics should the initial release emit for observability parity with REST?

## References

- `documentation/reference/web/entity-endpoint-service.md`
- `src/Koan.Web/Endpoints/*`
- `documentation/decisions/AI-0012-mcp-jsonrpc-runtime.md`
- Model Context Protocol specification: https://modelcontextprotocol.io
- ADR `documentation/decisions/AI-0005-protocol-surfaces.md`

---

This proposal supersedes earlier drafts and reflects the delivered prerequisite architecture. Feedback on scope, sequencing, and transport assumptions is welcome before Phase 1 execution begins.
