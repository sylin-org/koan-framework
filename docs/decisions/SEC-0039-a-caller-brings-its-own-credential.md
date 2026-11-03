---
id: SEC-0039
slug: a-caller-brings-its-own-credential
domain: Security
status: Accepted
date: 2026-11-02
title: A caller brings its own credential, and SEC-0037 is overturned
related:
  - SEC-0006
  - SEC-0016
  - SEC-0025
  - ARCH-0141
  - AI-0037
supersedes: SEC-0037
---

# SEC-0039: A caller brings its own credential, and SEC-0037 is overturned

Amends [SEC-0006](SEC-0006-embedded-oauth-authorization-server.md) D2 and D3, [SEC-0025](SEC-0025-an-application-accepts-its-own-bearer-on-its-pages.md),
[ARCH-0141](ARCH-0141-one-verb-a-route-that-is-also-a-tool.md) rule 2 and [AI-0037](AI-0037-mcp-streamable-http-transport.md).

## Context

[SEC-0037](SEC-0037-an-edge-that-is-part-of-the-application.md) rests on a premise that is wrong: that something acts on a
person's behalf, holding a token the application mints for it, and that the MCP edge should take that token. A person signs
in with their own credentials, and an agent with its own. Nothing in an application of Koan holds a credential for someone
else, so there is no token for the application to mint and no second audience for the edge to answer to.

## Decision

1. **SEC-0037 is overturned.** Its rules 1 to 5 are gone: `Koan:Mcp:AcceptApplicationBearer`, the authentication it
   implied, `AccessToken.For` and the record it answered, and the `params` names of `KoanBearerResource.Authenticate`,
   which is again one resource. What existed only for them goes with them: the flows' token mint and standing rule are
   theirs again, `SessionPrincipal` names a client again, and `AtprotoServiceAuth.SignIn` answers nothing.
2. **What stood on its own is kept.** These were SEC-0037's rules 6 to 9 and its fixes, and they are now this record's:
   - `initialize` answers a version the server speaks, from the one list the transport checks.
   - Outside Development a request's `Host` names no audience, at any door: `KoanWebBaseUrl.Audience` is the configured or
     accepted URL, and in Development the request's. Until one exists, Web Auth's resource edge refuses every bearer, on a
     page and at `/mcp` alike, and a cookie still signs in. An explicit `ResourceServer:Resource` names its own.
   - A session cookie that no longer holds is a visitor on every route (`SessionCookieHandler`). A credential a caller
     presented stays rejected.
   - A token holds what was requested and what the person holds (SEC-0006 D6), in the code flow, its remembered path and the
     device flow, and the consent page shows that.
   - `IdentityAuthFlowHandler` rewrites `sub` with the person it settles a linked subject on, so a token from the flows names
     that person.
   - An empty array binds empty, `= null` parameters included; a JSON content type with no body has no fields; a body or a
     form past its limit answers 413 on both doors; a `GET` and a `HEAD` on one action are one tool; a tool call's refusals.
3. **A tool says what it does, as the specification reads it.** `[Tool]` states `Destructive`, `Idempotent` and `OpenWorld`,
   on an action or for a whole controller. Unstated, a route says what its method promises: a read is read-only, idempotent,
   not destructive and closed-world; a `DELETE` is destructive; a `PUT` and a `DELETE` are idempotent; of a `POST`, `PUT` or
   `PATCH` nothing is said about destruction or the open world, which the specification reads as "it may". An entity tool
   says all four, an upsert and a patch being destructive. Every hint known is on the wire, where a destructive hint was
   once lost when read-only was unknown. A tool's title is its name in words (`delete_role` is "Delete role"), not its
   description. A route tool offers no `dry_run`: its effects are the action's.
4. **The protocol as written.** `ping` answers an empty object, an unknown tool is `-32602`, and the server speaks
   `2025-11-25` beside the three revisions before it.
5. **No MCP session, as the only mode.** A tool call is already a fresh request, and each request is authorized as the
   credential it carries (SEC-0016), so a session held nothing. Each `POST /mcp` stands alone and is answered as one
   `application/json` object; `initialize` returns no `Mcp-Session-Id`, and one a client still sends means nothing. A `GET`
   that is not a browser's answers `405` with `Allow: POST`, with no credential asked for, since there is no stream to
   refuse one for; a `DELETE` answers `405` from routing. The session registry and its cap and idle timeout, the subject
   check on a session, server push, resumption with `Last-Event-ID`, and the per-request SSE answer are gone, with the
   transport options that tuned them. Nothing in Koan sent anything on a stream: `listChanged` was already false. The
   deprecated `/sse` + `/rpc` pair needs a session by nature and is removed with `EnableLegacySseTransport`, as is the
   validation that named it for the retired `EnableHttpSseTransport`. Any node can answer any request.
7. **A tool call carries the caller's own credential, and the edge has none of its own.** The request a route tool makes
   keeps `Authorization` and `Cookie`, and places no principal and no authentication result: the application's
   authentication finds the caller there as on any route, a rejected credential rejected as anywhere. A tool with no route
   takes the principal the application's authentication made of the `/mcp` request, which runs before every endpoint.
   With no request behind the call (STDIO) the inner request is the principal given, origin `local`. `McpEdgeAuth`, its
   challenge, `McpResourceIdentity`, the protected-resource metadata at `/.well-known/oauth-protected-resource{route}` and
   `ResourceUri` are removed: they existed for an OAuth door at the edge. A caller with no credential is a visitor, judged
   by each tool's gate. `RequireAuthentication` now defaults to false in every environment, and true closes the door to
   visitors with `RequireAuthorization()`, answered as the application's authentication answers anyone signed out. Koan's
   OAuth server stays for other applications: its token is for the application, and the ordinary resource edge reads it
   at `/mcp` as on a page; the dev token defaults to that audience. A session cookie that no longer holds is a visitor
   there too, and a write is refused with the sign-in gate.
8. **A forged tool call cannot be sent, and a forged request is refused, with nothing set.** Without sessions nothing
   forced a preflight on a tool call, and `/mcp` never read the content type, so a `text/plain` post from a sibling
   subdomain or another port of `localhost`, which `SameSite=Lax` sends the cookie to, ran tools as the person.
   - **`POST /mcp` takes `application/json` alone** and answers anything else `415`. Every conformant client sends JSON,
     and a page of another site cannot without a preflight no CORS policy grants. It needs no option.
   - **`Koan:Web:RefuseCrossSiteWrites`, on by default,** refuses with 403 an unsafe request that carries a cookie and that
     a page of another site sent, at every endpoint, `/mcp` included. A forgery borrows an ambient credential, so a request
     with no cookie has nothing to borrow and is not judged: a client posting to Koan's OAuth token endpoint passes.
     `Sec-Fetch-Site` decides when present (`same-origin` and `none` pass); else `Origin` must be the request's own scheme
     and host, `null` refused; with neither, the request is not a browser's and passes. Proxies and `Host` cannot touch the
     first branch. A tool call's own request keeps `Sec-Fetch-Site` and drops `Origin`, so it is judged as the call was.
     No Koan sign-in posts across sites: OIDC is `ResponseMode=query` and the OAuth and atproto callbacks are redirects,
     so the correlation cookie never rides a cross-site post. The option exists for one need: a front end on another site
     that calls this application with its session cookie under a credentialed CORS policy, such as `Koan:Mcp:AllowedOrigins`.
   - **A credential the caller presented and the application refused is `401` at `/mcp`** before any tool runs, as REST
     answers it, where it once reached entity tools as a visitor (they build their request with no `HttpContext`, so the
     refusal never reached them). A client holding an expired bearer is told to refresh. A session cookie that no longer
     holds is no result, so it stays a visitor.
9. **Code mode keeps its HTTP pieces.** `koan_code_execute` is unreachable over HTTP, since the HTTP dispatcher knows
   entity and custom tools alone, but mapping `/mcp/sdk/definitions` is also what writes the `.d.ts` the code-mode sample
   reads. Removing code mode's HTTP side means moving that write first; removing code mode is its own decision.

## Compatibility

- `Koan:Mcp:AcceptApplicationBearer` is no longer read. An application that set it sees the edge take its own audience
  alone; the edge then has no authentication of its own, and `RequireAuthentication` defaults to false everywhere.
- `AccessToken` is removed from `Koan.Web.Auth.Server.Protocol`. A door that minted one has no replacement here: a caller
  signs in with its own credential.
- `KoanBearerResource.Authenticate(context, resource)` takes one resource again.
- `AtprotoServiceAuth.SignIn` returns `Task`.
- A `POST`, `PUT` or `PATCH` route tool no longer says `destructiveHint: false` and `openWorldHint: false` unless it states
  them, and an entity upsert or patch now says `destructiveHint: true`. A client asks its person where it did not.
- A tool's `annotations.title` is its name in words. A route tool's schema has no `dry_run`.
- `ping` answers `{}` where it answered `"pong"`; an unknown tool is `-32602` where it was `-32601`; a client asking
  for `2025-11-25` is answered in kind.
- **MCP over HTTP keeps no session.** A client that read `Mcp-Session-Id` reads none and goes on, as a standard client
  does. A client that parsed the per-request SSE answer reads JSON, which the specification already required it to.
  `EnableLegacySseTransport`, `MaxConcurrentSessions`, `SessionIdleTimeout`, `Transport.StreamableJsonResponse`,
  `Transport.StreamReplayBufferSize`, `Transport.MaxRetainedStreamsPerSession`, `Transport.SseBufferSize` and
  `Transport.SseKeepAliveInterval` are no longer read. `McpSessionManager`, `McpSession`, `McpSseStream`, `HttpSseTransport`
  and `HttpSseHeaders` are removed; `McpSessionManager.RequestPrincipal` is `StreamableHttpTransport.RequestPrincipal`.

- **The MCP edge has no authentication of its own.** `Koan:Mcp:ResourceUri` is no longer read, the protected-resource
  metadata and the `resource_metadata` challenge are gone, and a standard MCP client no longer discovers Koan's OAuth
  server from a `401` at `/mcp`. `RequireAuthentication` defaults to false in Production and containers too, where it
  was true: a caller with no credential is a visitor, and each tool's gate decides. An application that kept the door
  closed by default sets it. A bearer is read at `/mcp` only by the application's own authentication, so a token for
  `{base}/mcp` is no credential; ask for the application's resource with Web Auth's resource edge on. The dev token's
  default audience is the application's resource, where it was `{base}/mcp`. `KoanBearerResource.Challenge` takes no
  `resourceMetadata`.
- **Open entity tools and `[McpTool]` verbs take anonymous writes in Production too.** `RequireAuthentication` was true
  there and is now false, so a visitor reaches every tool whose gate lets a visitor through. That is REST's answer for an
  entity with no `[Access]`, since both doors allow by default, and visitors are wanted. The remedy is to declare it:
  `[Access]` on the entity, or on the verb.
- **Known gap: a controller's `[Authorize]` does not reach an entity's tools.** Entity tools call `IEntityEndpointService`
  directly (`EndpointToolExecutor`), not the controller, so an `EntityController<T>` closed with `[Authorize]` is closed on
  REST and open over MCP. Declare `[Access]` on the entity. The natural fix is entity tools being route tools of their
  controller, which is its own package.
- `Koan:Web:RefuseCrossSiteWrites` is new and on: a cross-site unsafe request carrying a cookie is refused, at every
  endpoint. An application whose front end lives on another site turns it off.
- `POST /mcp` with a content type that is not JSON answers `415`.
- A refused credential at `/mcp` is `401` with the application's challenge, where a tool call once ran as a visitor.
- `Koan.Mcp` no longer references `Koan.Security.Trust` or `Koan.Web.Sse`, so an application referencing `Koan.Mcp` alone
  no longer activates the trust fabric. Its `dependency-versions.json` still lists both until the release run of
  `stamp-dependents`, which on this branch rewrote 101 stamps from branch heights and so belongs to the release.

## Limits

- Which MCP clients can set a `Cookie` header is not known, nor what WebKit before 16.4 sends for `Sec-Fetch-Site`.
- `[RequireScope]` on a controller action is enforced when the action is a `[Tool]`; the specs here keep theirs tools, as
  SEC-0037's did. Whether a plain action's `[RequireScope]` is enforced was not checked.

## Evidence

- `ToolRequestSpec` (Koan.Mcp.Verbs.Tests): empty arrays on both doors, a JSON content type with no body, a form and a body
  past the limit, a `GET` and a `HEAD` as one tool. `StaleCookieWithTheEdgeSpec`, `StaleCookieWithoutTheEdgeSpec`,
  `UnaddressedPagesSpec` and `AddressedPagesSpec`: SEC-0037's specs of what is kept, on the same real host.
- `GrantedScopeSpec` (Koan.Web.Auth.Server.IntegrationTests): SEC-0037's specs of the scope clamp, on the routes.
- `ToolAnnotationsSpec` (Koan.Mcp.Verbs.Tests) and `AnnotationDerivationSpec` (Koan.Mcp.Conformance.Tests): all four hints
  on every route and entity tool, what a route and a controller state, what an unstated mutating route leaves out, a title
  from the name.
- `StreamableHttpTransportSpec`, `StreamableAuthSpec`, `StreamableWithExplorerSpec` and `StreamableAuthWithExplorerSpec`
  (Koan.Mcp.Streamable.IntegrationTests): `initialize` answers JSON with no session id; `tools/list` stands alone; a stray
  session id means nothing; a notification is 202; a `GET` and a `DELETE` are 405 and a stream `GET` asks for no
  credential; `ping` is empty; an unknown tool is `-32602`; each request is its own bearer's, a scope downgrade honored at
  once. `TransportContractSpec` (Koan.Mcp.Conformance.Tests): no session option and no legacy setting remain.
- `ResourceServerSpec` and `CookieToolSpec` (Koan.Mcp.Verbs.Tests): a token for the application is its subject on both
  doors, a token for an MCP audience is no credential, roles and ASP.NET's answers alike on both doors, a cookie reaches a
  route tool as its person and none is a visitor. `McpAuthRampSpec`, `AcceptedBaseUrlSpec` and `BaseUrlPathSpec`
  (Koan.Web.Auth.Server.IntegrationTests): no metadata, a closed door refused as the application refuses, a foreign token
  refused by the resource edge, the dev token for the application reaching `/mcp`, a stale token refused there after
  the URL moves. `StreamableAuthSpec`: the same over the Streamable transport.
- `CrossSiteWritesSpec` and `CrossSiteWritesOffSpec` (Koan.Mcp.Verbs.Tests): on `/mcp` with a `text/plain` body,
  `cross-site` and `same-site` refused, `same-origin` and `none` pass whatever `Origin` says, a foreign `Origin` and
  `null` refused without `Sec-Fetch-Site`, its own passes, neither passes; a route's post refused alike and a read not;
  off, nothing is refused.
- `CrossSiteWritesSpec`, `CrossSiteWritesOffSpec` and `RejectedCredentialAtTheEdgeSpec` (Koan.Mcp.Verbs.Tests), on a host that
  sets nothing: `/mcp` answers a `text/plain`, form or multipart post `415`; a cookie-bearing `cross-site` or `same-site` post is
  refused there and on a route, a cookie-less one is not judged; a refused bearer is `401 invalid_token` at `/mcp` as on
  REST, for `tools/list` and `tools/call`, and a stale session cookie stays a visitor. Each fails with its rule removed:
  any content type taken (3), every request judged cookie or not (1), off by default (5), a refused credential reaching
  the tools (3, with `OneVerbTwoDoorsSpec`).
