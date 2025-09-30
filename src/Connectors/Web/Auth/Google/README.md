# Sylin.Koan.Web.Auth.Connector.Google

Google OAuth/OIDC provider adapter for Koan Web.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Google
```

## Notes
- Configure client id/secret and redirect URI via typed Options.
- Use MVC controllers for challenge/callback endpoints (no inline endpoints).

See [`TECHNICAL.md`](TECHNICAL.md) for details.
