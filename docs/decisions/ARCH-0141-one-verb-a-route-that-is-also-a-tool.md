---
id: ARCH-0141
slug: one-verb-a-route-that-is-also-a-tool
domain: Architecture
status: Accepted
date: 2026-11-01
title: One verb is one route, and a tool call is a request to it
related:
  - ARCH-0092
  - DATA-0167
  - SEC-0004
  - SEC-0011
  - SEC-0017
  - WEB-0083
---

# ARCH-0141: One verb is one route, and a tool call is a request to it

## Context

ARCH-0092 gave entity operations one core with two faces: `EntityController<T>` and the MCP tools both call `IEntityEndpointService`, so a gate declared
once answers on both. That core has a closed set of operations per entity. A custom verb has none beneath it: a controller action and an `[McpTool]` method were
two definitions with two binders (MVC and Newtonsoft), two authorities (whatever the route did, and a scope list on the attribute) and two sets of answers, and nothing
kept them alike. An application that wanted one verb on both doors wrote it twice and hoped.

**The business sentence:** say that this route is also a tool.

```csharp
public sealed record ReplyInput(
    [Description("The reply, in plain words.")] string? Body,
    [Description("The Post it answers; empty, it answers the Topic.")] string? Parent = null,
    [Description("A chosen id; left out, the same words from the same author are one reply.")] string? Id = null);

public sealed class RepliesController : ControllerBase
{
    [HttpPost("/places/{place}/topics/{topic}/replies")]
    [Tool(Name = "reply")]
    [Description("Reply in a Topic.")]
    public async Task<IActionResult> Write(string place, string topic, ReplyInput input, CancellationToken ct)
    {
        var author = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return this.Answer(await new Reply(input.Id ?? Minted(author, topic, input.Parent, input.Body), place, topic, input.Body, author, input.Parent).Send(ct),
            post => Created($"/places/{place}/topics/{topic}/posts/{post.Id}", post));
    }
}
```

The complete surface is that attribute, the `Answer` call, and `Koan.Mcp` among the references for the tool to exist. There is no registration, option or second type. With
`Koan.Mcp` absent the route is all there is, and `[Tool]` lives in `Koan.Web` so that nothing else changes. Without `Answer`, the action answers as any action does.

## Decision

1. **The action is the definition, and a tool call is a request to it.** The arguments become route values, a query and a JSON body; MVC's action invoker runs the action on a request
   made in process, with its binding and filters; what it answers becomes the tool's result. Input, judgement, refusals and effect have one definition, so there is nothing to keep
   alike. A controller's action stays where the law says HTTP lives, and no route is generated.
2. **The input is the action's parameters.** A route value is required and comes from the route, so a body can never name the address: JSON fields are read after route values. An author comes from `User` in
   the action body, because no input carries it, so a forged one binds to nothing. A plain controller reads a form and one JSON object alike, property by property, so a page's form and a tool
   call are the same input; an `[ApiController]` action keeps its `[FromBody]`. An id a caller need not mint is the action's to derive: from the call's own content, it makes a retry one row on either door.
3. **A value that cannot bind is refused with its field named,** on every custom verb. A `[Tool]` action answers 400 with the field in `errors`; an `[McpTool]` is refused naming the parameter, and
   so is a required one left out. The lenient fallback to the default is gone.
4. **One gate, for every custom verb.** Its authority is `IAuthorize`, asked for the verb's `MethodInfo`, on a route, on a tool call and in a list. `[Access]` and `[RequireScope]` now apply to a method, else to its
   class, compiled by the gate that compiles an entity's. An application's own `IAuthorizationProvider` can answer for the method: a list asks with no resource and a call passes the route values in `AuthorizeRequest.Context`. So a list hides what is knowable with
   no resource, and a verb only its resource can decide stays listed and is judged by its command. A rejected credential is challenged, as an entity operation does (WEB-0083). `McpToolAccessPolicy` and `[McpTool(RequiredScopes)]` are removed.
5. **STDIO asks too.** It lists and calls custom verbs as its one principal (SEC-0017), which is what it already did for entity tools. An `origin:local` gate admits it: `OriginFeature` carries the tier of the edge onto
   the request made for it, because that request's connection says nothing, and a client cannot set a feature.
6. **A route's one answer to an outcome.** `this.Answer(outcome, done)` maps `ReasonKind` once: invalid 400, gated 401 to a visitor and otherwise 403, conflict 409, a failure 503 if it never ran with `Retry-After` and otherwise 500, naming no
   cause. **Absent is 404 in one sentence whatever the reason says,** so a hidden resource and a missing one cannot differ by an application's wording. Success stays the action's: `done` answers 201, 303 or anything. The answer is a problem with a `kind`.
7. **The tool surface's one answer, which follows the route.** Over 400 is `isError: true`, with the problem's `kind`, or else one read from the status, as `errorCode`; its `detail` or each field's errors as text; and its `status` and `Location` in the result's metadata. The request says
   `Accept: application/json`, so a route that answers in kind answers JSON. This reads a status back, which ARCH-0092's entity path is criticised for; here the route's answer is the truth, and not a detail of a shared core.
8. **Exposure: only a route can be a tool.** `[Tool]` on a controller exposes every action it declares, and permissions decide who uses them; on an action, that action. A command is never a tool, so one that acts for whoever it is told is reachable only if an application writes a route for it,
   and then it is reachable over plain HTTP regardless. A tool opens no door a route had not opened. A page that only shows a form belongs on a controller without `[Tool]`, so there is no opt-out. `[Tool]` on an `EntityController<T>` also exposes that entity's operations as the entity's tools, as `[McpEntity]` does.
9. **Names.** The route's literal segments and then the particle, or else the HTTP method, in lower-case words joined by underscores: `[Route("/data/posts")]`, `[HttpGet("{id}")]` and `[Tool(particle: "getById")]` make `data_posts_get_by_id`. `[Tool(Name = "reply")]` states one. Two tools of one name, across
   entity tools, `[McpTool]` and routes, stop the host at start. So does a name past 64 characters or outside those words, a route tool with no attribute route, or one taking a file or a header, with the correction. A dot is refused because Claude's API refuses it, though MCP allows it; Koan's existing dotted names stay as they are for now, and that is recorded below.
10. **`[McpTool]` stays for a verb with no route.** An operational toolset or an analytics question has no HTTP surface on purpose, and no second definition to drift from. It takes the same gate and refusal.

## What this overturns

- ARCH-0092 §D (3.3b) and §H: custom verbs "keep the policy"; a custom verb's `RequiredScopes`. `Toolset`, `EntityToolset<T>` and `[McpTool]` themselves stand.
- SEC-0017's exposure-only listing of custom verbs over STDIO.
- The lenient binding of `McpCustomToolInvoker`.
- `IAccessGateCache` gains a method overload, and `RequiredScopes` leaves the `tools/list` metadata.
- A separate verb type, binder or adapter, which earlier notes proposed: the action already is all three. The rule that HTTP is controllers-only stands, and gains from this.

## Considered and rejected

| Shape | Why not |
|---|---|
| The action, called by reflection | Skips binding and filters: the drift this ends. |
| A transport-free `IVerb<TInput,TResult>` declared on the input, with a generated route and tool | Four new concepts (the verb, its binder, the success hook, the generated route) to do what an action does. Its success hook needs HTTP's vocabulary to answer 201 or 303, so it is not transport-free after all. Filters, `[RequestSizeLimit]` and a page's form each need a way back in. Its one gain is a tool result mapped from `ReasonKind` directly instead of through a status. |
| An attribute on the action naming a command, `[Tool(typeof(Reply))]` | The six fields of a command are not its input, so a binder is needed somewhere; it is the action's body again, plus an attribute saying so. |
| `[Tooling]` on the class and `[Tool]` on the action | Two attributes for one meaning. One takes both targets. |
| A sub-pipeline with middleware | The call has already passed the application's middleware once, at the MCP route: authentication, tenancy, the rate limiter. Twice would count it twice, and a second authentication would replace the principal. |

## A form

The same definition can supply a page's form descriptor. Every field a tool's schema lists comes from the action's parameters and their `[Description]`, so `ReplyInput` is what a page offers:

```csharp
Form.Posting(address, token, Form.Fields(typeof(RepliesController).GetMethod(nameof(RepliesController.Write))!));
// body: "The reply, in plain words."  parent: "The Post it answers; ..."  id: "A chosen id; ..."
```

The schema reader in `RouteTools` would lift to `Koan.Web` for that, unchanged. It is not built here.

## Other people's words

MCP (specification 2026-07-28) offers no marker for untrusted content in a tool result. Content annotations say audience, priority and modification time, and the specification tells a server to sanitize and a client to validate. This is a gap, and Koan does not invent a marker no client reads. A route that answers JSON keeps others' words as string
values, so they stay data within a structure; an application should say so in `[Description]` where a verb returns them.

## Coalescence

- **Closest pattern:** `EndpointToolExecutor`, which reaches `IEntityEndpointService` from a tool. A custom verb's equivalent of that core is the action itself.
- **Owner:** `Koan.Web` owns the attribute, the filter, the mapping and the gate; `Koan.Mcp` owns the discovery and the in-process request. No abstraction assembly: `[Tool]` in `Koan.Web` read by `Koan.Mcp` is what `[Access]` already does.
- **Deleted:** `McpToolAccessPolicy`; `RequiredScopes`; the transport-edge `-32604` for a custom verb; lenient binding.
- **One registry:** a route tool is an `McpCustomTool` with a `Route`, so listings, `koan://self`, the Explorer and the access map carry it with no new code.

## Ergonomics

One attribute and one extension method are the whole of it, and an action reads as it did. A coding model that can write a controller can write a tool. `[Description]`, `[Required]`, `[Access]` and `[FromBody]` are what they already are.

## Compatibility

- `[McpTool(RequiredScopes = ...)]` no longer compiles: declare `[RequireScope(...)]` or `[Access(...)]` on the method.
- A denied `[McpTool]` over HTTP is a failed call with `errorCode: gated`, not JSON-RPC `-32604`. Over STDIO a scoped one is now denied and hidden, since STDIO is anonymous.
- A custom verb under `AuthorizeDefault.Forbid` is denied unless a provider allows it, as an entity's verb is.
- A value that cannot bind to an `[McpTool]`, and a required argument left out, are refused where they took the default.
- `[Tool]` adds to a route only what it must: the gate, reading a JSON object as fields, and the refusal of a bad value.
- `[Authorize]` with a named policy or schemes on a tool stops the host at start, because only ASP.NET's middleware could enforce it, on the route alone (SEC-0012). `[Authorize]` with roles lowers to the gate; on the route ASP.NET's own middleware answers first, by the scheme's challenge, so prefer `[Access]`.
- Middleware does not run again for the request made in process. What it set ambiently flows with the call, in its async flow, which no spec here pins for a tenant; the principal is handed over. A route constraint such as `{id:int}` is not matched either, so a bad value is a 400 naming the field where HTTP answers 404.
- A binary answer reaches a client as decoded text; a streaming action buffers until it ends. A `[Tool]` is for an answer a model can read.
- A global antiforgery filter would refuse a tool call, which sends no token and carries no ambient credential. An application's own token check exempts a request with no session cookie.
- Entity tools keep their dotted names and still run the endpoint service, so a controller's overrides and `[Pagination]` reach REST alone for them, as before (WEB-0076).
- `Koan.Mcp` registers `IHttpContextAccessor`. During a call it holds the request made for it.

## Evidence

- `OneVerbTwoDoorsSpec` (Koan.Mcp.Verbs.Tests, 23 specs): a real host on a real port and a SQLite file, every case a real HTTP request and a real `tools/call` over Streamable HTTP in a session of its own, asserting the same status, kind, text and `Location`:
  an absent Topic, a hidden Topic and an anonymous visitor to a private place answer alike, in words that differ in the application; a forged author is ignored; a body cannot name the address; a value that cannot bind and a required field left out are refused with the field named; a retried call makes one row on either door and across them; a gated caller is told why;
  a role change commits with its entry; one that throws or refuses after adding the role leaves no role and no entry; a rule spanning actor, target and role answers alike; a list hides what is knowable, by a verb's gate and by an application's provider, and a provider refuses per resource at a call; a form and JSON are one input; a 303 is the action's; a DELETE carries its body and query; a rehearsal runs nothing; a rejected credential is not a visitor;
  over STDIO a route tool answers with no request around it and `origin:local` admits only STDIO; a controller over an entity exposes its operations and its own actions.
- `ToolsThatCannotStartSpec` (4): two tools of one name, a bad name, a file parameter and no route each stop the host and say why, and a particle answers the first.
- `ToolRouteSpec` (Koan.Web.Extensions.Tests, 9): in an assembly with no reference to `Koan.Mcp`, the route, its binding, its gate and every kind of the mapping, with a command that never ran answering 503.
- `EnforcementConsolidationSpec` (Koan.Mcp.Conformance.Tests): an `[McpTool]` answers to the gate on a call and in a list, and a bad value is refused by name.
- **Measured by removing them,** one at a time, against `Koan.Mcp.Verbs.Tests`: with absence saying its reason, the alike spec fails; with JSON fields read before the route, the address spec fails; with no gate, four specs fail; with no refusal of a bad value, two fail; with no origin on the call, the STDIO spec fails; with no name check, the collision spec fails.
- **Seen by accident:** a `[Tool]` with a named policy in a test assembly stopped every host in it at boot, with the correction. It stands unspecced, because a spec of it would stop its own assembly.

## Later

- Koan's dotted names (`article.get-by-id`, `koan.code.execute`, `analytics.ask`) are refused by Claude's API by pattern, and a client has to rename them. About 120 references in specs and samples; a package of its own.
- An entity's operations through its controller, which would end the `[Pagination]` and override drift, needs each operation to declare its query fields.
- `CallToolResult.meta` is not the specification's `_meta`, and `tools/list` says `next` where the specification says `nextCursor`.
