---
name: koan-mcp-integration
description: MCP server patterns, Code Mode integration, tool building
---

# Koan MCP Integration

## Core Principle

**Reference = Intent — referencing `Koan.Mcp` projects your entities to agents automatically.** You never hand-write a tool class or an `IMcpTool`: an `[McpEntity]` exposes its `Save`/`Remove`/`Query` verbs as MCP tools with the SAME schema, visibility, and `[Access]` gate as REST. Hand-written, non-CRUD actions are `[McpTool]` static methods. Access is the entity's `[Access]` gate (SEC-0004) — never a per-tool reimplementation. **Anti-pattern:** manually building tool/schema/result objects, or calling `AddKoanMcp()`/`MapKoanMcpEndpoints()` — the framework does all of it from the package reference.

## MCP Server Setup

### Basic MCP Server

```csharp
using Koan.Mcp;

// Reference = Intent: referencing the Koan.Mcp package IS the whole opt-in — its auto-registrar wires the MCP
// server (STDIO is hosted; HTTP/SSE is config-gated) and AddKoan() discovers it. There is NO AddKoanMcp() to
// call, and no MapKoanMcpEndpoints() either (the MCP endpoint contributor maps them inside Koan's pipeline).
// Any [McpEntity] in the app is then exposed automatically.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

### Expose entities and custom verbs

Entity verbs are exposed automatically — annotate the entity; write no tool class. A non-CRUD action is a `[McpTool]` static method.

<!-- validate -->
```csharp
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;          // .Save() / .Query() entity statics + extensions
using Koan.Data.Core.Model;    // Entity<T>
using Koan.Mcp;                // [McpEntity], [McpTool], [McpIdempotent]
using Koan.Web.Authorization;  // [Access]

// CRUD/query verbs (todo.get-by-id, todo.collection, todo.upsert, …) are projected automatically.
[McpEntity(Name = "todo", Description = "Task management")]
[Access(read: "anyone", write: "has:scope:todos:write")]   // the SAME gate REST enforces (SEC-0004)
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

// A verb that isn't entity CRUD: a public static method, marked with the right hint.
public static class TodoTools
{
    [McpTool(Name = "complete_all", Description = "Mark every open todo complete.")]
    [McpIdempotent]
    public static async Task<int> CompleteAll(CancellationToken ct)
    {
        var open = await Todo.Query(t => !t.Completed);
        foreach (var todo in open) { todo.Completed = true; await todo.Save(); }
        return open.Count;
    }
}
```

### Configuration (real `Koan:Mcp` keys)

```json
{
  "Koan": {
    "Mcp": {
      "EnableStdioTransport": true,
      "EnableHttpSseTransport": false,
      "RequireAuthentication": false,
      "Exposure": "Auto",
      "AllowedEntities": ["Todo"]
    }
  }
}
```

## Code Mode Integration

An agent can send one sandboxed JavaScript script (`koan.code.execute`) over an SDK mirroring your entities — `SDK.Entities.Todo.upsert({...})`, `.collection()`, `SDK.Out.answer(...)` — instead of N tool round-trips. The SDK runs through the **same gate / constrain / origin** as direct tool calls (not a privilege bypass). Quotas bound the call count; `[McpEntity(Exposure = "code")]` or `Koan:Mcp:Exposure` choose tools / code / both.

## When This Skill Applies

- ✅ Building MCP servers
- ✅ Claude integrations
- ✅ Tool development
- ✅ MCP Code Mode

## Reference Documentation

- **Walkthrough (start here):** `docs/guides/mcp-agent-native-howto.md` — one entity from `[McpEntity]` to governed access (grants/audit/door), what you write vs what the agent sees
- **Transport guide:** `docs/guides/mcp-http-sse-howto.md` — STDIO vs HTTP/SSE, auth, sessions, streaming
- **Conformance suite (end-to-end exercise):** `tests/Suites/Mcp/Koan.Mcp.Conformance.Tests` (a dedicated MCP showcase sample is being reworked)
- **Module:** `src/Koan.Mcp/`
