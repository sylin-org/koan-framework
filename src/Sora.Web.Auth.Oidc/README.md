# Sylin.Sora.Web.Auth.Oidc

Generic OpenID Connect provider adapter for Sora Web.

## Install

```powershell
dotnet add package Sylin.Sora.Web.Auth.Oidc
```

## Notes
- Configure authority, client id/secret, response type, scopes, and callback path via typed Options.
- Use controllers for challenge/callback.

See [`TECHNICAL.md`](TECHNICAL.md) for details.