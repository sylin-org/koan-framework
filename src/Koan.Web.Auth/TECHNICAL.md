# Sylin.Koan.Web.Auth — technical contract

## Ownership

Web Auth owns one host-level authentication plan. Connector modules register immutable `AuthProviderDefinition`
values; application configuration overlays those defaults; `AuthProviderPlan` compiles availability, eligibility,
priority, correction, and default election once. The same plan drives scheme seeding, controllers, startup reporting,
composition facts, and the credential-free `IAuthProviderCatalog` projection.

Cross-module contracts live in inert `Sylin.Koan.Web.Auth.Abstractions`. Optional modules must reference that assembly
directly instead of depending on functional Web Auth merely to consume a contract.

## Provider rules

- A connector definition is inactive until its provider has explicit configuration, unless the definition declares an
  automatic local provider.
- Explicit configuration outranks automatic providers; then priority and stable provider ID break ties.
- `PreferredProviderId` is required intent: unknown, disabled, unavailable, or incomplete targets stop startup.
- Configuration-only provider IDs are supported because OAuth2/OIDC mechanics belong to Web Auth itself.
- Required OIDC values: `Authority`, `ClientId`, `ClientSecret`.
- Required OAuth2 values: `AuthorizationEndpoint`, `TokenEndpoint`, `UserInfoEndpoint`, `ClientId`, `ClientSecret`.
- Other protocols require one connector-owned `IAuthProtocol` validator plus the named ASP.NET handler. Unknown
  protocols, duplicate validator identities, and attempted replacement of reserved `oidc`/`oauth2` mechanics stop
  startup. Protocol identities are trimmed and compared without case sensitivity.

Provider callbacks use `/auth/{id}/callback`. Relative endpoints and authorities are reserved for the self-hosted local
test provider; deployment providers should use absolute HTTPS endpoints.

The local relative OIDC authority is projected as one logical provider with separate transport addresses: the live
request supplies its public issuer/authorization origin, while `IServerAddressesFeature` supplies its internal
back-channel origin. If neither a bound address nor a loopback public origin is available, challenge fails with a
correction naming the required Kestrel binding instead of attempting an unreachable public hostname.
Local issuer origins use standard URI authority canonicalization so explicit default ports agree with
implicit ones across browser redirects. Canonicalization applies to locally projected issuers only;
external issuers are validated exactly as configured.

## Runtime behavior

`AuthModule.Start` resolves the immutable plan and ensures one ASP.NET scheme per eligible provider. OIDC uses
`OpenIdConnectHandler`; OAuth2 uses `OAuthHandler<OAuthOptions>`. PKCE, state, correlation, OIDC nonce, issuer,
audience, and signature validation remain owned by maintained ASP.NET handlers.

A custom connector registers its `IAuthProtocol`, `AuthProviderDefinition`, and ordinary ASP.NET named scheme in its
module registration phase. Web Auth passes the effective, merged `ProviderOptions` into the protocol validator once
for each active provider. The validator returns an empty list for complete configuration, or secret-free corrective
messages with full configuration paths. It must reject unsupported scope, callback, or other overrides that its
handler cannot honor; it must not perform network I/O or mutate configuration. Inactive, disabled, and unavailable
providers do not undergo protocol validation.

The existing seeder preserves a registered scheme and rejects any eligible route that still lacks one. Merely
declaring a custom `Type` does not implement configuration-only provider IDs: the connector must register each
corresponding scheme. Normalized protocol and merged provider options remain owned by the compiled plan. There is no
second custom protocol startup lifecycle and no per-request protocol discovery.

The challenge controller resolves `return` through `ReturnUrlPolicy.Resolve(HttpContext, …)` before invoking the
named handler, which redirects to `AuthenticationProperties.RedirectUri` without checking it again (SEC-0029): a local
path — one leading `/`, printable ASCII without spaces, a path that after percent-decoding (up to three passes) names no
host and holds no backslash and no `.` or `..` segment, beginning with the application's path base at a segment
boundary — or an absolute URL under an allow-list entry is kept; anything else becomes the default landing,
`ReturnUrl:DefaultPath` under the base path once (`ReturnUrlPolicy.DefaultLanding`), or as it is when it is an absolute
http(s) URL. An allow-list entry is compared as an origin: the parsed URL is http(s) without userinfo, its scheme, host
and port equal the entry's (the host in its IDNA form), each path segment percent-decoded holds no slash, backslash or
control character and is not `.` or `..`, and its path is the entry's path or under it at a segment boundary; the
redirect is built from what was compared (scheme, `IdnHost`, a non-default port, escaped path, query and fragment), so
the browser receives exactly that and the `Location` header is ASCII. An absolute `DefaultPath` lands in the same form. Start validation refuses an
entry that is not an absolute http(s) URL without userinfo, query or fragment (`ReturnUrlPolicy.IsAllowListEntry`) and a
`DefaultPath` that, rooted, is not a local path and is not an allow-listed URL (`ReturnUrlPolicy.AcceptsDefaultPath`).
Logout resolves its `return` the same way, so a refused value lands on the default instead of reaching
`LocalRedirect`. The cookie's login redirect names the page asked for under the base path in its configured spelling:
`UsePathBase` matches the base without regard to case and keeps the request's spelling, so the request's base at the
head of the cookie handler's return is replaced by the configured one, where the authentication cookie applies. The controller also
carries the failure landing named beside `return` (`failure`, a rooted path checked by `SignInFailureReturn.TryAccept`)
in the authentication properties (WEB-0080). `TryAccept` admits only printable ASCII from the characters a path and a
query carry, percent-decodes the path and the query, and refuses a host (`//`), a `.` or `..` segment, a backslash, an
encoded slash or backslash in the path, an encoded control or non-ASCII byte, a fragment and a query that names
`error`; the same rule validates `Koan:Web:Auth:ReturnUrl:FailurePath` at start, so no accepted landing can leave the
base path, carry a second code or fail as a header. `SignInFailureReturn.Redirect` resolves that value, then
`Koan:Web:Auth:ReturnUrl:FailurePath`, under the application's path base exactly once (SEC-0027), and sends the person
there with the
`SignInFailure` code in `error` and `Cache-Control: no-store`; it answers false and leaves the response alone when
nothing is named. The seeded OAuth2 and OIDC handlers ask it from `OnRemoteFailure` and `OnAccessDenied`; a
connector asks it at its own failure sites.
Protocol-specific initiation input is available through the handler's request; the connector owns validation and
must bind any security-relevant value into its protected transaction state rather than trusting callback query
parameters. Custom handlers own their protocol's remote validation, subject mapping, external linkage, and any token
storage. They sign in through `AuthenticationExtensions.CookieScheme` to retain Koan's cookie lifecycle and rejection
semantics. A registered scheme proves composition only, not that these security requirements are implemented.

The framework cookie scheme is `Koan.cookie`. Challenge and access-denied responses pass through the discovered
`IKoanAuthFlowHandler` pipeline. JSON/API requests receive the built-in JSON challenge behavior; interactive requests
redirect to the elected provider when one exists.

## Resource edge

`Koan.resource` (`AuthenticationExtensions.ResourceServerScheme`, SEC-0025) is registered with the cookie and stays
inert until `Koan:Web:Auth:ResourceServer:Enabled`; `ResourceServerComposition` then makes it the default authenticate,
challenge and forbid scheme, leaving the default, sign-in and sign-out schemes on `Koan.cookie`. Its
`ForwardDefaultSelector` forwards every operation of a request without a `Bearer` `Authorization` header — none, or
one of another scheme such as a proxy's `Basic` credentials — to the cookie (`ResourceServerHandler.CarriesBearer`,
the scheme compared without case); a request with one is the handler's alone. The handler authenticates through Trust's `KoanBearerResource` for
`ResourceServer:Resource`, else the base URL `KoanWebBaseUrl.Resolve` answers (which refuses a request-derived origin in
Production, WEB-0078), and turns a header that carries no bearer into a failure. Its challenge answers 401 with
`Bearer error="invalid_token"` after a failed authentication and a bare `Bearer` otherwise; its forbid is ASP.NET's 403.
`RejectedBearerFilter`, a global MVC authorization filter, challenges through the same scheme before any controller
action or Razor Page when the header was not accepted, so an anonymous page does not read a rejected agent as a
visitor. It answers only a request that carries a bearer, and skips an action whose endpoint metadata carries
`ResourceServerExemptAttribute`: the dev Test provider's protocol controllers carry it, since they check a token of
their own, and the embedded authorization server's controller carries it, since its endpoints never authenticate by bearer and owe a client their own protocol
answers (a refresh, a registration, a device request, the discovery documents). Endpoints outside MVC — minimal APIs,
`MapRazorComponents`, the MCP edge — get the 401 only when they require an authenticated caller, or answer in their own
terms. A `Resource` that is not an absolute URI refuses the start. The
composition facts carry `koan.auth.resourceserver.enabled` with the resource while it is on.

A path Koan writes for the browser follows one rule (`ApplicationPaths`, SEC-0027): an application-relative, rooted
path gets the application's path base — the configured base URL's, canonical, else the request's — exactly once, even
when the path begins with the path base's own segment. The cookie's login redirect builds its provider challenge URL
that way, the discovery document's `challengeUrl`, the default landing and the failure landing are written that way;
`IAuthProviderCatalog.ChallengePath` stays the application-relative route. `ReturnUrlPolicy.UnderPathBase`, which keeps a
URL that already carries the base, remains only for the self-hosted development provider's own endpoints.

## Lifecycle failure semantics

- bootstrap, sign-in, challenge, and access-denied exceptions propagate;
- validation exceptions reject the principal and are logged;
- an explicit sign-in rejection prevents cookie issuance;
- external identity linkage completes before sign-in and fails closed;
- sign-out handler failures are logged and local sign-out continues.

Handlers are scoped, auto-discovered implementations of `IKoanAuthFlowHandler`, ordered by `Priority` and then full
type name. Framework-contributed and application handlers share one pipeline. Refreshing application-owned role claims
is a flow-handler decision (projecting them onto the request's principal during cookie-principal validation), never a
Web Auth normalization. A cookie is reissued from the ticket the handler read, so what a handler projects onto a request
does not reach the cookie unless it asks to renew. These cookie events do not validate bearer tokens. The built-in
role-list handler applies an allow/revoke JSON file keyed on verified emails at sign-in when
`Koan:Web:Auth:Lifecycle:RoleListFile:FilePath` is configured. At priority 50 it applies revocations after
lower-priority handlers; a later handler can add roles again. Choose ordering deliberately for
application-owned role authority. Implement only the events the module owns; no DI registration is required.

## Provider assertions

`AuthSchemeSeeder` maps sub, name and avatar for every provider, and whether it verified its email; userinfo's
`email_verified` replaces what the id_token said. Roles, permissions and extra claims map only under
`ProviderOptions.MapRolesAndClaims`, and an extra claim never takes a `koan*` type. `ProviderClaimPolicy` then runs once
when a provider's sign-in reaches the cookie, before any flow handler and for every protocol: it stamps
`SignInProof.ProviderSubjectClaim`, replacing anything a provider asserted under that type, removes every role claim
of a provider that has not opted in, and removes `koan:*` roles always. An OIDC id_token's `roles` are role claims
before Koan sees them, which is why this sits at the cookie and not in the userinfo mapping. A sign-in on a path the
provider plan does not know is the application's own and passes untouched.

## Subject standing

`SubjectStandings` is scoped and composes every discovered `ISubjectStandingContributor`, ordered by type name so a
result never rests on registration order: the first inactive answer ends the walk, roles are the union of those
reported, and the first name stands. No contributor knowing the subject answers null. A contributor's failure propagates.

## Inspectability

Startup logs include total providers, eligible providers, default ID, and election reason. Composition facts expose one
observation per provider plus the default election receipt. `IAuthProviderCatalog` deliberately excludes credentials.
The public discovery route returns eligible providers only.

## Unsupported and deferred

- no SAML handler;
- no automatic secret-vault reference resolution;
- no built-in provider-token persistence or refresh-token API for calling third-party APIs;
- no claim that a provider console, callback URI, consent policy, or tenant allow-list can be inferred by Koan.

The embedded authorization server has its own package and contract.
