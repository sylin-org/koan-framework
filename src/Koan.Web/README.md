# Sylin.Koan.Web

Turn Koan entities into controller-first ASP.NET Core APIs, with shared policy, health, and runtime facts.

- Target framework: net10.0
- License: Apache-2.0

## What it adds

- `EntityController<TEntity, TKey>` and the string-key `EntityController<TEntity>` convenience surface
- attribute-routed ASP.NET Core MVC integration
- `[Tool]` on a controller or action, which makes a route also a tool where `Koan.Mcp` is present, and `this.Answer(outcome, done)`, a route's one answer to a command (ARCH-0141)
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
writes derives from it: the authorization server's issuer, the MCP resource identity, OpenGraph card URLs, the atproto
client metadata. Each of those keeps its explicit override (`Issuer`, `ResourceUri`, `ClientId`) for the rare case
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

- A constrained Create branch uses atomic insertion. An identity collision returns existence-hiding
  404, including identities hidden by request predicates. Unsupported providers return 501 with
  `web.mutation.insertUnsupported`. Ordinary unconstrained upsert keeps its existing semantics.
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
