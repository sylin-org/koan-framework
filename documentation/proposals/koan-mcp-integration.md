# Koan MCP Integration Proposal

**Status:** Draft
**Author:** Koan Framework Team
**Date:** February 2025
**Version:** 1.1

## Abstract

This proposal describes `Koan.Mcp`, an optional module that turns Koan entity endpoints into Model Context Protocol (MCP) tools once the HTTP-bound orchestration logic has been extracted into the shared `EntityEndpointService` layer (see `documentation/proposals/entity-endpoint-service-extraction.md`). By consuming the new service abstractions instead of instantiating MVC controllers directly, MCP gains parity with REST and GraphQL surfaces while preserving Koan's "Reference = Intent" philosophy: reference the package, opt-in the entity, and LLM agents can safely call it.

**Key Design Principles:**
- **Protocol Reuse**: Build on `EntityEndpointService` so REST, GraphQL, and MCP share the same behaviour and hook pipeline.
- **Attribute-Guided Opt-In**: Lightweight attributes advertise MCP intent without coupling controller logic to MCP.
- **Zero Configuration**: Package reference + attribute enables MCP, sensible defaults work out of the box.
- **Extendable Metadata**: Tool descriptions and schemas derive from endpoint descriptors, with room for custom overrides.

**Prerequisite:** Complete the Entity Endpoint Service extraction outlined in `documentation/proposals/entity-endpoint-service-extraction.md`.

## Background

### What is MCP?

The Model Context Protocol (MCP) is an open standard introduced by Anthropic that allows Large Language Models to interact with external systems through a consistent JSON-RPC 2.0 interface. Servers expose tools, resources, and prompts; clients discover and invoke them on behalf of an LLM. MCP is rapidly becoming the lingua franca for agent-style integrations.

### Current Gap

Today, exposing a Koan entity API to LLMs requires a bespoke MCP server that re-implements query parsing, hook execution, validation, and response shaping. This duplicates logic already living in `EntityController<TEntity, TKey>` and breaks the "Reference = Intent" developer promise.

With the planned extraction of the controller logic into `EntityEndpointService` (`documentation/proposals/entity-endpoint-service-extraction.md`), Koan gains a protocol-neutral execution surface. `Koan.Mcp` will sit on top of that service to provide an AI-oriented protocol adapter.

### Goals

1. **Rapid Enablement**: Developers opt-in entities for MCP exposure via attributes or configuration; no manual MCP wiring.
2. **Behaviour Parity**: MCP tools mirror REST semantics—hooks, dataset routing, pagination, validation all reuse the shared service.
3. **Rich Metadata**: MCP tool definitions include meaningful summaries, schemas, and examples to improve LLM success.
4. **Secure by Default**: Respect existing Koan auth policies and scopes; expose configuration for MCP-specific guards.
5. **Composable Transports**: Support STDIO for local DevHost workflows and HTTP(S)/WebSocket for remote scenarios.

---

## Proposed Solution

### Architectural Overview

Once `EntityEndpointService` is available, `Koan.Mcp` introduces an MCP adapter that:

1. **Discovers Eligible Endpoints**: Uses Koan's assembly cache and the existing auto-registration pattern to locate `IEntityEndpointDescriptor` instances. Only descriptors tagged with `[McpEntity]` (new attribute) or enabled via configuration are exposed.
2. **Generates Tool Definitions**: Converts descriptors (operations, shapes, filters, mutation capabilities) into MCP `Tool` definitions, including JSON Schemas sourced from `EntityEndpointService` metadata and optional overrides.
3. **Executes via Service Layer**: Dispatches MCP tool calls through `IEntityEndpointService<TEntity, TKey>` rather than invoking controllers, ensuring consistent hook execution and dataset handling without requiring `HttpContext`.
4. **Formats Responses**: Transforms `EntityEndpointResult` payloads and metadata into MCP responses (`content`, `isError`, warnings). HTTP-centric headers become structured diagnostics accessible to clients.
5. **Hosts MCP Server**: Provides pluggable transports (STDIO, HTTP+SSE, WebSocket) through a hosted service that can run alongside existing REST endpoints.

```
┌────────────────────┐       ┌────────────────────────┐       ┌──────────────────────┐
│ MCP Client (LLM)   │  RPC  │ Koan.Mcp Transport     │  →    │ EntityEndpointService │
│  • Claude Desktop  │──────▶│  • STDIO / HTTP / WS   │──────▶│  • Shared CRUD logic │
│  • Agentic Runtime │       │  • Tool Registry       │       │  • Hook Pipeline      │
└────────────────────┘       └────────────────────────┘       └──────────────────────┘
```

### Opt-In Model

- **`[McpEntity]` Attribute**: Developers decorate an entity controller or a dedicated descriptor class to signal MCP availability. The attribute can override defaults (tool prefix, description templates, security scopes).
- **Configuration Fallback**: `appsettings` can enable MCP globally or per entity using `Koan:Mcp:Entities` with allow/deny lists for environments.

### Metadata Source

`EntityEndpointService` exposes descriptors describing available operations (collection query, get, create/update, delete, bulk, patch) along with arguments, validation rules, and hook metadata. `Koan.Mcp` consumes these descriptors to:

- Build JSON Schema for tool parameters (filters, body payloads, identifiers).
- Inject templated descriptions using entity names and custom annotations (`McpDescriptionAttribute` optional for overrides).
- Surface capability flags (read/write/remove) so clients understand which tools exist.

### Execution Flow

1. MCP request arrives (`tools/call`, `resources/read`, etc.).
2. Transport resolves the matching tool and creates an `EntityEndpointInvocation` with:
   - `EntityRequestContext` seeded from MCP session (user claims, dataset selection, pagination hints).
   - Arguments converted into the expected request contract (e.g., `EntityCollectionRequest`).
3. Service executes the operation; hooks fire as usual.
4. Result metadata (warnings, pagination) is included in the MCP response's `content` and optional `diagnostics` extension.

### Module Structure (Revised)

```
src/Koan.Mcp/
├── Abstractions/
│   ├── IMcpServer.cs
│   ├── McpToolDefinition.cs
│   └── McpInvocationContext.cs
├── Attributes/
│   ├── McpEntityAttribute.cs
│   └── McpDescriptionAttribute.cs
├── Discovery/
│   ├── McpEntityRegistry.cs        # Scans descriptors via AssemblyCache
│   └── DescriptorMapper.cs         # Maps EntityEndpoint descriptors to MCP tools
├── Execution/
│   ├── EndpointToolExecutor.cs     # Calls IEntityEndpointService
│   └── SchemaBuilder.cs            # Generates JSON Schema from descriptors
├── Hosting/
│   ├── McpServer.cs
│   ├── StdioTransport.cs
│   ├── HttpSseTransport.cs
│   └── WebSocketTransport.cs
├── Options/
│   └── McpServerOptions.cs
└── Initialization/
    └── KoanAutoRegistrar.cs
```

### Tool Generation

Instead of reflecting the MVC action methods, `DescriptorMapper` reads the shared descriptor model:

```csharp
public McpToolDefinition Map(EntityEndpointDescriptor descriptor)
{
    var schema = _schemaBuilder.Build(descriptor.Parameters);
    return new McpToolDefinition
    {
        Name = descriptor.ToolName,
        Description = descriptor.GetDescription(),
        InputSchema = schema,
        RequiredScopes = descriptor.RequiredScopes,
        SupportsStreaming = descriptor.SupportsStreaming,
    };
}
```

`EntityEndpointDescriptor` comes from the service layer and already knows which hooks run, whether pagination applies, and what inputs it accepts.

### Transport Strategy

- **STDIO**: Default for local DevHost or CLI experiences; gated behind configuration in production.
- **HTTP+SSE**: Enables remote MCP clients via long-lived SSE sessions.
- **WebSocket**: Optional advanced transport for bidirectional streaming once available.

Each transport shares the same registry and executor, differing only in framing and session lifecycle.

### Security Model

- Inherits Koan authentication/authorization via `EntityRequestContext` (when the MCP client authenticates, the resulting principal flows into the hook pipeline).
- MCP-specific scopes can be declared via `[McpEntity(RequiredScopes = ...)]` or configuration.
- Supports rate limiting and audit logging using existing Koan middleware (transport hosts expose events to the logging pipeline).

---

## Prerequisite Alignment

`Koan.Mcp` depends on the successful delivery of the `EntityEndpointService` extraction. Key touchpoints:

- **Descriptor Access**: MCP must obtain operation metadata from the shared service (`IEntityEndpointDescriptorProvider`).
- **Context Construction**: MCP transports build `EntityRequestContext` instances without `HttpContext`, leveraging the protocol-agnostic design.
- **Hook Compatibility**: The hook pipeline must tolerate non-HTTP contexts while still exposing `HttpContext` when available.

If the extraction timeline slips, MCP implementation should pause after scaffolding the registry and transport shells until the shared service is ready.

---

## Migration Plan

### Phase 0: Foundation (Blocked by Prerequisite)
- [ ] Ship `EntityEndpointService` and descriptor APIs (`documentation/proposals/entity-endpoint-service-extraction.md`).
- [ ] Update documentation to guide hook authors on protocol-neutral practices.

### Phase 1: MCP Core
- [ ] Implement descriptor registry and mapping (`McpEntityRegistry`, `DescriptorMapper`).
- [ ] Build transport-agnostic executor that calls `IEntityEndpointService`.
- [ ] Provide STDIO transport and minimal configuration surface.
- [ ] Deliver initial unit tests covering descriptor mapping and execution flow.

### Phase 2: Metadata & Tooling
- [ ] Add schema generation, description templating, and overrides (`McpDescriptionAttribute`).
- [ ] Implement HTTP+SSE transport with authentication.
- [ ] Produce example project and walkthrough documentation.

### Phase 3: Advanced Integrations
- [ ] Streaming support and WebSocket transport.
- [ ] Flow/event integration for notification-style MCP resources.
- [ ] Diagnostics, observability hooks, and admin tooling.

---

## Benefits

- **Consistency**: MCP, REST, and GraphQL align on behaviour, reducing bug surface and maintenance.
- **Developer Velocity**: Opt-in attribute + package reference exposes entities to LLM agents without bespoke code.
- **AI Readiness**: Rich schemas and metadata increase success rates for agent tools and automated workflows.
- **Extensibility**: Descriptor-based design allows third parties to supply custom tool generators or transports.

---

## Considerations

- **Auth Surface**: Transport-level authentication must integrate with Koan identity providers; out-of-process clients require secure token exchange.
- **Transport Selection**: STDIO is unsuitable for many production deployments; configuration must default to safe choices.
- **Adoption Curve**: MCP ecosystem is emerging; prioritise documentation and samples to validate usefulness.
- **Testing**: End-to-end tests should run MCP calls against sample entities, comparing results to REST responses to guarantee parity.

---

## Conclusion

By anchoring MCP integration on the shared `EntityEndpointService`, Koan can offer a first-class AI protocol adapter without duplicating controller logic or compromising maintainability. The prerequisite extraction lays the groundwork for reliable cross-protocol behaviour; `Koan.Mcp` adds discovery, metadata, and transport hosting so LLM-driven tooling becomes a natural extension of existing applications.

Once the extraction milestone is complete, this proposal delivers the missing protocol surface called out in ADR `documentation/decisions/AI-0005-protocol-surfaces.md`, positioning Koan as the .NET framework that treats AI agents as first-class consumers alongside REST and GraphQL.
