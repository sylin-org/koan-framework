# Sylin.Koan.Web

Turn Koan entities into controller-first ASP.NET Core APIs, with shared policy, health, and runtime facts.

- Target framework: net10.0
- License: Apache-2.0

## What it adds

- `EntityController<TEntity, TKey>` and the string-key `EntityController<TEntity>` convenience surface
- attribute-routed ASP.NET Core MVC integration
- `[Tool]` on a controller or action, which makes a route also a tool where `Koan.Mcp` is present, and `this.Answer(outcome, done)` or `this.Refused(reason)`, a route's one answer to a command (ARCH-0141): a `Problem` as `application/problem+json`, carrying the reason's `word` and its field (ARCH-0145); an application that renders pages registers an ordinary `text/html` output formatter that writes `Problem` and sets `RespectBrowserAcceptHeader`, and a browser reads its page (ARCH-0143)
- middleware an application adds in `Program` runs for a route, after authorization and before the endpoint; a default CORS policy runs before authentication with no call of yours; a fallback belongs on `app.MapFallback`, because a terminal `app.Run` answers every route (ARCH-0142)
- health and optional OpenAPI wiring
- static-file middleware only when the host supplies a real web-root provider
- the same configured security headers on default/static files and API responses
- redacted runtime facts at `GET /.well-known/Koan/facts`
- response transformers for deliberate representation shaping
- one ordered request-context contributor lifecycle for validated principal, tenant, and Entity read context

## Install

```powershell
dotnet add package Sylin.Koan.Web
```

## Meaningful result

Define the business model and inherit one controller. `AddKoan()` discovers the capability; the base controller supplies
the CRUD projection through the shared Entity operation boundary:

```csharp
using Koan.Data.Core.Model;      // Entity<T>
using Koan.Web.Controllers;      // EntityController<T>
using Microsoft.AspNetCore.Mvc;  // Route

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

[Route("api/todos")]
public sealed class TodosController : EntityController<Todo>;
```

The result is the `/api/todos` entity API. Lifecycle, authorization, relationship budgets, and other
Entity policies execute at the shared operation boundary rather than being copied into controller actions. Add custom
actions only for business operations that are not entity CRUD.

Use transformers when a representation must differ from the stored model; see WEB-0035 below.

For localized content, keep publication authority in the canonical partition while filtering and sorting
the requested language. An access realization adds that requirement to the ordinary query:

```csharp
public sealed class ArticleAccess : EntityAccess<Article>
{
    public override IAccessFilter<Article> Constrain(IAccessFilter<Article> q, AccessAction action)
    {
        if (action != AccessAction.Read) return q;
        var editor = Principal.IsInRole("editor");
        return q.Where(article => editor || article.Published, partition: "");
    }
}
```

Here `Article` is the application's Entity type with a `Published` property. The same ID must exist in
the default partition and satisfy the predicate before the selected content is counted or paged. Even
an editor requires that counterpart; orphan content stays absent. `null` inherits the current partition,
while `""` explicitly selects the default. REST and MCP use the same declaration without extra registration.
Mongo supports the native cross-partition query. Unsupported connectors or query combinations refuse it.

For request-derived business context, implement `IWebContextContributor` with ordinary scoped DI. Koan invokes it
after authentication and before endpoints. The contributor validates standard `HttpContext` evidence once, then may
contribute a principal, a capability scope, typed Entity predicates, or an existence-hiding rejection. For example, a
share-link contributor can prove `?event=` against a durable grant and call
`context.Where<Photo>(photo => photo.EventId == eventId)`; all downstream Entity, Vector, and Entity-backed Media
reads inherit that predicate automatically. Query values are evidence, never authorization by themselves.

## Inspection and configuration

Runtime facts use the same schema as startup, health, and `koan://facts`. Outside Development, enable
`Koan:Web:ExposeObservabilitySnapshot` deliberately and protect `GET /.well-known/Koan/facts` as an operational
surface. Static-file wiring stays dormant in API-only hosts that have no real web root.

### Public URL and proxy

Set `Koan:Web:ApplicationUrl` to the one public URL the application answers at — scheme, host and optional path:

```json
{ "Koan": { "Web": { "ApplicationUrl": "https://www.example.com/app", "TrustedProxies": ["10.0.0.5"] } } }
```

When it is set it is authoritative. Koan serves the application under its path and nothing at the root (routes,
static files, `/health` and its alias, the well-known documents all live under `/app`), and every absolute URL Koan
writes derives from it: the authorization server's issuer, the dev token's audience, OpenGraph card URLs, the atproto
client metadata. Each of those keeps its explicit override (`Issuer`, `ResourceServer:Resource`, `ClientId`) for the rare case
where it must differ. The value is held normalised — lowercase scheme and host, the port only when it is not the
default, the path percent-encoded in canonical form, no trailing slash — and a value that is not the public URL
(not absolute http(s), a query or fragment, an encoded `/`, or a listen address such as `0.0.0.0`) stops boot with
a correction. Read it in code through `KoanWebBaseUrl` (injected) or `KoanWeb.Urls.Base()`. When it is unset, URLs
derive from each request's scheme, host and path base, which is the Development posture; Staging, Test and CI say
so once, and Production refuses the first absolute URL anyone asks for, naming the key, while the host itself still
boots. The startup facts state which applies (`koan.web.baseurl`).

An application that learns its address instead of asking for it up front accepts it at runtime (WEB-0081). It
registers one `IApplicationUrlStore` — `Load` and `Save`, kept wherever the application keeps its settings — and calls
`KoanWebBaseUrl.Accept(url)` on its operator's word:

```csharp
public sealed class PlaceAddress : IApplicationUrlStore
{
    public async Task<string?> Load(CancellationToken ct) => (await Settings.Get(Settings.Id, ct))?.Address;
    public async Task Save(string applicationUrl, CancellationToken ct) { /* write it to the settings row */ }
}

await baseUrl.Accept("https://www.example.com", ct);
```

The precedence is configured, then accepted, then the request. Koan reads the store before the host serves, validates
an accepted URL as it validates the key, keeps it through the store and holds it in memory; from the next request every
absolute URL derives from it, whatever `Host` says, with no restart. An accepted URL is an origin: a path is mounted
when the host starts, so it is configured. It is https, or http on a loopback host in Development, because an issuer
must be https and an AT Protocol client is https or, in Development only, the localhost profile; `Accept` refuses any
other origin before the store sees it, and any origin a capability active in the host cannot use
(`IApplicationUrlRule`), such as an atproto client's on a port of its own. `WouldAccept` asks without accepting, so an
application offers only what it can keep. Tokens, atproto sign-ins and atproto sessions issued under the previous URL
end as they would after a restart with the new one configured. A stored URL this host cannot use — set up in
Development on `http://127.0.0.1:5220`, then started in Production — never stops the start: Koan says so once, treats
it as nothing accepted and leaves the store alone, and the next accepted URL replaces it, so moving a place between
environments needs no database edit.

An application set up on the address its first run is reached at declares that on its store:
`public bool DerivesUntilAccepted => true;`. Then, while nothing is configured or accepted, Production derives absolute
URLs from each request as the trusted proxies resolve it, instead of refusing; Koan says so once in the log, naming the
posture and how it ends. The first accepted URL ends it for good, and a configured URL makes it moot. It is the
application's consent in code, not `Koan:AllowMagicInProduction`.

`Koan:Web:TrustedProxies` names the reverse proxies (IP addresses or CIDR networks) whose `X-Forwarded-Proto`,
`X-Forwarded-Host` and `X-Forwarded-For` Koan believes, at the head of its pipeline. Behind a CDN and then a reverse
proxy, declare both, by network where the CDN publishes its ranges: the walk steps back through every declared proxy and
stops at the first address nobody declared, which is the client. Nothing declared
means no forwarded header is honoured, and a blank entry (`Koan__Web__TrustedProxies__0=` from `${PROXY:-}`) declares
nothing. A configured or accepted base URL still decides every absolute URL Koan
writes, whatever host the proxy forwards. `IsProxiedApi` is a separate, older switch: it only tells Koan that the
proxy emits the security headers.

Koan notes the origins requests arrive on other than the base URL in force — scheme and host as that posture resolved
them, with a count and when each was first and last seen — in memory, for at most `Koan:Web:ObservedOriginLimit`
distinct origins (32), the least seen evicted, so a scan of random hosts cannot push out the address people use. Inject `KoanWebOrigins` to read them or to hear of a new one:

```csharp
origins.Appeared += (_, seen) => { if (!seen.IsLocal) offers.Record(seen.Origin); };
var recent = origins.Seen();   // most recent first, never the base URL in force
```

`Appeared` fires once per new origin, on the request that brought it, so an application offers an address without
polling. `AddressClass` tells loopback and unspecified addresses (`IsLocal`) from private and public ones. An observed
origin changes nothing until the application accepts it.

### Key ring

Every auth cookie and every protected sign-in state is encrypted with the ASP.NET Data Protection key ring. Set
`Koan:Web:KeyRingDirectory` to the folder it persists in, absolute or relative to the content root:

```json
{ "Koan": { "Web": { "KeyRingDirectory": "/data/keys" } } }
```

Keys then live there under the application name the folder remembers, so a container rebuilt from its image, a
renamed application and every instance over the same folder read what was protected before. The first start over a
folder writes the name it uses, the application's code, into `koan-application-name.txt`, and every later start reads
it back; `Koan:Web:KeyRingApplicationName` pins the name instead, and a pin the folder does not hold refuses the start,
since every key in it would become unreadable. Koan creates the folder at start (owner-only, 0700, on Unix; an
existing folder's mode is left alone and reported when it is wider), proves it writable and loads the ring, and a
folder this process cannot use refuses the start with a correction naming the key. Unset, the ring stays where
ASP.NET keeps it, which a rebuilt container does not keep.

The application's own Data Protection configuration wins, whether made before or after `AddKoan()`: a
`PersistKeysTo…` call replaces Koan's folder, a `SetApplicationName` call replaces the remembered name, and
`UseEphemeralDataProtectionProvider` or an `IDataProtectionProvider` of its own leaves no key it uses in the
folder. The facts state
what is in effect (`koan.web.keyring`), naming the application's override when there is one. The keys are written in
clear, as ASP.NET's own file-system ring is; the folder's permissions protect them, and an application that needs an
encryptor adds one through ASP.NET's `IDataProtectionBuilder`.

## Boundaries and failures

- A constrained Create branch uses atomic insertion. An identity collision with a row the caller may read
  answers 409 `id-taken`; only a row their record access's Read constraint hides answers the
  existence-hiding 404, and field access or row projection change nothing about it. Unsupported providers return 501 with `web.mutation.insertUnsupported`. Ordinary
  unconstrained upsert keeps its existing semantics.
- Mutation constraints mean declared Create/Update predicates or stamps. A read-only realization
  does not constrain writes. Existing coarse authorization, including server grants, remains authoritative.
- Constrained bulk requests containing a new or unavailable identity return 501 with
  `web.mutation.bulkCreateUnsupported` before persistence; submit creates individually. An update-only
  batch keeps ordinary bulk semantics. Atomic mixed bulk creation is not offered.
- Dry-run remains a tentative validation preview; it cannot prove identity availability or reserve a key.
  Existing authorized-update races require a separate conditional-write guarantee.

- This package projects capabilities into ASP.NET Core; it is not a standalone server and does not choose a data
  provider. Use `Sylin.Koan.App` for the shortest web entry bundle or compose lower-level packages deliberately.
- `EntityController<T>` exposes direct Entity CRUD. It does not infer workflow endpoints, recursive graph traversal,
  authorization policy, or a public contract versioning strategy.
- Relationship expansion preserves per-type visibility and finite budgets. Unsupported implicit scans return a
  corrective response instead of silently loading a whole source.
- Runtime facts are redacted, not anonymous. Non-Development exposure remains an operator-owned security decision.
- Authentication and richer authorization projections live in optional Web/Auth packages; referencing Web alone does
  not make an API secure.
- An `AgentGrant` states when it was issued (`IssuedAt`) and, when Koan's authorization server issued it, to which
  client (`ClientId`, `ClientName`); each approval is its own grant, and `AgentGrant.Approvals(subject, take)` reads a
  person's live ones newest first. Koan's issuers set the fields; a grant an application saves carries what it sets,
  and a grant saved before these fields existed answers null. None of them takes part in evaluation. `Remove()` is the
  revoke; with the authorization server referenced, removing one of its live grants also makes that client ask the
  person again.
- Contributed predicates are request-lifetime read visibility. They do not authorize writes, secure raw storage/SQL,
  or travel into durable jobs; those boundaries must establish or re-resolve their own application authority.

## An entity write commits as one

Every write `EntityController<T>` serves runs from its prior read to its last after-hook as one, so what its hooks write stands with
the row or neither does, and what refuses answers as a command's refusal does:

```csharp
public sealed class PostHook : IModelHook<Post>
{
    public async Task OnBeforeSave(HookContext<Post> ctx, Post post)
    {
        if (ctx.Prior is { } stored && stored.Body == post.Body) { ctx.ShortCircuit(stored); return; } // a replay answers as written
        if (post.Body.Length == 0) throw new Refusal(Reason.Invalid("body", "Say something."));       // 400, naming the field
        await new Event { Kind = "post.written", PostId = post.Id }.Save(ctx.Ct);                     // commits with the post
    }
    // ...
}
```

- **Where it holds:** inside a scope already open the write joins it, and its owner commits it. Otherwise, where the entity's provider
  declares `write.heldTransaction` (SQLite), it is an `EntityContext.Hold` of its own; elsewhere it runs as it does without one.
- **When:** the hold opens at the prior read, after the gate, field admission and `OnBeforePatch`, and commits after the last after-hook,
  before the emit hooks compose the answer. A stop of 400 or more, a thrown `Refusal`, a lifecycle `Cancel`, any throw and a rehearsal
  roll it back. Keep a hook inside it short: every other writer of the store waits.
- **What it answers:** a `Refusal` through `Answers.Refused`, with its kind, word and field; a lifecycle `Cancel` as a 409 whose word is
  its code; a seam denial through `Answers.Denied`. A hold that cannot begin behind a busy store never ran: `503` with `Retry-After`,
  nothing written, safe to send again. An MCP entity tool carries the same problem in `meta.problem`.
- **The prior row** is `HookContext<T>.Prior`, read where the hold opens. A hook that stops a write with a row answers it through the
  emit, as the first write was. See [WEB-0086](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/WEB-0086-an-entity-write-commits-as-one-and-refuses-like-a-command.md).

## A declared entity controller

`[Declared]` makes an `EntityController<T>` serve what it overrides and nothing else, and each override declares what it writes and
what it takes beside the entity:

```csharp
public sealed record Placing([property: Description("The board it goes on.")] string? Where = null);

[Route("/boards/notices")]
[Declared]
public sealed class NoticesController : EntityController<Notice>, IModelHook<Notice>
{
    [HttpGet("{notice}")]
    [Tool(Name = "read_notice")]
    public override Task<IActionResult> GetById(string notice, CancellationToken ct) => base.GetById(notice, ct);

    [Tool(Name = "post_notice")]
    [Writes(nameof(Notice.Id), nameof(Notice.Text))]
    [Input<Placing>]
    public override Task<IActionResult> Upsert(Notice model, CancellationToken ct) => base.Upsert(model, ct);

    public Task OnBeforeSave(HookContext<Notice> ctx, Notice notice)
    {
        if (ctx.Prior is null) notice.Board = ctx.Input<Placing>()?.Where ?? "general";
        return Task.CompletedTask;
    }
}
```

- **What is served:** the overrides, each at its own HTTP attribute's route in place of the inherited one, and as a tool where `[Tool]`
  says so. Everything it inherits is neither, and the entity has no `{entity}_{op}` tools there. `POST ""` only creates: a row holding
  the id answers `409 id-taken`, unless a hook that sees it as `ctx.Prior` answers the retry with it (`ctx.ShortCircuit(prior)`), as
  the first write was answered.
- **Addresses:** override `KeyOf(segment, ct)` to answer a slug, a handle or another key; a singleton routes `[HttpGet("")]` and
  `[HttpPatch("")]` and answers its one id from `KeyOf`. `KeyOf` works on any `EntityController`.
- **What it takes:** `[Writes]` names the members a write writes; a body naming another is refused 400 with that field, an omitted
  member keeps its value on `PATCH` and its default or stamped value on `POST`, and record access admits the members named.
  `[Input<T>]` declares values that are not the entity's, from the query and the same body, read by hooks with `ctx.Input<T>()`. Any
  query word an operation does not take, `filter`, `page` or `with` among them, is refused with its name, except on a read from a sender
  that names no JSON: a link another site tagged with `fbclid` or `utm_source` is sent on, `302`, to the address with only the words it
  takes ([WEB-0090](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/WEB-0090-a-page-reached-with-words-it-does-not-take-is-sent-on-without-them.md)).
- **Partitions:** every `EntityController` refuses `set`, in the query or a query's body, unless it declares `[Partitions("archive")]`
  or `[Partitions(Any = true)]`; its entity tools follow the same rule.
- **Hooks:** a controller that implements `IModelHook<T>`, `IRequestOptionsHook<T>`, `ICollectionHook<T>` or `IEmitHook<T>` runs as
  that hook for its own requests only, after the registered hooks and before the access floor. Implement only the steps you need.
  Every hook reads which operation it serves as `ctx.Operation` (`Collection`, `New`, `Read`, `Create`, `Change` or `Delete`), so a
  controller with two writes keeps no field to tell them apart; whether a row stood is `ctx.Prior`.
  An undeclared controller carrying `[Tool]` is answered on MCP by its entity's own tools, which never run the controller, so one that
  is a hook or gates an override stops the host at start until it is `[Declared]`.
- **Lists:** a declared collection is keyset-only. `[Pagination(DefaultSize = 20, MaxSize = 50, DefaultSort = "-LastActivityAt")]`
  on its `GetCollection` override names its order, the identity breaking ties; it takes `after`, the sealed `next` of the page
  before, and `size`, at most `MaxSize`, and answers `{ "items": [...], "next": "..." }`, `next` null on the last page, with no
  count, no offset and no total header. An `after` that does not open is refused naming it. An emit hook composing a page reads
  `ctx.Next`. A list you page yourself seals its key with `SealedCursor` (DATA-0170).
- **Gates:** a gate an override declares is judged before binding, tool or not. A declared collection answers in the format the caller
  accepts. See [WEB-0087](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/WEB-0087-a-declared-entity-controller-serves-what-it-declares.md).

## A form writes, and every write answers once

A declared controller's writes take a page's form as well as JSON, and every committed write, an entity's or a verb's, answers through
`Answers.Written`:

```html
<form method="post" action="/boards/notices/first-steps">
  <input type="hidden" name="_method" value="PATCH">
  <input type="hidden" name="return" value="/boards">
  <textarea name="text"></textarea>
</form>
```

```csharp
protected override string AddressOf(Notice row) => "/boards/notices/" + Uri.EscapeDataString(row.Slug); // where a written notice answers

[HttpPost("{notice}/pin")]                                                                              // a verb beside the entity
public async Task<IActionResult> Pin(string notice, CancellationToken ct)
    => this.Answer(await new PinNotice(notice).Send(ct), pinned => this.Written("/boards/notices/" + Uri.EscapeDataString(notice), pinned, created: false));
```

- **A form:** `Upsert`, `Patch` and `Delete` read a urlencoded or multipart form as the JSON object of its fields, typed as the members
  they name, and judge it by the same write set, input and refusals. A urlencoded `POST` names `PATCH` or `DELETE` in `_method`, which a declared controller's change or delete answers.
  Such a form is read within the strictest `[RequestSizeLimit]` and `[RequestFormLimits]` of the routes at its address, and answers
  `413` past them; every other form route keeps its own limits.
- **A body that cannot bind:** JSON that is not one object, or a value its member cannot be (an enum word that names nothing), is
  refused `400` naming the field, word `invalid`, on both doors, as every other refusal of the write is.
- **The answer:** never cached. A sender that reads pages (a form, or an `Accept` asking for HTML) is sent on with `303` to its form's
  `return`, a path under the base, or else to the row's address, `AddressOf(row)`; a delete goes to the address its row was listed under.
  A sender that reads JSON (`Answers.WantsJson`) reads the row, `201` for a create and `200` otherwise, with `Location`. An address
  you hand `Written` is a path under the base, as `AddressOf` is; the base is put before it for you.
- **A refusal:** a refused form answers its problem with `sent`, each field it sent. Override `RefusedPage(operation, refused, ct)` to
  hand the page that form reads again, which your formatter draws at the refusal's status, never cached; it is asked for a refusal by
  the gate, by binding and by the write alike, and only of a sender that reads pages, so a tool call reads the problem. See
  [WEB-0088](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/WEB-0088-a-declared-entity-write-takes-a-form-and-answers-once.md)
  and [WEB-0089](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/WEB-0089-a-hook-knows-its-operation-and-a-refused-form-reads-its-page-again.md).

## A request answered in process

`KoanWebPipeline` answers a request of the application here, through its whole pipeline — routing, authentication,
authorization, the limiter, its middleware, filters, binders and formatters — as that request would be answered over HTTP:

```csharp
public sealed class DraftsController(KoanWebPipeline pipeline) : ControllerBase
{
    [HttpGet("/drafts/{id}/preview")]
    public async Task<IActionResult> Preview(string id, CancellationToken ct)
    {
        var page = new InProcessRequest(HttpMethods.Get, $"/drafts/{id}") { Headers = { Accept = "text/html" } };
        var answer = await pipeline.Send(page, ct);
        return File(answer.Body.ToArray(), answer.Headers.ContentType.ToString());
    }
}
```

- **The address** is a path and query under the application's base URL; the path base is added for you. An address that
  is not a path, or that names a `.` or `..` segment, is refused.
- **Who reads it.** By default, the caller of the request in hand: its own `Cookie` and `Authorization` are carried and
  the application's authentication reads them, as on any route. With no request in hand (a STDIO tool call, a command-line
  verb), `Principal` is the reader. `AsNobody = true` reads as a visitor: nothing of the request in hand reaches it but its
  scheme and host. A credential among the headers you give, and a value handed in as one of ASP.NET's own server features,
  are refused; a request is never read as anyone else.
- **What it carries of the request in hand:** every header except its connection's, its body's and its own answer's —
  `Host`, the hop-by-hop and forwarding headers, every `Content-*`, `Accept`, `Accept-Charset`, `Accept-Encoding`, the
  conditionals, `Range`
  and `Origin`. Give `Accept` and `Content-Type` yourself, and name your transport's own headers in `Withheld`.
- **Values handed in process** go in `Features` and become features of the inner `HttpContext`, which an action or a
  formatter reads; the inner request has its own DI scope, `Items` and execution flow, and the caller's
  `IHttpContextAccessor` is as it was afterwards.
- **The answer** is the status, the headers and the body, buffered whole. An exception nothing in the pipeline handled
  reaches you; with `WithExceptionHandler()` the application's handler answers it first. `OnStarting` and `OnCompleted`
  callbacks never fire. A process that serves no web requests answers `503`.
- A request made in process runs inside the outer request's limiter permit and is not an observed origin. A limit a route
  names still binds it. See [ARCH-0144](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/ARCH-0144-one-request-answered-in-process.md).

## Who may reach a route

A routed action that declares what it needs is judged once, before anything is bound, by your own `IAuthorizationProvider`, and the
action reads what the judgement found:

```csharp
public sealed class Needs(Bit bit) : AccessAttribute<Bit>(bit);

public sealed class Doorway : IAuthorizationProvider
{
    public int Order => 10;

    public async Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct)
    {
        if (request.Resource is not MethodInfo verb || AccessAttribute<Bit>.Of(verb) is not [var bit] || request.Context is not { } route)
            return null;                                         // not declared, or a listing: defer
        var here = await Here.Resolve(request.Subject, route, ct);
        return here.Refusal(bit) is { } reason ? AuthorizeDecision.Refused(reason) : AuthorizeDecision.Allowed(here);
    }
}

[HttpPost("/t/{tangent}/{topic}")]
[Needs(Bit.Post)]
public IActionResult Reply(string tangent, string topic, ReplyInput input) => ... HttpContext.Features.Get<Here>() ...
```

- **Who is judged:** every `[Tool]` action, and every routed action that declares `[Access]`, `[RequireScope]` or your
  `AccessAttribute<TNeed>` on itself or its controller. `[Open("why")]` says that nothing judges a route. The judgement runs
  before every resource filter and before binding, so nobody is told to fix their input before being told they may not act.
- **What it answers:** `AuthorizeDecision.Refused(reason)` answers as a command's refusal does, through `Answers`.
  `Allowed(value)` puts the value on the request as a feature of its own type. A listing asks with no route values.
- **A 401 asks for a credential:** each `IChallenge` you register adds a challenge to `WWW-Authenticate`.
- **The start check:** `Koan:Web:Authorization:RoutesDeclareAccess=true` stops the host at start over any of your routed
  actions that declares neither a gate nor `[Open]`. See [ARCH-0146](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/ARCH-0146-a-route-that-declares-access-is-judged-before-binding.md).

## Technical reference

- [Koan.Web technical reference](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Web/TECHNICAL.md)
- [Web API conventions](https://github.com/sylin-org/koan-framework/blob/main/docs/api/web-http-api.md)
- [WEB-0035 — EntityController transformers](https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/WEB-0035-entitycontroller-transformers.md)
- [Engineering guardrails](https://github.com/sylin-org/koan-framework/blob/main/docs/engineering/README.md)

## Ordered response hooks

`IEmitHook<T>` runs in ascending `Order`. `EmitDecision.With(payload)` passes the replacement to
subsequent hooks; `Next()` preserves the current payload. `HookContext.ShortCircuit(...)` stops the
pipeline immediately and takes precedence over a returned replacement. This applies to both collection
and model responses through the shared REST/MCP endpoint pipeline.

For a governed collection summary, use the selected rows as the projection source:

```csharp
return EmitDecision.Project(articles, article => ArticleSummary.From(article));
```

The hook may load bounded personalization data first and capture it in this mapper. Koan verifies the
exact selected source sequence, maps each row once, and checks identity and scope again before emitting
any view. `Project` is terminal: later emit hooks cannot replace its sources or receive its DTOs. Ordinary
`IProjectionOf<TEntity, TView>.From` remains application mapping code, not an authority claim.

`Project` supports flat collection and body-query responses, including `shape=full`. To keep map, dict,
or relationship responses, a hook can return `Next()` when `ctx.Options.Shape` is `map` or `dict`, or
`ctx.Options.IncludeRelationships` is true. Those options are normalized at the shared endpoint before
hooks. Framework shapes are built after emit hooks, so hooks receive the selected entity rows rather
than mutable response wrappers. Combining `Project` with those shapes refuses before mapping.

Counterpart reads reject arbitrary `With` payloads and successful custom short-circuits. Hook denial
statuses remain usable, but arbitrary denial object bodies are removed. A failed mapper returns no
partial view; its side effects, if any, are not undone. Model and mutation emit do not support `Project`.
Mapper exceptions are logged server-side and return a safe 500 response with `web.read.projectionFailed`.
Source-bound projection initially supports string, Guid, and the eight built-in integer key types.
Other keys, including mutable byte arrays, refuse before mapping; ordinary `With` is unchanged.

Every invoked model hook's `ShortCircuit` result is honored. A pre-save, pre-delete or pre-patch stop
prevents persistence, including dry runs and batch pre-save validation. An after-fetch stop precedes
relationship expansion. Post-write stops control the response without undoing the committed mutation;
the mutation is audited before response hooks run.

## Conditional properties

Use the existing access declaration on a property when operators may see or write a field that
ordinary callers cannot:

```csharp
[Access(read: "is:admin", write: "is:admin")]
public List<string> ClaimedByUserIds { get; set; } = [];
```

The normal `AddKoan()` MVC pipeline applies the property gate to typed Entity, custom ObjectResult
and JsonResult responses, including typed lists and dictionaries. Entity authorization still runs
first. AgentGrants retain resource scope. MCP uses the same decisions and keeps its stronger
`McpIgnore` exclusions. Storage and trusted server predicates remain unchanged.

Caller filters, sorts, delete predicates and patch paths cannot read a denied field. Full typed
replacement, including a custom MVC body, refuses when an omitted field could erase a protected
value. Use JSON Patch for an ordinary editable field. Partial JSON and JSON Merge Patch documents
require permission to write every protected member, including when a document only changes a public
field. Property `remove` and `owner`
have no supported meaning and fail with a corrective explanation.

Known denied caller fields return 403; unknown or unsupported field intent returns 400. MVC's
native invalid-model response returns 400 when complete typed input binding is refused. Unsupported
typed serializer customization fails before buffered output. Static schemas remain a conditional
superset. Explicit standard Newtonsoft casing and ordinary ShouldSerialize rules are preserved.

This protects retained typed member contracts, not secrets copied by custom code into an unannotated
DTO, JToken or string. Such application projections must own their privacy. Direct execution of an
IActionResult outside the MVC invocation pipeline is also outside the result-filter boundary.

For example, `PATCH /works/id` with `Content-Type: application/json-patch+json` and
`[{"op":"replace","path":"/name","value":"Updated"}]` can edit an admitted field. The equivalent
`application/json` or `application/merge-patch+json` document is admitted as a whole-value write and
requires whole-value permission. Typed application restores a merged working copy rather than editing
the stored row: identity and family-discriminator members, repeated members whose names differ only by
case, and values that would create an abstract or interface member refuse correctively. Enum values
bind by member name, dictionary keys keep their exact spelling, and a null merge-patch dictionary
entry removes that entry.

Typed access sidecars and relationship responses preserve member policy through their known framework
wrappers. Inherited property gates use the actual containing resource type. An explicitly Access-declared
JSON-ignored property still restricts replacement; JSON ignoring remains intact on output.

Object/interface slots, legacy ISerializable contracts and nonsealed object contracts conservatively
require guarded serialization, including a nonsealed root supplied by its actual runtime type. Custom
formatters/resolvers/converters or suppressed buffering on those contracts refuse. Sealed, fully known
unrestricted output contracts retain their original native formatter path. No application override bypasses
this boundary.

## Typed JSON collections

Standard typed POST bodies and Entity PUT replacements can bind additive collection arrays, including
values inside dictionaries. A concrete `IEnumerable<T>` needs a public parameterless constructor and
`Add(T)` when Json.NET has no native constructor. Seeded collections also need `Clear()` so supplied
values replace defaults. Native constructors remain authoritative. No application converter is needed.

This shares collection construction with Data, not persistence permissions: nonpublic setters remain
unwritable and storage discriminators are not introduced. Governed bodies retain complete field-write
admission; PUT uses the same prepared serializer before normal endpoint authorization. Standard naming,
converters and MVC settings are retained. Custom unrestricted resolvers and input formatters remain application-owned;
arbitrary custom serialization and standalone JsonConvert calls are outside this guarantee.
