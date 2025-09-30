# Sylin.Koan.Web.Auth.Connector.Test

A test/dummy auth provider useful for local and CI scenarios.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Test
```

## Notes
- Issues a deterministic ClaimsPrincipal for testing flows.
- Dev login UI at `/.testoauth/login.html` lets you set roles, permissions, and arbitrary claims; persists to LocalStorage (persona export/import supported).
- You can also pass extras via query: `roles=admin,author&perms=content:write&claim.department=ENG&claim.scope=read&claim.scope=write`.
- Use only in non-production environments.

### Prompts and cookies
- To force the login UI even if a previous TestProvider session exists, add `prompt=login` (or `prompt=select_account`) to the authorize request.
- Logging out from the app clears the development `_tp_user` cookie so subsequent auth flows won't auto-approve.

See [`TECHNICAL.md`](TECHNICAL.md) for details.
