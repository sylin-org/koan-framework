---
id: WEB-0081
slug: an-application-accepts-its-url-at-runtime
domain: Web
status: Accepted
date: 2026-10-29
title: An application accepts its URL at runtime, and sees the origins requests arrive on
related:
  - WEB-0078
  - SEC-0023
  - ARCH-0128
---

# WEB-0081: An application accepts its URL at runtime, and sees the origins requests arrive on

## Context

WEB-0078 made `Koan:Web:ApplicationUrl` the one canonical base URL, fixed when the host is built. With nothing
configured, `KoanWebBaseUrl` derives the base from each request, and Production refuses that, because a caller who
controls `Host` would control the issuer.

An application that never asks its operator for an address up front has no configured value. It starts on whatever
address it is first reached at, often loopback, and learns its public address from where requests come from. Koan
gave it nowhere to put that address once it knew it: the base URL could not be given a value after start, so every
change meant editing configuration and restarting. Nor could it learn the address: Koan kept no record of the origins
requests arrived on, and its proxy posture did not read the host a proxy forwards. And in Production it could not even
be set up without one, because the first absolute URL it wrote — the sign-in that sets it up — was refused until an
operator configured the key.

Measured by a consumer that sets itself up on its first request and keeps its address on its own settings row.

## Application intent and expression

**Intent.** "Use this address as the place's address from now on, without a restart, and keep it."

**Expression.** The application registers one store and calls one verb:

```csharp
public sealed class PlaceAddress : IApplicationUrlStore
{
    public async Task<string?> Load(CancellationToken ct) => (await Settings.Get(SettingsId, ct))?.Address;
    public async Task Save(string applicationUrl, CancellationToken ct) { /* write the settings row */ }
}

// in the application's module: services.AddSingleton<IApplicationUrlStore, PlaceAddress>();
await baseUrl.Accept("https://tangent.sylin.org", ct);   // KoanWebBaseUrl, injected
```

**Guarantee.** From the request after `Accept` returns, every absolute URL a pillar writes is rooted at the accepted
URL, whatever `Host` says; after a restart it is still the accepted URL, read before the host serves. The correction
when it cannot hold: a value that is not a public origin is refused with an `ArgumentException` naming the problem
and never reaches the store; `Accept` without a store is an `InvalidOperationException` naming the contract; a stored
value that is not a public origin refuses the start, naming the store.

**Concepts.** `IApplicationUrlStore` (where the URL lives, which is the application's decision) and
`KoanWebBaseUrl.Accept` (the operator's word). Nothing else is new to the application.

**Intent, observed.** "Tell me, once, when this place is reached at an address that is not its own, so I can offer it
to the operator."

```csharp
origins.Appeared += (_, seen) => { if (!seen.IsLocal) offers.Record(seen.Origin); };   // KoanWebOrigins, injected
IReadOnlyList<ObservedOrigin> recent = origins.Seen();
```

**Guarantee, observed.** Each distinct origin other than the base URL in force is noted with a count and when it was
first and last seen, as the trusted-proxy posture resolved it; `Appeared` fires once when an origin enters the record;
nothing is persisted and the record never exceeds its bound. An observed origin changes nothing until the application
accepts it.

**Intent, first run.** "Set this place up, in Production too, on the address its first run is reached at; take every
later address to the operator as an offer."

```csharp
public sealed class PlaceAddress : IApplicationUrlStore
{
    public bool DerivesUntilAccepted => true;   // this application's posture, named
    // Load and Save as above
}
```

**Guarantee, first run.** While nothing is configured or accepted, absolute URLs derive from each request in
Production, honouring the declared proxies, and Koan says so once, naming the posture and how it ends. From the request
after the first `Accept` returns, every absolute URL is the accepted URL whatever `Host` says. Without the declaration
Production refuses exactly as WEB-0078 decided; with a configured URL the declaration has no effect.

**Coalescence.** The owner stays `KoanWebBaseUrl`, which already decides the base URL for every pillar; the accepted
value is one more source in its precedence, not a second owner. Pillars that copied the configured value at start —
the authorization server's issuer outside a request, the atproto client identity and its cached clients, the
atproto start-time validation — now read the base URL at use. Persistence stays the application's, through DI, the
way `IExternalIdentityStore` and `IIssuerKeyStore` are supplied. The observation belongs beside the base URL in
Koan.Web, at the one place the pipeline's head is composed (`KoanWebStartupFilter`, WEB-0069's owner), and reads the
same origin normalisation `KoanWebBaseUrl` derives with, so an observed origin and a derived base URL are spelled
alike. The notice is a standard .NET event, as `IHealthAggregator.ProbeRequested` and the readiness managers raise.

## Decision

**The base URL has three sources, in this order: configured, accepted, derived. The application supplies where the
accepted one is kept; Koan holds it, validates it and serves it.**

1. **Precedence.** `KoanWebBaseUrl.Resolve` answers `Koan:Web:ApplicationUrl` when set, else the accepted URL, else
   the request's origin under ARCH-0128's gate, unchanged. `Configured`, `Accepted` and `Current` (configured, else
   accepted; null when each request's is derived) state which is in force; `DerivesFromRequests` says whether Koan
   derives one from the request in this environment when there is neither.
2. **The store.** The application registers one `IApplicationUrlStore` (`Load`, `Save`) in DI. `WebModule.Start`
   reads it once, inside the host's ambient scope, before the host serves; Koan never persists the value itself.
   `WebModule` declares `[After(KoanDataCoreModule)]`, so a store over an `Entity<T>` row reads it after the data
   pillar has started, whatever the module ids sort to.
   `KoanWebBaseUrl.Accept(url, ct)` validates the URL, saves it through the store and then holds it, under one lock,
   so a URL the store refused is never served. `CanAccept` says whether a store is registered. Composition runs
   before module start, so a consumer that checks the base URL while the host composes sees no accepted URL yet and
   must defer to use when `CanAccept` is true.
3. **Validation.** An accepted URL is validated by the same `TryParse` as a configured one — absolute http(s), no
   query, fragment or user information, no `%2F`, not an unspecified listen address — and normalised the same way.
   It is also an origin a sign-in can be published under: https, or http on a loopback host (`localhost`, 127/8,
   `::1`) in Development only. OAuth 2.1 requires an https issuer, and the AT Protocol accepts an http client only as
   its localhost profile, which exists in Development only; any other origin, once accepted, would break atproto
   sign-in at once (`http://tangent.lan:5220` in Development) or refuse the next start
   (`http://localhost:5220` in Production or Test). `Accept` refuses such an origin with an `ArgumentException`
   naming why, before `Save`, so nothing that breaks the rule reaches a store; `Load` refuses a stored one at start as
   it refuses any stored value that fails validation, naming the store. A configured URL keeps WEB-0078's rule.

   > **Amended 2026-10-30.** A stored value the host cannot use no longer refuses the start; moving a place between
   > environments must not need a database edit. A place set up in Development keeps `http://127.0.0.1:5220`
   > legitimately, and the same data started in Production held a value this rule refuses, so the start failed with
   > "Correct or clear the value it holds" — even when a configured URL would have won. Now `Load` does not hold such a
   > value (http loopback outside Development, http on any other host, a value that no longer parses or carries a
   > path): the base URL behaves as if nothing were accepted, so it derives from requests where the environment or
   > `DerivesUntilAccepted` allows it and is refused at the point of need otherwise. With nothing configured Koan logs
   > one warning naming the store, the value and why it is not used, and the derived `koan.web.baseurl` fact names it
   > too; with a configured URL the value plays no part and nothing is logged. Koan never writes to the store; the
   > application's next valid `Accept` replaces the value as usual. `Accept` refuses exactly what it refused. The
   > atproto start-time check keeps its rule and sees nothing accepted: in Production without the posture it refuses
   > the start as for an empty store, naming the base URL and the accept; with the posture the identity derives from
   > the request. The guarantee above, "a stored value that is not a public origin refuses the start", is replaced by
   > this.
4. **An origin, not a path.** An accepted URL carries no path. The path base is mounted when the host starts
   (`UsePathBase`, the 404 guard, the boot-time resolution of relative provider endpoints in `AuthSchemeSeeder`), and
   moving it live would move every route, cookie path and relative link a browser already holds. A path is
   configured. Since configured wins over accepted, the path base is always the configured one.
5. **Every pillar reads it at use.** The authorization server's issuer (`AuthServerIssuer`, inside and outside a
   request, so SEC-0023's stated issuer follows), its advertised endpoints, the MCP resource identity, its
   authorization server and challenge metadata, the dev token's audience, OpenGraph's `og:url`, `og:image` and
   canonical link, the Test IdP's issuer, the resource server's audience, and the atproto client-metadata URL and
   redirect all read `KoanWebBaseUrl` per request. The atproto connector derives its client identity from the base
   URL in force (`Current`), else from the request where Koan derives the base URL; its cached session clients are
   keyed by the client identity they were built for. Its start-time check runs while the host composes: a configured
   base URL is checked there as before; with nothing configured it passes where Koan derives the base URL from
   requests, and where the application registers a store it waits for the store, which `AtprotoAuthModule.Start`
   (ordered after `WebModule`) checks once read, refusing the start with the same correction. So a Production
   application with nothing configured, nothing stored and no posture still fails at start.
6. **What was issued under the previous base.** Accepting a URL is the same as configuring it and restarting, less
   the restart:
   - Access tokens carry the issuer and audience they were minted under. `Koan.bearer` reads the stated issuer at
     validation time (SEC-0023), so from the next request a token from the previous base is refused and its client
     re-authorizes, discovering the new issuer from the metadata. The refresh token is opaque and server-side, but it
     is bound to the previous resource. Keeping the previous issuer accepted would make two issuers for one
     application, which SEC-0023 rules out.
   - An atproto sign-in in flight was pushed with the previous client identity, and its callback exchanges the code
     under the current one, which the code was not issued to; the callback fails as any callback that cannot complete
     does (WEB-0080) and the person signs in again. A stored atproto session was granted to the previous client
     identity, which the provider binds its tokens to, so it is gone: requests and the session metadata treat it as
     absent, the next sign-in by either path is a fresh authorization that replaces it, and `Disconnect` removes it,
     revoking in the old identity's name only as a best effort. It is kept until then, so accepting the previous URL
     again restores it.
   - Session cookies are not bound to an origin by Koan. A browser that keeps reaching the application at its
     previous address keeps its session; a browser at the new address has its own cookies.
7. **Facts.** `koan.web.baseurl` states the accepted URL and the store (`base-url-accepted`), and is stated again
   when the store is read at start and whenever the application accepts a URL.
8. **The origins requests arrive on.** `KoanWebOrigins` (Koan.Web, a singleton) notes each request's origin — scheme,
   host and port, normalised like a base URL — right after the forwarded-headers middleware and before the path base
   turns anything away, unless it is the base URL in force; with none in force, every origin is noted. It keeps a
   count and the first and last time seen, in memory only, for at most `Koan:Web:ObservedOriginLimit` distinct
   origins (32 by default, at least 1), evicting the least recently seen. A request on the base URL costs a string
   comparison; any other costs a lookup and interlocked writes, and a lock only when its origin is new. Nothing is
   written per request and nothing persists.
9. **The proxy posture reads the forwarded host.** Decided 2026-10-29: `Koan:Web:TrustedProxies` honours
   `X-Forwarded-Host` with `X-Forwarded-Proto` and `X-Forwarded-For`, for the same one hop from the same declared
   proxies, and the forwarded-headers middleware rewrites `Request.Host` from it; an undeclared proxy's headers still
   count for nothing. This amends WEB-0078 §3, which read no forwarded host because the host was always the
   configured one. The reasons: a declared proxy is already trusted for the scheme and the client address of the same
   hop, and its forwarded host carries the same trust; an application that learns its address must see the host the
   public used, not the one the proxy connected with; and the observed origin and a request-derived base URL must be
   spelled alike, or an application set up behind a host-rewriting proxy would accept the proxy's internal host. A
   configured or accepted base URL still decides every absolute URL Koan writes whatever `Request.Host` says; only a
   base URL derived from the request, where WEB-0078's gate allows one, follows the forwarded host, as it already
   followed the forwarded scheme. Reverting it is one flag in `KoanWebTrustedProxies` and this paragraph; observation
   would then see the host the proxy connected with.
10. **Address classes.** `ObservedOrigin.AddressClass` is `Loopback` (`localhost`, `*.localhost`, 127/8, `::1`),
    `Unspecified` (`0.0.0.0`, `::`), `Private` (10/8, 172.16/12, 192.168/16, 100.64/10, 169.254/16, fc00::/7,
    fe80::/10) or `Public` (every other address, and every DNS name, which Koan does not resolve). A host is classified
    without a trailing root dot, which makes a name absolute without changing it (RFC 1034), so `localhost.` and
    `127.0.0.1.` are loopback. The origin itself keeps the dot. `IsLocal` is true for loopback and unspecified, the
    origins only this machine reaches.
11. **What the application gets.** `Seen()` answers the record, most recent first, bounded by the limit, without the
    base URL in force, enumerating the dictionary without taking a lock. `Appeared`, an `EventHandler<ObservedOrigin>`, fires once when an origin
    enters the record, on the request that brought it, before that request goes on; a handler that throws is logged
    and never fails the request. An origin evicted as least recent and seen again enters, and is reported, again. The
    application makes its offer from the event and never polls per request.
12. **Until an address is accepted, an application may consent to request-derived URLs in Production.** An
    application that supplies the store may declare `IApplicationUrlStore.DerivesUntilAccepted`. It is the
    capability's own consent in ARCH-0128's sense — `KoanMagic.Consent` on the request-derived convenience — so the
    verdict in Production is `AllowedByConsent`; it is the application's word in code, not `Koan:AllowMagicInProduction`,
    which it leaves alone. While nothing is configured or accepted, `Resolve` answers the request's origin as the proxy
    posture resolved it (a declared proxy's forwarded scheme and host, nothing from an undeclared one), and
    `DerivesFromRequests` is true, so the atproto identity derives from the request too. Koan says so once, at the
    first derived URL, naming the store, the posture and how it ends, and the `koan.web.baseurl` fact names it.

    The window is bounded, which is why the consent is acceptable where the framework-wide flag is not. It lasts only
    until a URL is accepted: `Resolve` answers the accepted URL before it would derive, so the first `Accept` ends it
    for every later request, whatever `Host` says, and a restart reads that URL back before the host serves. Nothing
    derived from a request is kept: no pillar stores a derived origin, an observed origin is a candidate in memory, and
    only the application's explicit `Accept` makes one the base URL. The risk during the window is the one WEB-0078
    named — a caller who controls `Host` controls the URLs written for that request — taken by an application whose
    first run is set up by whoever reaches it first, and visible in the log and the facts. A configured URL wins over
    the posture as it wins over an accepted one; without the declaration Production refuses as before.

## Compatibility

- Additive for an application that registers no store: nothing is accepted, and the precedence is WEB-0078's.
- `KoanWebBaseUrl` gains `Accepted`, `Current`, `DerivesFromRequests`, `CanAccept` and `Accept`, and two optional constructor
  parameters (the store, and the fact recorder). `IApplicationUrlStore` is added.
- `AuthServerIssuer` states the accepted URL outside a request, where it stated only a configured one.
- The atproto connector no longer refuses to start when nothing is configured and either Koan derives the base URL
  from the request (Development, and Staging, Test and CI with WEB-0078's notice) or the application registers a
  store; it derives its client identity at use. In Production with nothing configured and no store it still refuses,
  naming both keys. `AtprotoClientIdentity.Derive` takes the base URL as a string.
- The `koan.web.baseurl` fact's derived summary also names the store when one is registered.
- A declared proxy's `X-Forwarded-Host` now sets `Request.Host` for one hop. Behind such a proxy, with nothing
  configured or accepted, a derived base URL follows the forwarded host instead of the host the proxy connected with;
  with a configured or accepted base URL nothing it writes changes. An application that relied on the proxy's own host
  reaching it must stop the proxy forwarding the header or configure the base URL.
- `KoanWebOrigins`, `ObservedOrigin`, `OriginAddressClass` and `KoanWebOptions.ObservedOriginLimit` are added; one
  middleware joins the head of Koan's pipeline.
- `IApplicationUrlStore.DerivesUntilAccepted` is a default interface member, false, so a store that does not declare
  it keeps WEB-0078's refusal in Production.
- `WebModule` now registers and starts after `KoanDataCoreModule`, an order the module ids already produced.
- `AtprotoAuthModule` gains a `Start` and runs after `WebModule`.
- Amended 2026-10-30: a host whose store holds a value it cannot use now starts where it refused, with one warning;
  no signature changes. An application in Production without the posture and with nothing configured then refuses
  absolute URLs at the point of need, and with an eligible atproto provider still refuses the start, as with an empty
  store.

## Evidence

- `AcceptedBaseUrlSpec` (Koan.Web.WellKnown.Tests), the publishable-origin rule: `http://127.0.0.1:5220` and
  `http://localhost:5220` are accepted in Development, and `https://tangent.example.com` in Development, Test,
  Staging and Production; `http://tangent.lan:5220` and `http://192.168.1.10:5220` are refused in Development,
  `http://localhost:5220` in Test and Production, `http://127.0.0.1:5220` and `http://tangent.example.com` in
  Production, each before the store with the reason named; a stored `http://localhost:5220` refuses a Production start
  naming the store. Accepting any http origin again fails seven of them.
- `AcceptedBaseUrlSpec` (Koan.Web.WellKnown.Tests): in Production with nothing configured, a store holding
  `https://place.example` answers it from the first request — under a generic host and under the canonical
  `WebApplication.CreateBuilder` grammar alike — and the fact states it with `base-url-accepted`; a
  configured URL wins over the accepted one and over a URL accepted later, which still reaches the store; in
  Development, accepting `HTTPS://Place.Example:443/` while running moves the answer from `http://localhost` to
  `https://place.example`, which is what the store receives; a bare host, an ftp URL, a query, user information,
  `0.0.0.0`, `::` or a path is refused before the store; accepting without a store names the contract; a stored URL with a path
  refuses the start, naming the store.
- `AcceptedBaseUrlSpec` (Koan.Web.Auth.Server.IntegrationTests, Kestrel, Development): the metadata's issuer and
  endpoints, the MCP resource and its authorization server, the challenge's metadata URL and the dev token's audience
  sit under `https://first.example`, and the issuer stated outside a request is that URL; after accepting
  `https://second.example` all of them move to it, the dev token minted under the first URL is refused at the MCP edge
  and one minted under the second is accepted. Mutating `AuthServerIssuer` back to the configured value fails it.
- `AcceptedClientIdentity` (Koan.Web.Auth.Atproto.Tests, TestServer, Development): the client-metadata document's
  `client_id` and redirect follow the accepted URL and move when another is accepted; with nothing accepted the
  host starts and derives the localhost profile from the request; in Production a stored URL names the client and
  Web Auth reports no composition failure, although its provider plan is compiled before the store is read; a
  configured `http://www.example.com` refuses the start with a store registered; a store holding nothing without the
  posture refuses the start once read; the posture defers the identity to the request. Mutating the connector back
  to the configured value fails the first; restoring the round-one start-time skip fails the two refusals.
- `StaleClientIdentity` (Koan.Web.Auth.Atproto.Tests, a loopback PLC directory and provider with PAR, token and
  revocation endpoints): with a session stored under the identity of `https://first.example` and
  `https://second.example` accepted since, a handle-first challenge pushes a fresh request under the new identity, a
  server-first callback completes and replaces the stored session, the session reads as absent to
  `GetSessionMetadata` and `Send`, and `Disconnect` removes it, revoking in the old identity's name when the provider
  advertises revocation and removing it when the provider does not. Treating every stored session as current fails
  all five.
- `EntityApplicationUrlStoreSpec` (Koan.Web.Auth.Server.IntegrationTests, the JSON adapter): one host accepts a URL
  its store writes to an `Entity<T>` row; the next host over the same data reads it while starting and its metadata
  states it as the issuer from the first request. The default module order already started the data pillar first, so
  this spec guards the outcome, not the declared edge: it passes with the edge reversed too.
- `AcceptedBaseUrlCardSpec` (Koan.Web.OpenGraph.Tests) and `AcceptedBaseUrlSpec` (Koan.Web.Auth.Integration.Tests):
  OpenGraph's card URLs and the Test IdP's issuer move with an accepted URL, whatever the request's host.
- `ObservedOriginsSpec` (Koan.Web.WellKnown.Tests): distinct origins are counted with their first and last time, the
  scheme and host compared without case; the base URL in force is never observed, however its host is spelled, and
  once another is accepted the previous one is observed like any other; with nothing in force every origin is; with a
  limit of two the least recent is evicted; twenty-four hosts land in their address class, the root-dotted ones among them; a new origin is reported once
  and a throwing handler does not fail the request; a limit of zero is refused naming the key. On Kestrel, a declared
  proxy's forwarded scheme and host are the origin observed and an undeclared proxy's are not.
- `TrustedProxiesSpec` (Koan.Web.WellKnown.Tests): a declared proxy's `X-Forwarded-Host` becomes the request's host,
  and without a declaration it is ignored. Dropping `XForwardedHost` from the posture fails both specs.
- `DerivedUntilAcceptedSpec` (Koan.Web.WellKnown.Tests): on Kestrel in Production with the posture and nothing
  configured or accepted, `Resolve` answers a declared proxy's forwarded `https://tangent.example.org`, the listen
  address without forwarded headers, and the listen address when an undeclared proxy forwards `attacker.example`;
  without the posture it refuses naming the key; after an accept, and from the first request when the store already
  holds a URL, it answers that URL whatever the host; with a configured URL the posture changes nothing and logs
  nothing; the warning comes once and names the posture, the store, Production, the accept that ends it and the key.
  Dropping the consent fails four of the seven.
- Amended 2026-10-30. `UnusableStoredUrlSpec` (Koan.Web.WellKnown.Tests): in Production with the posture, a stored
  `http://127.0.0.1:5220` starts, logs the warning once, leaves `Current` null and `Resolve` derives from the request,
  and the fact names the value; without the posture the same store starts and `Resolve` refuses naming the key; with a
  configured URL it starts with no warning from the base URL and the configured URL answers; in Development the same
  value is held; after the start an accept replaces it in the store and the fact; a stored URL with a path is set aside
  too. The store is never written at start. Restoring the start-time refusal fails five of the six; the Development
  case is the guard. `AcceptedClientIdentity` (Koan.Web.Auth.Atproto.Tests): in Production without the posture that
  stored value refuses the start with the empty-store correction, not the store's; with the posture the host starts,
  derives, and a later accept names the client. Restoring the refusal fails both. The two `AcceptedBaseUrlSpec` cases
  above in which a stored `http://localhost:5220` and a stored URL with a path refused the start are retired with the
  refusal.
