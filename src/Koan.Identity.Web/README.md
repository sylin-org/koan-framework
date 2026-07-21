# Sylin.Koan.Identity.Web

Authenticated HTTP management for Koan Identity. Reference the package and keep `AddKoan()` unchanged; Koan discovers
the controllers and exposes subject-scoped self-service plus role-gated operator APIs.

## Install

```powershell
dotnet add package Sylin.Koan.Identity.Web
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The package brings `Sylin.Koan.Identity` transitively. A Web Auth provider must establish the authenticated principal,
and a selected Data provider persists the identity plane.

## Meaningful behavior

- `/api/identity/me` projects only the current subject's profile, emails, connected providers, and cookie sessions.
- The current subject can unlink their own provider link and sign out every other session.
- `/api/identity/admin` lists/searches people and supports suspend, reactivate, and core-dependent deletion.
- `/api/identity/admin/identities/{id}/access` explains effective access and grants/revokes global roles.
- `/api/identity/admin/impersonation` implements a reasoned, dual-control, time-boxed acting-as workflow.
- Startup reporting advertises both route groups and their capabilities.

Every self-service action requires authentication and resolves the subject from the principal. Operator routes require
the standard `koan:identity-operator` role, grantable globally through `IdentityRole` or an external host identity.
Tenant membership projection strips this host role at its chokepoint.

## Boundaries

- This is an API projection, not a bundled operator or end-user UI.
- Provider linking initiation still belongs to a verified provider callback; the API only lists and owner-unlinks
  existing links.
- Personal access token and group-management routes are intentionally absent because Identity has no accepted
  personal-token authentication path or group-to-access semantics.
- Operator search is bounded by `size`, but a text query currently evaluates the Identity set in process; use the
  endpoint within its current administrative scale boundary.
- Authorization attributes and impersonation guards protect the controllers; deployment policy still decides who
  receives the operator role.

See [TECHNICAL.md](TECHNICAL.md) and the core [Identity contract](../Koan.Identity/README.md).
