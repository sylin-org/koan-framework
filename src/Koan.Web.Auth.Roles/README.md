# Koan.Web.Auth.Roles

Role-management surface for Koan: first-class `Role` / `RoleAlias` / `RolePolicyBinding` entities, a REST admin API for CRUD + import/export, and a one-shot admin elevation contributor for FirstUser / ClaimMatch bootstrap.

> **Note (WEB-0065):** The per-request claims-transformation attribution pipeline that previously lived here (`IRoleMapContributor`, `IRoleAttributionService`, `KoanRoleClaimsTransformation`, etc.) was removed in `Koan.Web.Auth.Roles` 0.7.0. Role attribution now happens at sign-in via `IKoanAuthEventContributor` in `Koan.Web.Auth`. Applications stamp roles into the cookie identity once per sign-in; the cookie carries the role claims for the life of the session. See [WEB-0065](../../docs/decisions/WEB-0065-auth-event-contributor-pipeline.md) for the full design and migration notes.

## What this module ships

| | Lives here | Lives in `Koan.Web.Auth` |
|---|---|---|
| `Role`, `RoleAlias`, `RolePolicyBinding` entities | ✓ | |
| `IRoleStore`, `IRoleAliasStore`, `IRolePolicyBindingStore` + defaults | ✓ | |
| `IRoleBootstrapStateStore` (one-shot elevation persistence) | ✓ | |
| `RolesAdminController` (admin REST API at `/api/auth/roles`) | ✓ | |
| Config-template seeding via `RoleBootstrapHostedService` | ✓ | |
| `AdminBootstrapContributor` (`IKoanAuthEventContributor`) | ✓ | |
| `IKoanAuthEventContributor` contract + dispatcher | | ✓ |
| `RoleListFileContributor` (allow/revoke JSON file) | | ✓ |

## Quick start

Reference the package; `AuthRolesModule` wires everything. No DI code is required for the defaults.

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();   // Koan auto-discovers Koan.Web.Auth.Roles
```

Controllers use standard ASP.NET Core role authorization:

```csharp
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    [HttpGet("dashboard")]
    public IActionResult Dashboard() => Ok("Admin dashboard");
}
```

For roles to actually arrive on the principal, your application needs to stamp them at sign-in via an `IKoanAuthEventContributor`. See **[Stamping roles at sign-in](#stamping-roles-at-sign-in)** below.

## Stamping roles at sign-in

This module does not, by itself, decide what roles a user has. That decision belongs in your application — most platforms persist roles on their User entity. Implement an `IKoanAuthEventContributor` to bridge your User row to the cookie:

```csharp
using System.Security.Claims;
using Koan.Web.Auth.Contributors;
using MyApp.Domain;

public sealed class UserRolesContributor : IKoanAuthEventContributor
{
    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.UserId)) return;
        var user = await User.Get(ctx.UserId, ct);          // Koan Entity<User> statics
        if (user is null || user.Roles is null) return;

        foreach (var role in user.Roles)
            ctx.Identity.AddClaim(new Claim(ClaimTypes.Role, role));
    }
}
```

Auto-discovered by `Koan.Web.Auth`'s assembly scan — no DI registration call required. Runs at default priority, after any identity-mapping contributor (the contributor that rewrites the OAuth provider sub to your platform user id should use `Priority = int.MinValue`).

Role claims live in the cookie; changes to `User.Roles` take effect on the user's next sign-in.

## Admin REST API

`RolesAdminController` exposes the role-management surface under `/api/auth/roles`. All endpoints require `[Authorize(Policy = "auth.roles.admin")]`, which by default maps to `role:admin`.

| Method | Path | Description |
|---|---|---|
| GET | `/api/auth/roles` | List all roles |
| PUT | `/api/auth/roles/{key}` | Upsert role |
| DELETE | `/api/auth/roles/{key}` | Delete role |
| GET | `/api/auth/roles/aliases` | List aliases |
| PUT | `/api/auth/roles/aliases/{alias}` | Upsert alias |
| DELETE | `/api/auth/roles/aliases/{alias}` | Delete alias |
| GET | `/api/auth/roles/policy-bindings` | List policy bindings |
| PUT | `/api/auth/roles/policy-bindings/{policy}` | Upsert binding |
| DELETE | `/api/auth/roles/policy-bindings/{policy}` | Delete binding |
| GET | `/api/auth/roles/export` | Export current state as appsettings template |
| POST | `/api/auth/roles/import?dryRun=&force=` | Import from appsettings template |
| POST | `/api/auth/roles/reload` | Reload the in-memory snapshot from the stores |

Production guardrails: `import` and seeding are disabled in Production unless either `KoanEnv.AllowMagicInProduction` or `Koan:Web:Auth:Roles:AllowSeedingInProduction` is true.

## Bootstrap seeding

`RoleBootstrapHostedService` runs once at startup. If the `Role` / `RoleAlias` / `RolePolicyBinding` stores are all empty, it seeds them from the configuration template:

```jsonc
{
  "Koan": {
    "Web": {
      "Auth": {
        "Roles": {
          "Aliases": {
            "Map": {
              "administrator": "admin",
              "moderator": "moderator",
              "viewer": "reader"
            }
          },
          "Roles": [
            { "Id": "admin", "Display": "Administrator", "Description": "Full system access" },
            { "Id": "moderator", "Display": "Moderator", "Description": "Content moderation" }
          ],
          "PolicyBindings": [
            { "Id": "auth.roles.admin", "Requirement": "role:admin" }
          ]
        }
      }
    }
  }
}
```

Skipped in Production unless `AllowSeedingInProduction` is set. On non-empty stores the seeder is a no-op.

## Admin bootstrap (`AdminBootstrapContributor`)

One-shot admin elevation, configured at `Koan:Web:Auth:Lifecycle:AdminBootstrap` (note: lives in `Koan.Web.Auth`'s options shape):

```jsonc
{
  "Koan": {
    "Web": {
      "Auth": {
        "Lifecycle": {
          "AdminBootstrap": {
            "Mode": "ClaimMatch",            // None | FirstUser | ClaimMatch
            "AdminEmails": ["admin@example.com"]
          }
        }
      }
    }
  }
}
```

Modes:

- **`None`** (default): no automatic admin. Admin role must be assigned by the application (e.g. via `User.Roles`) or via the role-list override file.
- **`FirstUser`**: the first authenticated user to reach the contributor gets `admin`. One-shot, persisted via `IRoleBootstrapStateStore`. Useful for development.
- **`ClaimMatch`**: grants `admin` when a configured claim matches one of `ClaimValues` (or the convenience `AdminEmails` list against `ClaimTypes.Email`).

The contributor runs at `Priority = 100` so it observes claims stamped by earlier contributors. If the principal already has `admin` from an upstream contributor, this is a no-op for that sign-in (and the one-shot is not burned).

## Override / allow-list file (`RoleListFileContributor`)

For pre-seeding admin emails before the user has signed in for the first time, or for stripping roles a user record still carries, configure the file-backed override channel in `Koan.Web.Auth`:

```jsonc
{
  "Koan": {
    "Web": {
      "Auth": {
        "Lifecycle": {
          "RoleListFile": {
            "FilePath": "/data/role-list.json",
            "PollInterval": "00:00:30"
          }
        }
      }
    }
  }
}
```

File shape:

```json
{
  "allow":  { "user@example.com": ["admin", "curator"] },
  "revoke": { "ex-admin@example.com": ["admin"] }
}
```

Empty `FilePath` (default) disables the contributor.

## Versioning

| Version | Notes |
|---|---|
| 0.7.0 | Removed the per-request attribution pipeline (WEB-0065). `IRoleMapContributor`, `IRoleAttributionService`, `IRoleAttributionCache`, `KoanRoleClaimsTransformation`, `DefaultRoleAttributionService`, the old `RoleListContributor`, and `DefaultCapabilityContributor` are gone. `AdminBootstrapContributor` is new (ported from inline bootstrap logic). |
| 0.6.x and earlier | Old per-request attribution pipeline. See WEB-0049 for the original design. |

## See also

- [WEB-0065](../../docs/decisions/WEB-0065-auth-event-contributor-pipeline.md) — event-contributor pipeline ADR (the design this module now defers to for attribution)
- [WEB-0049](../../docs/decisions/WEB-0049-role-attribution-layer-and-claims-transformation.md) — superseded; describes the pre-0.7.0 attribution layer
