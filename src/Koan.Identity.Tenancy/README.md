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

The bridge also contributes automatically to `IdentityLifecycleService.EraseAsync`. Whole-person erasure removes
memberships and tenant-scoped `AgentGrant` rows for registered tenants, then de-identifies matching deprovisioning
receipts and tenancy audit summaries. The final identity-erasure receipt reports this package as
`Koan.Identity.Tenancy`; no extra registration is required.

## Invitations (PMC-035)

`InviteIssuanceService` issues a tenant seat to one email address and returns the raw token **once** — only its
SHA-256 hash is stored. `InviteAcceptanceService.AcceptAsync(token, identityId)` runs the claim: the signed-in person
must own a **verified** `IdentityEmail` matching the invitation, and the claim itself is a conditional write on the
invitation row (`Status == Pending && ClaimedBy == null`). Two identities racing one token — on one host or across a
fleet — converge to one claimant and one deterministic `Membership` seat; a claimant whose run was interrupted
re-drives the same token idempotently until they complete or an operator revokes. Issuance, revocation, and
acceptance are audited through the tenant audit log. Reserved host roles (`koan:tenancy-operator`) can never travel
through an invitation.

```csharp
var issued = await issuance.IssueAsync(actor, tenantId, "ada@example.com", "editor");

// the invited person, signed in and holding a verified IdentityEmail for that address:
var result = await acceptance.AcceptAsync(issued.Token, identityId);
// result.Outcome: Accepted | AlreadyMember | AlreadyClaimed | NotFound | Expired | Revoked |
//                 EmailNotOwned | ReservedRoleRefused — reported, never thrown (except the
//                 conditional-write guarantee: an adapter without write.conditionalReplace refuses at boot of the ceremony).
```

Over HTTP, `Koan.Tenancy.Web` exposes the ceremony: `POST /api/tenancy/invitations` (issue, operator policy),
`POST /api/tenancy/invitations/{id}/revoke` (operator policy), and `POST /api/tenancy/invitations/accept`
(any authenticated subject).

## Boundaries

- The bridge scopes inbound ASP.NET Core requests. Background work must establish its tenant through the normal
  captured/explicit Koan context rather than copying a raw header or path value.
- Membership roles cannot project Koan host-operator roles, and invitations cannot grant them either.
- Already-issued bearer tokens remain governed by their issuer outside tenant scope; this package does not revoke
  OAuth tokens or claim global authorization closure.
- Public/anonymous tenant routing is not a switch on this security boundary and is not currently provided.
- Tenant suspension and custom-domain ownership verification are not enforced here.
- Invitation delivery (email send) is the application's job; this package owns the token, the claim, and the seat.

See [TECHNICAL.md](TECHNICAL.md) and the public [tenancy guide](../../docs/guides/tenancy-howto.md).
