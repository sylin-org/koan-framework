# Sylin.Koan.Web.Auth

Koan's external sign-in runtime for ASP.NET Core. Reference a provider connector, supply credentials, and
`AddKoan()` compiles the provider plan, registers maintained OAuth2/OIDC handlers, maps the framework endpoints, and
reports the result at startup.

## Install

Use a connector when one matches your provider; it brings Web Auth transitively:

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Google
```

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

No authentication-specific registration or middleware call is required:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Register `https://your-app/auth/google/callback` with Google. Start sign-in at
`GET /auth/google/challenge?return=/`.

`return`, on the challenge and on `/auth/logout`, is honoured only when it is a local path of the application — one
leading `/`, printable ASCII, no host after percent-decoding (`//`, `/\`), no backslash, no `.` or `..` segment, under
the base path — or an absolute URL on the exact origin (scheme, host and port) of an entry in
`Koan:Web:Auth:ReturnUrl:AllowList`, without userinfo, at or under the entry's path. Anything else, and no `return` at
all, lands on `Koan:Web:Auth:ReturnUrl:DefaultPath` (`/`) under the base path, never on the value. An allow-list entry
is an origin with an optional path (`https://partner.example.com/callback`) and no userinfo, query or fragment;
`DefaultPath` is an application-relative local path or an allow-listed URL. Anything else refuses the start.

Say where a failed sign-in lands, beside `return` (`&failure=/sign-in`) or for every provider in
`Koan:Web:Auth:ReturnUrl:FailurePath`: a rooted path of the application in printable ASCII, with no `.` or `..`
segment, no backslash or encoded slash, no fragment and no `error` in its own query, application-relative: the base
path is added once, so `/sign-in` under `/me` lands on `/me/sign-in`. The person
then arrives there with one code in the query — `provider_unreachable`, `handle_unresolved`, `access_denied`,
`callback_mismatch` or `sign_in_failed` — and never the provider's text. Nothing named keeps each handler's own
failure answer.

## Meaningful behavior

- `GET /.well-known/auth/providers` returns eligible providers only.
- `GET /auth/{provider}/challenge` invokes the provider's ASP.NET handler. Built-in OAuth2/OIDC flows use PKCE.
- `/auth/{provider}/callback` is consumed by the corresponding ASP.NET authentication handler.
- `GET /me` projects the current cookie principal; `GET|POST /auth/logout` removes the local session.
- Explicitly configured providers outrank automatic local defaults. `PreferredProviderId` selects among eligible
  providers.
- Startup logs and Koan composition facts report provider state, eligibility, default election, reason, and correction
  without exposing credentials.

Web Auth can also run a configuration-only OIDC/OAuth2 provider. No generic connector package is needed; set `Type`,
provider endpoints or `Authority`, `ClientId`, and `ClientSecret` under `Koan:Web:Auth:Providers:{id}`.

## Accept the application's own bearer on its pages

An application whose people approve agents through `Sylin.Koan.Web.Auth.Server` lets those agents read its ordinary
pages as the person with one setting:

```json
{ "Koan": { "Web": { "Auth": { "ResourceServer": { "Enabled": true } } } } }
```

A request that carries a `Bearer` `Authorization` header is then authenticated by its bearer alone, never by a session
cookie it also carries. A header of another scheme — the `Basic` credentials a reverse proxy passes upstream — carries
no token the edge could accept and is left to the session, so a signed-in person behind such a proxy still reads as
themselves. The token is accepted only when its audience is the application's base URL (`Koan:Web:ApplicationUrl`,
with or without a trailing slash), or `Koan:Web:Auth:ResourceServer:Resource` when set; its subject becomes the same
`ClaimTypes.NameIdentifier` the person's session carries, so every page and gate answers for the agent as for the
person. A token that is expired, tampered, foreign or issued for another resource answers 401 with
`WWW-Authenticate: Bearer error="invalid_token"` wherever an endpoint requires an authenticated caller, and before any
MVC controller action or Razor Page that admits anonymous callers; a minimal-API or `MapRazorComponents` endpoint that
admits anonymous callers runs as a visitor instead. The embedded authorization server's protocol endpoints never
authenticate by the application's bearer, so they keep their own answers when a client attaches a stale token. A
provider endpoint that checks a token of its own must carry `[ResourceServerExempt]`, or the edge refuses that token
before the endpoint sees it; the dev Test provider's authorize, token, userinfo and discovery endpoints carry it. A
request without a bearer keeps the session cookie. The MCP edge keeps its own resource (`/mcp`) on the same rule.

Under a base URL with a path, the login redirect and the discovery document's `challengeUrl` carry the path once
(`/notes/auth/google/challenge`).

## Claims and role refresh

Provider-owned claims and application-owned role decisions stay distinct. Web Auth maps the provider's
roles to `ClaimTypes.Role` and its extra claims one-for-one; it does not define, normalize, or rank a
role vocabulary, so a role means exactly what the application's authorization declares. Refreshing
application-owned role claims belongs to the shared sign-in flow: an `IKoanAuthFlowHandler` may stamp or
strip role claims on the mutable sign-in identity and re-check them during cookie-principal validation. An
optional built-in handler applies an email-keyed allow/revoke file at sign-in
(`Koan:Web:Auth:Lifecycle:RoleListFile`); an empty file path, the default, disables it.

## Guarantees and failure posture

- Connector references declare availability; they do not silently enable an unconfigured real provider.
- Explicit but incomplete provider intent fails startup with the exact missing fields and configuration path.
- Custom protocol connectors participate in the same plan, discovery, facts, and election; an eligible provider with
  no registered ASP.NET handler stops startup with a correction naming the missing scheme.
- Unknown or ineligible `PreferredProviderId` values fail startup instead of silently selecting something else.
- Missing external subject identifiers, identity-link persistence failures, and security-bearing lifecycle-handler
  failures reject the sign-in flow.
- Cookie validation failures reject the principal. Sign-out cleanup alone is best-effort.
- A failure landing that could leave the application or its base path, could not be a `Location` header, or already
  names `error` is refused: at the challenge with 400, in configuration at startup naming the key. The landing
  receives one sanitized code, not the provider's response.

## Boundaries

- Includes OAuth2 and OIDC interactive sign-in. Other protocols require a connector providing both `IAuthProtocol`
  configuration validation and a standard ASP.NET handler; declaring a `Type` alone does not add protocol mechanics.
  No SAML handler is included.
- Web Auth signs users into this application; `Sylin.Koan.Web.Auth.Server` is the separate opt-in capability that
  issues OAuth tokens to clients.
- Referencing `Sylin.Koan.Web.Auth` alone does not add a simulated provider. Use
  `Sylin.Koan.Web.Auth.Connector.Test` for local OAuth/OIDC flows.
- Secrets are ordinary configuration values today; use your deployment platform's configuration provider. A
  `SecretRef` indirection is not implemented.

See [TECHNICAL.md](TECHNICAL.md) and the public
[authentication guide](../../docs/guides/authentication-setup.md).
