# Sylin.Koan.Tenancy.Web

A reference-mounted operator UI and API for the supported Tenancy control plane: tenant registry, membership seats,
and administration audit.

## Install

```powershell
dotnet add package Sylin.Koan.Tenancy.Web
```

Keep the existing bootstrap:

```csharp
builder.Services.AddKoan();
```

The reference adds controllers and the embedded UI automatically. Start the application and open `/tenancy`; the
operator API is mounted at `/api/tenancy/admin`.

## Meaningful use

An admitted operator can:

- list, inspect, create, and rename tenant registry rows;
- grant or replace a known subject's tenant roles through one deterministic membership seat;
- revoke a membership; and
- inspect the newest administration audit entries.

Equivalent grants converge to `Membership.KeyFor(tenantId, identityId)`. Reserved host operator roles are rejected
before the seat is saved. The administration service records every completed supported mutation.

## Exposure and authority

Exposure answers where the surface exists; authority answers who can operate it. They are deliberately separate:

```json
{
  "Koan": {
    "Tenancy": {
      "Console": {
        "Exposure": {
          "Hosts": [ "ops.example.com" ],
          "RequireHeader": "X-Koan-Console"
        },
        "Grant": {
          "Operators": [ "operator-subject-id" ],
          "Role": "koan:tenancy-operator"
        }
      }
    }
  }
}
```

A failed exposure condition returns 404 before routing. It is never authority. Closed posture requires the configured
operator identity or host role and returns 403 otherwise. Development is open by default; set
`RequireLoopbackForOpenPosture` when a development host can receive remote traffic.

Startup reporting shows the routes, resolved posture, exposure conditions, configured operator count, and audit page
size. `AuditPageSize` defaults to 100 and `MaxAuditPageSize` to 500.

## Boundaries

- `IdentityId` is a subject key. This package does not reference or validate an Identity provider; the optional
  Identity Tenancy bridge enforces active durable Identity when a membership is used for request scope.
- Invitation/delivery/acceptance, tenant suspension, product-data erase, operations jobs, and server-side act-as are
  intentionally absent until they can provide complete guarantees.
- Registry and audit writes are ordered Entity operations, not a cross-provider transaction or immutable ledger.
- Direct writes to the underlying control-plane Entities bypass the administration service's validation and audit.

See [TECHNICAL.md](TECHNICAL.md) and the public [tenancy guide](../../docs/guides/tenancy-howto.md).
