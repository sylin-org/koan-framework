---
type: REFERENCE
domain: mcp
title: "Agents"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: governed MCP Entity, tool, resource, and transport entry points
---

# Agents

Use this pillar when an application must expose governed Entity operations, custom tools, or runtime
self-description to an MCP client.

## Smallest Entity surface

```csharp
[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
public sealed class Todo : Entity<Todo>;
```

Reference `Sylin.Koan.Mcp` and keep the ordinary `AddKoan()` host. The module contributes discovery,
tools, resources, and the configured transport. Applications do not call a separate MCP registration
method or manually map MCP endpoints.

## Guarantee and correction

`[McpEntity]` projects applicable Entity operations through the same model and access policy used by
other surfaces. `[McpTool]` owns custom operations that are not Entity CRUD. Field exposure attributes
shape input/output schemas without changing persistence.

An operation that fails access, input binding, capability, or transport requirements returns the
specific protocol/application correction. A referenced MCP package does not make every Entity or
method agent-visible.

## Declare what the client may see

| Declaration | Result |
|---|---|
| `[McpEntity]` | Projects applicable Entity operations and schema |
| `[Access(...)]` plus `EntityAccess<T>` | Applies the same gate and row constraint as other Entity projections |
| `[McpTool]` | Adds one custom operation that is not Entity CRUD |
| `[McpDescription]` / `[McpIgnore]` | Describes or omits fields in the agent schema |
| `[McpReadOnly]`, `[McpDestructive]`, `[McpIdempotent]` | Adds behavioral hints to a custom tool |
| `IMcpResourceProvider` | Adds an inspectable `koan://...` resource |

Advertisement and enforcement share the caller's grant. A walled Entity, verb, edge, or field is
absent rather than replaced with an existence-leaking placeholder. Tool-call failures retain the
same access, input, capability, and correlation meaning as the application operation beneath them.

## Choose a transport deliberately

- STDIO is the local process default.
- Streamable HTTP is the current network transport.
- Network exposure requires the application's authentication, authorization, origin, proxy, and TLS
  decisions; MCP does not create those policies.

Inspect `koan://facts`, `koan://entities`, and `koan://self` before guessing which resources and tools
were compiled.

## Deeper contracts

- [Agent-native task guide](../../guides/mcp-agent-native-howto.md)
- [MCP HTTP transport](../../guides/mcp-http-sse-howto.md)
- [MCP package](../../../src/Koan.Mcp/README.md)
- [Product and package surface](../product-surface.md)
