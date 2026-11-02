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
var token = await AccessToken.For(HttpContext, principal, ct);
return token is null ? Forbid() : Ok(new { access_token = token.Value, token_type = "Bearer", expires_in = (int)token.Lifetime.TotalSeconds });
```

## Decision

1. **`Koan:Mcp:AcceptApplicationBearer` (default false) adds the application's resource to the names the edge answers
   to.** That resource is what the ordinary routes accept: `Koan:Web:Auth:ResourceServer:Resource` when set, else the
   base URL. `KoanBearerResource.Authenticate` takes the names one edge answers to. `Koan.Mcp` references no web-auth
   package for this: it reads the key the resource edge binds, as the authorization server reads the MCP keys. An
   application that says nothing keeps the edge's own audience alone.
2. **The edge still advertises its own resource.** Its challenge and its RFC 9728 metadata do not change, and a token
   for `{base}/mcp` still works there and on no route. A client that knows only `{base}/mcp` holds the narrower token; a
   delegate that asked for the application holds one token for both doors.
3. **On, the edge requires authentication,** unless `RequireAuthentication` is explicitly false. A standard client
   starts OAuth only on a 401, so an edge that takes a credential must not answer without one.
4. **What stands.** The edge is bearer-only: a cookie never authenticates a request there, and a rejected bearer never
   falls back to one. A token for any other resource, an expired or tampered one, and one from another issuer answer 401
   `error="invalid_token"` with the metadata, with the resource edge reading the same header first. A session stays its
   subject's whichever of their tokens they present. An origin is the connection's. `[Authorize]`, `[Access]` and
   `[RequireScope]` judge the token alike on both doors, because a tool call is a request (ARCH-0141).
5. **`AccessToken.For(context, principal, ct)` (`Koan.Web.Auth.Server`) is the token the server's flows mint,** for
   the application's own resource, for `AccessTokenLifetime`, with the subject's standing over what the principal
   captured. Null when the subject may not act. It names no client and has no refresh token and no grant: whoever proved
   themselves once proves themselves again. One mint and one standing rule now serve it and the flows.
6. **`initialize` answers a version the server speaks,** from one list the transport also checks: the one asked for
   when it speaks it, else `2025-06-18`, as the specification says. The client then decides.

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

- Opt-in, but for rule 6: a client asking for a version the server does not speak is answered `2025-06-18` where it was
  echoed. Such a client could not have made a second request.
- `KoanBearerResource.Authenticate(context, resource)` still compiles; its second parameter is now `params string[]`.

## Evidence

- `ApplicationBearerSpec` (Koan.Mcp.Verbs.Tests, 24) and `NamedResourceSpec` (1): a real host on a real port, the resource edge on, every call real HTTP. A token for the application reaches a route tool as its subject, with its trailing slash too; `[Authorize]`, `[Access]` and `[RequireScope]` answer alike on both doors, each with a caller who passes and one who fails; a scope narrows nothing a role opens; the token is `remote` at the edge; a token for the edge works there and on no route; the metadata names the edge; a sibling's token, the issuer's default audience, another issuer, an expired, a tampered and a malformed token each answer exactly `Bearer error="invalid_token", resource_metadata=...`; no credential, in Development with nothing else set, is the challenge; a `Basic` header and a cookie are no credential, on POST and on the stream, and a rejected bearer does not fall back to the cookie; a cookie does not drive a session its subject opened; another subject is refused 403 on a session and its DELETE, and the same subject's other tokens pass. A configured resource replaces the base URL at the edge.
- `AccessTokenSpec` (8) and `NamedAccessTokenSpec` (1) (Koan.Web.Auth.Server.IntegrationTests): the token names the application and the metadata's issuer, lasts the configured lifetime and names no client; it is good on a route and at the edge; it carries the roles it was given; a verb no route offers is reached as far as its subject may; a subject who may not act gets none; standing replaces what the proof captured; a principal with no subject is refused saying so; a code-flow token for the application with `scope=notes.read` answers as the person's cookie does on a role, and is refused a scope it did not ask for, on both doors. A configured resource is the audience.
- `StreamableHttpTransportSpec.Initialize_answers_a_version_it_speaks_and_the_next_request_may_say_it` (3): `2025-11-25` and an unknown version are answered `2025-06-18`, `2025-03-26` is answered in kind, and the next request saying the answer is accepted.
- **The real client.** `@modelcontextprotocol/sdk` 1.32.2's client and OAuth provider contract against a real Koan host, knowing only `{base}/mcp`: 401 with RFC 9728 metadata, discovery, dynamic registration with a loopback redirect, authorization code with PKCE, a person not yet signed in sent through the dev Test provider and returned to the consent page by `return`, approval, redemption, `tools/list`, `tools/call`, a refresh after a refused token, and the token from `AccessToken.For` through the SDK and on a page. Before rule 6 it stopped at `notifications/initialized` with 400 `unsupported_protocol_version`. It passes for the edge's own resource, for a delegate asking for the application (`validateResourceURL`), with `scope=mcp` and with `ResourceUri` set to the base URL. It is not part of Koan's build, which adds no Node dependency.
- **Measured by removing them,** one at a time, each failing the specs named: the option ignored, 19 specs across both suites; authentication not implied, 13; the edge's own resource forgotten, 2; any audience accepted, 4; a rejected credential proceeding, 8; a cookie authenticating, 2; a session unbound, 1; the application advertised as the resource, 1; a configured resource ignored at the edge, 1; `initialize` echoing, 2; always answering its default, 1; `AccessToken` ignoring a configured resource, 1, naming the edge, 3, ignoring the lifetime, 1, ignoring standing, 2; no roles in the token, 3; the call stamped local, 1; a principal with no subject taking a token, 1.

## Later

- **A scope a client asks for is granted as asked.** SEC-0006 D6 says authority is what was requested and what the person
  holds, and no code intersects them, so a token asking for `x` passes `[RequireScope("x")]` where the same person's
  cookie is refused. Measured by the walk (`-Scope shelf:scoped`). This predates this decision and is the same at
  both audiences. `ScopeClaimSpec` reads consent as the grant, D6 reads it as a ceiling, and one of them has to give.
- A loopback redirect is matched exactly (D4), where RFC 8252 §7.3 and D5's own words allow any port. A client that keeps
  its registration and changes its port registers again.
- The protected-resource metadata offers no `scopes_supported`, because Koan has no scope vocabulary to state.
