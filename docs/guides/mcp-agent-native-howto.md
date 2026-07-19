---
type: GUIDE
domain: mcp
title: "Agent-native MCP — from one attribute to governed access"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: verified
  scope: patterns-exercised
  notes: "Every layer below is exercised end-to-end by tests/Suites/Mcp/Koan.Mcp.Conformance.Tests and tests/Suites/Web/Koan.Web.Extensions.Tests (gate/constrain/project/origin/grant/audit/door). The transport half is mcp-http-sse-howto.md; the current MCP conformance suite passes 80/80."
related_guides:
  - mcp-http-sse-howto.md
  - authorization-howto.md
  - entity-capabilities-howto.md
---

# Agent-native MCP — from one attribute to governed access

> A single walkthrough that grows one entity from *"an agent can call it"* to *"an agent calls exactly what it's allowed to, is told what it could unlock, and leaves an audit trail."* Each step shows **what you write**, **what the agent sees**, and **what happens**. Streamable HTTP, authentication, and session details are in [MCP over HTTP](mcp-http-sse-howto.md); the authorization model is in [authorization-howto.md](authorization-howto.md). One-screen maps: [agent-native card](../reference/cards/agent-native.md) · [mcp card](../reference/cards/mcp.md).

## The one idea

A Koan app projects itself to an agent as `project(model × grant)` — **one projection, two faces**: what you *declare* and what an agent *sees and may do* are the same truth, computed per-caller, so the description can never drift from the enforcement. You climb a ladder; each rung is one declaration:

| Rung | You add | The agent gains / loses |
|---|---|---|
| 1 Expose | `[McpEntity]` | the entity's CRUD verbs as tools |
| 2 Discover | *(automatic)* | a `koan://entities` map + `koan://self` greeting |
| 3 Gate | `[Access]` | verbs it lacks the grant for vanish |
| 4 Own | `EntityAccess<T>` | rows narrow to the caller; each row says what it `can` |
| 5 Where | `origin:` | local-only verbs (STDIO) separate from remote |
| 6 Verb | `[McpTool]` | a non-CRUD action |
| 7 Compose | *Code Mode* | many steps in one sandboxed call |
| 8 Govern | `AgentGrant` · `[Audit]` · `[Door]` | lent access, an audit trail, and signposted locks |

**Reference = Intent.** Referencing `Koan.Mcp` contributes its `KoanModule`; `AddKoan()` compiles and
activates that module and hosts the STDIO server. Ordinary applications never call `AddKoanMcp()`.
Everything below is a declaration on entities you already have.

```csharp
// Program.cs — the whole bootstrap for a local (STDIO) MCP server.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();   // referencing Koan.Mcp → the STDIO MCP server is hosted (no AddKoanMcp() to call)
await builder.Build().RunAsync();
```

---

## Rung 1 — Expose an entity

**What you write** — one attribute on an `Entity<T>` you already have.

```csharp
[McpEntity(Name = "note", Description = "A personal note")]
public sealed class Note : Entity<Note>
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}
```

**What the agent sees** — `tools/list` returns the entity's CRUD verbs, each with a JSON Schema derived from the type and spec annotation hints:

```jsonc
{ "tools": [
  { "name": "note.collection", "description": "List Note records …",
    "inputSchema": { "type": "object", "properties": { "filter": {…}, "page": {…} } },
    "annotations": { "title": "List Note", "readOnlyHint": true } },
  { "name": "note.upsert", "description": "Insert or update a Note record.",
    "annotations": { "readOnlyHint": false, "idempotentHint": true } },
  { "name": "note.delete", "annotations": { "destructiveHint": true } }
  // …get-by-id, query, get-new, patch, delete-many, …
] }
```

**What happens** — a `note.upsert` call runs through the *same* governed path a REST `POST /api/note` would: identical schema, identical validation, identical visibility. The hints (`readOnlyHint`/`destructiveHint`/`idempotentHint`) are derived mechanically from the verb, so an agent knows a `delete` is dangerous without you saying so. `AllowMutations = false` makes the entity read-only.

---

## Rung 2 — The agent's map (automatic)

You write nothing. The framework ships two introspection **resources** so an agent can orient itself before calling anything.

**What the agent sees** — `koan://entities` (the catalog) lists each projected entity, the verbs *this caller* may use, and the navigable **edges** (your `[Parent]` relationships, as routes — never extra verbs):

```jsonc
// resources/read  koan://entities
{ "entities": [
  { "name": "note", "description": "A personal note",
    "verbs": [ { "name": "note.collection", "operation": "Collection", "isMutation": false }, … ],
    "edges": [ { "kind": "parent", "target": "author", "via": "AuthorId" } ] }
] }
```

`koan://self` is a first-person greeting in **two faces in one resource** — a `prose` menu *and* the `structured` contract beneath it:

```jsonc
// resources/read  koan://self
{ "prose": "I'm Notebook. You can work with: note. For note you can read and modify records.",
  "identity": { "name": "Notebook", … },          // from [assembly: KoanApp(...)]
  "entities": [ { "name": "note", "verbs": [ … ] } ] }
```

**What happens** — both reshape per grant: rename the app and the greeting updates; wall an entity (next rung) and it *vanishes* from the map. The menu is authored by nobody — it writes itself from your declarations.

---

## Rung 3 — Gate it (`[Access]`)

**What you write** — a per-action gate. Each of `read`/`write`/`remove` is a comma-OR of terms (`anyone` · `authenticated` · `is:role` · `has:scope:x` · `owner`); an unspecified action is open.

```csharp
[McpEntity(Name = "note", Description = "A personal note")]
[Access(read: "anyone", write: "authenticated", remove: "is:admin")]
public sealed class Note : Entity<Note> { public string Title { get; set; } = ""; public string Body { get; set; } = ""; }
```

**What the agent sees** — exactly the verbs it may invoke. An anonymous caller's `tools/list` and catalog show `note.collection`/`note.get-by-id` (read = anyone) but **not** `note.delete` (remove = is:admin) — a walled verb is *absent*, never a redaction (**walled-means-silent**: no name, no count, no existence signal).

**What happens** — the gate is the **same authority on REST and MCP** (one seam, no per-transport copy). If an agent calls a verb it can't see, the data-layer gate denies it and the denial rides back as `meta.shortCircuit` — the MCP mirror of a REST `403`/`401`. The catalog never advertises what a call would deny.

> Legacy `[Authorize]` / `[RequireScope]` on the entity still work — they *lower* into this gate as sugar. Full model: [SEC-0004](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md).

---

## Rung 4 — Own it (`EntityAccess<T>` + `can:[]`)

A gate answers *may you write Notes at all*; ownership answers *may you write **this** Note*. Declare it **once** and it drives the row filter, the create-stamp, the 404-on-other's-row, and the per-row manifest.

**What you write** — add an owner field and a realization. Read stays open; writes/removes narrow to the owner.

```csharp
[McpEntity(Name = "note", Description = "A personal note")]
[Access(read: "anyone", write: "owner", remove: "owner")]
public sealed class Note : Entity<Note>
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? OwnerId { get; set; }
}

public sealed class NoteAccess : EntityAccess<Note>
{
    protected override Expression<Func<Note, bool>>? Owner => n => n.OwnerId == CurrentUserId;
    public override IAccessFilter<Note> Constrain(IAccessFilter<Note> q, AccessAction action) => action switch
    {
        AccessAction.Create => q.Stamp(n => n.OwnerId, CurrentUserId),                 // server-truth, not the payload
        AccessAction.Update => q.Where(Owner!).Stamp(n => n.OwnerId, CurrentUserId),    // own row + freeze owner
        AccessAction.Delete => q.Where(Owner!),
        _ => q,                                                                          // read open
    };
}
```

**What the agent sees** — a collection result pairs each row with the verbs it may invoke on *that* row:

```jsonc
// note.collection result metadata — the per-row can:[] manifest
{ "access": {
  "note-1": { "can": ["read", "write", "remove"] },   // the caller owns this one
  "note-2": { "can": ["read"] }                        // someone else's — read-only
} }
```

**What happens** — the agent *sees* every Note (read is public) but is *told* it can only modify its own. `create` stamps the owner from the principal (a forged `OwnerId` in the payload is overwritten); editing another's row is a `404` (existence-hiding); a mass delete is bounded to owned rows automatically. The `can:[]` is what makes allow-by-default honest — openness is advertised per row. (REST exposes the same manifest via `?access=true`.)

---

## Rung 5 — Where, not just who (`origin:`)

Some verbs should depend on *where the call arrived*, not *who* the caller is — a maintenance action you only allow from the local box, regardless of identity.

**What you write** — an `origin` term: `local` (STDIO, same process) · `internal` (a LAN you declare) · `remote` (anything across the perimeter).

```csharp
[Access(read: "anyone", write: "owner", remove: "origin:local, is:admin")]   // local agent OR an admin may remove
```

**What the agent sees** — a STDIO agent (running beside the app) sees `note.delete`; a remote Streamable HTTP agent does not (its `can:[]` simply omits the local-only verb).

**What happens** — the framework stamps an **un-forgeable** `koan:origin` claim at the transport edge (STDIO → `local`, HTTP → `remote`, or `internal` when the source IP is in a declared trusted network — fail-closed). Origin is *orthogonal to identity*: a STDIO call is `local` yet anonymous, so it satisfies `origin:local` without signing in. Declare the LAN once (`Koan:Web:Origin:InternalNetworks`) and homelab clients are treated as `internal`.

---

## Rung 6 — A verb that isn't CRUD (`[McpTool]`)

When the operation isn't entity CRUD, expose a public static method.

**What you write** —

```csharp
public static class NoteTools
{
    [McpTool(Name = "archive_stale", Description = "Archive notes untouched for 90+ days.")]
    [McpDestructive]                                   // the agent sees destructiveHint: true
    public static async Task<int> ArchiveStale(CancellationToken ct)
    {
        var stale = await Note.Query(n => n.Body == "");
        foreach (var note in stale) await note.Remove();
        return stale.Count;
    }
}
```

**What the agent sees** — a new `archive_stale` tool alongside the entity verbs, with the input schema inferred from the parameters (`IServiceProvider`/`CancellationToken` are injected, not advertised) and the `destructiveHint` you marked.

**What happens** — custom verbs gain *nothing* automatically: the framework can't infer that `archive_stale` is dangerous, so you mark it (`[McpReadOnly]`/`[McpDestructive]`/`[McpIdempotent]`). A custom verb's access is the `RequiredScopes` on `[McpTool]` (it has no entity/row, so the entity gate doesn't apply).

---

## Rung 7 — Many steps, one call (Code Mode)

For multi-entity workflows, an agent can send one sandboxed script instead of N round-trips.

**What the agent sends** — JavaScript over a synchronous SDK mirroring your entities:

```js
// tools/call  koan.code.execute  { "code": "...", "correlationId": "..." }
function run() {
  const note = SDK.Entities.Note.upsert({ title: "Groceries", body: "milk, eggs" });
  const all  = SDK.Entities.Note.collection();
  SDK.Out.answer(JSON.stringify({ created: note.id, total: all.length }));
}
```

**What happens** — `SDK.Entities.<Entity>.upsert/getById/collection/…` run through the **same gate, constrain, and origin** as a direct tool call — code mode is *not* a privilege bypass: the script runs as the caller's principal, so it can only touch what the agent could touch one call at a time. Quotas bound the number of SDK calls. Expose modes (`[McpEntity(Exposure = "code")]`, or server `Exposure`) choose tools / code / both.

---

## Rung 8 — Govern the agent (grants · audit · doors)

The final rung makes access *governed* — lent, recorded, and honestly signposted. See [SEC-0005](../decisions/SEC-0005-governed-agent-access-grants-audit-door.md).

### Lend a capability — `AgentGrant`

**What you write** — a grant is an entity; issue it, revoke it, query it.

```csharp
// "kitchen-agent may write Notes for the next 8 hours" — beyond whatever its token carries.
await new AgentGrant
{
    Subject = "kitchen-agent",
    Capability = "has:scope:notes:write",   // an [Access] term: is:role / has:scope / has:claim
    Resource = "Note",
    ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
}.Save();

// later, instantly, fleet-wide:
await grant.Remove();
```

**What happens** — when the agent's token alone is denied a gated verb, the gate loads its **active** grants for that resource, materializes them as scoped effective-claims, and re-evaluates the *same* gate — so a grant composes with the gate, origin, and ownership rather than bypassing them. Grants load fresh per request, so `Remove()`/expiry take effect on the **next** call (no epoch machinery). An anonymous caller has no subject — you grant to a *known* agent.

### Leave a trail — `[Audit]`

```csharp
[McpEntity(Name = "note", Description = "A personal note")]
[Access(read: "anyone", write: "owner")]
[Audit]                                       // every write/remove records one AgentAction
public sealed class Note : Entity<Note> { /* … */ }
```

**What happens** — every successful mutation writes one `AgentAction { Subject, Resource, Action, EntityId, At }` through the normal entity path — queryable like anything else (`await AgentAction.Query(a => a.Subject == "kitchen-agent")`). Reads are never audited.

### Disclose a lock — `[Door]`

By default a verb the agent can't invoke is a silent **Wall** (absent). `[Door]` turns it into a signposted **Door** instead.

```csharp
[McpEntity(Name = "note", Description = "A personal note")]
[Access(read: "anyone", write: "has:scope:notes:write")]
[Door]                                        // disclose locked verbs with how to unlock them
public sealed class Note : Entity<Note> { /* … */ }
```

**What the agent sees** — in the catalog, the locked verb is named with its `needs`:

```jsonc
{ "name": "note",
  "verbs": [ { "name": "note.collection", "operation": "Collection" } ],
  "doors": [ { "name": "note.upsert", "operation": "Upsert", "needs": "requires scope:notes:write" } ] }
```

**What happens** — the `needs` is rendered from the **same gate that enforces** it (Description = Enforcement — it can't drift), and the verb is still denied on call. **Admin stays a Wall:** a verb gated on a *role* is never disclosed even with `[Door]` (disclosing it would let an agent enumerate that a privileged capability exists). So `[Door]` discloses *capabilities you could be granted*, never *privilege tiers*.

---

## Rung 9 — Go remote

Everything above is transport-neutral. STDIO (rung 1's bootstrap) is local-trust for *discovery* but its *data* runs anonymous + `origin:local`. To reach an agent over the network, add the HTTP transport — and you do **not** hand-roll an auth scheme: referencing the embedded Authorization Server *is* the scheme.

```csharp
// Referencing Koan.Web (for the HTTP host) + Koan.Mcp is the whole change — the framework maps the /mcp HTTP edge
// (Streamable HTTP by default; AI-0037) inside its own pipeline (no AddKoanWeb / AddKoanMcp / MapKoanMcpEndpoints).
// The transport is config-gated.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
// appsettings: "Koan:Mcp:EnableStreamableHttpTransport": true, "Koan:Mcp:RequireAuthentication": true
var app = builder.Build();
await app.RunAsync();
```

With `RequireAuthentication: true`, `/mcp` is an OAuth 2.1 **resource server**. Reference the opt-in leaf **`Koan.Web.Auth.Server`** (Reference = Intent) and the framework's `Koan.bearer` scheme validates the ES256 token, lands the identity in `context.User`, and the *same* `[Access]`/`origin`/grant decisions (rungs 3–8) apply to a remote agent — no hand-rolled `AddJwtBearer`. The AS lives at its own `/oauth/…` root (distinct from `/auth/{provider}/` login); `Koan:Mcp:ResourceUri` is the canonical audience the edge enforces.

**The app owns exactly two pages.** A consent page and a "you can close this page" terminal page (wired via `Koan:Web:Auth:Server:ConsentPath` / `DonePath`) — the app *renders*; the framework owns the OAuth protocol. The full transport + auth story — sessions, CORS, streaming, the Authorization Code + device on-ramp, the consent-seam contract — is in [mcp-http-sse-howto.md](mcp-http-sse-howto.md) and [oauth-server-howto.md](oauth-server-howto.md).

---

## Rung 10 — Operational toolsets (ops verbs)

Sometimes the agent's job is to *operate* the system, not just its data — trigger a background job, flush a cache. Reference **`Koan.Mcp.Operations`** to make those toolsets available, then explicitly enable the ones the host intends to expose. They use the *same* grant/audit rails as everything above (rung 8):

- `koan.jobs.{trigger,cancel,status}` · `koan.cache.{flush,flushAll}`

They are **off by default** — operational verbs are privileged, so each toolset is opt-in (posture, not wiring):

```jsonc
{ "Koan": { "Mcp": { "Operations": { "Jobs": true, "Cache": true } } } }   // both default OFF, incl. Development
```

A disabled toolset's verbs are simply **absent** from `tools/list`. When enabled, each verb requires an **`@ops:{jobs|cache}` `AgentGrant`** — the same revocable grant from rung 8, namespaced for operations (a blanket `"*"` entity grant does *not* confer ops; operational authority is explicit). Without it, the call fails loud, naming the grant it needs:

```text
new AgentGrant { Subject = "kitchen-agent", Resource = "@ops:jobs" }.Save();   // issue
agent → koan.jobs.trigger { "workType": "ImportJob", "action": "import" }     → { "jobId": "…" }
```

**Destructive** verbs (`cancel`, `flushAll`) require `"confirm": true`; called without it they return a **dry-run** describing what *would* happen (the safe default). Every mutating call writes an `AgentAction` audit row (rung 8). An anonymous/STDIO caller has no subject and so cannot hold an ops grant — ops are for governed *remote* agents.

The boot report names availability and effective activation: `mcp.ops: available jobs,cache · enabled jobs,cache · grants required · destructive confirm`. Exercised end-to-end (real host, real ledger, real grant + audit) in [`tests/Suites/Mcp/Koan.Mcp.Operations.IntegrationTests`](../../tests/Suites/Mcp/Koan.Mcp.Operations.IntegrationTests). *(A Data toolset — re-embed / transfer — is deliberately absent until demand exists.)*

## Recap — the ladder

```text
[McpEntity]            → CRUD verbs as tools (+ koan://entities, koan://self for free)
  + [Access]           → per-action gate; walled verbs vanish; denial = meta.shortCircuit
  + EntityAccess<T>    → rows narrow to the owner; each row advertises its can:[]
  + origin:            → where the call came from, distinct from who (STDIO local vs remote)
  + [McpTool]          → a non-CRUD verb
  + Code Mode          → many steps, one sandboxed call (same gate — not a bypass)
  + AgentGrant         → lend a capability, revocable + expiring
  + [Audit]            → an AgentAction per mutation (reads never audited)
  + [Door]             → disclose a locked verb's needs (role tiers stay silent Walls)
  + Koan.Mcp.Operations → governed ops verbs (jobs/cache); opt-in + @ops grant + confirm + audit
```

Each rung is one declaration; together they are *one projection*, computed per caller, identical on REST and MCP. Where it's exercised end-to-end: [`tests/Suites/Mcp/Koan.Mcp.Conformance.Tests`](../../tests/Suites/Mcp/Koan.Mcp.Conformance.Tests) and [`tests/Suites/Web/Koan.Web.Extensions.Tests`](../../tests/Suites/Web/Koan.Web.Extensions.Tests).
