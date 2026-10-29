---
id: WEB-0078
slug: one-canonical-base-url
domain: Web
status: Accepted
date: 2026-10-29
title: Koan.Web owns one canonical base URL, and serves under its path
related:
  - WEB-0069
  - SEC-0004
  - SEC-0006
  - WEB-0070
  - WEB-0071
  - ARCH-0128
---

# WEB-0078: Koan.Web owns one canonical base URL, and serves under its path

## Context

A Koan web application had no single answer to "what is my public URL?". `Koan:Web:ApplicationUrl` was read only by
`KoanWeb.Urls.Base()`, a display helper with no callers, and it dropped the path. Four pillars answered the question
alone: `Koan.Web.Auth.Server` from `Issuer` or the request host (`RequestHost`), `Koan.Mcp` from `ResourceUri` or the
request host (`McpResourceIdentity`), `Koan.Web.OpenGraph` from the bare request scheme and host, and the atproto
connector from a `ClientId` the application had to write by hand. Nothing in Koan.Web or Koan.Core applied a path base,
so an application that must run under `https://www.example.com/app` wrote its own `IKoanWebPipelineContributor` and
still could not get ahead of the static-file middleware and the health alias, which `KoanWebStartupFilter` mounts
before the `BeforeRouting` stage. Forwarded headers were left to the application (`OriginOptions`), which cannot place
them before Koan's pipeline either, because a startup filter wraps the application's own `Configure`.

The same question was being answered four times, each from the request `Host` header a caller controls.

## Decision

**`Koan:Web:ApplicationUrl` is the one canonical base URL. Koan.Web parses it once, serves under its path and nothing
outside it, and every pillar that writes an absolute URL derives from it.**

1. `KoanWebBaseUrl` (Koan.Web, a singleton) holds the configured value in two forms: the URL string with its path
   percent-encoded in canonical form (`Configured`, `https://www.example.com/my%20space`) and the decoded path the
   pipeline matches (`PathBase`, `/my space`); lowercase scheme and host, the port only when it is not the scheme's
   default, no trailing slash. A value that is not the application's public URL stops boot with a correction: not
   absolute http(s); a query, fragment or user information; an encoded path separator (`%2F`), which has no single
   request path; or an unspecified listen address (`0.0.0.0`, `::`, `+`, `*`), which is where the server binds, not
   where the public reaches it. `Resolve(HttpContext)` returns the configured value or, when none is configured,
   the request's scheme, host and path base — silently in Development, with one warning in Staging, Test and CI,
   and in Production it refuses at the point of need with a correction naming the key (`KoanEnv.Gate.Evaluate` on
   a `KoanMagic`, ARCH-0128; `Koan:AllowMagicInProduction` is the consent). An application that never writes an
   absolute URL keeps booting without the key; a consumer that needs one — the issuer, the MCP audience and
   metadata, an OpenGraph card, the atproto identity, the Test IdP — cannot publish an origin a caller chose.
   `KoanWeb.Urls.Base()` reads the same normalisation.
2. `KoanWebStartupFilter` applies the posture at the head of its pipeline, before the exception handler, the security
   headers, static files, the health alias and every stage: `UseForwardedHeaders` when `Koan:Web:TrustedProxies`
   declares a proxy, then `UsePathBase` with the base URL's path, then a guard that answers 404 to any request whose
   path base is not the configured one. Nothing escapes it because nothing is mounted before it.
3. `Koan:Web:TrustedProxies` lists the proxies (IP or CIDR) whose `X-Forwarded-Proto` and `X-Forwarded-For` are
   believed, for one hop. Empty honours nothing, like `Koan:Web:Origin`. `X-Forwarded-Host` is not read: the host is
   the base URL's. The pillar owns this because only the pipeline owner can place it first (the WEB-0069 argument);
   the application keeps `ASP.NET`'s own `ForwardedHeadersOptions` for anything beyond one trusted hop.
4. Consumers derive from the base URL and keep their explicit overrides: `AuthServerOptions.Issuer` defaults to it;
   `McpServerOptions.ResourceUri` defaults to it plus the MCP route, and the authorization-server and metadata URLs
   derive from it; the dev token's default audience is that same MCP identity (read through the `Koan:Mcp` keys,
   since Auth.Server does not reference Koan.Mcp); OpenGraph roots `og:url`, `og:image` and the canonical link at
   it; the atproto connector derives its client-metadata URL and redirect from it (`AtprotoClientIdentity`), so
   `ClientId` is an override; the Development Test IdP issues under it, serves its login page under the path base,
   its discovery endpoints follow the back-channel request under that path base, its zero-config redirect
   whitelist accepts the callback under the path, and the OAuth2 handler's relative endpoints are rooted at the
   path base (the configured one at boot, the request's at challenge time).
5. Cookies are scoped to the application root: the Web Auth session cookie takes the request's path base (the
   cookie builder's default once no explicit `/` is set), and the WEB-0071 correlation and nonce cookies take the
   path base or `/` through `RequestSchemeAdaptiveCookieBuilder`.
6. The host states its base URL in the runtime facts (`koan.web.baseurl`) and the boot report.

## Compatibility

- An application that set `Koan:Web:ApplicationUrl` with a path now serves only under that path. One that set it to
  a listen address with a wildcard host (`http://+:5080`) fails boot with a correction; the key means the public URL.
- An application behind a path-stripping proxy that set `ApplicationUrl` with the path must now have the proxy
  forward the path, or set the value without it. `ApplicationUrl` without a path changes nothing in the pipeline.
- `Issuer`, `ResourceUri` and `ClientId` keep their meaning when set. When unset and `ApplicationUrl` is set, they
  now follow it instead of the request host.
- Auth cookies issued under a path base now carry that path instead of `/`.
- The atproto validator accepts a client-metadata URL and redirect under a path prefix; both were root-only before.
- No signature changes. `KoanWebBaseUrl` and `KoanWebOptions.ApplicationUrl`/`TrustedProxies` are added.
- Deriving from the request is announced once outside Development and refused in Production at the point of
  need: a consumer that asks `KoanWebBaseUrl` for an origin with the key unset and no `Koan:AllowMagicInProduction`
  consent throws a corrective `InvalidOperationException`. Boot is unaffected.
- `http://0.0.0.0:5080` and `http://[::]:5080` are refused as the base URL; `KoanWeb.Urls.Base()` used to display
  them as `localhost`. A path with `%2F` is refused.
- `KoanWeb.Urls.Base()` reads `Koan:Web:ApplicationUrl`; it previously read the bare key `ApplicationUrl`, which no
  caller in `src/` or `samples/` set. An application that set the bare key must move it under `Koan:Web`.
- The dev token's default audience follows `Koan:Mcp:ResourceUri` and `Koan:Mcp:HttpRoute` and the base URL, so it
  matches what the MCP edge enforces; previously it was always the request host plus `/mcp`.
- A request-derived origin is normalised like the configured one: lowercase scheme and host, the default port
  dropped. `koan-browser.test:80` and `koan-browser.test` name one issuer, as the OIDC default-port spec requires.

## Evidence

- `BaseUrlPathBaseSpec` (Koan.Web.WellKnown.Tests): with `https://www.example.com/tangent`, `/tangent/health/live`,
  `/tangent/api/health` and `/tangent/.well-known/Koan/facts` answer 200 and their root forms 404; a trailing slash,
  an uppercase scheme and host, an explicit default port, a non-default port and a percent-encoded segment normalise
  and serve, the last as `https://www.example.com/my%20space` with path base `/my space`; the fact carries the base
  URL; a non-URL value, `%2F`, `0.0.0.0` and `::` stop boot naming the key.
- `BaseUrlPostureSpec` (Koan.Web.WellKnown.Tests): with the key unset, Production refuses at `Resolve` naming the
  key while the host still boots and answers `/health/live`; Test answers from the request and warns once;
  Development is silent.
- `BaseUrlAuthSwapSpec` (Koan.Web.Auth.Integration.Tests): under `http://127.0.0.1:{port}/tangent` the Test IdP's
  discovery issues `…/tangent/.testoauth` with endpoints under the path, the root document is 404, and the OAuth2
  and OIDC challenge→callback round trips land authenticated.
- `TrustedProxiesSpec` (Koan.Web.WellKnown.Tests, Kestrel loopback): a declared proxy sets the scheme and the client
  address for one hop; a network declaration covers it; no declaration, or one that does not cover the caller, leaves
  the headers ignored.
- `BaseUrlPathSpec` (Koan.Web.Auth.Server.IntegrationTests): the issuer and every advertised endpoint sit under the
  path; the root well-known document is 404; the session cookie carries `path=/tangent`; the MCP resource, its
  authorization server and its challenge metadata URL derive from the base URL; a dev token minted under the path
  is bound to `https://www.example.com/tangent/mcp` and is accepted by the MCP edge.
- `BaseUrlCardSpec` and `DerivedBaseUrlCardSpec` (Koan.Web.OpenGraph.Tests): `og:url`, `og:image` and the canonical
  link carry the configured base URL and never the internal host; without one they follow the request's path base.
- `ProtocolGuards` (Koan.Web.Auth.Atproto.Tests): the client-metadata URL and redirect derive from the base URL with
  its path, the localhost profile derives in Development only, the correction names both keys, and an explicit
  `ClientId` still wins.
