---
id: SEC-0037
slug: an-edge-that-is-part-of-the-application
domain: Security
status: Accepted
date: 2026-11-02
title: An MCP edge that is part of the application takes the application's bearer
related:
  - SEC-0006
  - SEC-0025
  - SEC-0026
  - ARCH-0141
---

# SEC-0037: An MCP edge that is part of the application takes the application's bearer

Amends [SEC-0025](SEC-0025-an-application-accepts-its-own-bearer-on-its-pages.md) rule 4 and narrows, by the application's
own word, [SEC-0006](SEC-0006-embedded-oauth-authorization-server.md) D2.

## Context

ARCH-0141 made a route a tool. A delegate that holds a bearer for the application's base URL, and reads its ordinary
routes with it, was then refused by those same routes through `/mcp`: the edge wanted a token whose audience is
`{base}/mcp`. And an application that has verified an account by itself, at a door the authorization server does not
stand behind, could reach `IIssuer` but had to know the resource, the lifetime, the shape of a token and the subject's
standing to use it as the server does.

Walking a real TypeScript MCP client through the whole path also found the first thing such a client meets after it
has a token: `initialize` echoed any version asked for, and the transport then refused that version on the next
request. SDK 1.32.2 asks for `2025-11-25`.

**The business sentences:** my MCP edge is part of my application. This account has proved itself; give it the token.

```jsonc
"Koan": { "Mcp": { "AcceptApplicationBearer": true } }
```

```csharp
await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, person);
var token = await AccessToken.For(HttpContext, person, ct);
return token is null ? StatusCode(StatusCodes.Status403Forbidden) : Ok(new { access_token = token.Value, token_type = "Bearer", expires_in = (int)token.Lifetime.TotalSeconds });
```

## Decision

1. **`Koan:Mcp:AcceptApplicationBearer` (default false) adds the application's resource to the names the edge answers
   to.** That resource is what the ordinary routes accept: `Koan:Web:Auth:ResourceServer:Resource` when set, else the
   base URL. `KoanBearerResource.Authenticate` takes the names one edge answers to. `Koan.Mcp` references no web-auth
   package for this: it reads the key the resource edge binds, as the authorization server reads the MCP keys. An
   application that says nothing keeps the edge's own audience alone. **Outside Development a request's `Host` names no
   application:** until a resource or a URL is configured or accepted, the edge answers to its own resource alone, even
   under `DerivesUntilAccepted`. In Development, before a URL exists, the `Host` chooses, as it does everywhere there.
2. **The edge still advertises its own resource.** Its challenge and its RFC 9728 metadata do not change, and a token
   for `{base}/mcp` still works there and on no route. A client that knows only `{base}/mcp` holds the narrower token; a
   delegate that asked for the application holds one token for both doors.
3. **On, the edge requires authentication,** unless `RequireAuthentication` is explicitly false. A standard client
   starts OAuth only on a 401, so an edge that takes a credential must not answer without one.
4. **What stands.** With authentication on, the edge is bearer-only: a cookie never authenticates a request there, and a
   rejected bearer never falls back to one. An explicit `RequireAuthentication=false` is the open edge of SEC-0016, where
   a cookie user is the caller. A token for any other resource, an expired or tampered one, and one from another issuer
   answer 401 `error="invalid_token"` with the metadata, with the resource edge reading the same header first. The limit
   of "any other": until a URL is fixed outside Production, or under `DerivesUntilAccepted`, the edge's own `{base}/mcp` is
   still derived from the request (WEB-0078), so a token this host signed for `https://other/mcp` passes with
   `Host: other`. That predates this decision and yields only the token's own subject: a replay, not an escalation. A session stays its
   subject's whichever of their tokens they present. An origin is the connection's. `[Authorize]`, `[Access]` and
   `[RequireScope]` judge the token alike on both doors, because a tool call is a request (ARCH-0141).
5. **`AccessToken.For(context, principal, ct)` (`Koan.Web.Auth.Server`) is the token the server's flows mint,** for
   the application's own resource, for `AccessTokenLifetime`. It carries the principal's subject, name, email, roles and
   scopes, with the subject's standing over the name and the roles. Null when the subject may not act. It names no client
   and has no refresh token and no grant: whoever proved themselves once proves themselves again. One mint and one standing
   rule now serve it and the flows. Outside Development, with nothing naming the application, it refuses with the correction
   rather than stamp a caller's `Host`.
   **A door signs in first.** Under Koan.Identity a subject with no row has no standing, so a new account gets no token
   until it has signed in; sign-in also runs the application's admission, roles and ban. The door passes the principal it
   signed in, because sign-in settles it: a provider subject linked to a person becomes that person, in `NameIdentifier`
   and now in `sub`, which a token reads first. `AtprotoServiceAuth.SignIn` answers that principal. A sign-in path that
   sets no cookie scored below 5: a door that keeps its cookie needs none.
6. **`initialize` answers a version the server speaks,** from one list the transport also checks: the one asked for
   when it speaks it, else `2025-06-18`, as the specification says. The client then decides.
7. **SEC-0006 D6 is now what the code does: a token holds what was requested and what the person holds.** Any signed-in
   person could register a client, ask for any scope and approve their own consent, and the gate read it as held: a token
   passed `[RequireScope("x")]` where the same person's cookie was refused. The code flow, its remembered path and the
   device flow now grant `AccessGateEvaluator.HasScope(person, scope)` of what was requested, with the spelling requested.
   Remembered consent asks after that same set, the one a grant's key names, so a narrowed consent is remembered. The
   consent page shows it once the person is signed in, and what was requested until then. A grant still answers at the
   gate (SEC-0005), and `/oauth/dev-token` keeps its Development-only override. A refresh carries what was granted: no
   session is there to ask.

## Is the second audience a protection?

SEC-0006 D2 calls the separate audience the confused-deputy fix. Read closely, the deputy it closes is a token for
*someone else*: a sibling resource, a service-mesh token, anything the issuer signed. All of those are still refused,
so this option reopens no confused deputy. A token for the application names this application.

The audience also did two smaller things, and an application that opts in gives up the first of them:

- **It kept a token for the pages away from what no route offers.** A `[Tool]` is a route, so nothing is gained there.
  An `[McpTool]`, an entity's tool and an operational toolset have no route on purpose (ARCH-0141 rule 7), so a token for
  the application now reaches them, behind the same gates, as far as its subject may. "A tool is only a route" is true of
  `[Tool]` alone. An application that opts in is saying this is what it wants.
- **It keeps a token for the edge away from the routes.** This one stands: see below.

## Shapes considered

| Shape | Why not |
|---|---|
| One resource: the edge advertises the base URL, so every client ends with a token for the whole application. Built first in thought, and it is what the package was asked for | RFC 9728 §3.3 says the metadata's `resource` MUST be identical to the URL the client requested when the metadata came from `resource_metadata`, and its data MUST NOT be used otherwise. The TypeScript SDK allows an ancestor (`checkResourceAllowed`), and the walk passes; VS Code's `fetchResourceMetadata` compares exactly and refuses. It also turns every stock client's token into a credential for every route of the application, a settings page included, where today that client can reach only what `[Tool]` exposes. The person's consent would say so, and nothing that client does needs it. An application that wants this has it already: `Koan:Mcp:ResourceUri` set to its base URL, which the walk also proves. |
| The edge advertises `{base}/mcp` and the server stamps the base URL into the token | Strict clients pass, and a person consents to `/mcp` for a token that reaches every route. |
| A second metadata document at the root | No client in evidence needs it, and under a path base the application does not serve it. |
| A concept of its own for an application's token: a client, a grant, a token type | The token is the server's token. A record with a value and a lifetime is all a door has to answer with. |

## Coalescence

- **Closest pattern:** SEC-0025's `KoanBearerResource`, the one rule for a bearer accepted for a resource, and SEC-0028's
  `ConsentPage`, what an application's own page needs from the server, in process.
- **Owner:** Trust owns the rule; `Koan.Mcp` owns which names its edge answers to; the authorization server owns the
  token. `OAuthProtocolEndpoints.Standing` and `Mint` are lifted out of the flows, not copied.
- **Deleted:** the transport's private version list.

## Ergonomics

One setting and one call. A coding model that can write a door can write `AccessToken.For`, and IntelliSense finds it
beside `ConsentPage`. `IIssuer` stays public for what is not this.

## Compatibility

- Opt-in, but for rules 6 and 7. A client asking for a version the server does not speak is answered `2025-06-18` where
  it was echoed. On Streamable HTTP, a client that then sent the echoed version in its header was refused on its next
  request; one that sends no header, and every other transport, never was, and now reads the version it should speak.
- **A token no longer holds a scope its person lacks,** so an application whose gate relied on consent alone to grant a
  scope must give the person that scope, by a claim on the session or an `AgentGrant`. A refresh family issued before this
  keeps the scopes it was granted until it expires or its grant is revoked.
- `IdentityAuthFlowHandler` rewrites `sub` with `NameIdentifier` when it settles a linked subject on a person, so a linked
  person's OAuth token now names that person. It named the provider's subject, and standing then refused it.
- `AtprotoServiceAuth.SignIn` answers the principal. `await proofs.SignIn(...)` still compiles.
- `KoanBearerResource.Authenticate(context, resource)` still compiles; its second parameter is now `params string[]`.

## Evidence

- `ApplicationBearerSpec` (Koan.Mcp.Verbs.Tests, 24), `NamedResourceSpec` (1), `UnaddressedEdgeSpec` (2) and `AddressedEdgeSpec` (1): a real host on a real port, the resource edge on, every call real HTTP. A token for the application reaches a route tool as its subject, with its trailing slash too; `[Authorize]`, `[Access]` and `[RequireScope]` answer alike on both doors, each with a caller who passes and one who fails; a scope narrows nothing a role opens; the token is `remote` at the edge; a token for the edge works there and on no route; the metadata names the edge; a sibling's token, the issuer's default audience, another issuer, an expired, a tampered and a malformed token each answer exactly `Bearer error="invalid_token", resource_metadata=...`; no credential, in Development with nothing else set, is the challenge; a `Basic` header and a cookie are no credential, on POST and on the stream, and a rejected bearer does not fall back to the cookie; a cookie does not drive a session its subject opened; another subject is refused 403 on a session and its DELETE, and the same subject's other tokens pass. A configured resource replaces the base URL at the edge. In Staging with nothing naming the application, a token for a claimed `Host` is refused and only the edge's own is taken; with a URL configured, it is the application whatever the `Host` says.
- `AccessTokenSpec` (15), `NamedAccessTokenSpec` (1), `UnaddressedAccessTokenSpec` (1) and `AddressedAccessTokenSpec` (1) (Koan.Web.Auth.Server.IntegrationTests): the token names the application and the metadata's issuer, lasts the configured lifetime and names no client; it is good on a route and at the edge; it carries the roles it was given; a verb no route offers is reached as far as its subject may; a subject who may not act gets none; standing replaces what the proof captured; a principal with no subject is refused saying so; a code-flow token for the application with `scope=notes.read` answers as the person's cookie does on a role and on a scope, on both doors. The code flow and the device flow grant no scope the person lacks and the ones they hold; a consent to fewer scopes than requested is remembered; the consent seam says what approving gives; a grant still answers at the gate. A configured resource is the audience; in Staging with nothing naming the application it refuses naming the key; with a URL configured it names that URL, whatever the `Host`.
- `AccessTokenAfterSignInSpec` (Koan.Identity.Revocation.Tests, 2), on a host with Koan.Identity: a new account gets no token until it has signed in and then gets its own; an account linked to a person gets that person's.
- `StreamableHttpTransportSpec.Initialize_answers_a_version_it_speaks_and_the_next_request_may_say_it` (3): `2025-11-25` and an unknown version are answered `2025-06-18`, `2025-03-26` is answered in kind, and the next request saying the answer is accepted.
- **The real client.** `@modelcontextprotocol/sdk` 1.32.2's client and OAuth provider contract against a real Koan host, knowing only `{base}/mcp`: 401 with RFC 9728 metadata, discovery, dynamic registration with a loopback redirect, authorization code with PKCE, a person not yet signed in sent through the dev Test provider and returned to the consent page by `return`, approval, redemption, `tools/list`, `tools/call`, a refresh after a refused token, and the token from `AccessToken.For` through the SDK and on a page. Asking for `shelf:scoped` as a person who lacks it, the route is refused and left out of the list. Before rule 6 it stopped at `notifications/initialized` with 400 `unsupported_protocol_version`. It passes for the edge's own resource, for a delegate asking for the application (`validateResourceURL`), with `scope=mcp` and with `ResourceUri` set to the base URL. It is not part of Koan's build, which adds no Node dependency.
- **Measured by removing them,** one at a time, each failing the specs named: the option ignored, 21 specs across both suites; authentication not implied, 16; the edge's own resource forgotten, 2; any audience accepted, 7; a rejected credential proceeding, 11; a cookie authenticating, 2; a session unbound, 1; the application advertised as the resource, 1; a configured resource ignored at the edge, 1; `initialize` echoing, 2; always answering its default, 1; `AccessToken` ignoring a configured resource, 1, naming the edge, 5, ignoring the lifetime, 1, ignoring standing, 3; no roles in the token, 4; the call stamped local, 1; a principal with no subject taking a token, 1. The code flow granting what was requested, 5; the device flow, 2; remembered consent asking after what was requested, 1; the remembered path granting it, 1; the consent page showing it, 2; a grant not answering at the gate, 1; sign-in leaving a linked subject in `sub`, 1; a `Host` naming the application at the edge, 2; the edge ignoring a configured URL, 1; `AccessToken` stamping a `Host`, 1, ignoring a configured URL, 1.

## Later

- A loopback redirect is matched exactly (D4), where RFC 8252 §7.3 and D5's own words allow any port. A client that keeps
  its registration and changes its port registers again.
- The protected-resource metadata offers no `scopes_supported`, because Koan has no scope vocabulary to state.
