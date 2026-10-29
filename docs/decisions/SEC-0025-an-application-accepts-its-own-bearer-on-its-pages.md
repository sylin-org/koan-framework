---
id: SEC-0025
slug: an-application-accepts-its-own-bearer-on-its-pages
domain: Security
status: Accepted
date: 2026-10-29
title: An application accepts its own bearer on its pages
related:
  - SEC-0001
  - SEC-0006
  - SEC-0016
  - SEC-0023
  - WEB-0078
---

# SEC-0025: An application accepts its own bearer on its pages

## Context

Only the MCP edge accepted the authorization server's bearer: it ran `Koan.bearer` explicitly and compared the token's
audience with its own resource (SEC-0006 D2/D3). An application whose people approve an agent so that the agent reads
its ordinary pages as them had to compose the rest itself: a policy scheme choosing the bearer when an `Authorization`
header is present and the cookie otherwise, an `OnTokenValidated` hook checking the audience against the base URL and
copying `sub` into `ClaimTypes.NameIdentifier`, and an MVC filter answering 401 to a rejected token on pages that admit
anonymous callers. Each consumer would write the same security-bearing composition, and the audience rule would exist
twice, once in the MCP edge and once in each application.

Measured by a consumer that composed exactly that edge beside Koan's authorization server.

## Decision

**Trust owns the rule for a bearer accepted for one resource; Web Auth offers it for the application's own pages as one
setting; the MCP edge uses the same rule for its resource.**

1. `KoanBearerResource.Authenticate(context, resource)` (Trust) runs `Koan.bearer`, leaves its no-result alone, fails a
   token whose `aud` does not name the resource with or without one trailing slash (ordinal) or that has no `sub`, and
   otherwise adds `ClaimTypes.NameIdentifier` from `sub`, the identifier the person's session carries.
   `KoanBearerResource.Accepts` is the audience rule and `KoanBearerResource.Challenge` the RFC 6750 header value.
2. `Koan:Web:Auth:ResourceServer:Enabled` (Web Auth, default false) makes the application a resource server for its
   ordinary requests. The scheme `Koan.resource` (`AuthenticationExtensions.ResourceServerScheme`) becomes the default
   authenticate, challenge and forbid scheme; sign-in, sign-out and the default scheme stay `Koan.cookie`. The edge
   engages only on an `Authorization` header whose scheme is `Bearer`, compared without case (RFC 9110 §11.1). A
   request without one — no header, or a header of another scheme — is forwarded whole to the cookie: a `Basic` header
   carries no token this edge could accept, and a reverse proxy that passes its own `Basic` credentials upstream (nginx
   `auth_basic` does by default) must not turn every page into a 401 for a signed-in person; the session still needs
   antiforgery for any post. A request with a bearer is authenticated by it alone, for
   `Koan:Web:Auth:ResourceServer:Resource` when set (an absolute URI, checked at start) or the base URL (WEB-0078); an
   empty or rejected bearer is a failure, never a fall back to the cookie.
3. A rejected token answers 401 with `WWW-Authenticate: Bearer error="invalid_token"`: from the scheme's challenge when
   an endpoint requires authentication, and from a global MVC authorization filter before any controller action or
   Razor Page that admits anonymous callers, so an agent learns its token was refused instead of reading as a visitor.
   The filter skips an endpoint marked `ResourceServerExemptAttribute`. The embedded authorization server's controller
   carries it: its protocol endpoints never authenticate by bearer (SEC-0026), so a client that attaches its token by
   default — an expired one included — still refreshes, registers, starts a device request and reads the discovery
   documents, and gets the RFC 6749 §5.2 errors rather than the edge's 401. A provider endpoint that checks a token of
   its own, hosted in the application, must carry it too, or the edge refuses that token before the endpoint sees it:
   the dev Test provider's authorize, token, userinfo and discovery controllers carry it, so its OAuth handler's call to
   its own userinfo endpoint with the provider's opaque token completes the sign-in with the edge on. A minimal-API or `MapRazorComponents`
   endpoint that admits anonymous callers runs as a visitor; endpoints outside MVC that authenticate a bearer for a
   resource of their own answer in their own terms.
4. The MCP edge authenticates through `KoanBearerResource` for its resource (`McpResourceIdentity.Resolve`). Its
   resource stays distinct from the application's: a token for the base URL is refused at `/mcp`, and a token for
   `/mcp` is refused on the pages. It keeps its RFC 9728 challenge; `McpResourceIdentity.AudienceMatches` and
   `BuildChallenge`, which the shared rule replaces, are removed.
5. The composition facts carry `koan.auth.resourceserver.enabled` with the resource while the edge is on.

## Compatibility

- Opt-in. Without the setting the application authenticates exactly as before; the scheme is registered and unused.
- Amended by K8: with the edge on, a request whose `Authorization` header has another scheme than `Bearer` used to be
  a rejected token (401 before the action, the cookie never read); it is now the session's request, a visitor without
  one. With the edge on, sign-in through the dev Test provider used to fail at its userinfo call; it completes.
- An application that composed its own edge removes its policy scheme, its `DefaultAuthenticateScheme` assignment, its
  `OnTokenValidated` audience hook and its rejected-bearer filter, and sets `Koan:Web:Auth:ResourceServer:Enabled`.
  Its pages then read the person from `ClaimTypes.NameIdentifier` as before.
- With the edge on and MCP's `RequireAuthentication` off, a request to MCP carrying a token for the application's pages
  is the default scheme's user, as SEC-0016 already treats a cookie user; with it on, MCP still requires its own
  resource.
- The MCP edge now accepts its resource with a trailing slash, answers an expired, tampered or foreign token with
  `error="invalid_token"` (it answered those with a bare challenge; the wrong-audience case is unchanged), no longer
  sends `error_description="audience"`, and its principal gains `ClaimTypes.NameIdentifier`.
- `KoanBearerResource`, `ResourceServerOptions`, `AuthOptions.ResourceServer`,
  `AuthenticationExtensions.ResourceServerScheme` and `ResourceServerExemptAttribute` are added; `McpResourceIdentity.AudienceMatches` and `BuildChallenge`
  are removed.

## Evidence

- `ResourceEdgeSpec` (Koan.Web.Auth.Integration.Tests, Kestrel under `https://www.example.com/app`, edge on): a token for
  the base URL, with or without its trailing slash, authenticates an anonymous page as its subject under
  `ClaimTypes.NameIdentifier`, by `Koan.bearer`; a token for `/app/mcp`, for another origin, tampered or not a token
  answers 401 with exactly `Bearer error="invalid_token"` on an anonymous page and on an `[Authorize]` one; signed in by cookie, the page reads the cookie user without a header, the bearer's
  subject with a valid one, and 401 with a rejected one; a configured `urn:` resource replaces the base URL; without
  the setting a bearer is not read; a resource that is not an absolute URI refuses the start naming the key.
- `ResourceEdgeExemptionSpec` (Koan.Web.Auth.Server.IntegrationTests, edge on): carrying an expired token signed by the
  application's own key, a refresh grant returns new tokens, `/oauth/device` and `/oauth/register` answer 200 and 201,
  an unknown device client gets `400 invalid_client`, and the metadata and JWKS answer 200; an application action that
  admits anonymous callers answers 401 with `Bearer error="invalid_token"`. Before the exemption the first two specs
  fail.
- `ResourceEdgeSpec.An_authorization_header_of_another_scheme_is_left_to_the_session` (K8): signed in by cookie, a
  request with `Authorization: Basic …` reads as the person on an `[Authorize]` action and on an anonymous one; a
  request with only that header reads as a visitor; a rejected bearer still answers 401 `Bearer error="invalid_token"`.
  Before the amendment the first request answered 401. `ResourceEdgeTestProviderSpec` (K8, Kestrel under a loopback
  base URL with a path, edge on, the dev Test provider): sign-in through `/app/auth/test/challenge` lands on the page
  named by `return` and the page reads the person by the cookie; an application action still answers 401
  `Bearer error="invalid_token"` to a token the edge rejects. Before the exemption the callback answered 500, since the
  provider's userinfo call was refused by the edge.
- `KoanBearerResourceTests` (Koan.Security.Trust.Tests): the audience rule's table, trailing slash both ways, ordinal,
  an unbound token, and the challenge's forms.
- `McpEdgeResourceSpec` (Koan.Web.Auth.Server.IntegrationTests): the MCP resource with a trailing slash reaches the
  transport; a token for the base URL and one signed under another issuer answer `error="invalid_token"` with the
  metadata URL; no token is a bare challenge pointing at the metadata. `McpAuthRampSpec`, `McpConfiguredResourceSpec`,
  `BaseUrlPathSpec` and the Streamable suite still pass.
