# Koan.Web.Auth.Roles — Technical Reference

Post-WEB-0065 the module's surface is small: data shapes for role administration, a REST admin controller, the bootstrap seeder, and one event contributor for one-shot admin elevation. The per-request claims-transformation pipeline that previously occupied this module was removed in 0.7.0.

For the auth lifecycle pipeline this module plugs into, see [`Koan.Web.Auth/README.md`](../Koan.Web.Auth/README.md) and [WEB-0065](../../docs/decisions/WEB-0065-auth-event-contributor-pipeline.md).

## Public types

### Contracts (`Koan.Web.Auth.Roles.Contracts`)

| Type | Purpose |
|---|---|
| `IKoanAuthRole` | Instance-shape interface for a role row: `Id`, `Display`, `Description`, `RowVersion`. |
| `IKoanAuthRoleAlias` | `Id` (the alias key) → `TargetRole`. |
| `IKoanAuthRolePolicyBinding` | `Id` (policy name) → `Requirement` (e.g. `role:admin`). |
| `IRoleStore` / `IRoleAliasStore` / `IRolePolicyBindingStore` | CRUD over the three entity collections; `All` / `UpsertMany` / `Delete`. |
| `IRoleBootstrapStateStore` | One-shot admin-bootstrap persistence: `IsAdminBootstrapped` / `MarkAdminBootstrapped`. |
| `IRoleConfigSnapshotProvider` | In-memory snapshot of alias map + policy bindings the admin surface consults. `Get()` / `Reload(ct)`. |

### Models (`Koan.Web.Auth.Roles.Model`)

`Role : Entity<Role>` — fields: `Display`, `Description`, `RowVersion`. Default-store impl persists via Koan `Entity<Role>` statics.

`RoleAlias : Entity<RoleAlias>` — `Id` is the alias key, `TargetRole` is the canonical role.

`RolePolicyBinding : Entity<RolePolicyBinding>` — `Id` is the policy name, `Requirement` is the expression (e.g. `role:admin` or `perm:auth.roles.admin`).

### Stores (`Koan.Web.Auth.Roles.Services.Stores`)

All four (`DefaultRoleStore`, `DefaultRoleAliasStore`, `DefaultRolePolicyBindingStore`, `DefaultRoleBootstrapStateStore`) are thin pass-throughs to Koan `Entity<T>` statics. Apps override via `TryAddSingleton<IRoleStore, MyRoleStore>()` before the module auto-registers.

### Contributors (`Koan.Web.Auth.Roles.Contributors`)

`AdminBootstrapContributor : IKoanAuthEventContributor`

- **Priority:** `100` — runs late in the sign-in dispatch so it observes claims stamped by application contributors.
- **Modes:** `None` (default, no-op), `FirstUser`, `ClaimMatch`.
- **Persistence:** uses `IRoleBootstrapStateStore` (single fixed-id `RoleBootstrapState` row) so the elevation fires at most once across the cluster.
- **No-op when:** principal already has `admin` from an earlier contributor, or `IsAdminBootstrapped()` returns true.
- **Configuration:** `Koan:Web:Auth:Lifecycle:AdminBootstrap:*` (in `Koan.Web.Auth`'s options shape, not the local `Roles` section).

## Configuration

`RoleAttributionOptions` at `Koan:Web:Auth:Roles` — purely a seed template for the role admin surface, no runtime attribution:

```jsonc
{
  "Koan": {
    "Web": {
      "Auth": {
        "Roles": {
          "AllowSeedingInProduction": false,
          "Aliases": { "Map": { "administrator": "admin" } },
          "Roles": [
            { "Id": "admin", "Display": "Administrator" },
            { "Id": "moderator", "Display": "Moderator" }
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

`RoleBootstrapHostedService` reads this on startup, seeds the three stores when empty, then exits. The admin REST API (`/api/auth/roles/import`) reuses the same template shape for explicit re-imports.

## REST surface (`/api/auth/roles`)

All endpoints `[Authorize(Policy = "auth.roles.admin")]`. The policy is seeded by default to `RequireRole("admin")`.

| Verb | Route | Notes |
|---|---|---|
| GET | `/` | List roles. |
| PUT | `/{key}` | Upsert role. `RowVersion` honored for optimistic concurrency. |
| DELETE | `/{key}` | Delete role. 404 if absent. |
| GET | `/aliases` | List aliases. |
| PUT | `/aliases/{alias}` | Upsert alias. |
| DELETE | `/aliases/{alias}` | Delete alias. |
| GET | `/policy-bindings` | List bindings. |
| PUT | `/policy-bindings/{policy}` | Upsert binding. |
| DELETE | `/policy-bindings/{policy}` | Delete binding. |
| GET | `/export` | Emit the current store state in the appsettings template shape. |
| POST | `/import?dryRun=&force=` | Import the configuration template. `dryRun=true` returns the diff without applying. `force=true` deletes entries not present in the template (otherwise merge-add only). |
| POST | `/reload` | Reload the alias / policy-binding snapshot from the stores. |

Production guardrails on `import`: refuses to run in Production unless `KoanEnv.AllowMagicInProduction` or `Koan:Web:Auth:Roles:AllowSeedingInProduction` is true.

## DI registration

`KoanAutoRegistrar.Initialize` (called by `services.AddKoan()`) wires:

```csharp
services.AddKoanOptions<RoleAttributionOptions>(RoleAttributionOptions.SectionPath);

services.TryAddSingleton<IRoleConfigSnapshotProvider, DefaultRoleConfigSnapshotProvider>();
services.TryAddSingleton<IRoleStore, DefaultRoleStore>();
services.TryAddSingleton<IRoleAliasStore, DefaultRoleAliasStore>();
services.TryAddSingleton<IRolePolicyBindingStore, DefaultRolePolicyBindingStore>();
services.TryAddSingleton<IRoleBootstrapStateStore, DefaultRoleBootstrapStateStore>();
services.AddHostedService<RoleBootstrapHostedService>();
```

`AdminBootstrapContributor` is auto-discovered by `Koan.Web.Auth`'s `IKoanAuthEventContributor` assembly scan — no explicit registration here.

Apps that want to replace a store register the override **before** `AddKoan()`, or after with a follow-up `services.Replace(...)` call.

## What no longer lives here

The 0.6.x module shipped a per-request claims-transformation pipeline. All of it is gone in 0.7.0:

| Removed | Why |
|---|---|
| `IRoleMapContributor` + implementations | Superseded by `IKoanAuthEventContributor.OnSignIn` (in `Koan.Web.Auth`). Sign-in fires once; per-request was redundant given the cookie carries claims. |
| `IRoleAttributionService` + `DefaultRoleAttributionService` | Same — the dispatcher in `Koan.Web.Auth` replaces it. |
| `IRoleAttributionCache` + `InMemoryRoleAttributionCache` | No per-request computation, no cache needed. The cookie *is* the cache. |
| `KoanRoleClaimsTransformation` (`IClaimsTransformation`) | The sign-in event stamps claims directly; no per-request transformation pipeline. |
| Old `RoleListContributor` | Moved to `Koan.Web.Auth.Contributors.Builtin.RoleListFileContributor` as a sign-in-time contributor. |
| `RoleAttributionOptions.ClaimKeys` / `DevFallback` / `MaxRoles` / `MaxPermissions` / `EmitPermissionClaims` | Tied to the deleted attribution pipeline. Applications that need claim-key extraction or alias normalization write their own contributor. |
| `RoleAttributionOptions.Bootstrap*` | Moved to `Koan:Web:Auth:Lifecycle:AdminBootstrap` (lives in `Koan.Web.Auth`'s options). |
| `RoleAttributionOptions.RoleList` | Moved to `Koan:Web:Auth:Lifecycle:RoleListFile` (lives in `Koan.Web.Auth`'s options). |

## Migration

For 0.6.x applications upgrading to 0.7.0:

**1. Move role attribution to an `IKoanAuthEventContributor` in your app.** The simplest pattern reads roles from your User entity and stamps them onto the cookie identity at sign-in. See the README's "Stamping roles at sign-in" section.

**2. Move config keys:**

```jsonc
// Before
"Koan:Web:Auth:Roles:Bootstrap:Mode" = "FirstUser"
"Koan:Web:Auth:Roles:Bootstrap:AdminEmails" = ["admin@example.com"]
"Koan:Web:Auth:Roles:RoleList:FilePath" = "/data/role-list.json"

// After
"Koan:Web:Auth:Lifecycle:AdminBootstrap:Mode" = "FirstUser"
"Koan:Web:Auth:Lifecycle:AdminBootstrap:AdminEmails" = ["admin@example.com"]
"Koan:Web:Auth:Lifecycle:RoleListFile:FilePath" = "/data/role-list.json"
```

The `Koan:Web:Auth:Roles` section now only carries seed-template fields (`Aliases`, `Roles`, `PolicyBindings`, `AllowSeedingInProduction`).

**3. Remove custom `IRoleMapContributor` implementations.** Re-implement as `IKoanAuthEventContributor.OnSignIn` — same logic, runs once per sign-in instead of per-request. Auto-discovered, no DI registration needed.

**4. Remove any code that depended on `IRoleAttributionService` or `IRoleAttributionCache`.** These were intended for cache-busting role recomputation; the new model is "re-sign-in to refresh." If you need an admin endpoint that invalidates a specific session, sign the user out from the admin surface — the next request will reauthenticate.

## See also

- [WEB-0065](../../docs/decisions/WEB-0065-auth-event-contributor-pipeline.md) — event-contributor pipeline (this module's runtime dependency)
- [WEB-0049](../../docs/decisions/WEB-0049-role-attribution-layer-and-claims-transformation.md) — superseded; describes the pre-0.7.0 attribution layer
- `Koan.Web.Auth/Contributors/IKoanAuthEventContributor.cs` — the auto-discovered contract
- `Koan.Web.Auth/Options/AuthLifecycleOptions.cs` — `AdminBootstrap` + `RoleListFile` options
