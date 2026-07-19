# Sylin.Koan.Security.Trust

Koan's workload-token and ambient request-identity pillar. Reference it and keep `AddKoan()` as the only bootstrap;
Trust generates an ES256 keypair, registers the non-default `Koan.bearer` scheme, and projects authenticated request
principals through `Identity.Current`.

## Install

```powershell
dotnet add package Sylin.Koan.Security.Trust
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Protect a token endpoint with the standard ASP.NET Core declaration:

```csharp
using Koan.Security.Trust.Inbound;

[Authorize(AuthenticationSchemes = KoanBearerDefaults.AuthenticationScheme)]
[HttpGet("/internal/sync")]
public IActionResult Sync() => Ok(Identity.Current.Id);
```

No issuer constructor, shared secret, bearer registration, or middleware call is required.

## Meaningful result: mint a workload token

Inject the one issuer contract where the application itself needs a short-lived token:

```csharp
using Koan.Security.Trust.Issuer;

public sealed class SyncToken(IIssuer issuer)
{
    public string Create() => issuer.Issue(
        new TrustClaims { Subject = "worker-1", Roles = ["service"] },
        audience: "koan://orders");
}
```

Trust validates signature, issuer, algorithm, and lifetime. A resource-specific audience is an authorization decision
owned by that resource; compare `aud` with the resource's canonical identifier before accepting the request. Koan MCP
does this automatically at its HTTP edge.

## Key and deployment posture

The default key store generates one random P-256 keypair per process. It is cryptographically safe and needs no
secret, but tokens and public keys change when the process restarts. This is the honest default for tests,
single-process applications, and same-host workload calls.

`Sylin.Koan.Web.Auth.Server` replaces that store outside Development with its persisted, encrypted-at-rest, rotating
key lifecycle and fails startup when continuity cannot be guaranteed. Use that package for OAuth public-client
issuance. Direct Trust does not claim remote JWKS discovery, cross-service enrollment, federation, or revocation.

Optional issuer metadata binds from `Koan:Security:Trust`:

```json
{
  "Koan": {
    "Security": {
      "Trust": {
        "Issuer": "https://api.example.com",
        "Audience": "koan://api",
        "DefaultLifetimeMinutes": 15
      }
    }
  }
}
```

Empty issuer/audience values and non-positive lifetimes reject options validation.

## Boundaries

- Trust owns ES256 issuance, local public-key verification, the bearer scheme, and ambient identity.
- Web Auth owns interactive external-provider sign-in and the cookie session.
- Auth Server owns OAuth protocol, public discovery/JWKS endpoints, and durable signing-key continuity.
- Each resource edge owns its exact audience and authorization policy.
- Development `?_as=` personas are activated only by Web Auth's Development-only context contributor; Trust alone
  does not mutate requests.
- Fleet enrollment, remote issuer discovery, identity federation, credential revocation, and a cross-channel security
  envelope are not V1 capabilities.

See [TECHNICAL.md](TECHNICAL.md) and the public [authentication guide](../../docs/guides/auth-howto.md).
