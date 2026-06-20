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

### Transports

STDIO (default, local-trust, ungated) plus the HTTP edge. `EnableHttpSseTransport` is the master HTTP switch — when on, the modern **Streamable HTTP** transport (AI-0037: a single `{baseRoute}` endpoint serving POST/GET/DELETE, spec 2025-06-18) is mounted by default. The deprecated legacy `/sse`+`/rpc` pair is a separate opt-in (`EnableLegacySseTransport`); both ride one session/dispatch core.

### Configuration (real `Koan:Mcp` keys)

```json
{
  "Koan": {
    "Mcp": {
      "EnableStdioTransport": true,
      "EnableHttpSseTransport": false,
      "EnableLegacySseTransport": false,
      "RequireAuthentication": false,
      "Exposure": "Auto",
      "AllowedEntities": ["Todo"]
    }
  }
}
```

### Authenticating the HTTP edge (SEC-0006)

Setting `Koan:Mcp:RequireAuthentication=true` makes `/mcp` an OAuth 2.1 **resource server**: it validates ES256 bearer tokens via the framework's `Koan.bearer` scheme, emits an RFC 9728 `WWW-Authenticate` challenge, serves `GET /.well-known/oauth-protected-resource/mcp`, and enforces the per-resource audience (RFC 8707 — `Koan:Mcp:ResourceUri` is the canonical id; a token for another resource is rejected). Once the bearer identity is in `context.User`, the **same** SEC-0004 `[Access]` gate chain runs unchanged. STDIO stays anonymous + `origin:local`.

The token issuer is the embedded **Authorization Server**, opt-in via **Reference = Intent**: reference `Koan.Web.Auth.Server` (no `AddKoanMcp()` / `UseAuthentication()` / `MapKoanMcpEndpoints()`). It lives at `/oauth/…` (distinct from `/auth/{provider}/` login), and the app renders only two pages — consent + done (`Koan:Mcp:Auth:ConsentPath` / `DonePath`). For local testing, `GET /oauth/dev-token` (Development only) mints a token for the current cookie user. Full flow + the two-page contract: [oauth-server-howto.md](../../../docs/guides/oauth-server-howto.md) and [mcp-http-sse-howto.md](../../../docs/guides/mcp-http-sse-howto.md).

### Operational toolsets (P3.2)

Reference `Koan.Mcp.Operations` (Reference = Intent) to ship governed **ops verbs** — `koan.jobs.{trigger,cancel,status}` + `koan.cache.{flush,flushAll}` — as `[McpTool]` verbs on `Toolset` subclasses. Each toolset is **opt-in + default OFF** via `Koan:Mcp:Operations:{Jobs,Cache}` (disabled ⇒ absent from `tools/list`). Every verb requires an `@ops:{jobs|cache}` **`AgentGrant`** (the SEC-0005 grant, exact-resource match — a `"*"` grant does NOT confer ops); destructive verbs (`cancel`, `flushAll`) need `confirm:true` else return a dry-run; every mutation writes an `AgentAction` audit row. Anonymous/STDIO callers can't hold a grant — ops are remote-governed. To author your own config-gated toolset, mark it `[McpOperationalToolset("key")]`; a custom verb can inject the caller via a `ClaimsPrincipal` parameter.

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
