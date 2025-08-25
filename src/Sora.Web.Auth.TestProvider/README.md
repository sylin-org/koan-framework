# Sylin.Sora.Web.Auth.TestProvider

A test/dummy auth provider useful for local and CI scenarios.

## Install

```powershell
dotnet add package Sylin.Sora.Web.Auth.TestProvider
```

## Notes
- Issues a deterministic ClaimsPrincipal for testing flows.
- Use only in non-production environments.

See [`TECHNICAL.md`](TECHNICAL.md) for details.