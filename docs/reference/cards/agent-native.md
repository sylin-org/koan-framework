---
type: REF
domain: mcp
title: "Agent-native projection — surface map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-19
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-19
  status: verified
  scope: docs/reference/cards/agent-native.md
---

# Agent-native projection — surface map

> One-screen map of how a Koan app presents itself to an AI agent — **one projection, two faces**: what you *declare* and what an agent *sees* are the same truth. Full detail: [09-agent-native-projection.md](../../assessment/09-agent-native-projection.md) · transport: [mcp.md](mcp.md).

**What it does** — Projects your app as `project(model × grant) → { tools, resources, schemas, errors }`: the *description* an agent reads and the *enforcement* it hits are the same per-caller projection, so they can't drift. An agent reaches it over MCP (tools + resources) and a human reaches the same entities over REST; both run the one governed read path with the same [WEB-0068](../../decisions/WEB-0068-query-options-predicates.md) visibility predicates ([AI-0012](../../decisions/AI-0012-mcp-jsonrpc-runtime.md)). **Walled-means-silent**: anything a caller may not see is *absent* — no count, no field name, no existence signal — never a redaction. Opt-in is Reference = Intent (`AddKoanMcp()`); the projection then writes itself from your declarations.

## What you DECLARE (the developer surface)

| Declaration | What it projects to the agent |
|---|---|
| `[McpEntity]` on an `Entity<T>` | The entity's `Save` / `Remove` / `Query` verbs become MCP tools, schema + visibility identical to REST. The entity's `[Access]` gate (SEC-0004) gates them — the same gate REST enforces; `AllowMutations=false` is read-only. |
| `[McpReadOnly]` · `[McpDestructive]` · `[McpIdempotent]` | Spec tool-annotation hints on a custom `[McpTool]` verb. Entity verbs derive these mechanically (Query/Get → readOnly, Delete* → destructive, Save* → idempotent); hand-written verbs gain nothing automatically — the dangerous ones must be marked. |
| `IMcpResourceProvider` (registered in DI) | A readable `koan://…` introspection resource, projected per grant. The framework ships `koan://entities` + `koan://self`; you add more over the same seam. |
| `[assembly: KoanApp(...)]` | The app identity (`Name`/`Description`) that `koan://self` renders as its first-person greeting — no MCP-specific app attribute. |
| `[McpDescription]` · `[McpIgnore]` | Per-property prose into the schema · hide a property from the agent (input/output/both) without touching storage or REST. |

```csharp
[McpEntity(Name = "Post", Description = "A blog post")]
[Access(write: "has:scope:posts:write")]   // SEC-0004 gate — same authority on REST + MCP
public sealed class Post : Entity<Post> { public string Title { get; set; } = ""; }

[assembly: KoanApp(Name = "Bloggr", Description = "A small blog you can manage by conversation.")]

public static class PostTools
{
    [McpTool(Name = "purge_drafts", Description = "Permanently deletes every draft.")]
    [McpDestructive]                                   // the agent sees destructiveHint: true
    public static async Task<int> PurgeDrafts(CancellationToken ct)
    {
        var drafts = await Post.Query(p => p.Title == "");
        foreach (var draft in drafts) await draft.Remove();
        return drafts.Count;
    }
}
```

## What the AGENT SEES (the consumer surface)

- **Tools** — spec-shaped `tools/list` objects: `inputSchema` (camelCase) + an `annotations` object carrying the verb hints. `[McpIgnore]` fields never appear in the input schema.
- **Resources** — `resources/list` / `resources/read`. `koan://entities` is the per-grant catalog (entities + the verbs *this caller* may use; a walled entity is absent), and each entity carries its navigable **edges** — `{ name, kind, target, via }` routes read from the declared `[Parent]` relationships. An edge is a *route, never a verb* (the whole graph is navigable without any edge becoming a tool), and an edge to a walled target is absent (its field name would leak the target's existence). Follow an edge by querying its `target` tool filtered on `via` — the one governed read path sizes the result to your grant. `koan://self` is the first-person introduction in **two faces in one resource** — a `prose` greeting *and* the `structured` contract beneath it (prose is the greeting, structured is the contract; prose is never the only form).
- **Per-grant projection** — every face is computed for the caller's grant. STDIO is local-trust (full); the remote HTTP/SSE edge enforces auth ∩ scopes through one shared `McpToolAccessPolicy`, so STDIO, HTTP/SSE, and Code Mode can't drift apart.
- **The pin** — pass `correlationId` on any call (or the server mints a GUIDv7); it's echoed in the result diagnostics for trajectory stitching. **Continuity ≠ authority**: the pin is opaque, untrusted, and gates nothing — authorization is always per-request against the grant.
- **Rehearsal & deltas** — every mutating tool accepts `dry_run: true`: the server runs the full validation pipeline, commits nothing, and returns the prospective state delta (`meta.diagnostics.delta` = `{ operation, changes: [{ field, from, to }] }`); a real run returns the **same-shaped** retrospective delta (rehearse → execute → same diff). A bad payload comes back with `didYouMean` corrections drawn from the **schema only** (enum members, required fields — never row data, so the error channel can't leak existence). The delta is walled-means-silent — an `[McpIgnore(Output)]` field never appears in it. A custom verb whose effects the framework can't inspect returns an honest *partial rehearsal* instead of executing.

## Frontier (designed, not yet shipped)

Grant disclosure (the **Door** — a locked-but-signposted verb), the headless **device-grant** auth on-ramp (RFC 8628, Reference = Intent over the configured providers), and **Streamable HTTP + OAuth 2.1**. Tracked in [09 §8 / the AN cards](../../assessment/prompts/07/AN-cards.md).

## Where it's exercised

`[McpEntity]` exposure, the `[Access]` gate, and the resource + self-introduction surfaces are exercised end-to-end against a real `AddKoan()` host by [`tests/Suites/Mcp/Koan.Mcp.Conformance.Tests`](../../../tests/Suites/Mcp/Koan.Mcp.Conformance.Tests). (A dedicated MCP showcase sample is being reworked.)
