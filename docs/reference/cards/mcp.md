---
type: REF
domain: mcp
title: "MCP — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-20
framework_version: source-first
validation:
  date_last_tested: 2026-06-20
  status: verified
  scope: docs/reference/cards/mcp.md
---

# MCP — pillar map

> One-screen map of the MCP pillar — exposing Koan entities + verbs to MCP clients. Walkthrough: [mcp-agent-native-howto.md](../../guides/mcp-agent-native-howto.md) (one entity from `[McpEntity]` to governed access). Transport detail: [mcp-http-sse-howto.md](../../guides/mcp-http-sse-howto.md).

**What it does** — Projects your `Entity<T>` types and hand-written verbs as Model Context Protocol tools that AI clients invoke over JSON-RPC through STDIO or one **Streamable HTTP** endpoint ([AI-0037](../../decisions/AI-0037-mcp-streamable-http-transport.md)). Annotate an Entity with `[McpEntity]` and its governed CRUD/query operations become MCP tools with the same read visibility as REST ([WEB-0068](../../decisions/WEB-0068-query-options-predicates.md)). Referencing `Koan.Mcp` is the whole opt-in. **Code Mode** ([AI-0014](../../decisions/AI-0014-mcp-code-mode.md)) lets a client compose several operations in one sandboxed script instead of one round-trip per call.

> **Beyond tools** — the server also emits spec-shaped tool annotations (`readOnly`/`destructive`/`idempotent`; mark custom verbs with `[McpReadOnly]`/`[McpDestructive]`/`[McpIdempotent]`), introspection **resources** (`koan://entities`, `koan://self`) over the `IMcpResourceProvider` seam, and an authority-free `correlationId` pin — all projected per grant. See the [agent-native projection card](agent-native.md).

> **Authenticated edge** ([SEC-0006](../../decisions/SEC-0006-embedded-oauth-authorization-server.md)) — with `Koan:Mcp:RequireAuthentication=true` the HTTP edge is an OAuth 2.1 **resource server**: it validates framework-issued ES256 bearer tokens (`Koan.bearer`), enforces the per-resource audience (RFC 8707), and serves RFC 9728/8414 discovery. The embedded **Authorization Server** (Reference = Intent via `Koan.Web.Auth.Server`) is the on-ramp that mints those tokens — once the identity lands in `context.User` the `[Access]` gate chain runs unchanged. See [mcp-http-sse-howto.md](../../guides/mcp-http-sse-howto.md) + [oauth-server-howto.md](../../guides/oauth-server-howto.md).

## The one canonical pattern

Annotate an `Entity<T>` with `[McpEntity]` — its `Save` / `Remove` / `Query` operations are exposed as
tools. **Reference = Intent:** referencing `Koan.Mcp` contributes its `McpModule`; `AddKoan()` activates
that module and its endpoint contribution. Ordinary applications write no MCP registration or mapping.

```csharp
[McpEntity(Name = "Todo", Description = "Task management entity")]
public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
}

// Program.cs — that's all. (Referencing Koan.Mcp hosts STDIO; referencing Koan.Web + Koan:Mcp:EnableHttpSseTransport
// adds the HTTP transport — Streamable HTTP by default. No AddKoanMcp() / AddKoanWeb() / MapKoanMcpEndpoints() to call.)
builder.Services.AddKoan();
```

`[McpEntity(AllowMutations = false)]` exposes read-only; `Exposure = "code"` routes the entity through Code Mode instead of raw tool calls.

## ≤5 attributes you'll use

| Attribute | What it does |
|---|---|
| `[McpEntity]` | Mark an entity for MCP exposure; `Name`, `Description`, `AllowMutations`, `Exposure` (`"auto"`/`"code"`/`"tools"`/`"full"`), `EnabledTransports`. Pure exposure — access is the entity's `[Access]` gate (SEC-0004), enforced identically on REST + MCP. |
| `[Access]` | The per-action access gate (SEC-0004) — `[Access(read: "anyone", write: "has:scope:posts:write")]`. The MCP edge gates entity tools through THIS (the same gate REST enforces); a walled verb is absent from `tools/list` and denied on call. Custom `[McpTool]` verbs keep `RequiredScopes`. |
| `[McpTool]` | Expose a public static method as a custom verb (not an entity CRUD op) over the same `tools/list` + `tools/call` surface; `RequiredScopes` gates it. |
| `[McpDescription("…")]` | Per-property description text surfaced into the generated JSON schema; optional `Operation` scope. |
| `[McpIgnore]` · `[McpIgnore(McpFieldDirection.Input)]` | Hide a property from MCP input, output, or both (mass-assignment / PII guard) without touching storage. |
| `[McpDefaults]` | Assembly-level defaults (`[assembly: McpDefaults(Exposure = "code")]`); overridden by config or per-entity attributes. |

## The escape hatch

When the operation isn't entity CRUD, write a custom verb instead of an entity tool — a public static method marked `[McpTool]`, discovered by `McpCustomToolRegistry` across loaded assemblies:

```csharp
public static class SearchTools
{
    [McpTool(Name = "search_recipes", Description = "Semantic recipe search")]
    public static async Task<IReadOnlyList<Recipe>> Search(string query, CancellationToken ct)
        => await Recipe.Query(r => r.Title.Contains(query));
}
```

Parameters of type `IServiceProvider` / `CancellationToken` are injected; every other parameter binds from the call `arguments` by name and contributes to the input schema. Give optional args a default to advertise them as optional.

## Where it's exercised

The full MCP surface — `[McpEntity]` tools, custom `[McpTool]` verbs, Code Mode, and the `koan://entities` / `koan://self` resources — is exercised end-to-end against a real `AddKoan()` host by [`tests/Suites/Mcp/Koan.Mcp.Conformance.Tests`](../../../tests/Suites/Mcp/Koan.Mcp.Conformance.Tests). (A dedicated MCP showcase sample is being reworked.)
