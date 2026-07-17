---
type: GUIDE
domain: tenancy
title: "Multi-Tenancy How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-06-24
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-24
  status: verified
  scope: Koan.Tenancy.Tests — AssertNoTenantLeakSpec + StorageTenantIsolationSpec (real AddKoan, SQLite + Local storage)
related_guides:
  - jobs-howto.md
  - framework-utilities.md
---

# Multi-Tenancy How-To

Think of tenancy in Koan as a single dial you turn on once. You don't thread a `tenantId` through every method,
add a `WHERE tenant = ?` to every query, or prefix every blob path by hand. You **reference `Koan.Tenancy`**, and
every entity that isn't explicitly global becomes isolated — its reads filter, its writes stamp, its blobs and
cache entries partition — all by the *ambient* tenant. The framework does this as a **registered contributor** over
the data-axis model (ARCH-0101): one invisible discriminator, applied at the data chokepoint, the blob chokepoint,
and the cache key, with the data core never naming "tenant". This guide shows the surface that exists **today** and
flags what is designed-but-not-yet-built at the end.

---

## 0. Prerequisites

Tenancy is **Reference = Intent**. Add the package; its `KoanModule` contributes the discriminator,
fail-closed guard, read-filter, blob-key particle, cache-key segment, and async-hop carrier. No
`AddTenancy()` or activation middleware is required.

```xml
<ProjectReference Include="..\..\src\Koan.Tenancy\Koan.Tenancy.csproj" />  <!-- or the Sylin.Koan.Tenancy package -->
```

Tenancy needs a backing store that **announces isolation** (`DataCaps.Isolation.RowScoped`) — SQLite, PostgreSQL,
SQL Server, Mongo. A non-isolating store (the JSON file adapter) **fails closed** for a tenant-scoped op rather than
leak. (ARCH-0095, ARCH-0099.)

---

## 1. Your first tenant-isolated entity

**Concept.** An ordinary `Entity<T>` is automatically tenant-scoped once `Koan.Tenancy` is referenced. There is no
tenant property on your model — the discriminator (`__koan_tenant`) is *invisible*, injected and filtered by the
framework, so your entity stays a pure description of your domain.

**Recipe.** Write the entity exactly as you would without tenancy.

**Sample.**

```csharp
public sealed class Invoice : Entity<Invoice>
{
    public decimal Amount { get; set; }
    public string Customer { get; set; } = "";
}
```

**When to use it.** Every per-customer / per-workspace / per-org row: invoices, documents, photos, settings owned by
a tenant. This is the default — you opt *out*, not in.

---

## 2. Marking global rows `[HostScoped]`

**Concept.** Some rows belong to the *platform*, not a tenant: feature flags, plan definitions, seeded reference
data, system config. Mark them `[HostScoped]` and they are exempt — visible and writable across all tenants, never
stamped, never filtered.

**Recipe.** Add `[HostScoped]`. For framework/infrastructure entities that must not take a `Koan.Tenancy`
dependency (e.g. a job ledger), implement the dependency-free `IAmbientExempt` marker instead — it earns the same
exemption.

**Sample.**

```csharp
[HostScoped]
public sealed class FeatureFlag : Entity<FeatureFlag>
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; }
}
```

**When to use it.** Anything genuinely cross-tenant. Be deliberate: `[HostScoped]` is the audited opt-out — a
mislabelled tenant row here is a cross-tenant leak. If you seed reference data at startup (no tenant in scope), it
**must** be `[HostScoped]`, or the seed fails closed under Closed posture.

---

## 3. Scoping an operation

**Concept.** A request, job, test, or admin task establishes the *ambient* tenant for a block of work. Everything
inside the scope — reads, writes, blob ops, cache — is isolated to that tenant. The scope flows across `await`
(it's async-local) and disposing restores the previous ambient.

**Recipe.** Wrap the work in `using (Tenant.Use(id)) { ... }`. Use `Tenant.None()` for explicit host-scope work and
`Tenant.Current` to read the ambient tenant.

**Sample.**

```csharp
using (Tenant.Use("acme"))
{
    await new Invoice { Amount = 100m, Customer = "Acme Corp" }.Save();  // stamped to "acme"
    var theirs = await Invoice.All();                                    // only "acme" invoices
    var one = await Invoice.Get(someId);                                 // null if it belongs to another tenant (IDOR-safe)
}

using (Tenant.None())                       // explicit host scope — touches only [HostScoped] entities
{
    var flags = await FeatureFlag.All();    // global config
}

var who = Tenant.Current?.Id;               // the ambient tenant id, or null when unscoped
```

**When to use it.** `Tenant.Use` for admin/support "act-as", background jobs, integration tests, and any code path
that establishes the tenant from a trusted signal. `Tenant.None` for platform maintenance. In a web request, set the
scope once per request (§7).

---

## 4. Posture — dev-open, prod-closed

**Concept.** Strictness is a property of the *environment*, not a code flag. **Open** (Development) lets a developer
land in a working control plane: a tenant-scoped op with no tenant in scope is *warned* and the auto-seeded **dev
tenant** stands in. **Closed** (Production / ambiguous) **fails closed**: a tenant-scoped op with no tenant in scope
throws a diagnostic that names the fix, rather than silently writing to a global namespace.

**Recipe.** Do nothing — the posture defaults from `IHostEnvironment` (Development → Open, else → Closed). Override
only to test the strict path locally:

```jsonc
// appsettings.json
"Koan": { "Data": { "Tenancy": { "Posture": "Closed" } } }   // Open | Closed
```

**When to use it.** Leave it on defaults in normal development and production. Set `Closed` in a test to prove
fail-closed behaviour; the dev-open auto-seed is for local DX only and is never made portable across the async-hop.

> **Production boot pre-flight:** tenancy active in Production with **no registered `ITenantResolver`** refuses to
> boot (every request would otherwise fail closed at the gate — a fail-fast, not a silent leak). In Development the
> dev tenant stands in, so a resolver is not required.

---

## 5. What gets isolated (and how)

**Concept.** Isolation feels uniform because one `Tenant.Use(...)` scope is interpreted at each pillar's own
chokepoint. The responsibilities stay separate: Data owns row policy, Storage owns blob keys, Cache owns cache
identity, and Core-owned context carriage lets Jobs preserve the scope across a durable hop.

| Plane | What happens | Reference |
|---|---|---|
| **Write** | The invisible `__koan_tenant` field is stamped at the data chokepoint; a cross-tenant upsert-by-id is rejected (no row takeover). | DATA-0105 |
| **Read** | A `__koan_tenant == <ambient>` equality filter is pushed down on every read; get-by-id returns null across tenants (IDOR-safe). | DATA-0106 |
| **Blob storage** | `StorageEntity`/`MediaEntity` blob keys gain a leading tenant particle (`acme/photo.jpg`); the chokepoint is the `IStorageService` decorator, so the media/presign/raw paths are covered too. | STOR-0011 |
| **Cache** | A `[Cacheable]` entity's cache key gains a tenant segment, so one tenant's cached row is never served to another; `entity.Cache.Evict()` captures the same tenant context and consumes the repository's policy/key plan. | DATA-0106 §5 + the cache scope-key convergence |
| **Async-hop** | The Core context carrier captures the tenant at job submit and Jobs restores it before loading the work item, so execution stays in the submitter's tenant. | R07-01 amendment to ARCH-0100 |

**Recipe.** Nothing — this is automatic. Just be aware that a non-isolating adapter **fails closed** and that a
**non-equality** axis (a future moderation/visibility predicate) excludes its entity from the cache and refuses a
blob-key (a physical path is equality-by-construction).

**When to use it.** Always on. The single thing to verify per entity is the `[HostScoped]` decision (§2).

---

## 6. Tenancy and background jobs

**Concept.** A job submitted under a tenant must *execute* under that tenant — even though it runs later, on another
thread or node. The tenant module independently registers a Core context carrier: Jobs captures it into the durable
record at submit and restores it before loading the work item or calling `Execute`. A job submitted with **no** tenant carries nothing and is governed by the same
posture (Closed → the handler's first tenant-scoped op fails closed; Open → dev-fallback).

**Recipe.** Submit jobs as usual under `Tenant.Use(...)`. The framework threads the scope; you write no carrier code.

**Sample.**

```csharp
using (Tenant.Use("acme"))
    await new SendInvoiceEmail { InvoiceId = id }.Job.Submit(); // executes later, still scoped to "acme"
```

**When to use it.** Any per-tenant async work (emails, exports, derivations). See the [Jobs how-to](jobs-howto.md).

---

## 7. Wiring a tenant per web request

**Concept.** The framework resolves the ambient tenant from a **trusted signal** per request. Concrete resolver
strategies (verified claim, host/domain, authorized header) are a **later slice** — today you establish the scope
yourself with middleware, using the `Tenant.Use` primitive. This is the app's responsibility because "what is the
trusted tenant signal" is an app/security decision.

**Recipe.** Add small middleware early in the pipeline that opens a `Tenant.Use(...)` scope around the request and
disposes it after. Resolve the id from whatever your app trusts (a verified claim in real systems; a header for a
demo). Leave global config entities `[HostScoped]` so startup seeding (which has no tenant) still works.

**Sample.**

```csharp
// Program.cs — after auth, before routing. A demo header resolver; production uses a verified claim/host.
app.Use(async (ctx, next) =>
{
    var tenant = ctx.User.FindFirst("tenant")?.Value
                 ?? ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(tenant)) { await next(); return; }  // unscoped → posture decides (dev-seed / fail-closed)
    using (Tenant.Use(tenant))
        await next();
});
```

**When to use it.** Every multi-tenant web app. Two cautions when retrofitting tenancy onto an existing app: (1)
mark genuinely-global entities `[HostScoped]` (otherwise startup seeders fail closed under Closed posture); (2)
background workers and startup tasks have **no request** — they must establish their own `Tenant.Use(...)` (e.g.
iterate tenants) or operate on `[HostScoped]`/`IAmbientExempt` data.

---

## 8. Proving isolation

**Concept.** Isolation is a security boundary, and its failure mode is silent. Koan ships an **executable proof** —
`DataAxis.AssertNoLeak<T>` (ARCH-0101 §10) — that drives the whole matrix (read · get-by-id IDOR · cross-scope
write-takeover · scoped delete · cache-key · async-hop) for any value-isolation axis and throws on the first leak.

**Recipe.** Call it in an integration test through a real `AddKoan()` boot, passing the scope-enter verb.

**Sample.**

```csharp
await DataAxis.AssertNoLeak<Invoice, string>(Tenant.Use, "acme", "globex");   // returns if isolated; throws on a leak
```

**When to use it.** For every tenant-scoped entity family you ship — it is the regression tripwire that a future
change (a new read surface, a new write path) hasn't punched a hole. Blob isolation has its own real-boot proof
(`StorageTenantIsolationSpec`).

---

## Roadmap — designed, not yet built

This guide documents what is **implemented today**: the scoping surface (`Tenant.Use`/`None`/`Current`),
`[HostScoped]`/`IAmbientExempt`, posture + dev-seed, and automatic isolation across data, blobs, cache, and the
job async-hop. The following are **designed** (ARCH-0095 / ARCH-0099 / the tenancy design canon) but **not yet
shipped** — do not assume them in code:

- **Lifecycle verbs** — `Tenant.Provision` / `Relocate` / `Erase` / `Rename`, and the rich current-tenant projection
  (`{ Id, Codes, Name }`) backed by a tenant registry.
- **Membership & roles** — per-tenant membership, roles-on-membership, the gate-above-roles model.
- **Concrete `ITenantResolver` strategies** — claim / host / authorized-header request resolution (today the seam
  is a marker the production boot pre-flight checks; you wire the scope yourself, §7).
- **Data-classification & residency** — `[Pii]`/`[Phi]` posture, cryptographic erasure certificate, region pinning.

---

## Related guides

- [Background Jobs How-To](jobs-howto.md) — the async-hop carrier in action.
- [Framework Utilities Guide](framework-utilities.md) — `[HostScoped]`, guards, and the contributor seams.
- ADRs: [ARCH-0095](../decisions/ARCH-0095-tenancy.md) · [ARCH-0099](../decisions/ARCH-0099-tenancy-realignment.md) ·
  [ARCH-0101](../decisions/ARCH-0101-data-axis-model.md) · [STOR-0011](../decisions/STOR-0011-storage-blob-key-axis-isolation.md) ·
  [ARCH-0100](../decisions/ARCH-0100-durable-ambient-carrier.md).
