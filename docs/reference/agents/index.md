---
type: REFERENCE
domain: mcp
title: "Agents"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-23
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: governed MCP Entity, tool, resource, and transport entry points
---

# Agent, meet your app.

Your `Todo` already persists. It may already have an HTTP API. Add Koan's MCP package and one
declaration:

```powershell
dotnet add package Sylin.Koan.Mcp
```

```diff
+using Koan.Mcp;
+
+[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
 public sealed class Todo : Entity<Todo>;
```

**That's it.**

An MCP client can now discover `Todo`, understand its shape, and use the operations available to
that caller.

Same Entity. Same data. Same access rules.

No second domain model. No mirrored service. No handwritten CRUD tools. The ordinary `AddKoan()`
host stays exactly as it is—the package reference brings MCP into the application.

## One idea, every doorway

```csharp
[McpEntity(Name = "Todo", Description = "Work the team intends to finish")]
public sealed class Todo : Entity<Todo>;

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

A Todo created by an agent is immediately visible through the HTTP API. A Todo created through HTTP
is immediately available to the agent. Both reach the same model and the same application policy.

## The magic respects your walls

An agent sees only what you deliberately expose and what its identity may use.

| Write this | The agent gets |
|---|---|
| `[McpEntity]` | The applicable Entity operations and schema |
| `[Access(...)]` and `EntityAccess<T>` | The same gate and row boundaries used by other Entity surfaces |
| `[McpTool]` | One business action beyond Entity operations |
| `[McpDescription]` and `[McpIgnore]` | A clearer schema with only the fields you intend |
| `[McpReadOnly]`, `[McpDestructive]`, and `[McpIdempotent]` | Useful behavioral hints on a custom tool |
| `IMcpResourceProvider` | An inspectable `koan://...` resource |

A forbidden Entity, operation, relationship, or field simply does not appear for that caller. Failed
calls preserve the application's access, input, capability, and correlation details so the client
can respond intelligently.

Referencing MCP does **not** expose every Entity or method, invent an authorization policy, or turn
behavioral hints into enforcement. Your application still decides who may do what.

## Start close. Open the network deliberately.

STDIO is the default: ideal when a local client owns the Koan process.

Streamable HTTP is available when an MCP client must reach the application over a network. Enable it
deliberately, then apply the same authentication, authorization, origin, proxy, and TLS decisions you
would to any other application endpoint. MCP does not create those policies for you.

## Let the application introduce itself

An agent does not have to guess what it connected to:

- `koan://self` introduces the application.
- `koan://entities` describes the Entity operations available to this caller.
- `koan://facts` explains what Koan composed, with sensitive values redacted.

## Make the next move

- [Build an agent-native workflow](../../guides/mcp-agent-native-howto.md)
- [Reach Koan over MCP HTTP](../../guides/mcp-http-sse-howto.md)
- [See what works in Koan 0.20](../what-works.md)
