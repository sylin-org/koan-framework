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
amended_by: ARCH-0142, SEC-0039, ARCH-0143, ARCH-0144, AI-0042, ARCH-0146, WEB-0087
---

# ARCH-0141: One verb is one route, and a tool call is a request to it

> **Amended by [ARCH-0142](ARCH-0142-the-realignment-finished.md) (2026-11-02): `Answer` answers a browser with a page, every tool name follows rule 12's words, and `_meta` and the list's shape are the specification's.** Rule 2's edge result is one of the places a principal becomes the request's authentication result.

> **Amended by [SEC-0039](SEC-0039-a-caller-brings-its-own-credential.md) (2026-11-02), keeping this from [SEC-0037](SEC-0037-an-edge-that-is-part-of-the-application.md):** rule 2's rejected credential is one a caller presented. A Koan session cookie that no longer holds is a visitor.

> **Amended by [ARCH-0143](ARCH-0143-one-refusal-shape.md) (2026-11-03): a refusal is one `Problem`, and a reason names no next step.** Rule 8's `Reason.Said` is `Answers.Nothing`, and rule 9's
> `next`, with its mention in rule 10, no longer holds: the problem carries `kind`, `gate` and a sentence, and the address to go to next is the surface's.

> **Amended by [ARCH-0144](ARCH-0144-one-request-answered-in-process.md) (2026-11-04): `Koan.Web` holds the pipeline and answers a request of it in process, and a tool call is one use of that.**
> `KoanWebPipeline` holds what `RoutePipeline` held, and `Koan.Web` registers it, `IHttpContextAccessor` and the global limiter's single count, which keys on the pipeline's own mark,
> so the Coalescence's holder of the pipeline and the Compatibility line on what `Koan.Mcp` registers no longer hold, nor `OneCountLimiter` as `Koan.Mcp`'s. Rule 2's request carries
> the caller's `Cookie` and `Authorization` and places no principal, as SEC-0039 rule 7 has it, and withholds what ARCH-0144's one header rule withholds.

> **Amended by [AI-0042](AI-0042-the-edge-reads-by-address-and-fails-in-one-shape.md) (2026-11-04): every failure of the edge carries a `Problem`, and an agent may read any address.** Rule 10's refusal the edge makes before any request
> carries one too, with `retryAfter`, `challenge` and whether anything `landed` in `meta`; a route value of the wrong type is refused on its field; and rule 11 gains `read`, a tool
> of the edge that reaches every GET route where `Koan:Mcp:ReadByAddress` is on, each answering to its own gate, while a route tool takes an `address` in place of its route values.

> **Amended by [ARCH-0146](ARCH-0146-a-route-that-declares-access-is-judged-before-binding.md) (2026-11-04): the gate is its own filter, on every `[Tool]` action and every routed action that declares access,**
> and a decision may refuse with a reason or allow with a value the action reads. Rule 5's "asked by `[Tool]`'s filter" no longer holds.

> **Amended by [WEB-0087](WEB-0087-a-declared-entity-controller-serves-what-it-declares.md) (2026-11-07): on a `[Declared]` entity controller the operations it overrides are its own route tools, taking what each declares,**
> and the entity has no tools of its own there, so the compatibility line on a controller's overrides and `[Pagination]` reaching REST alone holds only for an undeclared controller with `[Tool]`.

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

1. **The action is the definition, and a tool call is a request to it, through the application's whole pipeline.** The arguments become a path, a query and a JSON body, as they were sent: a string that looks like a date stays that string. The request goes to the pipeline the host built, every middleware of it, on an execution flow of its own as a request from a
   server is: routing and its constraints, authentication, tenancy, ASP.NET's authorization, the limiter, the filters, binding, the action. Nothing in `Koan.Mcp` decides what a route would, so there is nothing to keep alike. What the invoker makes is a request; what it reads is an answer. Two things it refuses for routing's sake: an empty route value, which would address something else, and a dot segment, which HTTP resolves before any route sees it, so no action is ever told one. Routing then chooses only among the action named (`NamedActionPolicy`), so a call never runs another.
2. **The request is the caller's, without its credential.** The caller's headers are carried, so middleware reading a tenant or a culture from one answers alike, except `Authorization`, `Cookie` and what belongs to the connection or to MCP. The edge has spent the credential, and its principal is set on the request. Authentication then finds nothing to read and leaves it, and ASP.NET's policy evaluator is told the edge's result: a token the edge accepted for its own
   resource is the caller, answered 403 and not 401 where a policy denies it, though the application's default scheme would refuse that audience. A credential a handler rejected with nothing to replace it stays rejected (WEB-0083). The tier of the edge goes with it (`OriginFeature`), since the request's connection says nothing of STDIO.
3. **The input is the action's parameters.** A route value comes from the route, so a body can never name the address: JSON fields are read after route values. An author comes from `User` in the action body, because no input carries it, so a forged one binds to nothing. A plain controller reads a form and one JSON object alike, property by property, so a page's form and a tool
   call are the same input; an `[ApiController]` action keeps its `[FromBody]`, and a body that is one value or a list is the argument of that name. An id a caller need not mint is the action's to derive: from the call's own content, it makes a retry one row on either door.
4. **A value that cannot bind is refused with its field named,** on every custom verb. A `[Tool]` action answers 400 with the field in `errors`; an `[McpTool]` is refused naming the parameter, and so is a required one left out. The lenient fallback to the default is gone.
5. **Two authorities, each on both doors, and neither of them MCP's.** `[Authorize]` and `[AllowAnonymous]`, with named policies, are ASP.NET's: its middleware enforces them because the call goes through it. `[Access]`, `[RequireScope]` and any `IAuthorizationProvider` are Koan's gate, asked by `[Tool]`'s filter with the verb's `MethodInfo` and the route values in `AuthorizeRequest.Context`.
   The gate reads the verb and the class it is reached through, which for an inherited action is the derived controller, and **every declaration holds**, as ASP.NET treats `[Authorize]`; a method never opens what its class closes. A provider can answer `AuthorizeDecision.Hidden()`, which is the answer of something missing. A rejected credential is challenged, as an entity operation does. `McpToolAccessPolicy` and `[McpTool(RequiredScopes)]` are removed.
6. **A list hides only what is knowable with no resource.** The gate with no route values, and of a route's ASP.NET policy the requirements that read only the principal: roles, claims, names, authentication. A policy's own handlers may need what a request addresses, so a tool only they could deny stays listed and is refused at the call. A rehearsal is gated by the same question, because it makes no request. A role in a token issued here is a role: the issuer keeps the claim types its factory wrote, so `[Authorize(Roles)]` and `[Access("is:x")]` answer a bearer as they answer a session.
7. **`[McpTool]` stays for a verb with no route.** An operational toolset or an analytics question has no HTTP surface on purpose, and no second definition to drift from. It takes the same gate and refusal, asked by the handler. `[Authorize]` on one stops the host at start, because nothing would enforce it. STDIO lists and calls them as its one principal (SEC-0017), which is what it already did for entity tools.
8. **A route's one answer to an outcome.** `this.Answer(outcome, done)` maps `ReasonKind` once: invalid 400, gated 401 to a visitor and otherwise 403, conflict 409, a failure 503 if it never ran with `Retry-After` and otherwise 500, naming no
   cause. **Absent is 404 in one sentence whatever the reason says** (`Reason.Said`), so a hidden resource and a missing one cannot differ by an application's wording. Success stays the action's: `done` answers 201, 303 or anything. The answer is a problem with a `kind`.
9. **A refusal says what happened and what one can do now.** A gated reason declares a stable word and its sentence once, and can name a next step: `Reason.Gated("sign-in", "Sign in to write here.", next: "/sign-in")`. The problem carries them as `gate` and `next` beside `kind`, so a client branches on the word and a person or a mind reads the sentence.
10. **The tool surface's one answer follows the route's, which stays the truth.** Over 400 is `isError: true`, with the problem's `kind`, or else one read from the status, as `errorCode`; its `detail` or each field's errors as text, and for a 404 the one sentence; and its `status` and `Location` in the result's metadata. What the route answered as a JSON object is also the result's `structuredContent`, the specification's field, so `gate` and `next` reach a client that ignores `meta`; the specification allows any JSON value there, and its TypeScript SDK refuses anything but an object, so an array or a value stays in the text. An answer with nothing to read says where it went, in the text and as `{ status, location }`.
    The request says `Accept: application/json`, so a route that answers in kind answers JSON. This reads a status back, which ARCH-0092's entity path is criticised for; here the route's answer is the truth, and not a detail of a shared core.
11. **Exposure: only a route can be a tool.** `[Tool]` on a controller exposes every action it answers, inherited ones included, and permissions decide who uses them; on an action, that action. A command is never a tool, so one that acts for whoever it is told is reachable only if an application writes a route for it,
    and then it is reachable over plain HTTP regardless. A tool opens no door a route had not opened. A page that only shows a form belongs on a controller without `[Tool]`, so there is no opt-out. The operations a controller inherits from `EntityController<T>` are the entity's own tools, as `[McpEntity]` makes them.
12. **Names.** The route's literal segments and then the particle, or else the HTTP method, in lower-case words joined by underscores: `[Route("/data/posts")]`, `[HttpGet("{id}")]` and `[Tool(particle: "getById")]` make `data_posts_get_by_id`. `[Tool(Name = "reply")]` states one. Two tools of one name, across
    entity tools, `[McpTool]` and routes, stop the host at start. A dot is refused because Claude's API refuses it, though MCP allows it; Koan's existing dotted names stay as they are for now, and that is recorded below.
13. **What a call cannot carry stops the host, with the correction:** no attribute route; a name past 64 characters or outside those words; a file or a header parameter; a nested object read from a query; an ASP.NET policy naming an authentication scheme, on the attribute, a named policy, the default or the fallback, because the evaluator would authenticate that scheme again from a credential the call does not carry.

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
| MVC's action invoker on a request made in process | Built first. It re-implemented route values and origin, read `[Authorize]` through a gate that could not read what ASP.NET reads, and skipped route constraints, `[EnableRateLimiting]`, and whatever middleware put in a scoped service. A review reproduced a tool call with more authority than its route. Each skipped part was a place the doors could differ. |
| Routing, authorization and the limiter as a smaller pipeline | Ends the gate findings and not the middleware ones: an application's tenant is set by middleware this cannot know of. It also takes more lines than the whole pipeline: a builder to compose, and the endpoints to hand it. |
| A transport-free `IVerb<TInput,TResult>` declared on the input, with a generated route and tool | Four new concepts (the verb, its binder, the success hook, the generated route) to do what an action does. Its success hook needs HTTP's vocabulary to answer 201 or 303, so it is not transport-free after all. Filters, `[RequestSizeLimit]` and a page's form each need a way back in. |
| An attribute on the action naming a command, `[Tool(typeof(Reply))]` | The six fields of a command are not its input, so a binder is needed somewhere; it is the action's body again, plus an attribute saying so. |
| `[Tooling]` on the class and `[Tool]` on the action | Two attributes for one meaning. One takes both targets. |

The whole pipeline has two costs, both accepted. **A tool call over HTTP is two requests to its middleware:** one to `/mcp` and one to the route, so a request log shows both. A global limiter counts it once, at `/mcp`, and the inner request passes it (`OneCountLimiter`), because a concurrency limit would otherwise wait on itself; a limit a route names binds its tool, which is what a limit means. Keeping `/mcp` out of the limiter instead, and counting the inner request, would leave lists and entity tools unlimited, since a limiter cannot see which of them a body holds. **Middleware runs for STDIO too,** with no connection, as for a request from nowhere, and with no edge a global limiter counts nothing. The second authentication the first design feared replaces nothing, because the request carries no credential.

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

- **Closest pattern:** `EndpointToolExecutor`, which reaches `IEntityEndpointService` from a tool. A custom verb's equivalent of that core is the action itself, and of its entry, the request pipeline.
- **Owner:** `Koan.Web` owns the attribute, the filter, the mapping and the gate; `Koan.Mcp` owns the discovery and the request. `RoutePipeline` holds the host's pipeline by decorating `IApplicationBuilderFactory`, ASP.NET's own seam, so that it sits outside every middleware and startup filter and nothing in `Koan.Web` changes for it. No abstraction assembly: `[Tool]` in `Koan.Web` read by `Koan.Mcp` is what `[Access]` already does.
- **Deleted:** `McpToolAccessPolicy`; `RequiredScopes`; the transport-edge `-32604` for a custom verb; lenient binding.
- **One registry:** a route tool is an `McpCustomTool` with a `Route`, so listings, `koan://self`, the Explorer and the access map carry it with no new code.

## Ergonomics

One attribute and one extension method are the whole of it, and an action reads as it did. A coding model that can write a controller can write a tool. `[Description]`, `[Required]`, `[Authorize]`, `[Access]` and `[FromBody]` are what they already are.

## Compatibility

- `[McpTool(RequiredScopes = ...)]` no longer compiles: declare `[RequireScope(...)]` or `[Access(...)]` on the method. `[Authorize]` on an `[McpTool]` stops the host.
- A denied `[McpTool]` over HTTP is a failed call with `errorCode: gated`, not JSON-RPC `-32604`. Over STDIO a scoped one is now denied and hidden, since STDIO is anonymous.
- A custom verb under `AuthorizeDefault.Forbid` is denied unless a provider allows it, as an entity's verb is.
- A value that cannot bind to an `[McpTool]`, and a required argument left out, are refused where they took the default.
- `[Tool]` adds to a route only what it must: the gate, reading a JSON object as fields, and the refusal of a bad value.
- A route the arguments do not match answers 404, as HTTP does, including a route constraint on a value. Where a literal route shadows a parameter, the two doors differ on purpose: `POST /items/special` is its own action on HTTP, and a tool naming the action on `/items/{id}` reaches that one.
- Tokens the issuer writes carry a role as `ClaimTypes.Role`, the URI, where the default map wrote `role`. Koan's bearer and `TryValidate` read it; a third party reading `role` from them must change. A global limiter partitioned by path sees a tool call under `/mcp`. Numbers keep Newtonsoft's parsing: a decimal past a double's precision does not reach a route exactly.
- `[RequestSizeLimit]` is checked by the invoker from its metadata, because its filter sets a limit a server enforces while reading, and the call's body is already read; the arguments are refused with 413.
- A challenge is the default scheme's. Where that is a cookie redirect, `[Authorize]` answers a tool call as it answers a browser: a 302 with its `location`, and no error. An application with tools and cookie sign-in should use `[Access]`, which answers 401 on both doors.
- A response's `OnStarting` and `OnCompleted` callbacks do not fire: nothing is sent. A binary answer reaches a client as decoded text; a streaming action buffers until it ends. A `[Tool]` is for an answer a model can read.
- Antiforgery refuses a tool call, which sends no token and carries no ambient credential. An application's own token check exempts a request with no session cookie; a global `[AutoValidateAntiforgeryToken]` needs `[IgnoreAntiforgeryToken]` on a tool.
- Entity tools keep their dotted names and still run the endpoint service, so a controller's overrides and `[Pagination]` reach REST alone for them, as before (WEB-0076).
- `Koan.Mcp` registers `IHttpContextAccessor` and replaces `IApplicationBuilderFactory` with a decorator of ASP.NET's own. With no web host there is no pipeline, and a route tool answers 503.

## Evidence

- `OneVerbTwoDoorsSpec` (Koan.Mcp.Verbs.Tests, 56 cases): a real host on a real port and a SQLite file, every case a real HTTP request and a real `tools/call` over Streamable HTTP in a session of its own, asserting the same status, kind, text, `gate`, `next` and `Location`. Its credential is `Authorization`, which stops at the edge, so the route a tool reaches has only the principal the edge placed:
  - **The verbs:** an absent Topic and a hidden Topic answer alike to someone signed in, in words that differ in the application; a visitor to a private place is told to sign in at every address in it; a forged author is ignored; a body cannot name the address; a value that cannot bind and a required field left out are refused with the field named; a retried call makes one row on either door and across them; a gated caller is told why; a role change commits with its entry; one that throws or refuses after adding the role leaves no role and no entry; a rule spanning actor, target and role answers alike.
  - **Authority:** `[Authorize]` with roles and with a named policy, `[AllowAnonymous]`, `[Access]` and `[RequireScope]`, on a class, a method, a derived controller over an inherited action, and on both at once, each with callers who pass and fail; a provider refuses and hides per resource; a rejected credential is not a visitor; a rehearsal is gated.
  - **The request:** a dot segment and an empty route value refused, a call reaching the action it named; strings like dates exact in both directions; a route constraint, a route default, `[FromQuery(Name)]`, `[FromBody]` of a list and of a string, a DELETE's body and query; a scoped service and `Items` set by real middleware outside everything else; the caller's accessor intact afterward; `[EnableRateLimiting]`; a failure downstream of the action; a 303 saying where it went; only an object structured; over STDIO, with no request around it, `origin:local` admits only STDIO.
  - **The list:** hides what claims and a verb's gate answer, by a provider too, and keeps what a policy's handler would decide. A controller over an entity exposes its operations and its own actions.
- `ResourceServerSpec` (6): with `ResourceServer:Enabled` and `RequireAuthentication`, a real token for the MCP resource reaches a route tool as its subject, and answers an `[Authorize(Roles)]` action as a token for the application answers its route, with the role held and without it.
- `OneCountSpec` (2): with a global concurrency limit of one permit, queued and not, a tool call answers and does not wait on itself.
- `WebApplicationSpec` (1): on `WebApplication`, a tool call passes through a startup filter's middleware and answers as a request does.
- `ToolsThatCannotStartSpec` (8): two tools of one name, a bad name, a file parameter, no route, a scheme by attribute, named policy and default policy, a nested query and `[Authorize]` on an `[McpTool]` each stop the host or are refused and say why, and a particle answers the first.
- `ToolRouteSpec` (Koan.Web.Extensions.Tests, 9): in an assembly with no reference to `Koan.Mcp`, the route, its binding, its gate and every kind of the mapping, with a command that never ran answering 503.
- `EnforcementConsolidationSpec` (Koan.Mcp.Conformance.Tests): an `[McpTool]` answers to the gate on a call and in a list; a bad value is refused by name; a thrown refusal keeps its kind and absence says one thing.
- **Measured by removing them,** one at a time: with a rejection copied whatever the principal, the token spec fails; with the caller's headers not carried, the tenant spec fails; with the request on the caller's flow, the accessor spec fails; with the gate reading where a method is declared, the inherited-action cases fail; with a rehearsal ungated, its spec fails; with the list ignoring ASP.NET, two fail; with a thrown absence saying its reason, its spec fails. With the edge's principal not placed on the request, 33 fail; with dot segments let through, an empty value let through, routing not narrowed to the action, dates parsed at the transport or in the answer, any JSON structured, the limiter counting both requests, the issuer's map not cleared, schemes not refused and a redirect saying nothing, each fails its own spec. Earlier: absence saying its reason, JSON fields read before the route, no gate, no refusal of a bad value, no origin on the call and no name check each fail their specs.

## Later

- Koan's dotted names (`article.get-by-id`, `koan.code.execute`, `analytics.ask`) are refused by Claude's API by pattern, and a client has to rename them. About 120 references in specs and samples; a package of its own.
- An entity's operations through its controller, which would end the `[Pagination]` and override drift, needs each operation to declare its query fields.
- `CallToolResult.meta` is not the specification's `_meta`, and `tools/list` says `next` where the specification says `nextCursor`.
- A tool's answer is buffered with no cap: a cap on what a call reads belongs with the response stream.
- `KoanEnv`'s snapshot is process-wide, so the first host in a test process fixes the environment for the rest: a spec here found it by order, and pins the environment.
