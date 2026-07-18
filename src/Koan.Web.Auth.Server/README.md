# Sylin.Koan.Web.Auth.Server

Koan's embedded OAuth 2.1 authorization server. Reference the package and keep `AddKoan()` as the only bootstrap;
Koan publishes discovery, issues audience-bound ES256 tokens through Authorization Code + PKCE and Device flows, and
connects those tokens to the same authorization declarations used by the application's HTTP and MCP surfaces.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Auth.Server
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

No authorization-server registration, middleware, endpoint mapping, issuer construction, or bearer configuration is
required. The application supplies two ordinary pages: a consent page at `/me/connect` and a terminal page at
`/me/connect/done`. Change those paths with `Koan:Web:Auth:Server:ConsentPath` and `DonePath`.

## Meaningful behavior

- `/.well-known/oauth-authorization-server` and `/.well-known/openid-configuration` advertise the server.
- `/.well-known/jwks.json` publishes the current and overlapping ES256 verification keys.
- `/oauth/authorize` + `/oauth/token` implement Authorization Code with mandatory PKCE-S256.
- `/oauth/device` + `/oauth/token` implement the Device Authorization Grant.
- `/oauth/register` dynamically registers short-lived, public, loopback-only clients when enabled.
- `/oauth/request/{rid}` and its approve/deny actions form the small consent-page seam owned by the application.
- `/oauth/dev-token` provides a real audience-bound token for the current cookie user in Development only.
- Refresh tokens rotate with reuse detection and remain backed by a revocable grant.

The consent page reads `rid` or `user_code`, renders the framework's request projection, and posts Allow or Deny. The
framework owns every protocol decision and response around that page.

## Pre-register a client

All supported clients are public: there are no client secrets. When dynamic registration is disabled, pre-register a
client directly through Koan's Entity model:

```csharp
using Koan.Web.Auth.Server.Protocol;

await new OAuthClient
{
    Id = "operations-console",
    ClientName = "Operations Console",
    RedirectUris = ["https://console.example.com/oauth/callback"],
    CreatedUtc = DateTimeOffset.UtcNow
}.Save();
```

Dynamic clients are restricted to loopback redirects. Deliberate non-loopback redirects belong only on explicitly
pre-registered clients and are matched exactly.

## Production posture

Set `Koan:Web:Auth:Server:Issuer` to the canonical public origin behind a proxy. Outside Development, Koan persists and
rotates encrypted-at-rest signing keys through the configured Data provider; startup fails closed if only an ephemeral
key is available unless that unsafe posture is explicitly acknowledged. OAuth clients, codes, requests, grants, and
keys are Entity-backed, so a durable Data provider is part of a real deployment.

Startup reporting states whether the key is ephemeral or persisted and whether the development token endpoint is
available. OAuth discovery and JWKS expose the effective client-facing protocol decisions.

## Boundaries

- This package issues tokens to clients after a user has signed into this application. It does not replace the external
  providers in `Sylin.Koan.Web.Auth` that establish the user's cookie session.
- It supports public clients only. Confidential clients, client secrets, `client_credentials`, SAML, general identity
  federation, and third-party public-IdP operation are not supported.
- Dynamic registration is intentionally loopback-only, rate-limited, and expiring; disable it when every client is
  known in advance.
- The application owns consent and completion presentation. Koan does not infer branding, legal copy, or consent
  policy.
- Set an explicit issuer and use durable Data and Data Protection storage before relying on tokens across production
  restarts or multiple nodes.

See [TECHNICAL.md](TECHNICAL.md) and the public
[authorization-server guide](../../docs/guides/oauth-server-howto.md).
