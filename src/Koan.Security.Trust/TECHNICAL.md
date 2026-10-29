# Sylin.Koan.Security.Trust — technical contract

## Ownership and activation

`TrustModule` is the single composition owner. Package discovery under `AddKoan()` binds issuer and development
identity options, selects an `IIssuerKeyStore`, registers one `IIssuer`, adds `Koan.bearer`, and supplies
`IHttpContextAccessor` for `Identity.Current`. `AddAuthentication()` is additive and does not replace Web Auth's cookie
defaults.

Package reference is activation intent. The bearer-registration extension is internal; functional consumers do not
assemble the scheme or depend on Web Auth merely to make Trust work.

## Issuer and key lifecycle

`IIssuer` is the one mint/verify/public-JWKS contract. `EcdsaIssuer` pins ES256 over P-256 and uses the current
`EcdsaKeyRing` from `IIssuerKeyStore` on every issue or validation operation. `EphemeralIssuerKeyStore` is the default
and holds a random per-process keypair. The implementation publishes public-only JWKs and accepts active plus retiring
keys during rotation overlap.

Auth Server is the genuine cross-module key-store consumer. Outside Development, and in Development when the
application names a key ring folder, it replaces the ephemeral store with `PersistedIssuerKeyStore`, backed by Koan Data and protected through ASP.NET Data Protection. Auth Server owns the
continuity guard, rotation service, public JWKS route, and the deployment prerequisites that make those guarantees
possible. A separate contracts package would add no isolation: every production consumer of these contracts also
requires functional Trust behavior.

There is no symmetric issuer, shared-secret configuration, trust-mode election, or insecure escape flag. Naming
`Issuer` changes the token's `iss`; it does not enable an unimplemented federated verifier.

The issuer name is one per request (SEC-0023): what a registered `IIssuerIdentifier` states, else the named `Issuer`,
else `koan-dev`. Auth Server registers the identifier, so the issuer its discovery metadata states is the `iss` every
token carries; it may derive from the request in Development, so it is read when a token is minted and when one is
validated, never captured at configuration time.

## Token and validation contract

`TrustClaims` and `JwtClaimFactory` define one wire projection. Roles use `ClaimTypes.Role`; scopes use the standard
space-delimited `scope` claim; `Koan.permission` remains descriptive and has no authorization effect by itself.
`IIssuer.Issue` accepts an optional lifetime and audience, falling back to typed options.

`ConfigureKoanBearerOptions` obtains alg-pinned `TokenValidationParameters` from the issuer and resolves its key ring
again for every validation, so rotation becomes visible without a restart. It validates signing key, ES256 algorithm,
issuer, and lifetime with one-minute clock skew; the accepted issuers are the stated one for the request and the
named-or-default one, which a token minted where nothing could be stated carries. It deliberately does not validate one global audience: MCP and other
resource servers compare the authenticated token's `aud` with their own canonical resource identifier.

`KoanBearerResource` is that comparison, written once (SEC-0025). `Authenticate(context, resource)` runs `Koan.bearer`
and answers its no-result unchanged; a success becomes a failure when no `aud` names the resource with or without one
trailing slash (ordinal, RFC 7519) or the token has no `sub`, and otherwise the principal gains
`ClaimTypes.NameIdentifier` from `sub`, the identifier a cookie session carries. `Challenge` builds the RFC 6750
`WWW-Authenticate` value with an optional error, description and RFC 9728 metadata URL.

`KoanBearerDefaults.AuthenticationScheme` is non-default. Endpoints opt in through ASP.NET Core's
`[Authorize(AuthenticationSchemes = ...)]`; Web Auth's cookie remains the default for browser flows.

## Ambient and development identity

`Identity.Current` reads the current `HttpContext.User` through the ambient application host and returns a
`KoanIdentity` projection of subject, name, roles, authentication state, and underlying principal. It is
unauthenticated outside an active request.

`DevIdentity.Resolve` is a pure parser for `_as` and `_roles`. Trust owns the parser, options, and constants; Web
Auth's ordered Development-only context contributor decides when it may affect a request. An existing authenticated
principal is never overwritten.

## Configuration

`TrustIssuerOptions` binds from `Koan:Security:Trust` and validates on start:

- `Issuer` — the named `iss`, optional; blank is refused; unnamed, the stated issuer or `koan-dev`;
- `Audience` — non-empty default `aud`, default `koan`;
- `DefaultLifetimeMinutes` — positive lifetime, default 15.

`DevIdentityOptions` binds from `Koan:Security:Trust:DevIdentity`. No signing secret is accepted.

## Consumers and limits

- Web Auth consumes ambient/dev identity and composes its cookie pipeline.
- Auth Server consumes `IIssuer`, `IIssuerKeyStore`, `EcdsaKeyRing`, and claim projection for OAuth tokens and JWKS.
- MCP consumes `KoanBearerResource` for its own resource, and Web Auth's resource edge for the application's.

The current package is a same-trust-boundary issuer/verifier. It does not discover remote JWKS, enroll nodes, exchange
tokens, bind credentials to a transport identity, revoke issued credentials, federate external issuers, or carry one
security envelope through Messaging and Jobs. Those dated roadmap concepts are not inferred from source presence.
