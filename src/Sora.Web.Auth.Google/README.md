# Sylin.Sora.Web.Auth.Google

Google OAuth/OIDC provider adapter for Sora Web.

## Install

```powershell
dotnet add package Sylin.Sora.Web.Auth.Google
```

## Notes
- Configure client id/secret and redirect URI via typed Options.
- Use MVC controllers for challenge/callback endpoints (no inline endpoints).

See [`TECHNICAL.md`](TECHNICAL.md) for details.