---
id: SEC-0026
slug: the-authorization-server-decides-by-the-session
domain: Security
status: Accepted
date: 2026-10-29
title: The authorization server decides by the person's session, never by a bearer
related:
  - SEC-0006
  - SEC-0025
---

# SEC-0026: The authorization server decides by the person's session, never by a bearer

## Context

SEC-0006 bridges the person's cookie session into tokens, and its endpoints read `HttpContext.User` — whatever the
application's default authenticate scheme produced. Once an application's default scheme authenticates a request by its
bearer when it carries an `Authorization` header (SEC-0025's resource edge, or a composition of the application's own),
an agent holding a token for a person is that person at the authorization server too: `GET /oauth/authorize` with the
token takes the remembered-consent path and mints a code for any client the person approved before, with no consent
and no browser; `POST /oauth/request/{user_code}/approve` approves a device request as the person; and the consent seam
reports the agent as signed in. A delegate could thereby give itself, or any client, new grants.

Measured by a consumer's security review of its composition, and reproduced with SEC-0025's edge.

## Decision

**The person who decides is the one signed in by their session, read explicitly.** `/oauth/authorize` (remembered
consent), the `/oauth/request/{rid}` projection (`user.loggedIn`), approve and deny authenticate the person through
`Koan.cookie` themselves and never read `HttpContext.User`. A request that carries only a bearer is not signed in there:
`/oauth/authorize` sends it to the consent page, approve answers `401 login_required`, and the projection reports
`loggedIn: false`. The code and device grants capture the subject, name, email and roles from that session principal.

## Compatibility

- An application whose default scheme is the cookie sees no change.
- In Development, a principal set by the `?_as=` development identity no longer approves a request; sign in through a
  provider (or the Test provider) to approve.
- The `/oauth/dev-token` endpoint, Development-only and documented as a convenience for the signed-in user, still reads
  the request's principal.
- No public surface changes.

## Evidence

- `SessionDecidesSpec` (Koan.Web.Auth.Server.IntegrationTests, Kestrel in Development with
  `Koan:Web:Auth:ResourceServer:Enabled`): after the person approved a client once and its token for the base URL was
  issued, `GET /oauth/authorize` with only that token redirects to `/me/connect?rid=` and mints no code, while the
  person's own browser still takes the remembered path; `POST /oauth/request/{user_code}/approve` with only the token
  answers `401 login_required` and the device keeps polling `authorization_pending`; the request projection with only
  the token reports `loggedIn: false`. With the endpoints reading `HttpContext.User` as before, all three specs fail:
  the token takes the remembered path, approves the device (302) and reads as signed in.
- `OAuthAuthCodeFlowSpec`, `OAuthDeviceFlowSpec`, `OAuthHardeningSpec` and `OAuthRefreshSpec` still pass with the
  person signed in by cookie.
