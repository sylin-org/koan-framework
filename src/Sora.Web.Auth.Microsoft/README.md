# Sylin.Sora.Web.Auth.Microsoft

Microsoft identity platform OAuth/OIDC provider adapter for Sora Web.

## Install

```powershell
dotnet add package Sylin.Sora.Web.Auth.Microsoft
```

## Notes
- Configure client id/secret, tenant, and redirect URI via typed Options.
- Use MVC controllers for auth flows (challenge/callback). No inline endpoints.

See [`TECHNICAL.md`](TECHNICAL.md) for details.