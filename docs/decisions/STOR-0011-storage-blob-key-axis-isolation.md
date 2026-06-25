---
id: STOR-0011
title: "Storage blob-key axis isolation — the data-axis model on the blob path"
status: Accepted
date: 2026-06-24
relates_to: [ARCH-0101, DATA-0105, DATA-0106, ARCH-0096, ARCH-0099, ARCH-0100, STOR-0001]
supersedes_design: docs/architecture/tenancy-storage-vector-isolation-design.md (§0.4)
revision: v2 — chokepoint relocated to IStorageService after design review wf_ac5a1e07-54a; implemented + impl-diff reviewed (wf_03ef19e6-88c)
---

# STOR-0011 — Storage blob-key axis isolation

> **Status: implemented + proven (`dev`, commits `9cff2a45`/`a7bb9029`/`9a1b89e3`/`a22c5e5e`).** The
> `ScopedStorageService` decorator, `StorageScope`/`StorageKeyScoper`/`StorageKeyParticleFormatter`, the
> `StorageEntity`/`MediaEntity` wiring, and the `StorageObjectExtensions` scope all ship. Proof:
> `StorageTenantIsolationSpec` (8 real-boot cases). Off = byte-identical (Storage Core 3, Media Core 567).
> **Impl-diff review `wf_03ef19e6-88c` (18/21 confirmed) folded** — see "Implementation review fold" below.

> Redesign gap C, step 0.4. The blob path is the last framework surface where a registered data axis (tenant
> today; classification/region tomorrow) does **not** isolate. This ADR makes the **blob storage boundary**
> (`IStorageService`) a consumer of the **existing** data-axis seams — no new seam, no `Koan.Tenancy`
> dependency — so a tenant's blobs are physically isolated by a leading key particle and an unscoped op fails
> closed. The storage twin of the data core's managed-field isolation (ARCH-0101 / DATA-0106).

## Context

`StorageEntity<TEntity>` and the media pillar reach blobs through `IStorageService.{Put,Read,ReadRange,Delete,
Exists,Head,TransferToProfile,PresignRead,PresignWrite,ListObjects}` — **bypassing** `RepositoryFacade`, so the
`__koan_tenant` managed field and the `IStorageGuard` chokepoint never see a blob op. Consequences:

- **No particle.** The physical blob key is `{profile}/{container}/{key}` with no axis segment. Two tenants
  write `photo.jpg` to the **same** path; a read under any scope returns **any** tenant's blob.
- **No guard.** A tenant-scoped blob op with no tenant in scope silently lands on a global path.

## Grounding + design review

Grounding (2026-06-24) confirmed the **seams already exist** — `Koan.Storage` references `Koan.Data.Abstractions`
+ `Koan.Data.Core`, so it reads **`ManagedFieldRegistry`** (the registry `Koan.Tenancy` already populates with
`__koan_tenant`) and resolves the generic **`IStorageGuard`** seam (which `TenantStorageGuard` already
implements). No new seam, no `Koan.Tenancy`→`Koan.Storage` edge.

**A first design (v1) put the compose+guard in a `StorageEntity<TEntity>` funnel. A pre-ratification design
review (`wf_ac5a1e07-54a`, 35 agents) rejected it** — `StorageEntity<T>` is **not** the chokepoint. 5 CRITICAL
+ 6 HIGH findings showed many **type-erased** paths reach a blob without traversing `StorageEntity<T>`:
`MediaController` → app-supplied **`IMediaSource.OpenAsync`** (the primary public read surface), the **`new`
`MediaEntity.OpenRead(key)`** override, the **`IStorageService` extension helpers** (`Onboard`/`Create*`/
`ReadAll*`/`CopyTo`), **presigned URLs** (`MediaEntityExtensions.Url` → `PresignRead`), the **backup services**,
and `From/To` hydration. A `StorageEntity`-only proof passes while the media surface leaks. **The true chokepoint
is `IStorageService`** — which is type-erased (strings only). This v2 relocates the design there.

## Decision (v2)

### 1. Chokepoint — a `ScopedStorageService` decorator over `IStorageService`

Register a decorator (Reference = Intent — wrap the singleton `StorageService` at both registration sites,
`StorageServiceCollectionExtensions` + the registrar) that, for **every** op, composes the leading axis
particle onto the key (and `prefix` for `ListObjects`, the target for `TransferToProfile`) and runs the guard,
then delegates to the inner provider. Because **all** blob paths — `StorageEntity<T>`, `MediaEntity`,
`IMediaSource`, presign, list, transfer, the extension helpers — funnel through `IStorageService`, one decorator
covers them all. **Off = structurally absent:** when `ManagedFieldRegistry.IsEmpty` AND no `IStorageGuard` is
registered, the decorator is a pass-through (byte-identical; existing storage suites untouched). The single
boundary the framework cannot cover is an `IMediaSource` that reaches a **non-Koan** backend directly — the ADR
states that boundary explicitly rather than implying universal coverage.

### 2. Scope source — ambient, type-refined; fail-safe by default

The decorator has no entity type, so the scope is **ambient**:

- **Type-aware layers refine it.** `StorageEntity<T>` / `MediaEntity<T>` ops wrap their `IStorageService` call in
  `using StorageScope.For(typeof(T))` (an `AsyncLocal<Type?>`). When the decorator sees a type, it uses
  `ManagedFieldRegistry.ForType(type)` for the equality particle values **and** the `[HostScoped]`/`IAmbientExempt`
  exemption, and runs `IStorageGuard.Guard(type)` — full data-path parity (the `[HostScoped]` exemption + posture
  come for free from `TenantStorageGuard`).
- **Raw callers fail safe.** A direct `IStorageService` call (no `StorageScope` set) composes the **ambient axis
  bag** (`AmbientCarrierRegistry.Capture()`, the type-less ARCH-0100 source) as the prefix and runs a **value-based
  guard**: if the host has tenancy active and no concrete axis value is in scope → fail closed (Closed posture).
  So a type-less caller isolates **by default** rather than leaking to a global path.
- **Infra opts out explicitly.** Host-scoped infrastructure (the backup services; a `[HostScoped]`/`IAmbientExempt`
  storage type) declares `using StorageScope.HostScoped()` to operate **unprefixed + unguarded** — the storage
  analog of `IAmbientExempt`, named and intentional (not an accidental unscoped op).

### 3. Equality-only; a non-equality axis fails closed

A blob key is a physical path = equality-by-construction (exactly like the cache namespace, DATA-0106 §5). Only
`AutoReadFilter == true` (equality) descriptors become a particle. A value-yielding `AutoReadFilter == false`
(non-equality / viewer-context) axis **fails closed** on the blob path (throw) — it is never silently dropped
(mirrors the data path's "a non-equality axis still fails closed", and the cache's whole-type exclusion).

### 4. Mandatory sanitizing particle formatter

The leading axis particle is rendered through a **new `StorageKeyParticleFormatter`** (a sanitizing
`IParticleFormatter`) that **rejects** a value containing `/`, `\`, `..`, a leading dot, or control/invalid-path
chars — so a hostile or malformed axis value cannot escape its own prefix (path traversal / cross-scope). Not
`VerbatimParticleFormatter`. The value is coerced `Convert.ToString(v, CultureInfo.InvariantCulture)` (deterministic,
culture-invariant); `null`/empty ⇒ the particle is omitted (the guard is the fail-closed authority for "no scope").

### 5. Logical (`StorageEntity.Key`) vs physical (`StorageObject`) boundary

`IStorageService` and `StorageObject` operate on **physical** (composed) keys; `StorageEntity<T>.Key` and every
user-visible surface (URLs, presign, `[HttpGet("{**key}")]`) carry the **logical** key (`photo.jpg`, never
`acme/photo.jpg`). The decorator composes logical→physical on the way in. `StorageEntity<T>` write-return paths set
`.Key` from the **caller's logical name** (`From(StorageObject, string logicalKey)`), never from `StorageObject.Key`;
metadata-row lookups (`Head(key)` fallback `Query(e => e.Key == key)`, content-dedup) key on the **logical** key.
`StorageObject` is documented as a **physical descriptor**.

### Findings-resolution map (the 5 CRITICAL + 6 HIGH from `wf_ac5a1e07-54a`)

| Finding | Resolved by |
|---|---|
| `IMediaSource`/`MediaController` read surface bypasses funnel (HIGH) | §1 decorator at `IStorageService` |
| `MediaEntity.OpenRead(key)` `new`-override bypass (CRITICAL) | §1 decorator (the override now hits the decorated service); plus delete the override or route via base |
| `IStorageService` extension helpers type-erased (CRITICAL) | §1 decorator + §2 fail-safe default |
| Presigned-URL path no particle/guard (CRITICAL) | §1 decorator covers `PresignRead`/`PresignWrite` |
| `From/To` can't honor logical-key (CRITICAL) | §5 `From(obj, logicalKey)` |
| Backup services raw, host container (HIGH) | §2 `StorageScope.HostScoped()` exemption |
| Guard is type-only but leak is value-driven (HIGH) | §2 value-based guard + type refinement |
| Verbatim formatter lets value escape prefix (HIGH) | §4 mandatory sanitizing formatter |
| Persisted `.Key` logical vs row queries (HIGH) | §5 logical-key at write time + round-trip proof |
| Non-equality axis dropped not fail-closed (MEDIUM→raised) | §3 fail-closed |
| `ListObjects`/`Exists`/presign omitted from inventory (MEDIUM) | §1 decorator covers the full op set |

MEDIUM/LOW (sharding nesting, transfer 1-key signature, dedup-under-tenancy, `StorageObject.Id` physical, non-string
coercion) are addressed by §3–§5 + documented limitations (cross-tenant transfer forbidden by the guard; local
sharding gives logical isolation not a browsable per-tenant root — shard after the leading particle if per-tenant
listing is later required).

### Scope

This ADR is **0.4 (storage) only**. **0.3 vector** (Weaviate row-discriminator) is a sibling follow-on (needs
Docker/Weaviate; designed axis-generic alongside ARCH-0098 classification's vector guard).

## Test plan (ARCH-0079, real `AddKoan()`, Local provider — no Docker)

A storage isolation proof exercising **every** surface the decorator must cover (not just `StorageEntity` ops):

- Two tenants write `photo.jpg` → physically distinct, isolated blobs; cross-read impossible — via **`StorageEntity<T>`**,
  via **`MediaEntity<T>`** (the `OpenRead` override path), and via a **raw `IStorageService`** call (fail-safe).
- A **presigned** URL minted under tenant A composes A's particle and cannot be made to address B's blob.
- A two-tenant **`GET /media/{id}`** (the `IMediaSource`/`MediaController` surface) returns only the caller-tenant's bytes.
- The **logical-key round-trip**: the persisted row's `.Key` == logical (not `acme/...`); `Head(logicalKey)` finds it;
  content-dedup is per-tenant.
- An **unscoped** write (no tenant, Closed posture) fails closed; a `StorageScope.HostScoped()` / `[HostScoped]` op is
  unprefixed + allowed; a **hostile axis value** (`../globex`, `a/b`) is rejected by the formatter.
- `ManagedFieldRegistry.IsEmpty` (no axis module) ⇒ the decorator is a pass-through ⇒ existing storage suites green.

## Consequences

- **Positive:** one decorator at the true chokepoint covers every Koan blob surface (the v1 funnel could not); zero new
  seam, zero new tenancy code; storage joins the data core + cache as a uniform axis consumer; byte-identical when no
  axis is registered.
- **Cost:** the decorator + the `StorageScope` ambient (type carrier + host-scope opt-out) + the logical/physical-key
  discipline are more than a string prefix; the `From(obj, logicalKey)` change and the sanitizing formatter are
  load-bearing and must be pinned by the round-trip + hostile-value tests.
- **Risk:** an `IMediaSource` reaching a non-Koan backend directly is outside any framework chokepoint (stated, not
  implied); a raw caller that *intends* host scope must declare it (`StorageScope.HostScoped()`) or its op is
  tenant-prefixed by the fail-safe default — a deliberate fail-closed bias.

## Implementation review fold (`wf_03ef19e6-88c`, 18/21 confirmed)

A post-implementation impl-diff adversarial review found a real surface the first pass missed and several
consistency gaps. **Fixed** (commit `a22c5e5e`):

- **`StorageObjectExtensions` was a parallel un-scoped surface** (HIGH/MEDIUM). Its six `IStorageObject` extension
  helpers (`ReadAllText`/`ReadAllBytes`/`Head`/`Delete`/`CopyTo`/`MoveTo`) bind when a reference is `IStorageObject`-
  typed (instead of the scoped `StorageEntity<T>` instance methods). They now each declare
  `StorageScope.For(obj.GetType())`, and `CopyTo`/`MoveTo` hydrate the target with the **source's logical** key/name
  (not the physical `StorageObject.Key`/`Name`), keeping the new entity's own GUID.
- **`StorageEntity.Name` leaked the physical key** (MEDIUM) — `StorageService` sets `StorageObject.Name = key`, so the
  persisted `Name` was `acme/photo.jpg`. `From`/`To` now set `Name` from the logical key.
- **`ScopeAmbient` consistency** (MEDIUM) — a value-yielding non-equality axis now **fails closed** (§3 parity with
  `ScopeTyped`, not silently dropped); guards run **before** the `IsEmpty` short-circuit (so a guard registered without
  a managed field still fires).

**Documented, not changed** (LOW / by-design): the off-path pays an `AsyncLocal` push + a `GetServices<IStorageGuard>`
resolve per op (storage is I/O-bound; a cached "any guard registered" gate is a future optimization); presign
isolation is asserted by code-reading only (the Local provider has no presign — needs an S3/MinIO spec); a tenant
`ListObjects` on the **sharding** Local provider returns empty (the leading particle nests under the SHA shard — logical
isolation holds, but a navigable per-tenant root would require sharding *after* the particle); a raw `IStorageService`
op for a logically-host-scoped resource is tenant-prefixed by the fail-safe unless it declares `StorageScope.HostScoped()`
(intended fail-closed bias). The two-tenant `GET /media/{id}` and presign specs are the remaining integration gaps
(Docker-gated), tracked with 0.3.
