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
`/me/connect/done`. Change those paths with `Koan:Web:Auth:Server:ConsentPath` and `DonePath`. They are
application-relative: under a base URL with a path (`Koan:Web:ApplicationUrl = https://example.test/notes`) the server
sends the person to `/notes/me/connect` and scopes its browser-binding cookie to `/notes/oauth`.

## Meaningful behavior

- `/.well-known/oauth-authorization-server` and `/.well-known/openid-configuration` advertise the server.
- `/.well-known/jwks.json` publishes the current and overlapping ES256 verification keys.
- `/oauth/authorize` + `/oauth/token` implement Authorization Code with mandatory PKCE-S256.
- `/oauth/device` + `/oauth/token` implement the Device Authorization Grant.
- `/oauth/register` dynamically registers short-lived, public, loopback-only clients when enabled.
- `/oauth/request/{rid}` and its approve/deny actions form the small consent-page seam owned by the application.
- `/oauth/dev-token` provides a real audience-bound token for the current cookie user in Development only.
- Refresh tokens rotate with reuse detection and remain backed by a revocable grant.
- Every token is minted for who the person is now: an inactive subject gets `invalid_grant`, and a live one's token
  carries their current roles and name (`SubjectStandings`), whatever they were at approval.

The consent page reads `rid` or `user_code`, renders the framework's request projection, and posts Allow or Deny. The
framework owns every protocol decision and response around that page. Only the person's session decides: the server
reads the `Koan.cookie` session itself at authorize, at the request projection and at approve and deny, so a bearer
token — even one for the same person, accepted on the application's pages — never approves anything.

## Render the consent page on the server

A page rendered on the server asks Koan in process instead of reading its entities:

```csharp
using Koan.Web.Auth.Server.Protocol;

[HttpGet("/consent")]
public async Task<IActionResult> Consent(string? rid, [FromQuery(Name = "user_code")] string? code, CancellationToken ct)
{
    var lookup = await ConsentPage.Find(HttpContext, rid ?? code, ct);
    if (lookup.Throttled) return StatusCode(429);
    if (lookup.ClientNotAllowed) return View("NotAllowed"); // a device request whose client lacks the device grant
    if (lookup.Request is not { } pending) return NotFound();
    // pending.ClientName (untrusted display text), pending.Scopes, pending.Resource, pending.UserCode or
    // pending.RedirectUri; the Allow form posts to pending.Approve and the Deny form to pending.Deny.
    return View(pending);
}

[HttpGet("/consent/done")]
public async Task<IActionResult> Done() => View(await ConsentPage.Outcome(HttpContext)); // Approved, Denied or null
```

`Find` takes a `rid` or a user code as a person typed it (any case, with or without its hyphen) and counts a user-code
lookup against the same per-address allowance as Koan's own `/oauth/request/{id}`. Approve and deny of a Device-flow
request both need the person signed in, and land on the done page with an `outcome` only this server can write, bound
to that person; `Outcome` reads it, and answers null for a plain, altered, expired or other person's value.

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

A client is allowed the grants it names in `GrantTypes`; naming none allows the code grant only. The Device
Authorization Grant is allowed only to a client that names it, and dynamic registration never does. The permission is
read when a device code is requested, looked up, approved and exchanged, so removing the grant also stops the client's
requests already waiting:

```csharp
await new OAuthClient
{
    Id = "operations-cli",
    ClientName = "Operations CLI",
    GrantTypes = [OAuthGrantTypes.DeviceCode],
    CreatedUtc = DateTimeOffset.UtcNow
}.Save();
```

## List and revoke the agents acting for a person

Each approval — a person allowing a client on the consent page, or a re-connect their remembered consent lets through —
issues its own SEC-0005 `AgentGrant`, which backs that connection's refresh tokens. The grant says when it was issued
and to which client, so an application lists a person's live approvals and revokes one alone:

```csharp
using Koan.Web.Authorization;

var approvals = await AgentGrant.Approvals(personId, take: 50); // live only, newest first
// each: grant.ClientName ?? grant.ClientId, grant.IssuedAt, grant.ExpiresAt
await approvals[0].Remove(); // that connection's next refresh fails; the others keep working
```

`Remove()` is the revoke. Removing a live grant this server issued, by any path, also forgets the person's remembered
consent for that client, scope set and resource, so the client's next connection shows the consent page again; the
person's other approvals keep refreshing until they expire or are removed. Two agents approved through the same client
with the same scopes are two approvals. `ClientName` is the name the client registered, kept as it was at approval;
the client chose it, so show it as unverified. A client that registered no name leaves it null.

Removing a person's every approval is one call, a page at a time, which is what Koan.Identity's suspend, deactivate and
erase make:

```csharp
await AgentGrant.RemoveApprovals(personId);
```

To refuse an agent while a person cannot act, without ending its approval, report it through the standing seam
(`ISubjectStandingContributor`): its refresh is refused and its token is not spent, so it resumes when the report ends.
Access it already holds lasts out `AccessTokenLifetime`.

A grant issued before Koan recorded these answers null for `IssuedAt`, `ClientId` and `ClientName`, so `Approvals`
leaves it out. It still refreshes and revokes, may back several connections, never keeps the person's consent
remembered, and expires `RefreshTokenLifetime` (30 days by default) after it was issued. With
`EnableRefreshTokens = false` a connection has no refresh token to back, so no grant is issued and every connection
asks the person.

## Production posture

Set `Koan:Web:ApplicationUrl` to the application's public URL, or have the application accept it at runtime
(WEB-0081); the issuer and every advertised endpoint derive from it, path included (WEB-0078), and every access token
carries that issuer as `iss`, so a verifier that reads the metadata
and the JWKS accepts it (SEC-0023). `Koan:Web:Auth:Server:Issuer` remains an explicit override, and a named
`Koan:Security:Trust:Issuer` is kept as the issuer when the server's own is unset. Outside Development, Koan persists and
rotates encrypted-at-rest signing keys through the configured Data provider; startup fails closed if only an ephemeral
key is available unless that unsafe posture is explicitly acknowledged. In Development the keys persist the same way
when the application names a key ring folder (`Koan:Web:KeyRingDirectory`, WEB-0079), so a local install's tokens
outlive a restart; with no folder named, Development keeps an ephemeral key (SEC-0022). OAuth clients, codes, requests, grants, and
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
