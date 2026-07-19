---
type: REF
domain: tenancy
title: "Tenancy — pillar map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-18
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-18
  status: verified
  scope: Koan.Tenancy.Tests 87/87 + Koan.Tenancy.Web.Tests 13/13 + Koan.Identity.Tests 85/85
---

# Tenancy — pillar map

> One-screen map of the Tenancy pillar — automatic, fail-closed multi-tenant isolation across data, cache, and
> blobs from a single ambient scope. Full detail: [tenancy how-to](../../guides/tenancy-howto.md).

**What it does** — Reference the `Sylin.Koan.Tenancy` package and every non-`[HostScoped]` entity becomes **tenant-isolated by
construction**: reads filter, writes stamp, blob keys and cache keys partition — all by the *ambient* tenant, with
**no per-entity code** (Reference = Intent). Isolation is a **registered contributor** over the data-axis model
([ARCH-0101](../../decisions/ARCH-0101-data-axis-model.md)): the framework injects an invisible `__koan_tenant`
discriminator (never a POCO property), a read-filter, a write-verify guard, a blob-key particle
([STOR-0011](../../decisions/STOR-0011-storage-blob-key-axis-isolation.md)), and a cache-key segment — the data
core never names "tenant". Strictness is set by **posture**, not a flag: **Open** in Development (a missing tenant
is warned, the dev tenant stands in), **Closed** in Production (a tenant-scoped op with no tenant in scope **fails
closed** — a fail-fast, never a silent leak). ([ARCH-0095](../../decisions/ARCH-0095-tenancy.md),
[ARCH-0099](../../decisions/ARCH-0099-tenancy-realignment.md).)

## The one canonical pattern

Add the `Koan.Tenancy` reference. Mark global rows `[HostScoped]`. Scope an operation with `Tenant.Use(id)`.

```csharp
public sealed class Invoice : Entity<Invoice>      // tenant-scoped: reads/writes/blobs/cache isolated by the ambient tenant
{
    public decimal Amount { get; set; }
}

[HostScoped]                                        // global / control-plane — the one audited opt-out
public sealed class FeatureFlag : Entity<FeatureFlag> { public bool Enabled { get; set; } }

using (Tenant.Use("acme"))                          // trusted host work · jobs · tests
{
    await new Invoice { Amount = 100m }.Save();     // stamped + isolated to "acme"
    var mine = await Invoice.All();                 // sees only "acme" invoices
}
```

## The ≤5 primitives you'll use

| Primitive | What it does |
|---|---|
| `Tenant.Use(id)` / `Tenant.WithTenant(id)` | Scope subsequent ops to a tenant for the `using` lifetime (ambient, async-flow). |
| `Tenant.None()` | Enter explicit **host scope** — touches only `[HostScoped]` entities; a tenant-scoped write still fails closed. |
| `Tenant.Current` | The ambient `TenantContext?` (null when unscoped). Read-only. |
| `[HostScoped]` | Exempt an entity type from tenancy (global config, system rows). `IAmbientExempt` is the dependency-free equivalent for infra. |
| `Koan:Tenancy:Posture` | `Open` (Development local fallback) / `Closed` (fail-closed). Defaults per environment. |

## The escape hatch

Cross-tenant work is **explicit and audited** — `Tenant.None()` (host scope) for `[HostScoped]` rows, or a job/admin
path that iterates tenants under `Tenant.Use(...)`. The ambient tenant rides the async-hop into `Koan.Jobs`
([ARCH-0100](../../decisions/ARCH-0100-durable-ambient-carrier.md)), so a job runs in the tenant it was submitted
under. Prove isolation for any entity with the framework-shipped `DataAxis.AssertNoLeak<T>(Tenant.Use, "a", "b")`.

## The sample that shows it

No dedicated tenancy sample yet — the **executable proof** is
[`tests/Suites/Tenancy/Koan.Tenancy.Tests`](../../../tests/Suites/Tenancy/Koan.Tenancy.Tests): `AssertNoTenantLeakSpec`
(read · get-by-id IDOR · write-takeover · scoped delete · cache-key) and `StorageTenantIsolationSpec` (blob isolation
across `StorageEntity`/`MediaEntity`/raw `IStorageService`), both through a real `AddKoan()` boot.

> **Built today:** scope + hard segmentation across active pillars, local Development fallback, durable tenant and
> membership rows, active-Identity request carriers, and the optional registry/membership operator projection.
> Invitation, tenant lifecycle/data erasure, verified domains, and server-side act-as remain outside the current
> contract. See the how-to's boundary section.
