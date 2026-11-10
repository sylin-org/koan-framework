---
id: ARCH-0144
slug: one-request-answered-in-process
domain: Architecture
status: Accepted
date: 2026-11-04
title: A request of the application is answered in process through its whole pipeline, which Koan.Web holds
amends:
  - ARCH-0141
  - WEB-0081
related:
  - SEC-0039
  - ARCH-0143
amended_by: WEB-0089
---

# ARCH-0144: A request of the application is answered in process through its whole pipeline, which Koan.Web holds

> **Amended by [WEB-0089](WEB-0089-a-hook-knows-its-operation-and-a-refused-form-reads-its-page-again.md) (2026-11-10): a refused form's page is handed by its controller, so §7's in-process GET drawing it again is not built;**
> the record serves a tool call, a read by address and the static copy.

## Context

A tool call is a request to its route, made in process and sent through the host's whole pipeline (ARCH-0141), and the parts that
did it lived in `Koan.Mcp`: a decorator of ASP.NET's `IApplicationBuilderFactory` holding the host's request delegate, and an
invoker that built the request, chose which of the caller's headers to carry, and served it. Three more uses need the same thing
and none of them is MCP's: reading the application at an address on a caller's behalf, drawing a refused form's page again as its
own reader, and writing a static copy of what a visitor reads. Each would otherwise build its own request, its own header rule
and its own idea of who the request is read as, or call actions directly and skip what a request passes through, as an
application's archive already does by repeating a gate by hand.

Two defects of the request as `Koan.Mcp` made it were found by reading it and reproduced:

- **The outer request's conditional headers reached the inner one.** Only the connection's headers, `Accept`, the body's
  `Content-Type` and `Content-Length`, `Origin` and MCP's own were withheld, so a client's `If-None-Match` on `POST /mcp` reached
  the route; an application's result filter that answers `304` when the tag matches answered the tool call `304` with no body,
  and the call read `Answered 304.` as a success.
- **The origin record noted a tool call twice.** The inner request arrives on the outer one's scheme and host and passes the head of
  the pipeline, where WEB-0081 notes every origin that is not the base URL in force, so one tool call over HTTP counted 2 for its
  origin; with no request in hand and no base URL, it was noted as `https://localhost`, an origin no one arrived on.

**The business sentence:** answer this request of the application, here, as its reader or as nobody.

```csharp
var page = await pipeline.Send(new InProcessRequest(HttpMethods.Get, "/places/open/topics") { Headers = { Accept = "text/html" } }, ct);
// page.Status 200, page.Headers.ContentType "text/html; charset=utf-8", page.Body the bytes a browser would read
```

The complete surface is `KoanWebPipeline` from DI, an `InProcessRequest` and the `InProcessAnswer` it returns. There is no
option, registration or attribute: `Koan.Web` holds the pipeline whenever the host builds one.

## Decision

1. **One request is answered in process through the application's whole pipeline,** every middleware, filter, binder and
   formatter of it, in a DI scope, `Items` and features of its own, on an execution flow of its own, as ARCH-0141 rule 1 has it
   for a tool call. Nothing cheaper keeps what it keeps: each shape measured below that skips part of the pipeline let a tool call
   through a route that `[Authorize]` refuses.
2. **Koan.Web holds the pipeline.** `KoanWebPipeline` decorates ASP.NET's `IApplicationBuilderFactory`, ASP.NET's own seam, so it
   sits outside every middleware and startup filter and holds what the host built, on a generic host and on `WebApplication` alike;
   `WebModule` registers it with `IHttpContextAccessor`. `Koan.Web` builds the pipeline (`KoanWebStartupFilter`), so it is also
   what answers a request of it; `Koan.Mcp` and every later use consume it as a public `Koan.Web` type, which needs no abstraction
   assembly, as `[Tool]` did not.
3. **It takes** a method and an address, a path and query under the application's base URL, which it prefixes with the path base,
   and which names no dot segment, since HTTP would resolve one before any route saw it and an address here is taken as it is; the
   headers it is given, `Accept` and `Content-Type` among them; a body; values handed in process, which become features of the
   inner `HttpContext`; and its reader. **It answers** the status, the headers and the body, buffered with no cap, as a tool call's
   answer already was. An exception nothing in the pipeline handled reaches the caller as an exception. With no pipeline, in a
   process that serves no web requests, it answers `503` with a `Problem` saying so.
4. **The reader is one of two.**
   - **The caller:** the request in hand's own `Cookie` and `Authorization` are carried, its connection's address too, and no
     principal is placed, so the application's authentication finds the caller as on any route and a credential it rejected stays
     rejected (SEC-0039 rule 7). With no request in hand (STDIO, a command-line verb) it is the principal the caller's host gives,
     with that principal's origin tier as an `OriginFeature`. A principal is never read while a request is in hand.
   - **Nobody:** nothing of the request in hand reaches it but its scheme and host: no header, no connection address, an empty
     principal. A visitor's read inside a signed-in request is a visitor's, whatever header an application's scheme or a web
     context contributor reads its credential from (`X-Test-User` on Koan's own test scheme), and it stands nowhere on a declared
     internal network.
   A request is never read as anyone else: a `Cookie` or `Authorization` among the headers it is given, and a value handed in under
   one of ASP.NET's own server feature interfaces (the `Microsoft.AspNetCore.Http.Features` namespace, `IHttpAuthenticationFeature`
   among them), are refused with an `ArgumentException`, because the reader's credential is the request in hand's to carry and the
   request line, the connection and the reader are the pipeline's to set.
5. **One header rule, the pipeline's.** Of the request in hand the caller's request carries every header except what belongs to its
   connection, its body or its own representation: `Host` and the hop-by-hop headers (`Connection`, `Keep-Alive`,
   `Proxy-Connection`, `Proxy-Authorization`, `TE`, `Trailer`, `Transfer-Encoding`, `Upgrade`, `Expect`); `Forwarded` and
   `X-Forwarded-For`, `-Host`, `-Proto` and `-Prefix`, which its scheme, host and address already resolved; every `Content-*`
   header; `Accept`, `Accept-Charset`, `Accept-Encoding`, the conditionals `If-Match`, `If-None-Match`, `If-Modified-Since`, `If-Unmodified-Since` and
   `If-Range`, and `Range`, which ask about the outer request's own answer; and `Origin`, as SEC-0039 rule 8 has it, while
   `Sec-Fetch-Site` stays so the cross-site check judges the request as the call was. A caller names the headers of its own
   transport to withhold as well (`Koan.Mcp` names `Mcp-Session-Id`, `MCP-Protocol-Version` and `Last-Event-ID`). The headers it is
   given come after, and win.
6. **The pipeline marks its own requests.** A request it makes passes an application's global limiter, because it runs inside the
   outer request's permit and a concurrency limit would wait on itself, and is not noted as an observed origin (WEB-0081), because
   it arrived nowhere: the origin record counts the request in hand once. A limit a route names still binds it. `Koan.Mcp`'s
   `RouteTool` feature still narrows routing to the action a tool names (`NamedActionPolicy`), which stays `Koan.Mcp`'s.
7. **A tool call is one use of it.** `RouteToolInvoker` maps a call's arguments to an address, a JSON body and `Accept:
   application/json`, hands its `RouteTool` in process, and maps the answer to a `CallToolResult` as before; it makes no request
   of its own. The other uses are reading by address, a GET of an address; a refused form's page drawn again, a GET of that page as
   the same reader with the outer request's `Accept` and the values the page draws handed in process, which never runs the write
   again; and the static copy, many GETs as nobody or as the reader, each `Accept: text/html` with a mark handed in process,
   answering bytes and a content type. Those are the packages that build them; this record serves those four and no other.

## The shapes

Each existing spec of the tool path ran unchanged under a prototype of each shape swapped into the invoker's serve step, on the
tree at `3fb5ee90b` (143 specs in `Koan.Mcp.Verbs.Tests`). The prototypes are not committed; B, B2 and C took the principal the
edge's own authentication had made of `/mcp`, with its result, since no authentication of theirs runs, as Koan built B at
`0b14c73e4`. What each shape keeps:

| | A. Nested request through the whole pipeline | B. MVC's action invoker, endpoint set by hand | B2. Routing's matcher, then the endpoint's delegate | C. The action called directly |
|---|---|---|---|---|
| **Authentication** | The application's, reading the carried credential; a rejected one stays rejected; with no request, the principal given and its tier | None runs: borrows the edge's principal, so only a caller the edge already read is anyone | As B | As B |
| **The gate** | Koan's gate before binding, with route values; `[Authorize]` by `UseAuthorization` | Koan's gate; **`[Authorize]` never enforced** | As B | **Neither** |
| **Routing** | Constraints, defaults, path base, the action named | **No constraint** (`../secret` reached the action); path base by hand | Constraints and defaults; path base by hand; the action named | **No constraint** |
| **Binding** | MVC's, Koan's and an application's binders; `[RequestSizeLimit]` | Kept | Kept | **Lost**: a bad value is a 500, a required field is not refused |
| **Filters** | Koan's and an application's | Kept | Kept | **Lost** |
| **Formatters** | Negotiation by `Accept`, an application's `text/html` first | Kept | Kept | Only the result's own execution |
| **Exceptions** | The exception handler middleware and the application's handler; `ToolFilter`'s | `ToolFilter`'s; **no handler middleware** | As B | **Neither** |
| **Cookies and headers** | The one header rule | The rule, but no authentication reads the credential | As B | As B |
| **ETags** | Result filters tag and may answer 304; the header rule withholds the outer conditionals | Filters run, so the outer `If-None-Match` answered **304** | As B | No filter runs |
| **Middleware** | Cross-site check, CORS, the authentication result kept for entities, stage and web-context contributors (tenancy, a placed principal, read filters), the limiter counted once, the application's own | **None**: no tenant, no read filter, no limit a route names | As B | As B |
| **Isolation** | Scope, `Items`, features and flow of its own; the outer accessor as it was | Scope and features of its own | As B | As B |
| **Cost** | Three public types: the pipeline, the request and the answer; 94 production lines in `Koan.Web`, which replace `RoutePipeline`'s 14 and the invoker's 40 that made the request; buffered, and `OnStarting` and `OnCompleted` never fire (read); 310–320 µs a call | The same three, and the action named in place of an address, since nothing routes (read); about 13 lines beyond A's request as prototyped; buffered, no callback fires (read); 210–240 µs | The same three (read); about 25 lines beyond A's request, and a second routing builder; buffered, no callback fires (read); 225–240 µs | The same three, and the method and a binder of its own in place of an address (read); about 45 lines beyond A's request; buffered, no callback fires (read); 120–165 µs |

The rows the tool path's specs cover, and the ETag row, were run; the formatter, exception and isolation cells of B, B2 and C are read
from their code, since no prototype ran `InProcessSpec`, and so is every cell marked (read). Production lines count neither blank lines,
comments nor `using` lines; a rejected shape's are its prototype's serve step, the request it shares with A aside.

**D. Routing, authorization and the limiter as a smaller pipeline** is B2 with three middlewares added: it would close the
`[Authorize]` and limiter rows and still fail every middleware row, since an application's own middleware and Koan's web-context
contributors, which set tenancy and read filters, are not in it, and it is a second copy of `KoanWebStartupFilter`'s order. Ruled
out without a prototype, as ARCH-0141 ruled it out.

**An `HttpClient` over a handler that serves in process** was considered for the surface, since `HttpRequestMessage` and
`HttpResponseMessage` are .NET's own: values handed in process and the reader are not headers, and would ride `Options` keys
that turn into features by convention, and an `HttpClient` that quietly carries the caller's cookie reads like a call to anywhere.
The surface is the pipeline's own small request instead.

## What this overturns

- ARCH-0141's holder of the pipeline and what `Koan.Mcp` registers: `RoutePipeline`, the `IApplicationBuilderFactory`
  replacement and `IHttpContextAccessor` are `Koan.Web`'s (`KoanWebPipeline`), and `OneCountLimiter` keys on the pipeline's own
  mark, registered by `Koan.Web`, so every request made in process passes a global limiter, not only a tool call's.
- ARCH-0141 rule 2's set of headers a tool call's request withholds: rule 5 above is the one header rule, and it withholds
  `Accept-Charset`, the outer conditionals, `Range`, every `Content-*`, the forwarding headers and the rest of the hop-by-hop headers
  as well. SEC-0039 rule 7 still holds: what a tool call keeps of `Cookie`, `Authorization`, `Sec-Fetch-Site` and `Origin` is
  unchanged.
- WEB-0081's record of observed origins no longer counts a request the pipeline made.

## Coalescence

- **Closest pattern:** `RoutePipeline` and `RouteToolInvoker`, the only maker of an in-process request. `McpCustomToolInvoker`
  stays a direct call: a verb with no route has no request to make.
- **Owner:** `Koan.Web.Hosting`, beside `KoanWebStartupFilter`, which builds the pipeline, and `KoanWebBaseUrl`, which names the
  address it answers at. The next wider owner, `Koan.Core`, knows no HTTP; the next narrower, `Koan.Mcp`, is one of four consumers,
  and two of the others are `Koan.Web` packages that cannot reference it.
- **Deleted:** `Koan.Mcp`'s `RoutePipeline`, its registration of the pipeline and the accessor, its wrapping of the global limiter,
  and `RouteToolInvoker`'s header list, request-making, scope and flow.
- **Not a second copy:** the reader places no principal with a request in hand; with none, it sets `HttpContext.User`, as an
  authentication handler's result would, and leaves `WebContext.UsePrincipal` and `PlacedAuthentication` as the one way a
  principal is placed on a request already authenticated.

## Ergonomics

- **Expression:** `pipeline.Send(new InProcessRequest(method, address) { Headers = { Accept = ... } }, ct)`, with `Body`,
  `Features`, `AsNobody`, `Principal` and `Withheld` only where a use needs them. A coding model that has sent an HTTP request
  reads it; `Address` is the request line's path and query, and `Features` is ASP.NET's own `IFeatureCollection`.
- **Guarantee and correction:** the answer is what the application answers that request over HTTP from the same reader; an
  address that is not a path or names a dot segment, a credential among the given headers and a value handed in as one of ASP.NET's
  own server features are refused before anything runs.
- **Public concepts:** `KoanWebPipeline` (the pipeline, which answers), `InProcessRequest` (what to ask, and as whom) and
  `InProcessAnswer` (status, headers, body). The mark, the header rule and the limiter's single count are internal.
- **Cognitive branches:** two readers, one of them the default. No option.

## Compatibility

- `Koan.Web` registers `IHttpContextAccessor` and replaces `IApplicationBuilderFactory` with its decorator in every application,
  where only one with `Koan.Mcp` did; an application that replaces the factory itself after Koan does takes the pipeline away from
  every in-process use, which then answers `503`.
- A global limiter in an application without `Koan.Mcp` is wrapped as one with it was: a request with the pipeline's mark passes it.
- A tool call's request no longer carries the client's `Accept-Charset`, conditionals, `Range`, `Content-*` other than its own, forwarding headers,
  `Keep-Alive`, `TE`, `Trailer`, `Proxy-Connection` or `Proxy-Authorization`. A tool whose route answered `304` to a client's
  `If-None-Match` now answers its body.
- With no web pipeline, a route tool's failed call carries the `Problem` it was answered with in `structuredContent` and
  `meta.problem`, with the same text and `errorCode` as before.
- An observed origin's count no longer includes tool calls' inner requests, and `https://localhost` is no longer noted for a call
  with no request in hand.

## Evidence

- **The tool path, existing specs in place** (`Koan.Mcp.Verbs.Tests`, 143 passing under A). Under B, 13 fail: `ApplicationMiddlewareSpec`
  (the application's middleware never ran: `Expected seen to be 1, but found 0`), `WebApplicationSpec`, `OneVerbTwoDoorsSpec`'s
  class-and-method (3), inherited-and-own declarations (2), ASP.NET policy and `[AllowAnonymous]` (route 403, tool 200
  `{"opened":true}`), route constraint (`Expected tool.Status to be 404 ... found 200 {"name":"../secret"}`), route rate limit
  (`Expected 429 ... found 200`) and middleware-sets-`Items`, and `ResourceServerSpec`'s ASP.NET-answers (route 403, tool 200
  `{"taken":true}`) and role-in-a-token. Under B2, the same 12 but the route constraint. Under C, 26: B's, with more of their cases, and every binding spec
  (`route said 400 invalid "notify: The value 'perhaps' is not valid.", tool said 500 "It failed."`), the required field, the API
  controller's body, each input shape, the empty array, the gated caller told why, the hidden tool, and over STDIO `origin:local`
  (`Expected remote.Status to be 403 ... found 200`). `CookieToolSpec`, `CrossSiteWritesSpec`, `OneCountSpec` and `ToolRequestSpec`'s
  stale credentials pass under every shape on the tool path, because the edge read the same credential first and no middleware of
  the inner request ran; `ToolRouteSpec` runs no in-process request and says nothing of a shape.
- **The time of one call** (a route tool with no request in hand, `Debug`, 50 calls to warm and 1000 measured, twice): A 309 and
  318 µs, B 239 and 209, B2 242 and 226, C 163 and 118. The whole pipeline costs under a tenth of a millisecond more than the
  narrowest shape that still runs filters.
- **The surprises, reproduced on `3fb5ee90b`:** a tool call carrying `If-None-Match: W/"tagged"` to a GET route whose result filter
  tags `W/"tagged"` read `Answer { Status = 304, Text = Answered 304., Failed = False }`; one tool call over HTTP added 2 to its
  origin's count, and one with no request in hand left `https://localhost=1` in the record. The same reproducers pass on this
  record's tree.
- **The primitive** (`InProcessSpec`, `Koan.Web.Extensions.Tests`, on a host of its own with a scheme reading `Authorization` and a
  second header, a `text/html` formatter claiming only the spec's own resource, `WithExceptionHandler` with an application handler
  and a result filter that answers `304`): a read as nobody inside a signed-in request is refused where a visitor is, whichever
  header the scheme reads; a read as the reader is the reader's own; with no request in hand it is the principal given, and a
  principal given beside a request in hand is not read; a GET by address keeps its route constraint; the application's `text/html`
  formatter answers when `Accept` asks for it; a value handed in process reaches the action and the formatter; an exception
  downstream answers as the application's handler answers; the outer request's `If-None-Match` does not reach the inner one, and
  the inner one's own tag still answers `304` to a conditional it is given; `Items` and features are the inner request's own and
  the outer accessor is as it was; a refused form's page is drawn again without running the write again; many GETs as nobody
  answer bytes and a content type; nobody carries no connection address, so a route gated `origin:internal` admits the reader
  arriving from a declared network and refuses nobody; a pipeline the host never built answers `503` with its `Problem`; a credential
  among the given headers, an address that is not a path, a dot segment, plain or escaped, and a value handed in as
  `IHttpAuthenticationFeature` are refused. `UnbuiltPipelineSpec` (`Koan.Mcp.Verbs.Tests`): a tool call through a pipeline never built
  says `This process serves no requests.` and carries that `Problem` in `structuredContent` and `meta.problem`.
- **The origin note** (`InProcessOriginSpec`, `Koan.Web.WellKnown.Tests`): a request whose action makes one in process counts its
  origin once, and a request made with none in hand notes nothing.
- **Measured by removing them:** with the conditionals carried again, the `If-None-Match` spec fails; with the origin note not
  exempting the mark, the origin spec fails; with nobody carrying the request in hand's headers, the nobody spec fails; with nobody
  given the request in hand's connection address, the `origin:internal` spec fails.

## Later

- A request made in process from a request that was itself made in process with no request in hand (a STDIO call whose route draws
  a page again in process) is read from the inner request's headers, which carry no credential, so it reads as a visitor; it fails
  closed, and no use does it yet.
- A tracing span does not flow to the inner request, which runs on a flow of its own: a request log shows two unrelated traces.
- An address that names a dot segment is refused rather than resolved; reading by address may resolve one as HTTP would.
