# E5 implementation brief — auth flow engine swap (re-derived 2026-06-17)

> Companion to the card [`E5-auth-flow-engine-swap.md`](E5-auth-flow-engine-swap.md). This is the
> empirical design from a 5-part re-derivation. **Status: ✅ DONE** — chunk 1 `1540fe05`, chunk 2 `244681c2`,
> ADR `WEB-0071` `4d021ea9`, chunks 3–5 (engine swap + OIDC IdP + e2e) `f71cdbe4`. Architecture chosen via a
> research+adversarial-decision workflow → **Option A** (see WEB-0071 + the E5 Divergence-log row for the
> traps + the three e2e-surfaced bugs). The design below is the historical record (line refs as of `1540fe05`).

## Architect decisions (binding)
- **Full OIDC round-trip**: extend the Test provider into a minimal OIDC IdP so the e2e spec does a real
  OIDC challenge→callback (not just an OAuth2 round-trip + an "OIDC-handler-wired" assertion).
- Engine swap = route through the ASP.NET handlers that `AddKoanWebAuthAuthentication` already configures;
  **preserve the public routes** `/auth/{provider}/challenge|callback|logout`.

## Already landed (`1540fe05`)
SAML stub excised (`SamlController` deleted; 7 dead SAML `ProviderOptions` fields + the `SamlMetadata`/`SamlAcs`
route constants + the SAML health/metadata branches removed); the 3× `ProviderOptions` merge bodies deduped to
`ProviderOptions.Merge(baseline, overlay)` + `ProviderOptions.WithEnabled(p, enabled)`.

## Two findings the card glosses (do NOT skip)
1. **Behavioral parity.** The live `AuthController.Callback` does two things `AddKoanWebAuthAuthentication`'s
   handlers do NOT — they MUST be ported onto the handler path before deleting `AuthController.Callback`:
   - **Claim mapping**: OAuth2 callback runs `Infrastructure.UserInfoMapper.Map(userObj)` → adds
     `ClaimTypes.Role`, `"Koan.permission"`, and extra claims (`AuthController.cs` ~:147, ~:168-179). The
     handler's OAuth `OnCreatingTicket` only maps sub/name/avatar (`AuthenticationExtensions.cs` ~:138-143).
     → add `UserInfoMapper.Map` to the OAuth `OnCreatingTicket`; on OIDC use
     `o.GetClaimsFromUserInfoEndpoint = true` + an `OnUserInformationReceived`/`OnTokenValidated` that runs
     `UserInfoMapper.Map` to mint the same claim shape.
   - **External-identity link**: `AuthController.Callback` calls
     `identities.Link(new ExternalIdentity{ UserId, Provider, ProviderKeyHash = SHA256-hex(sub), ClaimsJson })`
     best-effort (`AuthController.cs` ~:184-197; `IExternalIdentityStore`). → preserve for BOTH oauth2+oidc on
     sign-in (resolve `IExternalIdentityStore` in OAuth `OnCreatingTicket` / OIDC `OnTokenValidated`, or the
     cookie `OnSigningIn`). Keep it best-effort (try/catch).
2. **Effective-provider registration.** `AddKoanWebAuthAuthentication` iterates only CONFIG `bound.Providers`
   (`AuthenticationExtensions.cs` ~:82). Providers also come from `IAuthProviderContributor.GetDefaults()` at
   runtime (`ProviderRegistry.Compose` ~:40-46) — the **Test provider is contributed, not config**. ASP.NET
   handlers register at startup, so `AddKoanWebAuthAuthentication` MUST compose the EFFECTIVE set
   (config + contributor defaults, merged via `ProviderOptions.Merge`) at registration time and register a
   handler per effective provider. Resolve the `IAuthProviderContributor` instances at registration (build a
   temp `ServiceProvider`, or accept them as a param).

## Remaining work (4 chunks; build + commit each green)

### Chunk 2 — retire the event-contributor system (mechanical)
- `src/Koan.Web.Auth/Contributors/Builtin/RoleListFileContributor.cs` (Priority 50, only `OnSignIn`): change
  `: IKoanAuthEventContributor` → `: IKoanAuthFlowHandler`; keep `OnSignIn` verbatim (the other 5 members
  default). Confirm the `OnSignIn(AuthSignInContext, CancellationToken)` signature matches.
- `src/Koan.Web.Auth.Roles/Contributors/AdminBootstrapContributor.cs` (Priority 100, only `OnSignIn`): same.
- `src/Koan.Web.Auth/Flow/AuthFlowDispatcher.cs` ctor (~:41-63): drop the
  `IEnumerable<IKoanAuthEventContributor> legacyContributors` param + the adapter logic (~:46-53); just sort
  `handlers` by `Priority` then type name.
- DELETE: `src/Koan.Web.Auth/Flow/LegacyAuthContributorAdapter.cs`,
  `src/Koan.Web.Auth/Contributors/AuthEventDispatcher.cs`,
  `src/Koan.Web.Auth/Contributors/IKoanAuthEventContributor.cs` (a public discoverable interface — this is a
  deliberate breaking removal, fine pre-1.0).
- `src/Koan.Web.Auth/Extensions/ServiceCollectionExtensions.cs`: remove `DiscoverAndRegisterAuthEventContributors`
  (~:204-213) + the `AuthEventDispatcher` registration (~:54); register the migrated contributors as
  `IKoanAuthFlowHandler`.
- `src/Koan.Web.Auth.Roles` `AddKoanWebAuthRoles`: register `AdminBootstrapContributor` as `IKoanAuthFlowHandler`.
- Fix test fallout: `tests/Koan.Web.Auth.Tests/AuthFlowDispatcherTests.cs` (constructs the dispatcher with the
  legacy param → drop it); `tests/Suites/Integration/Bootstrap/.../AuthDiscoverableContributorSpec.cs` (asserts
  `LegacyAuthContributorAdapter` NOT registered + `RoleListFileContributor` discovered → update to the new world).

### Chunk 3 — engine swap (security-critical)
- Bring `AddKoanWebAuthAuthentication` to parity (finding 1) + compose effective providers (finding 2).
- Wire it into the boot: `ServiceCollectionExtensions.AddKoanWebAuth` currently hand-sets the cookie scheme
  (~:69-189) + cookie events dispatching to `AuthFlowDispatcher`
  (OnValidatePrincipal/OnSigningIn/OnSigningOut/OnRedirectToLogin/OnRedirectToAccessDenied). REPLACE that
  hand-rolled cookie setup with `AddKoanWebAuthAuthentication`; MOVE all 5 flow-dispatcher cookie events into
  `AddKoanWebAuthAuthentication`'s `AddCookie` events (it currently has only OnRedirectToLogin/OnRedirectToAccessDenied
  at ~:45-79 — add OnSigningIn/OnSigningOut/OnValidatePrincipal so the flow-handler dispatch is preserved).
- Rewrite `AuthController`: `Challenge` → resolve returnUrl via `ReturnUrlPolicy.Resolve(returnUrl, allowList,
  default)` (the allow-list is the security boundary), then
  `return Challenge(new AuthenticationProperties { RedirectUri = resolved }, providerScheme)`. DELETE: the
  hand-rolled authorize-URL building, the state/return cookies, the entire `Callback` action (the handler
  middleware owns `/auth/{provider}/callback` now — both CallbackPaths already = `/auth/{id}/callback`), and the
  `BuildAbsolute*` helpers. Keep `Logout`. (~300 LOC deleted; OIDC-501 gone; PKCE on.)

### Chunk 4 — Test provider OIDC IdP (`src/Connectors/Web/Auth/Test/**`)
It's OAuth2-only today (AuthorizeController = PKCE S256; TokenController; UserInfoController). Add OIDC so the
real ASP.NET `OpenIdConnect` handler can round-trip in-process:
1. discovery `/.well-known/openid-configuration` (or `/auth/test-oidc/.well-known/...`): issuer,
   authorization_endpoint, token_endpoint, jwks_uri, userinfo_endpoint, response_types_supported (`code`),
   subject_types_supported (`public`), id_token_signing_alg_values_supported (`RS256`).
2. JWKS endpoint: an RSA public key (generate an RSA key at startup, stable per process).
3. token endpoint: when `scope` contains `openid`, ALSO return a signed `id_token` (RS256 JWT — iss=issuer,
   aud=client_id, sub, nonce echoed from authorize, iat, exp, + name/email).
4. authorize endpoint: echo `nonce`; PKCE S256 already supported.
Register a contributed provider id `test-oidc` (Type=oidc, Authority=the test IdP base URL, ClientId) alongside
`test` (oauth2). Use `Microsoft.IdentityModel.Tokens` / `System.IdentityModel.Tokens.Jwt` for signing (add the
PackageReference if needed). Issuer/urls must be absolute + reachable in-process (the handler fetches
discovery+JWKS over HTTP from the same TestServer).

### Chunk 5 — ARCH-0079 integration spec (the safeguard)
`tests/Suites/Auth/Koan.Web.Auth.Integration.Tests/` — a `Microsoft.NET.Sdk.Web` test project using
**WebApplicationFactory** (NOT `KoanIntegrationHost` — that's a generic `IHost` with no TestServer; these need
real HTTP round-trips). Model it EXACTLY on the working `tests/Suites/Web/Koan.Web.WellKnown.Tests/`: top-level
`Program.cs` + `public partial class Program {}`, a marker `.sln` copied to output, `Properties/AssemblyInfo.cs`
with `[assembly: WebApplicationFactoryContentRoot(...)]`, `<GenerateProgramFile>false</GenerateProgramFile>`,
ProjectReferences to Koan.Core/Web/Web.Auth + the Test connector + Data.Connector.InMemory. Host `Program.cs`:
`AddKoan()` + controllers + the Test provider (`test` oauth2 + `test-oidc` oidc enabled). The spec uses an
`HttpClient` with a `CookieContainer` and MANUAL redirect-following (`HttpClientHandler.AllowAutoRedirect=false`,
follow 302s yourself carrying cookies, staying in-process via the factory). Two tests:
- `OAuth2_challenge_callback_round_trip_authenticates`: GET `/auth/test/challenge?return=/` → follow the
  redirect chain → assert the auth cookie is set + identity has the mapped `Role` claim.
- `OIDC_challenge_callback_round_trip_authenticates`: same against `/auth/test-oidc/challenge` — exercises
  discovery + JWKS + id_token validation + PKCE + nonce. **The test that would have caught OIDC-501.**
Add to `Koan.sln` via `dotnet sln Koan.sln add --in-root <csproj>`. Consider
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` (KoanEnv/auth statics).

## Verification gates (all must pass)
1. `dotnet build Koan.sln` = 0 errors.
2. The new auth integration spec: BOTH round-trips green.
3. Existing auth tests green: `tests/Koan.Web.Auth.Tests` (esp. `AuthFlowDispatcherTests`, updated for the ctor),
   `AuthPillarBootstrapSpec`, `AuthDiscoverableContributorSpec` (updated).
4. Prove the OIDC test is real (it would FAIL on the old `AuthController`, which 501'd).

## Constraints
Preserve `/auth/{provider}/challenge|callback|logout`. Newtonsoft canonical (no STJ). No public API rename
EXCEPT the deliberate `IKoanAuthEventContributor` removal. Never stage `src/Koan.Jobs/**`. Persona separation.
The cookie scheme constant is `AuthenticationExtensions.CookieScheme = "Koan.cookie"`.

## Progress (2026-06-17, this session)
- **Chunk 1 DONE** (`1540fe05`) — SAML excise + merge dedup.
- **Chunk 2 DONE** (`244681c2`) — event-contributor pipeline retired. Migrated `RoleListFileContributor` +
  `AdminBootstrapContributor` to `IKoanAuthFlowHandler` (signatures were identical; `OnSignIn` verbatim,
  Priority preserved); deleted `IKoanAuthEventContributor` + `AuthEventDispatcher` + `LegacyAuthContributorAdapter`;
  dropped `AuthFlowDispatcher`'s `legacyContributors` ctor param + `DiscoverAndRegisterAuthEventContributors`.
  Verified: full sln 0 err; `AuthFlowDispatcherTests` (18) + `AuthDiscoverableContributorSpec`/`AuthPillarBootstrapSpec` (3) green.
- **Chunks 3–5 (engine swap + OIDC IdP + e2e) — designed below, NOT started.** Atomic + security-critical.

## Chunk 3 grounded design — dynamic handler registration (the hard part, SOLVED)
**Why not a `BuildServiceProvider()` at `AddKoanWebAuth` time:** Koan.Web.Auth's registrar is only
`[After(Koan.Web)]` — there is NO ordering guarantee relative to the auth CONNECTORS (Test/Google/etc. register
their `IAuthProviderContributor` in their own registrars). A temp provider at registration could miss contributed
providers (e.g. `test`). **Decision: defer to the existing `KoanWebAuthStartupFilter` (`IStartupFilter`, already
registered in `KoanAutoRegistrar.Initialize` line ~37)**, where the container is built and ALL contributors resolve.

**Why the swap (not just completing the hand-rolled AuthController OIDC):** hand-rolling OIDC `id_token` validation
(signature/issuer/audience/nonce) is itself security-risky; the ASP.NET `OpenIdConnectHandler` does it correctly +
maintained. The swap is the right call DESPITE the registration cost.

**The registration recipe (replicates what `AddOAuth`/`AddOpenIdConnect` do, split across registration + startup):**
1. At `AddKoanWebAuth` (registration), on the existing `AddAuthentication().AddCookie(...)` builder, register the
   handler INFRA once (these are the easy-to-miss critical pieces):
   - `services.AddTransient<OAuthHandler<OAuthOptions>>()` and `services.AddTransient<OpenIdConnectHandler>()`.
   - `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OAuthOptions>, OAuthPostConfigureOptions<OAuthOptions, OAuthHandler<OAuthOptions>>>())`.
   - `services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>())`
     (this one depends on `IDataProtectionProvider` — already present via Koan.Web).
   - A dynamic `IConfigureNamedOptions<OAuthOptions>` + `IConfigureNamedOptions<OpenIdConnectOptions>` (singletons,
     resolve `IProviderRegistry` lazily). Their `Configure(name, options)` looks up provider `name` in
     `IProviderRegistry.EffectiveProviders`; if found + matching type, set endpoints/Authority/ClientId/ClientSecret/
     CallbackPath (`/auth/{name}/callback`)/Scopes, `SignInScheme = CookieScheme`, **`UsePkce = true`** (OAuthOptions
     gained `UsePkce` in .NET 9; OIDC has it), and the **parity hooks** (below).
2. At STARTUP, inside `KoanWebAuthStartupFilter` (resolve `IProviderRegistry` + `IAuthenticationSchemeProvider`):
   for each effective provider, `await schemeProvider.AddScheme(new AuthenticationScheme(id, displayName,
   type == oidc ? typeof(OpenIdConnectHandler) : typeof(OAuthHandler<OAuthOptions>)))`. Skip if already present.

**Parity hooks (finding 1 — MUST port; see top of brief):**
- OAuth `OAuthEvents.OnCreatingTicket`: after the existing sub/name/avatar mapping, also fetch userinfo →
  `UserInfoMapper.Map(jobject)` → add `ClaimTypes.Role` / `"Koan.permission"` / extra claims; then the external-identity
  link (`IExternalIdentityStore.Link`, best-effort). `UserInfoMapper` is `internal` in `Koan.Web.Auth.Infrastructure`
  — accessible from `AddKoanWebAuth` (same assembly).
- OIDC: `o.GetClaimsFromUserInfoEndpoint = true` + `OpenIdConnectEvents.OnUserInformationReceived` (Newtonsoft `JObject`
  from `e.User`) → same `UserInfoMapper.Map` + identity link.
- Note: `IExternalIdentityStore` is resolvable from `ctx.HttpContext.RequestServices` inside the events.

**AuthController rewrite (chunk 3 tail):** `Challenge` → resolve returnUrl via `ReturnUrlPolicy.Resolve(...)`, then
`return Challenge(new AuthenticationProperties { RedirectUri = resolved }, provider)`. DELETE the hand-rolled
authorize-URL building, the `Koan.auth.state`/`Koan.auth.return` cookies, the entire `Callback` action (the handler
middleware owns `/auth/{provider}/callback` — both CallbackPaths are `/auth/{id}/callback`), and the `BuildAbsolute*`
helpers (~300 LOC). Keep `Logout`. The cookie's existing `OnSigningIn` flow-dispatch (ServiceCollectionExtensions
lines ~155-178) still fires the contributors (RoleListFile/AdminBootstrap) — preserved automatically.

**Chunk-3 safeguard (commit it green before chunk 4):** `tests/Suites/Security/Koan.Security.Trust.IntegrationTests`
`AuthEndToEndSpec` (via `AuthE2EFixture`, full `AddKoan()`) exercises the Test provider OAuth2 challenge→callback —
VERIFY it's a real round-trip; if it stays green through the swap, the OAuth2 engine swap is proven. (Confirm before relying.)

## Chunk 4 grounded — Test provider OIDC IdP (building blocks already exist)
The Test connector ALREADY has: `Infrastructure/JwtTokenService.cs` (reuse/extend for RS256 `id_token` signing),
`AuthorizeController` (PKCE S256 already; just echo `nonce`), `TokenController` (add `id_token` to the response when
`scope` contains `openid`), `DevTokenStore` (the code/claims store — extend `IssueCode`/`TryRedeemCode` to carry `nonce`).
Add: discovery `/.well-known/openid-configuration` + a JWKS endpoint (RSA public key from `JwtTokenService`) + register a
contributed `test-oidc` provider (Type=oidc, Authority=test IdP base) in `TestProviderContributor.GetDefaults()` alongside
`test`. Check `JwtTokenService` for an existing RSA signing key to expose via JWKS.

## Chunk 5 grounded — see the "Chunk 5" section above (WebApplicationFactory; model on Koan.Web.WellKnown.Tests).

## Why a delegated one-shot failed before
A delegated full-impl agent 529'd (transient API overload) ~7 min in after only 2 partial edits (a broken
state, reverted). Do this yourself in build-verified, individually-committed chunks (the order above), with the
e2e spec as the real safeguard — not as one big delegated pass.
