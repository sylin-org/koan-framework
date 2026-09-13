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
- `/api/identity/scoped-roles/{tenant}/{scopeType}/{scopeId}` exposes bounded scoped-role definitions,
  assignments, policies, previews, and current-subject access.
- Startup reporting advertises both route groups and their capabilities.

Every self-service action requires authentication and resolves the subject from the principal. Operator routes require
the standard `koan:identity-operator` role, grantable globally through `IdentityRole` or an external host identity.
Tenant membership projection strips this host role at its chokepoint.

Scoped-role routes require ordinary authentication plus the application-provided authority envelopes for each
operation; they never require or confer the global operator role. Mutable resources use quoted numeric ETags and
require `If-Match`. Cookie-authenticated mutations require ASP.NET antiforgery validation, while authenticated
non-cookie schemes retain the application's configured authentication and CORS behavior. Effective and preview
responses expose only a safe allow/deny decision; role, policy, binding and version provenance stays inside core.

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
- Scoped directories require an adapter that proves full filter pushdown and provider-bounded pagination. In-memory
  directory paging is deliberately rejected; MongoDB and SQLite are the currently exercised subset.
- Assignable-role directories and scoped audit reads remain closed until authority-filtered pagination and a
  non-forgeable scoped audit owner are available.

See [TECHNICAL.md](TECHNICAL.md) and the core [Identity contract](../Koan.Identity/README.md).
