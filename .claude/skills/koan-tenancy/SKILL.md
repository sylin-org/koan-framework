---
name: koan-tenancy
description: Automatic, fail-closed multi-tenant isolation — reference Koan.Tenancy and every non-[HostScoped] Entity<T> is isolated by the ambient tenant across data reads/writes, blob storage, and cache; Tenant.Use/None/Current scoping, [HostScoped]/IAmbientExempt exemption, dev-open/prod-closed posture, and the DataAxis.AssertNoLeak proof
pillar: tenancy
card: docs/reference/cards/tenancy.md
status: current
last_validated: 2026-06-24
---

# Koan Tenancy

## Trigger this skill when you see

- `Tenant.Use("...")` / `Tenant.WithTenant(...)` / `Tenant.None()` / `Tenant.Current`
- `[HostScoped]` on an entity, or `IAmbientExempt` on an infrastructure entity
- References to `Koan.Tenancy`, `TenantContext`, `TenancyPosture`, `ITenantResolver`, the `__koan_tenant` field
- `Koan:Data:Tenancy:Posture` (`Open`/`Closed`) in config; "dev-open / prod-closed"; "fails closed"
- `DataAxis.AssertNoLeak<T>(...)` / `AssertNoTenantLeak`
- Plain-English: "multi-tenant", "tenant isolation", "per-customer data", "cross-tenant leak", "act as tenant",
  "every query needs a tenant filter", "prefix blobs by tenant", "which tenant am I in"

## Core principle

**One dial, not a thousand `WHERE tenant = ?`.** Referencing `Koan.Tenancy` makes every non-`[HostScoped]`
`Entity<T>` **tenant-isolated by construction** — the framework injects an *invisible* `__koan_tenant` discriminator
(never a POCO property) and a registered contributor (the data-axis model, ARCH-0101) stamps it on write, pushes a
`__koan_tenant == <ambient>` filter on read, prefixes blob keys, and segments cache keys — all by the **ambient**
tenant set with `Tenant.Use(id)`. There is no tenant column on your model and no filter in your queries. Strictness
is **posture**, not a flag: **Open** in Development (a missing tenant warns; the dev tenant stands in), **Closed** in
Production (a tenant-scoped op with no tenant in scope **fails closed** — a fail-fast, never a silent leak). Cross-
tenant work is the audited exception: `[HostScoped]` rows under `Tenant.None()`.

<!-- validate -->
```csharp
using System.Threading.Tasks;
using Koan.Data.Core.Model;
using Koan.Tenancy;

// Reference Koan.Tenancy ⇒ every non-[HostScoped] entity is isolated by the ambient tenant (data + cache + blobs).
public sealed class Invoice : Entity<Invoice>
{
    public decimal Amount { get; set; }
    public string Customer { get; set; } = "";
}

[HostScoped]                                            // global / control-plane — the one audited opt-out
public sealed class FeatureFlag : Entity<FeatureFlag>
{
    public bool Enabled { get; set; }
}

public sealed class Billing
{
    public async Task ChargeAcme()
    {
        using (Tenant.Use("acme"))                      // scope: admin · jobs · tests · support act-as
        {
            await new Invoice { Amount = 100m, Customer = "Acme Corp" }.Save();  // stamped to "acme"
            var theirs = await Invoice.All();           // only "acme" invoices
            var one = await Invoice.Get("some-id");     // null if it belongs to another tenant (IDOR-safe)
            _ = (theirs, one);
        }

        using (Tenant.None())                           // explicit host scope — only [HostScoped] entities
        {
            var flags = await FeatureFlag.All();        // global config
            _ = flags;
        }

        var current = Tenant.Current?.Id;               // the ambient tenant id, or null when unscoped
        _ = current;
    }
}
```

## Reference = Intent activation

| Add this reference | Effect |
|---|---|
| `Koan.Tenancy` | Registers the `__koan_tenant` discriminator + the fail-closed guard + read-filter + blob-key particle + cache-key segment + the async-hop carrier. Every non-`[HostScoped]` entity is now isolated. |
| (already present) `Koan.Storage` | Blob keys gain a leading tenant particle at the `IStorageService` chokepoint (STOR-0011) — `StorageEntity`/`MediaEntity`/presign/raw all covered. |
| (already present) `Koan.Cache` | `[Cacheable]` entities partition their cache key by tenant; out-of-band `entity.Cache.Evict()` consumes the same captured policy/key/scope plan. |
| (already present) `Koan.Jobs` | The ambient tenant rides the async-hop (ARCH-0100): a job executes in the tenant it was submitted under. |

Posture is environment-derived (Development → `Open`, else → `Closed`); override only to test the strict path:

```jsonc
// appsettings.json
"Koan": { "Data": { "Tenancy": { "Posture": "Closed" } } }   // Open | Closed
```

## The primitives you'll use

| Primitive | What it does |
|---|---|
| `Tenant.Use(id)` / `Tenant.WithTenant(id)` | Scope subsequent ops to a tenant for the `using` lifetime (async-flow). |
| `Tenant.None()` | Explicit host scope — touches only `[HostScoped]`; a tenant-scoped write still fails closed. |
| `Tenant.Current` | The ambient `TenantContext?` (null when unscoped). |
| `[HostScoped]` | Exempt an entity type (global config, system rows). `IAmbientExempt` = the dependency-free infra equivalent. |
| `DataAxis.AssertNoLeak<T>(Tenant.Use, "a", "b")` | The framework-shipped isolation proof (read · IDOR · write-takeover · scoped delete · cache · async-hop). |

## Steering — retrofitting tenancy onto an app

- **Per-request scope is the app's job (for now).** Concrete `ITenantResolver` strategies (claim/host/header) are a
  later slice; today add middleware that opens `Tenant.Use(resolvedId)` around the request. The trusted signal is a
  security decision — prefer a verified claim over a raw header.
- **Mark global rows `[HostScoped]` BEFORE turning tenancy on** — startup seeders and reference data have no tenant
  in scope and will **fail closed** under Closed posture otherwise.
- **Background workers / startup tasks have no request** — they must establish their own `Tenant.Use(...)` (e.g.
  iterate tenants) or operate only on `[HostScoped]`/`IAmbientExempt` data.
- **Need an isolating store** — SQLite/PG/SqlServer/Mongo announce `Isolation.RowScoped`; the JSON file adapter does
  not and a tenant-scoped op there fails closed.

## Anti-patterns to flag

| If you see | Suggest |
|---|---|
| A `TenantId` property on a domain `Entity<T>` + manual `.Where(x => x.TenantId == current)` | Delete it. Reference `Koan.Tenancy`; the invisible `__koan_tenant` field + the read-filter isolate automatically. |
| Threading a `tenantId` parameter through service/repository methods | Set the ambient once with `Tenant.Use(id)`; ops inside the scope are isolated — no parameter passing. |
| Manually prefixing a blob key / cache key with the tenant | Automatic — STOR-0011 blob particle + the cache-key segment. Hand-prefixing double-prefixes or diverges. |
| A global config / reference entity with no `[HostScoped]` (seeded at startup) | Add `[HostScoped]` — otherwise startup seeding fails closed under Closed posture. |
| `Tenant.None()` to "see everything" in business code | Host scope only reaches `[HostScoped]` rows; it is the audited platform-maintenance escape, not a cross-tenant read. |
| Assuming `Tenant.Provision` / membership / roles / a claim-resolver exist | Those are **designed, not built** (later slices). Today: scoping + isolation + posture only. |
| A tenant-scoped op on the JSON adapter expecting it to "just work" | It fails closed (no `Isolation.RowScoped`). Use an isolating store. |

## See also

- [Reference card: tenancy.md](../../../docs/reference/cards/tenancy.md) — one-screen pillar map
- [Multi-Tenancy How-To](../../../docs/guides/tenancy-howto.md) — Concept → Recipe → Sample for each surface + the roadmap
- [ARCH-0095 — tenancy](../../../docs/decisions/ARCH-0095-tenancy.md) · [ARCH-0099 — tenancy realignment](../../../docs/decisions/ARCH-0099-tenancy-realignment.md)
- [ARCH-0101 — the data-axis model](../../../docs/decisions/ARCH-0101-data-axis-model.md) (how isolation is a registered contributor)
- [STOR-0011 — storage blob-key axis isolation](../../../docs/decisions/STOR-0011-storage-blob-key-axis-isolation.md) · [ARCH-0100 — durable ambient carrier](../../../docs/decisions/ARCH-0100-durable-ambient-carrier.md)
- Proof: [`tests/Suites/Tenancy/Koan.Tenancy.Tests`](../../../tests/Suites/Tenancy/Koan.Tenancy.Tests) — `AssertNoTenantLeakSpec` + `StorageTenantIsolationSpec`
