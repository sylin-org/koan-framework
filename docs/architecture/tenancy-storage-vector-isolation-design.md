---
type: ARCH
domain: framework
audience: [architects, maintainers]
status: archived
last_updated: 2026-07-19
framework_version: v0.20.0
validation: 2026-07-19
---

# Tenancy storage-blob + vector isolation — grounding & canon-resolved design

- Date: 2026-06-24
- Scope: SnapVault Phase-0 steps **0.4 (storage blob per-tenant prefix)** and **0.3 (Weaviate vector tenant-isolation)** — the two remaining framework gaps before the SnapVault Phase-1 `AssertNoTenantLeak` can cover the blob + vector paths. Grounding: `wf_5a1c545c-d12` (3 readers).
- Canon: ARCH-0099 (posture/§1b gate), ARCH-0100 (carrier — DONE), ARCH-0096 (identifier-composition primitive), DATA-0105 (storage-composition contributor pipeline), the settled "**tenant never in the table-name spine**" ([tenancy-design.md]), [koan-design-principles] (conformity-by-design; generic seam not bespoke code).

## The two gaps (empirically confirmed)

**0.4 Storage** — `StorageEntity<T>` (Onboard/OpenRead/Head/Delete) calls `IStorageService` **directly**, bypassing `RepositoryFacade` → the `__koan_tenant` managed field and `IStorageGuard` never see a blob op. The blob key is `{profile}/{container}/{key}` with **no tenant particle** (`StorageServiceHelpers.ToStorageKey()` only normalises slashes). Two tenants `.Onboard("photo.jpg")` write the **same** path; an unguarded `OpenRead("photo.jpg")` reads **any** tenant's blob. `StorageMediaController` serves blobs through these unguarded statics.

**0.3 Vector (Weaviate)** — `Search` builds the GraphQL `where` only from the caller's filter (no tenant AND-clause); `Upsert` writes only caller metadata (no tenant property). The vector path **bypasses** `RepositoryFacade`/`ManagedFieldRegistry`. `EncodePartitionInName=true` means an explicit `EntityContext.Partition` flows into the class name — but nothing **enforces** a tenant partition, and partition-in-name would violate "tenant never in the spine". `VectorData.FailClosedIfManagedScoped()` already throws on **Search** under an active managed field — `Upsert` has no such guard.

## Canon-resolved design

### 0.4 Storage — a generic key-particle seam + a storage fail-closed guard

> **UPDATE (2026-06-24) — superseded by [STOR-0011](../decisions/STOR-0011-storage-blob-key-axis-isolation.md).**
> Empirical grounding resolved the open layering question in favour of **fewer parts**: a **new
> `IStorageKeyContributor` seam is NOT needed.** `Koan.Storage` already references `Koan.Data.Abstractions` +
> `Koan.Data.Core`, so it reads the **existing `ManagedFieldRegistry`** (the same registry `Koan.Tenancy` already
> populates) for the key particle and resolves the **existing `IStorageGuard`** seam (which `Koan.Tenancy`'s
> `TenantStorageGuard` already implements) for the fail-closed guard — no new seam, no `Koan.Tenancy`→`Koan.Storage`
> edge, no new tenancy code. The notes below are the original direction; STOR-0011 is canonical.

- **Layering law:** `Koan.Storage` must not name "tenant" (same discipline as the tenancy-agnostic data core). So introduce a **generic, DI-enumerable key-contributor seam** in `Koan.Storage` (e.g. `IStorageKeyContributor.Particle(entityType) → string?`), gathered at key composition and folded into the key via **`AmbientAxisComposer.Append(key, bag, Leading, "/")`** — the shared ARCH-0096 convergence helper (`Koan.Core.Naming`) that the **job coalesce key already uses** (added 2026-06-24, `ec…`). One engine appends the ambient axes to *any* identifier; storage is the second consumer. *Not* an inline `TenancyAmbient` call in storage. Mirrors `IStorageGuard` + `ManagedFieldRegistry` for the data core.
- **Koan.Tenancy** registers a `TenantStorageKeyContributor` returning the effective tenant id as a leading particle; it honours the **same** exemption (`[HostScoped]`/`IAmbientExempt`) so system blobs are unprefixed.
- **Fail-closed guard:** a storage-entry gate (Closed posture + tenant-scoped type + no tenant in scope → throw), reusing `TenancyRuntime.Posture` + `TenancyAmbient.EffectiveTenantId()` — the storage twin of `TenantStorageGuard`. The key particle is the primary isolation (composed key differs per tenant); the guard prevents the unscoped write to a global path.
- **Result:** `OpenRead("photo.jpg")` composes to `acme/.../photo.jpg` vs `globex/.../photo.jpg` — physical isolation; B cannot reach A's blob because the composed key differs.
- **Open layering question (resolve at impl):** where does `IStorageKeyContributor` live so `Koan.Tenancy` can register it without an awkward edge — `Koan.Storage` (tenancy refs storage) vs a low `*.Abstractions`? Verify `Koan.Tenancy`→`Koan.Storage` reference direction first.

### 0.3 Vector — the row-discriminator leak guard (NOT partition-in-name)

> **UPDATE (2026-06-24) — IMPLEMENTED (`dev`, commit `0db14602`).** Built as the **STOR-0011 twin**: a generic
> `ScopedVectorRepository<TEntity,TKey>` decorator over `IVectorSearchRepository`, wired at
> `VectorService.TryGetRepository` (the one place both facades — `Vector<T>` and `VectorData<T>` — and the
> direct-`Repo` writes resolve the repo). It **stamps** the registered equality axes (`ManagedFieldRegistry`,
> axis-generic) into the vector metadata on `Upsert`/`UpsertMany`, and **ANDs** `Filter.Eq(StorageName, value)`
> (the same Koan `Filter` the data read-filter uses) into `options.Filter` on `Search`. **Fail-closed** when the
> adapter does not announce `VectorCaps.Filters`. The blanket `FailClosedIfManagedScoped` in `VectorData.Search`
> is **retired**. Proof: `VectorTenantIsolationSpec` (real `AddKoan()` + the no-Docker **InMemory** adapter — a
> KNN under tenant A with a query nearer B's point returns only A's vector; a user filter composes with the scope
> filter). Off = byte-identical (InMemory surface 29/29). The notes below are the original Weaviate-specific
> framing; the implementation is generic (every Filters-capable adapter rides the same decorator). v1 follow-ons:
> `Delete`/`GetEmbedding` by-id IDOR; non-dictionary metadata (excluded on read, never leaked).

- **Canon decides the fork:** tenant is a **row discriminator**, not the class-name partition ("tenant never in the spine"). So: on **Upsert**, inject the ambient `__koan_tenant` value into the vector object's properties; on **Search**, AND a `__koan_tenant == <ambient>` filter into the `where` clause. One shared class, row-filtered — consistent with the relational managed-field model.
- **Fail-closed:** extend `FailClosedIfManagedScoped` discipline to **Upsert** (a tenant-scoped vector write with no isolation capability → throw); keep Search's existing guard. The adapter **announces** it can row-scope vectors (a `DataCaps.Isolation.RowScoped`-style capability for the vector pillar) — a tenant-scoped vector op on an adapter that can't isolate fails closed (the "embedding leak guard").
- **Axis-generic, shared with classification:** this same injection is what ARCH-0098 classification's vector leak guard needs — design it axis-generic (inject the *managed discriminator(s)*, not "tenant" by name) so classification rides the same seam. Ideal: make the vector path honour `ManagedFieldRegistry` (the proper convergence) rather than a tenant-special filter; assess cost at impl.
- **SnapVault Phase-1 assertions:** Studio A's `Vector<PhotoAsset>.Search("sunset")` returns only A's photos; Studio A's blobs are unreadable under B's scope. The existing eventId post-filter becomes defence-in-depth, not the isolation boundary.

## Sequencing

Storage (0.4) before vector (0.3) — storage is the more self-contained generic seam; the vector guard is best designed axis-generic alongside (or just before) ARCH-0098 classification's vector guard to avoid a second pass. Each warrants a focused ADR (or a shared "tenancy data-plane isolation" ADR) + TDD with a real-store `AssertNoTenantLeak` (SQLite/Local for storage; Weaviate needs Docker for the vector spec). The SnapVault Phase-1 spec is the acceptance test.

## Key files (injection points)

- Storage: `src/Koan.Storage/Extensions/StorageServiceHelpers.cs` (`ToStorageKey`, the key seam) · `src/Koan.Storage/Model/StorageEntity.cs` (the guard entry) · `src/Koan.Media.Web/Controllers/StorageMediaController.cs` (the exposure point) · `src/Koan.Core/Naming/IdentifierComposer.cs` (ARCH-0096).
- Vector: `src/Connectors/Data/Vector/Weaviate/WeaviateVectorRepository.cs` (Search ~L525 filter, Upsert ~L202 property) · `src/Koan.Data.Vector/VectorData.cs` (`FailClosedIfManagedScoped` ~L177; add the Upsert guard) · `WeaviateVectorAdapterFactory.GetNamingCapability` (capability announce).
- SnapVault: `samples/applications/SnapVault/Models/Photo*.cs` ([StorageBinding]) · `Services/PhotoProcessingService.cs` (Upload L107+, `Vector<PhotoAsset>.Search` L281) · `Controllers/MediaController.cs` (blob read) · `Controllers/PhotosController.cs` (search endpoint).
