---
type: REFERENCE
domain: identity
title: "Identity and isolation"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: authentication, authorization, external sign-in, tenancy, and classified Entity fields
---

# Identity and isolation

Use this pillar when the application must know who is acting, decide what that actor may do, or keep
Entity state separated by tenant or classification boundary.

## Capability map

| Need | First expression | Detail |
|---|---|---|
| Authenticate and issue an application session | reference an Auth provider and configure its credentials | [Authentication setup](../../guides/authentication-setup.md) |
| Apply the same access policy to REST and agent projections | `[Access(...)]` on the Entity or action | [Authentication setup](../../guides/authentication-setup.md) |
| Add Google, Microsoft, or Discord sign-in | reference the provider package and configure client credentials | provider package README |
| Test OAuth/OIDC deterministically | reference `Sylin.Koan.Web.Auth.Connector.Test` | [Test connector README](../../../src/Connectors/Web/Auth/Test/README.md) |
| Scope Entity work to one tenant | `using var scope = Tenant.Use(tenantId);` | [Tenancy](../../guides/tenancy-howto.md) |
| Classify and transform protected fields at rest | declare the supported classification attribute and provider | [Product surface](../product-surface.md) |

## Smallest authentication shape

```powershell
dotnet add package Sylin.Koan.Web.Auth
dotnet add package Sylin.Koan.Web.Auth.Connector.Google
```

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

The application retains its ordinary `AddKoan()` host. A complete provider definition becomes
eligible; incomplete configured intent rejects and names the missing fields. Referencing a connector
without credentials is inert.

## Govern an Entity surface

Use one access declaration for the Entity operations projected through HTTP and MCP:

```csharp
[Access(read: "authenticated", write: "owner", remove: "is:admin")]
public sealed class Memo : Entity<Memo>
{
    public string OwnerId { get; set; } = "";
    public string Body { get; set; } = "";
}
```

`EntityAccess<T>` owns row constraints and server-truth stamps when gate-only authorization is not
enough. Custom non-Entity controller actions continue to use ordinary ASP.NET Core authorization.

## Isolate tenant-owned work

Referencing `Sylin.Koan.Tenancy` contributes isolation to non-`[HostScoped]` Entities. Establish the
trusted tenant once for a logical flow:

```csharp
using (Tenant.Use(tenantId))
{
    await new Invoice { Amount = 100m }.Save(ct);
    var visible = await Invoice.All(ct);
}
```

Production posture fails closed when tenant-owned work has no tenant. Data filters and stamps, cache
keys, storage keys, and durable context carriage use the same ambient dimension. `[HostScoped]` is the
explicit global-data opt-out; it is not a cross-tenant bypass.

## Guarantee and correction

Identity owns authentication plans, callbacks, sessions, linking, token issuance, and lifecycle.
Access policy owns authorization at the applicable projection boundary. Tenancy contributes ambient
scope and managed-field isolation to participating capabilities; it is not authentication and does
not invent application membership or entitlement policy.

Provider availability is not proof of successful external authentication. Inspect startup facts and
the provider plan. Missing credentials, invalid authority, unavailable discovery, or an unsatisfied
access rule fails with a provider- or policy-specific correction rather than anonymous denial or
fallback to another identity source.

## Operate safely

- Keep secrets out of source and runtime facts.
- Use `/health/ready` for selected identity dependencies.
- Use `/.well-known/Koan/facts` or `koan://facts` to inspect redacted provider and policy decisions.
- Treat tenant scope as an application boundary that must be established before tenant-owned work.
- Use the Test connector only for deterministic local/integration behavior, never as a production
  identity provider.

## Security review path

| Review question | Canonical detail |
|---|---|
| How do users sign in and sessions begin? | [Authentication setup](../../guides/authentication-setup.md) |
| How are OAuth clients, PKCE, device flow, refresh, and signing keys handled? | [OAuth server](../../guides/oauth-server-howto.md) |
| Where are Entity access and tenant isolation enforced? | [Tenancy](../../guides/tenancy-howto.md) and this pillar's access contract |
| How are workload tokens issued and verified? | [Security Trust package](../../../src/Koan.Security.Trust/README.md) |
| What may an agent discover or invoke? | [Agents](../agents/index.md) |
| Which decisions are safe to inspect? | [Testing and operations](../operations/index.md) |

## Deeper contracts

- [Koan Identity package](../../../src/Koan.Identity/README.md)
- [Web Auth package](../../../src/Koan.Web.Auth/README.md)
- [Tenancy package](../../../src/Koan.Tenancy/README.md)
- [Current supported claims](../product-surface.md)
