# WEB-0071: Auth engine swap — delegate OAuth2/OIDC to maintained ASP.NET handlers via dynamic scheme registration

**Status**: Accepted (2026-06-17)
**Date**: 2026-06-17
**Deciders**: Enterprise Architect (with a research+adversarial-decision workflow: ASP.NET-internals · prior-art · security-threat-model)
**Scope**: Resolves assessment card **E5** chunk 3 — replace the hand-rolled `AuthController` OAuth2 flow (which 501s on OIDC and ships **no PKCE**) with the maintained `OAuthHandler`/`OpenIdConnectHandler`, so id_token validation, nonce, PKCE, state, and correlation are owned by the framework, not us.
**Related**: WEB-0066 (flow-handler pipeline — preserved) · SEC-0001 §10 (return-url allow-list) · SEC-0001 §11 (asymmetric dev token signing) · the chunks 1–2 cleanup (`1540fe05`, `244681c2`).

---

## Context

`AuthController` hand-rolls the OAuth2 authorize/callback by hand: GUID-in-cookie state, manual token exchange, manual userinfo, and **`501 Not Implemented` for the OIDC callback** (`AuthController.cs:153`). It sends **no PKCE** on OAuth2. The card's intent is to route through the maintained ASP.NET handlers. The obstacle: **the provider set is composed at runtime** from three sources no single connector owns — (1) `IAuthProviderContributor` defaults (the dev `test` provider), (2) `AuthOptions` config providers, and (3) **config-only arbitrary ids with no owning connector** (`ProviderRegistry.Compose` takes unknown `root.Providers` ids as-is). ASP.NET handlers are normally registered statically (`AddOAuth("google", …)`) at DI-build time.

Three architectures were evaluated (full analysis in the decision workflow transcript):
- **A — Framework dynamic registration**: a post-build seeder (the existing `KoanWebAuthStartupFilter`) resolves `IProviderRegistry.EffectiveProviders` and registers a scheme per provider via `IAuthenticationSchemeProvider.AddScheme` + seeds `IOptionsMonitorCache`.
- **B — Per-connector self-registration**: each connector calls `AddKoanOAuthProvider(id)` for its statically-known id.
- **C — Single generic handler** resolving options per-challenge.

## Decision — **Option A**

A is the cleanest correct architecture here:
- **Only A handles the ownerless config-only provider** (source 3): B can only register schemes whose id a connector statically knows, so B needs an A-style fallback anyway — strictly worse.
- **Only A may legally read the scoped `IProviderRegistry`**: the startup filter runs post-build and creates its own scope. B's lazy `IConfigureNamedOptions` is root-resolved by the singleton `IOptionsMonitor` and would capture-inject the scoped registry → throws under `ValidateScopes`.
- **Zero connector blast radius** — the connectors already contribute `IAuthProviderContributor` singletons; A consumes `EffectiveProviders` once. The "no registrar ordering guarantee" constraint **dissolves** (there is no shared `AuthenticationBuilder` to order against).
- **It is Duende IdentityServer's production "Dynamic Providers" pattern** (path-templated callbacks `~/federation/{scheme}/signin` ≙ our `/auth/{id}/callback`). We port a proven design.
- C is rejected: one scheme for N providers forces a single `CallbackPath` (breaks `/auth/{id}/callback`) and re-introduces hand-managed nonce/state/JWKS switching — the exact security surface we want the maintained handler to own.

## Implementation contract (binding — the traps are empirically verified on net10)

1. **Manual PostConfigure BEFORE cache seed.** `IOptionsMonitorCache<TOptions>.TryAdd(id, opts)` caches the instance **verbatim, without running configure/post-configure**. So the seeder MUST run `new OAuthPostConfigureOptions<OAuthOptions, OAuthHandler<OAuthOptions>>(dp).PostConfigure(id, opts)` (namespace `Microsoft.Extensions.DependencyInjection`) / `new OpenIdConnectPostConfigureOptions(dp).PostConfigure(id, opts)` (namespace `Microsoft.AspNetCore.Authentication.OpenIdConnect`) **before** `TryAdd`, or `StateDataFormat`/`DataProtectionProvider`/`Backchannel` stay null and the handler NPEs at challenge.
2. **Then** `IAuthenticationSchemeProvider.AddScheme(new AuthenticationScheme(id, displayName, typeof(OAuthHandler<OAuthOptions>) | typeof(OpenIdConnectHandler)))`. Handlers are activated per-request via `ActivatorUtilities`.
3. Every provider's options sets `SignInScheme = "Koan.cookie"`, `CallbackPath = /auth/{id}/callback`, **`UsePkce = true` on BOTH branches** (current code sets it only on OIDC; `OAuthOptions.UsePkce` exists in net10).
4. **Parity hooks live in handler EVENTS** (resolve services per-request via `ctx.HttpContext.RequestServices`), because the maintained handler structurally does NOT own these three application-policy controls:
   - **Claim mapping**: `UserInfoMapper.Map` → `ClaimTypes.Role` / `"Koan.permission"` / extras — in `OAuthEvents.OnCreatingTicket` (oauth2) and `OpenIdConnectEvents.OnUserInformationReceived`/`OnTokenValidated` (oidc). The stock handler maps only sub/name/avatar → roles/perms would be **dropped (authz regression)**.
   - **External-identity link**: `IExternalIdentityStore.Link` — same events.
   - **Return-url allow-list**: the challenge entry-point runs `ReturnUrlPolicy.Resolve` **before** assigning `AuthenticationProperties.RedirectUri`; the handler redirects to it **without re-validating** (open-redirect risk otherwise).
5. **Challenge** route → `ChallengeAsync(id, props)` (delete the hand-rolled authorize-URL building + state cookies). **Callback** action is **removed** for migrated providers — the `RemoteAuthenticationHandler` middleware owns `/auth/{id}/callback`. **Logout** (incl. the dev TestProvider cookie clear) is kept.
6. **Fail closed on missing identifier.** The old code's `sub = name ?? "user"` fallback + `SHA256("")` keyhash collapsed all anonymous links onto one account inside a swallowing `catch{}`. The port **drops the fallback** and skips the link when `sub` is absent (no colliding account); it does not swallow blindly.

## Consequences / operational

- **Data Protection key ring becomes load-bearing**: the OIDC nonce cookie, correlation cookie, and `state` are DP-protected. Single-process (dev/test) is fine with the in-memory ring; **multi-instance production MUST share/persist the key ring** (and should add `AddOidcStateDataFormatterCache` for server-side state). This is a new requirement vs the bespoke GUID-cookie state — documented here.
- **Backchannel reachability**: the dev Test provider's relative endpoints (`/.testoauth/*`) are reached server-side by the handler's `Backchannel`; resolve them to in-network absolute URLs (or configure the backchannel) so container/loopback dev still works.
- The dev **Test provider stays `oauth2`-typed** (it issues opaque tokens). A new **`test-oidc`** provider (signed id_token via the existing ES256 `JwtTokenService` + a JWKS + discovery doc) is added so the OIDC path has a real round-trip (the ARCH-0079 spec that would have caught OIDC-501).
- `IOptionsMonitorCache` caches per scheme-name for process lifetime; the effective-provider set is static-at-startup by design, so this is correct — but a future hot-reload would need explicit `TryRemove` + re-seed.
