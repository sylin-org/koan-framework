---
type: REF
domain: mcp
title: "Agent-native projection — surface map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-06-20
  status: verified
  scope: docs/reference/cards/agent-native.md
---

# Agent-native projection — surface map

> One-screen map of how a Koan app presents itself to an AI agent — **one projection, two faces**: what you *declare* and what an agent *sees* are the same truth. Full detail: [09-agent-native-projection.md](../../assessment/09-agent-native-projection.md) · transport: [mcp.md](mcp.md).

**What it does** — Projects your app as `project(model × grant) → { tools, resources, schemas, errors }`: the *description* an agent reads and the *enforcement* it hits are the same per-caller projection, so they can't drift. An agent reaches it over MCP (tools + resources) and a human reaches the same entities over REST; both run the one governed read path with the same [WEB-0068](../../decisions/WEB-0068-query-options-predicates.md) visibility predicates ([AI-0012](../../decisions/AI-0012-mcp-jsonrpc-runtime.md)). Authorization is the unified **gate · constrain · project** model ([SEC-0004](../../decisions/SEC-0004-capability-authorization-gate-constrain-project.md)): one `[Access]` gate per action, an `EntityAccess<T>` that narrows rows to the caller (ownership declared once), and a per-row `can:[]` manifest so an agent is *told* which verbs it may use on each row — **the same authority on REST and MCP** (the entity gate, not a per-transport copy). **Walled-means-silent**: anything a caller may not see is *absent* — no count, no field name, no existence signal — never a redaction. Opt-in is Reference = Intent — *referencing* `Koan.Mcp` activates it (no `AddKoanMcp()` to call); the projection then writes itself from your declarations.

## What you DECLARE (the developer surface)

| Declaration | What it projects to the agent |
|---|---|
| `[McpEntity]` on an `Entity<T>` | Pure exposure — the entity's `Save` / `Remove` / `Query` verbs become MCP tools, schema + visibility identical to REST. `AllowMutations=false` is read-only. Access is the entity's `[Access]` gate (below), never a per-`[McpEntity]` scope. |
| `[Access(read:, write:, remove:, all:)]` | The per-action gate (SEC-0004), enforced identically on REST + MCP. Each value is a comma-OR of terms: `anyone` · `authenticated` · `is:role` · `has:scope:x`/`has:role:y`/`has:claim:t=v` · `owner` · `origin:local`/`internal`/`remote`. A walled verb is **absent from `tools/list`** and denied on call. |
| `EntityAccess<T>` (a `[KoanDiscoverable]` realization) | Ownership declared ONCE: `Owner` + `Constrain(q, action)` narrows rows to the caller — reads filter, `create` stamps server-truth, `update` freezes the owner. Drives the per-row `can:[]` manifest. The compile-safe `Gate` builder (AND-within-a-bag the string can't express) lives here too. |
| `[Audit]` · `[Door]` (SEC-0005) | Govern the agent. `[Audit]` writes an `AgentAction` per mutation (queryable trail; reads never audited); `[Door]` discloses a denied verb with its `needs` instead of walling it (role-gated stays a Wall). Server-side `AgentGrant` entities lend access the token lacks — revocable, expiring. |
| `[McpReadOnly]` · `[McpDestructive]` · `[McpIdempotent]` | Spec tool-annotation hints on a custom `[McpTool]` verb. Entity verbs derive these mechanically (Query/Get → readOnly, Delete* → destructive, Save* → idempotent); hand-written verbs gain nothing automatically — the dangerous ones must be marked. |
| `IMcpResourceProvider` (registered in DI) | A readable `koan://…` introspection resource, projected per grant. The framework ships `koan://entities` + `koan://self`; you add more over the same seam. |
| `[assembly: KoanApp(...)]` | The app identity (`Name`/`Description`) that `koan://self` renders as its first-person greeting — no MCP-specific app attribute. |
| `[McpDescription]` · `[McpIgnore]` | Per-property prose into the schema · hide a property from the agent (input/output/both) without touching storage or REST. |

```csharp
[McpEntity(Name = "Post", Description = "A blog post")]      // exposure only
[Access(read: "anyone", write: "owner", remove: "owner")]   // gate: anyone reads; only the owner writes/removes
public sealed class Post : Entity<Post>
{
    public string Title { get; set; } = "";
    public string? OwnerId { get; set; }
}

// Ownership declared ONCE. Read stays open, so the agent sees every Post — but each row's can:[] advertises
// `write`/`remove` only on its OWN rows. create stamps server-truth; update freezes the owner.
public sealed class PostAccess : EntityAccess<Post>
{
    protected override Expression<Func<Post, bool>>? Owner => p => p.OwnerId == CurrentUserId;
    public override IAccessFilter<Post> Constrain(IAccessFilter<Post> q, AccessAction action) => action switch
    {
        AccessAction.Create => q.Stamp(p => p.OwnerId, CurrentUserId),
        AccessAction.Update => q.Where(Owner!).Stamp(p => p.OwnerId, CurrentUserId),
        AccessAction.Delete => q.Where(Owner!),
        _ => q,                                          // read open → can:[] differs per row
    };
}

[assembly: KoanApp(Name = "Bloggr", Description = "A small blog you can manage by conversation.")]

// A custom verb the framework can't derive — mark the dangerous ones explicitly.
public static class PostTools
{
    [McpTool(Name = "purge_drafts", Description = "Permanently deletes every draft.")]
    [McpDestructive]                                     // the agent sees destructiveHint: true
    public static async Task<int> PurgeDrafts(CancellationToken ct)
    {
        var drafts = await Post.Query(p => p.Title == "");
        foreach (var draft in drafts) await draft.Remove();
        return drafts.Count;
    }
}
```

> **Origin** (the *where*, not the *who*) — a gate term `origin: local | internal | remote`, server-stamped and un-forgeable, distinct from identity. `[Access(remove: "origin:local, is:admin")]` lets a **local** agent OR an admin remove: a STDIO tool call is `local` (yet anonymous — origin is orthogonal to auth), an HTTP caller is `remote` (or `internal` for a declared LAN, fail-closed). A remote agent's `can:[]` simply omits local-only verbs.

## What the AGENT SEES (the consumer surface)

- **Tools** — spec-shaped `tools/list` objects: `inputSchema` (camelCase) + an `annotations` object carrying the verb hints. `[McpIgnore]` fields never appear in the input schema.
- **Resources** — `resources/list` / `resources/read`. `koan://entities` is the per-grant catalog (entities + the verbs *this caller* may use; a walled entity is absent), and each entity carries its navigable **edges** — `{ name, kind, target, via }` routes read from the declared `[Parent]` relationships. An edge is a *route, never a verb* (the whole graph is navigable without any edge becoming a tool), and an edge to a walled target is absent (its field name would leak the target's existence). Follow an edge by querying its `target` tool filtered on `via` — the one governed read path sizes the result to your grant. `koan://self` is the first-person introduction in **two faces in one resource** — a `prose` greeting *and* the `structured` contract beneath it (prose is the greeting, structured is the contract; prose is never the only form).
- **Per-row `can:[]` manifest** — a collection result pairs each row with the verbs the caller may invoke on *that* row (`can: ["read","write"]`), computed as coarse-gate ∩ row-bound gate ∩ `Constrain`. It's what makes allow-by-default honest: with a public-read / owner-write entity an agent *sees* every row but is *told* it can only write its own. REST: opt-in `?access=true` `{ items, access }` sidecar (single item → a row-refined `Koan-Access` header); MCP: default-on in the tool-result metadata.
- **Per-grant projection** — every face is computed for the caller's grant, and the *advertisement mirrors the enforcement*: an entity tool's authority is the data-layer `[Access]` gate (the same gate REST hits), so `tools/list` and the catalog never offer a verb the call would then deny — a denial rides back as `meta.shortCircuit`, the MCP mirror of REST 403/401. The caller's principal threads through **both** data paths (direct tools *and* Code Mode), so Code Mode can't out-privilege the transport. STDIO is local-trust for **discovery** (tool names are visible over the trusted channel) but its **data** runs anonymous + `origin:local` — gated data still needs an identity or the right origin. Custom `[McpTool]` verbs (no entity, no row) keep the transport-edge `McpToolAccessPolicy` scope check.
- **The pin** — pass `correlationId` on any call (or the server mints a GUIDv7); it's echoed in the result diagnostics for trajectory stitching. **Continuity ≠ authority**: the pin is opaque, untrusted, and gates nothing — authorization is always per-request against the grant.
- **Rehearsal & deltas** — every mutating tool accepts `dry_run: true`: the server runs the full validation pipeline, commits nothing, and returns the prospective state delta (`meta.diagnostics.delta` = `{ operation, changes: [{ field, from, to }] }`); a real run returns the **same-shaped** retrospective delta (rehearse → execute → same diff). A bad payload comes back with `didYouMean` corrections drawn from the **schema only** (enum members, required fields — never row data, so the error channel can't leak existence). The delta is walled-means-silent — an `[McpIgnore(Output)]` field never appears in it. A custom verb whose effects the framework can't inspect returns an honest *partial rehearsal* instead of executing.

## Governed access — grants · audit · doors ([SEC-0005](../../decisions/SEC-0005-governed-agent-access-grants-audit-door.md))

Access an agent is *lent* (beyond its token), the *trail* it leaves, and what it's *told it could do*:

- **`AgentGrant`** — a server-side, queryable, revocable grant: `new AgentGrant { Subject = "kitchen-agent", Capability = "has:scope:orders:fulfill", Resource = "Order", ExpiresAt = … }.Save()`. The gate materializes a subject's active grants only when its token alone is denied, re-evaluating the **same** gate — so a grant composes with bags/origin/Constrain, never a per-transport bypass. `Remove()`/expiry revoke on the next call, fleet-wide.
- **`[Audit]`** — every successful write/remove on the entity writes one `AgentAction { Subject, Resource, Action, EntityId, At }` through the normal entity path (queryable like anything else: `AgentAction.Query(a => a.Subject == …)`); reads are never audited.
- **`[Door]`** (the Wall·Door·Verb model, 09 §8) — a verb the caller may invoke is a **Verb**; one they may not is, by default, a silent **Wall** (absent). `[Door]` discloses a denied verb as a **door** in `koan://entities` — `{ name, operation, needs }`, named + how-to-unlock — whose `needs` derives from the **same gate that enforces** it (Description = Enforcement). A **role-gated (privilege) verb is never a door** even with `[Door]` — admin stays a silent Wall (no privilege enumeration).

## The auth on-ramp ([SEC-0006](../../decisions/SEC-0006-embedded-oauth-authorization-server.md))

How an authenticated agent *gets to* the gate chain above. STDIO is anonymous + `origin:local` (local trust). An HTTP agent reaching `/mcp` (when `Koan:Mcp:RequireAuthentication=true`) hits an OAuth 2.1 **resource server**: a `401` carries the RFC 9728 `WWW-Authenticate` pointing at `/.well-known/oauth-protected-resource/mcp`; the agent discovers + drives the **embedded Authorization Server** (Reference = Intent via `Koan.Web.Auth.Server`) — Authorization Code + PKCE *or* the RFC 8628 **device grant** — to mint an ES256 token; `Koan.bearer` validates signature, issuer, algorithm, and lifetime, then the MCP edge enforces the RFC 8707 resource audience (a token for another resource is rejected) and lands the identity in `context.User`. From there the **same** gate · constrain · project · origin · grant chain runs unchanged. The full flow catalogue, the two app-rendered pages, and the config live in [oauth-server-howto.md](../../guides/oauth-server-howto.md).

## Where it's exercised

`[McpEntity]` exposure, the `[Access]` gate, and the resource + self-introduction surfaces are exercised end-to-end against a real `AddKoan()` host by [`tests/Suites/Mcp/Koan.Mcp.Conformance.Tests`](../../../tests/Suites/Mcp/Koan.Mcp.Conformance.Tests). (A dedicated MCP showcase sample is being reworked.)
