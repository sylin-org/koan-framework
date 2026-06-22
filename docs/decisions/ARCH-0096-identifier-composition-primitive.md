# ARCH-0096: The identifier-composition primitive — one anchor-and-particles engine across pillars

**Status**: Proposed (2026-06-22)
**Date**: 2026-06-22
**Deciders**: Enterprise Architect
**Scope**: Converge the *several uncoordinated* hand-rolled implementations of one shape — **an anchor plus an ordered set of policy-rendered particles, composed into a single identifier/key** — onto **one engine** living below every pillar that builds one. The cross-cutting axes that motivate this (partition today, **tenant** next) are then registered **once** as immutable particle descriptors and honored by every composer, instead of being re-derived per pillar. This is the foundational primitive **DATA-0105** (the storage-composition pipeline) consumes for its Name stage, and the seam that lets tenancy (**ARCH-0095**) add an axis by *registration*, not per-pillar code.
**Related**: **DATA-0105** (the data-pillar storage-composition pipeline — the first structured consumer) · **DATA-0104** (the generic-entity storage-name grammar — the anchor resolver this preserves) · **DATA-0095** (the naming-capability model — data+vector already share `StorageNameGenerator`) · **ARCH-0095** (tenancy — tenant is the second cross-cutting axis; it is a **cache-key** particle, not a name particle) · **ARCH-0086** (`[KoanDiscoverable]`/`KoanRegistry` — descriptor discovery) · the [Ambient Context Charter](../architecture/ambient-context-charter.md) (typed slices — a particle reads exactly its declared ambient axis, never `EntityContext.Current`) · **[koan-design-principles]** (conformity-by-design, layered memoization, descriptor-not-callback, hot-path discipline).

---

## Context

An empirical survey (2026-06-22, six-lens) found the **anchor + ordered particles → one string** shape implemented **five times**, uncoordinated, with divergent mechanics — and corrected two assumptions the redesign was carrying:

| Instance | Anchor | Particles | Render policy | Memoization (today) |
|---|---|---|---|---|
| **Data storage name** (`StorageNameGenerator.Generate`, `:35`) | base name (DATA-0104 grammar, adapter-styled) | partition | adapter `StorageNamingCapability` (separator, token policy, **max-bytes clamp+hash**) | `(Provider, Entity, Partition)` composed-name cache |
| **Vector collection name** (`VectorAdapterNaming`) | — | — | — | **already delegates to `StorageNameGenerator`** via the `INamingProvider` default; *no hand-rolled composition* |
| **Cache key** (`CacheKey.For`, `:95`) | `EntityTypeName(type)` | partition, id | hardcoded `:` join, `"_"` partition sentinel, **no length limit** | template parsed per-type; **composite key recomputed per op** |
| **Job coalesce key** (`JobTypeBinding.CoalesceKey`) | `WorkType` | action, coalesce-props | hardcoded `\|` join | property set bound once at bootstrap |
| **Blob binding** (`StorageBindingAttribute`) | profile | container (+ instance override) | **a `(profile, container)` tuple, not a joined string** | **none — `GetCustomAttributes` re-scanned per op** |

Two corrections fall out, and they tighten the scope:

1. **Data and vector are already converged.** DATA-0095/0104 already made `StorageNameGenerator` the single chokepoint; `VectorAdapterNaming` is a 24-line router with nothing to delete. So this is **not** "four uncoordinated naming impls." Vector benefits from this ADR *transitively* and is left untouched.
2. **The shapes are not all the same.** There are **two families**: a **physical-identifier** family (data+vector — adapter-capability-constrained, length-clamped/hashed) and a **logical-key** family (cache, jobs — unconstrained, delimiter-joined). Blob binding is a **third shape** (a routing *tuple*, not a composed string); its real defect is the per-op reflection, not divergent composition.

The load-bearing convergence — the one the redesign actually needs and that clears the dogfood gate **now** — is **data storage-name + cache-key**, because they are the two composers that both render the **same cross-cutting axis (partition)** and will both render **tenant**, and their divergence is a real bug: partition is sanitized by `cap.Partition.Format` under one and the bare `"_"` sentinel under the other, joined by an adapter separator under one and a literal `:` under the other. Two renderings of one ambient value across two pillars is exactly the drift conformity-by-design exists to make unrepresentable.

---

## Decision

### 1. One composition engine, below every pillar

A single pure engine composes an **anchor** and an **ordered, immutable array of particles** into one identifier under a **policy**. It lives in **`Koan.Core`** (working namespace `Koan.Core.Naming` — deliberately *not* `Koan.Core.Composition`, which is the boot-lockfile twin) so data, cache, and jobs reference it without cross-pillar coupling (cache must never reference data).

```
// readonly structs — no allocation, no boxing of axis values
readonly struct Particle            { AxisId Axis; string Value; }      // a rendered token + which axis produced it
readonly struct CompositionPolicy   { string Separator; IParticleFormatter Formatter; int? MaxBytes; ClampStrategy Clamp; Casing Casing; }

static class IdentifierComposer
{
    // pure, synchronous, deterministic: anchor + particles[0..N] rendered under policy
    static string Compose(string anchor, ReadOnlySpan<Particle> particles, in CompositionPolicy policy);
}
```

- **The physical-identifier policy** is derived from the existing `StorageNamingCapability` (separator, `PartitionTokenPolicy`, `MaxIdentifierBytes` → the clamp+hash that `StorageNameGenerator.Clamp` does today). **The logical-key policy** is a fixed value (cache: separator `:`, no clamp; jobs: separator `\|`, no clamp). **One engine, per-consumer policy** — the algorithm (anchor, ordered particles, deterministic placement, clamp-with-hash on overflow) is shared; the constraints are supplied.
- The DATA-0104 anchor resolver (`StorageNameResolver`, the generic grammar) and the per-adapter capability stay where they are; the engine consumes the resolved anchor. This ADR does **not** touch the grammar — only the *particle attachment* the grammar's downstream `Generate` currently hardcodes.

### 2. Cross-cutting axes are descriptors, not callbacks

A cross-cutting concern that wants to contribute a particle to **every** composer registers an **immutable descriptor**, discovered via `[KoanDiscoverable]`/`KoanRegistry` (reflection-free, ARCH-0086):

```
sealed record ParticleDescriptor(AxisId Axis, int Order, IParticleFormatter Formatter);
```

- It declares **which ambient axis** it reads, its **order** among particles, and **how to format** the axis value. It is invoked with **only that axis's value** (a typed slice), and **cannot read `EntityContext.Current`** — so an undeclared ambient dependency is *unrepresentable* and can never be frozen into a memoized plan (the closure law, [koan-design-principles] §3, Charter L3). `partition` becomes the first such descriptor; `tenant` the second (in the cache-key composer only — see §5).
- The framework owns the applicators; a descriptor declares **what**, never **how-per-op**. This is what makes the composed plan inspectable-as-data (Adapter-Forge fingerprint, boot report) and 0-alloc to apply.

### 3. Layered memoization — the structure is the plan, the axis value is the operand

Per [koan-design-principles] §2, the **ordered particle-descriptor set is a structural plan** memoized at the deepest stable plane; only the **axis value** is per-op:

| Plane | Memoized substructure |
|---|---|
| **Type** | the anchor's type-derived inputs; the resolved, ordered particle-descriptor array (which axes, what order) |
| **(Type, adapter)** | the resolved **anchor string** (adapter-styled — it is adapter-*dependent*, so **not** Type-invariant) and the resolved `CompositionPolicy` |
| **per-axis complement** | the composed identifier keyed by `(Type, adapter, declared-axis values)` — e.g. `(Type, adapter, partition)` |

This **fixes a real defect the survey found**: today `StorageNameGenerator.Cache` is keyed by `(Provider, Entity, Partition)`, so the base anchor is **never cached independent of partition** — every new partition re-resolves the DATA-0104 grammar. Splitting the anchor to the `(Type, adapter)` plane and keeping only the *composed* result at the per-axis complement makes anchor resolution O(1) per type and is a net perf win, not a tenancy tax: adding the tenant axis to the cache-key composer keeps the *structure* stable (there is a tenant particle, invariant per type) and makes only the *value* per-op.

**Correctness carve-out:** structural planes cache **forever**; the **tenant→source registry / connection routing** stays **live-truth, coherence-evicted** (ARCH-0095) — the memoization here is of *name/key structure*, never of *where a tenant lives*.

### 4. Deterministic ordering (binding)

Particle order determines the physical identifier, so non-determinism = split-brain / orphaned tables. Order is a total, stable function of declared `Order` (ties broken by `AxisId`), frozen at discovery. This ADR's sibling cleanup makes `IndexMetadata` deterministic (its per-call `Guid` group key and `Dictionary` iteration, `IndexMetadata.cs:35` & `:41`) before anything memoizes its output — a precondition the survey flagged.

### 5. Scope and delegation — honest, dogfood-gated, incremental

**Load-bearing (this ADR's deliverable), the dogfood-2:**
- **Data storage name** — `StorageNameGenerator.Generate` delegates to `IdentifierComposer`; the hardcoded `baseName + cap.PartitionSeparator + token` (`:35`) becomes a one-element particle array `[partition]`, byte-identical. **Vector rides this unchanged** (it already delegates to `StorageNameGenerator`); a regression test pins that.
- **Cache key** — the entity-key build delegates to `IdentifierComposer` with the logical-key policy; `CacheKey.For`'s `:`-join (`:95`) and `"_"` sentinel become the cache policy + a `[partition]` (later `[partition, tenant]`) particle array. This collapses the partition-rendering divergence to **one descriptor**.

**Tenant is a cache-key particle, not a name particle.** Per ARCH-0095/DATA-0104, tenant never enters the table-name spine (4a = a DB **schema qualifier** at the Route stage; 4b = a **discriminator column**). So tenancy adds a particle **only to the cache-key composer** (cache must be tenant-scoped) — registering one `ParticleDescriptor(tenant)`. The data storage name gains **no** tenant particle. This is why DATA-0105 **drops its "Key" stage**: cache-key composition is owned by the cache pillar and reads the tenant ambient axis itself, through *this* primitive.

**Same-shape follow-ons (NOT forced — [koan-design-principles] §1 caveat):**
- **Job coalesce key** — same shape, *different axis set* (not partition/tenant). A clean second logical-key consumer; converging it deletes the `\|`-join duplication but is not tenancy-critical. Tracked, not gated on this ADR.
- **Blob binding** — a *different shape* (a `(profile, container)` routing tuple, not a joined identifier). It does **not** join this primitive; its standalone defect (per-op `GetCustomAttributes`) is a separate memoization fix. Naming it here prevents a false "converge all four" over-reach.

---

## Consequences

- **One mental model for "build the name/key for X"**; the partition axis is rendered **once**, identically, everywhere it appears.
- **Named deletions (parts down):** the hardcoded particle concat in `StorageNameGenerator.Generate` (`:35`); the hand-rolled `:`-join + `"_"` sentinel in `CacheKey.For` (`:95`); the divergent partition rendering across the two pillars (collapsed to one descriptor); the partition-coupled base-anchor cache (split, so the anchor is memoized independent of partition).
- **A perf win, not a tax** — anchor resolution moves off the per-partition path; the composed result stays memoized; a `MemoryDiagnoser` benchmark gates **0 bytes added per compose** vs today.
- **Tenancy adds an axis by registration** — one `ParticleDescriptor(tenant)` for the cache-key composer, no per-pillar code; the structural closure makes a forgotten/leaked axis unrepresentable.
- **The composed plan is inspectable as data** — a contribution to the Adapter-Forge structural fingerprint (ARCH-0094) and the boot report.
- **Cost** — a primitive promoted to `Koan.Core` and two hot-path call sites re-pointed; mitigated by byte-identical re-homing, the green ratchet, and the allocation gate. Jobs/blob stay as-is until their own slices.

---

## Implementation plan (each its own green-ratcheted slice; ARCH-0079 + mutation)

0. **The engine + descriptor + memo planes** in `Koan.Core.Naming`: `IdentifierComposer.Compose`, `Particle`/`CompositionPolicy`/`Casing`/`ClampStrategy` readonly structs, `ParticleDescriptor` + `[KoanDiscoverable]` discovery, the plane tree (Type / (Type,adapter) / per-axis complement), deterministic ordering, the **0-alloc benchmark** as an acceptance gate. Lands alongside the standalone determinism fixes (`IndexMetadata` `:35`/`:41`) the memoization depends on.
1. **Data delegates** — `StorageNameGenerator` composes via the engine; partition becomes the first registered `ParticleDescriptor`; split the base-anchor cache to `(Type, adapter)`. Byte-identical; **vector-untouched** regression pinned; full naming suite green.
2. **Cache delegates** — the entity cache-key build composes via the engine with the logical-key policy; partition descriptor shared with the data composer; the `:95` join is deleted. The divergence test (a partition value that sanitizes differently across the two pillars) goes from red to *cannot-represent*.
3. **(follow-on)** Job coalesce-key delegation; the blob-binding per-op memo fix (separately, as a non-composition cleanup).

---

## Carve-outs / open

- The exact `Koan.Core` namespace (avoiding the `Composition` lockfile collision) is settled in phase 0; `Koan.Core.Naming` is the working name.
- **Blob binding** stays out of the primitive (different shape); its memo fix is tracked independently.
- **Tenant-as-particle** mechanics (cache-key only) land with the DATA-0105 / ARCH-0095 tenancy slices; this ADR only provides the descriptor seam.
- The logical-key policies (cache `:`, jobs `\|`) are fixed values in v1; no per-app override (bias-to-config would only apply if a real second consumer needs it).
