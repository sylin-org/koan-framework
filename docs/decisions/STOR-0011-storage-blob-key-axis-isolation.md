---
id: STOR-0011
title: "Storage blob-key axis isolation — the data-axis model on the blob path"
status: Proposed
date: 2026-06-24
relates_to: [ARCH-0101, DATA-0105, DATA-0106, ARCH-0096, ARCH-0099, ARCH-0100, STOR-0001]
supersedes_design: docs/architecture/tenancy-storage-vector-isolation-design.md (§0.4)
---

# STOR-0011 — Storage blob-key axis isolation

> Redesign gap C, step 0.4. The blob path is the last framework surface where a registered data axis
> (tenant today; classification/region tomorrow) does **not** isolate. This ADR makes `Koan.Storage` a
> consumer of the **existing** data-axis seams — no new seam, no `Koan.Tenancy` dependency — so a tenant's
> blobs are physically isolated by a leading key particle and an unscoped write fails closed. The storage
> twin of the data core's managed-field isolation (ARCH-0101 / DATA-0106).

## Context

`StorageEntity<TEntity>` (`Onboard`/`Create*`/`OpenRead`/`Head`/`Delete`/`ReadAll*`/`CopyTo`/`MoveTo` + the
static key-first `OpenRead(key)`/`Head(key)`) resolves `(profile, container)` from `[StorageBinding]` and calls
`IStorageService.{op}(profile, container, key, …)` **directly** — it bypasses `RepositoryFacade`, so the
`__koan_tenant` managed field and the `IStorageGuard` chokepoint never see a blob op. Consequences:

- **No particle.** The physical blob key is `{profile}/{container}/{key}` with no axis segment
  (`StorageServiceHelpers.ToStorageKey` only normalises slashes). Two tenants `Onboard("photo.jpg")` write the
  **same** path; an unguarded `OpenRead("photo.jpg")` under any scope reads **any** tenant's blob.
  `StorageMediaController` serves blobs through these unguarded statics.
- **No guard.** A tenant-scoped blob write with no tenant in scope silently lands on a global path.

## Grounding (empirically re-verified 2026-06-24, this session)

The 2026-06-24 design canon proposed a **new** `IStorageKeyContributor` seam and left the layering open
("where does it live so `Koan.Tenancy` can register it — verify `Koan.Tenancy`→`Koan.Storage` first"). Grounding
**resolves it in favour of fewer parts** — the seams already exist:

- `Koan.Storage` **references `Koan.Data.Abstractions` and `Koan.Data.Core`** → it can read
  **`ManagedFieldRegistry`** (the same static registry `Koan.Tenancy` already populates with `__koan_tenant`)
  and resolve **`IStorageGuard`** (the generic, tenancy-agnostic `Guard(Type entityType)` chokepoint seam,
  `Koan.Data.Core.Pipeline`).
- `Koan.Tenancy` **already** registers `TenantStorageGuard : IStorageGuard` (HostScoped/`IAmbientExempt` exempt,
  posture-aware: Open→warn, Closed→throw, "names the fix") **and** the `__koan_tenant` `ManagedFieldDescriptor`
  (with the same `AppliesTo` exemption + a `ValueProvider`).
- `Koan.Tenancy` does **not** reference `Koan.Storage`, and there is no `Koan.Storage.Abstractions`.

So storage needs **no new seam and no new tenancy code** — it becomes the **third consumer** of
`ManagedFieldRegistry` (after the data-core read/write path and the cache scope-fold, gap B) and a **second
caller** of `IStorageGuard`. This is the golden-contributor outcome: a future Moderation/Region/classification
axis isolates blobs for free, the moment its descriptor + guard are registered.

## Decision

### 1. Key particle — reuse `ManagedFieldRegistry`, fold via the one composer (ARCH-0096)

At the storage boundary, compose the **logical** key with the applicable **equality** managed axes as **leading**
particles, through the one `IdentifierComposer` (ARCH-0096):

- Read `ManagedFieldRegistry.ForType(typeof(TEntity))`; for each descriptor with `AutoReadFilter == true` whose
  `ValueProvider()` yields a value, emit a `Particle(order: priorityIndex, axis: StorageName, value:
  <bareValue>, ParticlePosition.Leading, separator: "/")`. Compose: `IdentifierComposer.Compose(logicalKey,
  particles, policy("/", sanitizing-formatter))`. Result: `acme/photo.jpg` vs `globex/photo.jpg` — physical
  isolation; B's composed key can never address A's blob.
- **Bare value, not `axisKey=value`.** Storage keys are physical paths; the cache fold (gap B) namespaces with
  `name=value` for opaque equality, but storage wants the clean `acme/…` of the canon. (Single axis today; a
  second storage axis composes as `acme/us/…` ordered by axis — see Open decision D.)
- **`ManagedFieldRegistry.IsEmpty` ⇒ no particle ⇒ byte-identical** to today's key (`off = structurally absent`).

### 2. Fail-closed guard — reuse `IStorageGuard`

`StorageEntity<TEntity>` resolves `IEnumerable<IStorageGuard>` (DI, via `AppHost.Current`) and calls
`guard.Guard(typeof(TEntity))` before every blob op — exactly as `RepositoryFacade` does for data.
`TenantStorageGuard` already supplies HostScoped exemption + posture (Open warn / Closed throw / names the fix).
The particle is the primary isolation (composed keys differ); the guard stops the **unscoped** write to a global
path. No guard registered ⇒ empty loop ⇒ no-op.

### 3. The logical-key invariant (the double-prefix hazard)

**`StorageEntity<TEntity>.Key` always holds the LOGICAL key; composition happens ONLY at the
`IStorageService` boundary.** This is load-bearing: today `From(obj)` sets `se.Key = obj.Key`, and the service
returns the **composed** key — so without care a write would store the entity with `acme/photo.jpg`, and a
later read would compose **again** → `acme/acme/photo.jpg` (a write/read mismatch = silent not-found). The
write path therefore sets the returned entity's `.Key` from the caller's **logical** `name`, never the
service's physical key. TDD pins a write→read round-trip with no double prefix.

### 4. Structural injection — one chokepoint, not 17 scattered call-sites

`IStorageService` takes only strings (no entity type), so the type-aware compose+guard cannot live there;
`TEntity` is known only in `StorageEntity<TEntity>`. To avoid the "missed a site = a leak" trap (the exact
8-sites problem DATA-0106 fixed for the data path), funnel **all** `StorageEntity<TEntity>` ops through **one**
internal guard+compose step (`GuardAndScope(logicalKey) → physicalKey`) rather than editing each call-site
ad hoc. The `AssertNoStorageLeak<T>` proof (below) is the can't-miss-a-site safety net.

### Scope

This ADR is **0.4 (storage) only**. The vector half (**0.3**, Weaviate row-discriminator — inject the managed
discriminator into the vector object on Upsert + AND it into the Search `where`; extend
`FailClosedIfManagedScoped` to Upsert; a vector `Isolation.RowScoped`-style capability announce) is a **sibling
follow-on** — it needs Docker/Weaviate and is best designed **axis-generic alongside ARCH-0098 classification's
vector leak guard** to avoid a second pass. Tracked in the canon doc §0.3 and the ledger.

## Test plan (ARCH-0079, real `AddKoan()`, Local provider — no Docker)

A storage twin of `AssertNoTenantLeak` (axis-generic, mirroring `DataAxis.AssertNoLeak<T>`):

- Two tenants `Onboard("photo.jpg")` → **distinct** physical blobs; `acme`'s `OpenRead`/`Head`/`Delete` never
  reach `globex`'s blob (composed key differs).
- A write/read round-trip leaves `.Key` logical (no double prefix); `Head(logicalKey)` finds the just-written blob.
- A `[HostScoped]`/`IAmbientExempt` storage entity is **unprefixed** and shared (the exemption).
- Closed posture + tenant-scoped type + no tenant in scope → the blob op **fails closed**; Open posture → warns.
- `ManagedFieldRegistry.IsEmpty` (no axis module) ⇒ byte-identical key + no guard ⇒ existing storage suites green.

## Open decisions (resolve at design-panel / impl)

- **A. Particle value sanitization.** A tenant id as a path segment — use a sanitizing `IParticleFormatter` (the
  storage-name token policy) so a value can't inject `/` or `..`? Tenant ids are GUID/surrogate (validated), but
  bias-to-strict argues for sanitization at the boundary.
- **B. Compose at the `StorageEntity<T>` funnel vs a type-carrying overload of `IStorageService`.** The funnel
  (decision 4) keeps `IStorageService` unchanged; a type-aware service overload would be the truer chokepoint but
  a broad signature change across every provider. Lean funnel.
- **C. `CopyTo`/`MoveTo`/`TransferToProfile`** compose **both** source and target keys; confirm cross-scope
  transfer semantics (can A copy into B? — the guard should forbid an unscoped/foreign target).
- **D. Multi-axis storage ordering** — bare leading values ordered by axis ordinal (`acme/us/…`); a future second
  storage axis with an equal value could collide unless namespaced. Single axis today; revisit when a second ships.
- **E. `StorageMediaController` / `MediaContentController`** serve blobs through the static key-first helpers —
  confirm those paths also fail closed (they are the exposure surface).

## Consequences

- **Positive:** zero new seam, zero new tenancy code, no new layering edge; storage joins the data core + cache
  as a uniform `ManagedFieldRegistry` consumer; any future axis isolates blobs by registration alone; byte-identical
  when no axis is registered.
- **Cost:** the logical-key invariant (decision 3) is subtle and must be covered by the round-trip proof; the
  funnel touches `StorageEntity<TEntity>`'s op surface.
- **Risk:** a missed op-site = a silent isolation leak — mitigated by the one-funnel structure + `AssertNoStorageLeak<T>`.
