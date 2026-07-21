# Sylin.Koan.Identity

Koan's durable person and day-two identity core. Reference the package beside Web Auth and keep `AddKoan()` as the
only bootstrap; successful sign-ins reconcile to Entity-backed people, create enforceable cookie sessions, and project
global roles through standard .NET role claims.

## Install

```powershell
dotnet add package Sylin.Koan.Identity
dotnet add package Sylin.Koan.Web.Auth.Connector.Test
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The Test connector is the shortest local sign-in path. Use a deployment provider instead when appropriate. Identity
does not add a second authentication flow: it consumes Web Auth's sign-in lifecycle when Web Auth is present.

## Meaningful behavior

- Each `(provider, subject)` reconciles idempotently to a durable `Identity` and its `IdentityEmail` factors.
- Matching email claims never merge people. A signed-in user must explicitly link another provider identity.
- Each cookie sign-in records a durable `Session`; revoked sessions and suspended/deactivated people are rejected on
  later cookie validation.
- `IdentityRole` binds ordinary role strings globally and sign-in projects them as `ClaimTypes.Role`.
- Effective-access contributors explain global roles and active grants; optional modules such as Identity Tenancy add
  their own facts through the same resolver.
- Identity-domain Entity mutations produce best-effort `AuditEvent` records. Optional hash chaining detects later
  alteration, deletion, or reordering.
- Dual-control, time-boxed impersonation preserves the real actor in a separate claim and rechecks the grant.

All records use Koan's selected Data provider. There is no Identity-specific repository or storage adapter.

## Configuration

```jsonc
{
  "Koan": {
    "Identity": {
      "Posture": "Closed",
      "SeedDevUsers": false,
      "DevUser": "local-operator",
      "HashChainAudit": true
    }
  }
}
```

`Posture` is a nullable `IdentityPosture` enum. Without an override, Development is `Open` and other environments are
`Closed`. Open posture may seed local people when `SeedDevUsers` is enabled; forcing Open outside Development refuses
startup. Invalid enum values fail standard .NET options binding.

## Add management HTTP APIs

Reference `Sylin.Koan.Identity.Web` to add subject-scoped self-service and role-gated operator APIs. No controller or
route registration is required. Add `Sylin.Koan.Identity.Tenancy` only when tenant membership and request resolution
are intended.

## Boundaries

- Identity persists and governs the person after authentication. `Sylin.Koan.Web.Auth` and its connectors establish
  the browser session; `Sylin.Koan.Web.Auth.Server` issues OAuth client tokens.
- Session revocation governs Koan cookie sessions. It does not revoke already-issued bearer tokens.
- Personal access tokens are not provided. Koan does not issue a credential unless a real authentication path accepts
  and enforces it.
- Group-based access is not provided. Use global `IdentityRole` or tenant `Membership.Roles`; a group model should
  return only with an effective-access consumer.
- Audit emission is best-effort after the domain mutation. Hash chaining detects tampering but does not make the
  underlying store append-only or deliver records to a SIEM.
- Core lifecycle deletion removes core-owned dependents and retains audit evidence; it is not a cross-provider
  transaction. Identity Tenancy owns tenant-seat deprovisioning.

See [TECHNICAL.md](TECHNICAL.md) and the public
[authentication guide](../../docs/guides/authentication-setup.md).
