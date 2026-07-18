# Sylin.Koan.Identity.Tenancy

Koan's durable Identity × Tenancy bridge. Reference it when an authenticated person should enter a tenant only through
a current membership. The package composes request scoping, tenant-role projection, access explanation, and lifecycle
closure through the application's existing `AddKoan()`—there is no bridge-specific setup call.

## Install

```powershell
dotnet add package Sylin.Koan.Identity.Tenancy
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The application also needs its ordinary Web Auth connector and a Data provider. Identity and Tenancy arrive through
the bridge package; add them directly only when application code intentionally uses their public types.

## Meaningful behavior

Create a tenant seat as business data:

```csharp
await new Membership
{
    TenantId = tenant.Id,
    IdentityId = person.Id,
    Roles = { "orders:review" }
}.Save();
```

An authenticated request can now select that tenant by the default `tenant` claim, `X-Koan-Tenant` header, or
`/t/{tenantCode}` path. The bridge verifies the durable person is active and the membership still exists, establishes
`Tenant.Current`, and projects only that membership's tenant roles for the rest of the request. A forged carrier,
anonymous caller, inactive person, or removed seat proceeds unscoped; tenant-managed operations then fail closed.

Subdomain routing is available but inert until the application's base hosts are configured.

## Configuration

```jsonc
{
  "Koan": {
    "Data": {
      "Tenancy": {
        "Resolution": {
          "ClaimType": "tenant",
          "HeaderName": "X-Koan-Tenant",
          "PathPrefix": "/t/",
          "BaseHosts": [ "app.example.com" ]
        }
      }
    }
  }
}
```

Carrier order is claim, header, subdomain, then path; the first resolved candidate wins. Every carrier always requires
an active durable member. Invalid empty carrier settings fail host startup through standard .NET options validation.
Startup reporting lists the effective carriers and says whether subdomain routing is live.

## Lifecycle closure

`DeprovisioningService.RemoveFromTenantAsync(personId, tenantId)` removes one seat. `DeactivateAsync(personId)` marks
the person deactivated first, revokes all Koan cookie sessions, then removes every tenant seat. Both return a
`DeprovisioningReceipt` whose content hash can detect later changes with `HasValidHash()`.

These operations are ordered, idempotent Entity writes—not a cross-provider transaction. A receipt is emitted only
after the requested workflow completes and attests only to its own recorded fields.

## Boundaries

- The bridge scopes inbound ASP.NET Core requests. Background work must establish its tenant through the normal
  captured/explicit Koan context rather than copying a raw header or path value.
- Membership roles cannot project Koan host-operator roles.
- Already-issued bearer tokens remain governed by their issuer outside tenant scope; this package does not revoke
  OAuth tokens or claim global authorization closure.
- Public/anonymous tenant routing is not a switch on this security boundary and is not currently provided.
- Tenant suspension and custom-domain ownership verification are not enforced here.
- `Invite` is a Tenancy control-plane record, but Koan V1 does not currently provide an invitation-acceptance ceremony.
  Applications should not treat a check-then-save flow as a distributed single-use claim.

See [TECHNICAL.md](TECHNICAL.md) and the public [tenancy guide](../../docs/guides/tenancy-howto.md).
