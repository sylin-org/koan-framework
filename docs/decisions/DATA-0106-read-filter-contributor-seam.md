# DATA-0106: The read-filter contributor seam — predicate-generic read scoping

**Status**: Proposed (2026-06-24)
**Date**: 2026-06-24
**Deciders**: Enterprise Architect
**Scope**: Make the data read-filter **predicate-generic**, not equality-shaped, so *any* data-segmentation capability — tenancy, classification, a future **moderation** — scopes reads as a **registered contributor**, never bespoke core code. Today the managed-field mechanism (DATA-0105) is axis-agnostic for the write-stamp but **hard-wires the read-filter to scalar equality**; a capability whose read semantics are a non-equality predicate (row-visibility) cannot register. This closes the one gap that keeps tenancy from being a *golden* example of the contributor model (contributor-purity assessment, 2026-06-24).
**Related**: **DATA-0105** (the storage-composition contributor pipeline this extends — the read-filter is its missing predicate-shaped sibling) · **ARCH-0095/0099** (tenancy: the first equality axis) · **ARCH-0098** (classification) · **[koan-design-principles]** (conformity-by-design; the seam consumers structurally can't diverge from; fail-closed > remember-the-filter).

---

## Context

The contributor-purity audit found the data core is genuinely axis-agnostic on naming (zero bespoke tenancy code; pure registration; N-axis generic seams for stamp/guard/carrier/key/schema) — **except** the read-filter, which is **tenant-*shaped***:

- `RepositoryFacade.ManagedReadFilter()` derives, per managed descriptor, exactly `Filter.Eq(d.StorageName, d.ValueProvider())` (`RepositoryFacade.cs:149-162`) and AND-folds them. It is applied two-shaped: into `Query`/`Count` (`ApplyManaged`) and into the IDOR key-op lowering (`ScopedById`/`ScopedByIds`, `cs:167-171`).
- `ManagedFieldDescriptor` carries **only** `ValueProvider: Func<object?>` — a scalar value, **no predicate member**. The framework owns the predicate shape and fixes it as scalar equality.

This is exactly right for tenancy (one tenant id; a row belongs to one tenant; a viewer sees it iff equal) and structurally **cannot** express a row-visibility predicate where the stamped value is a level/set and the match is `In`/`HasAny`/`Ne`/`Gte` over the viewer's context. The `Filter` model already supports these (`Filter.In/HasAny/AnyOf/Ne/Not`), and `FieldPathResolver` already resolves a managed name → storage leaf — so the wall is purely the missing seam, not the engine.

**The decision (architect):** introduce a **separate** read-filter contributor seam (not a `Func<Filter?>` bolted onto the descriptor), and **re-home** the equality read-filter onto it — so the read-filter is *uniformly* contributor-driven and tenancy's equality is simply the default contributor.

---

## Decision

### 1. `IReadFilterContributor` — a DI-enumerable read-scoping seam

In `Koan.Data.Core.Pipeline`, mirroring `IStorageGuard` (DI-enumerable, the data core never names the axis, absent ⇒ empty ⇒ no-op):

```
public interface IReadFilterContributor
{
    /// Return a predicate to AND-fold into every read of <paramref name="entityType"/> (Query/Count and the
    /// key-op IDOR lowering), or null when this contributor imposes no constraint in the current ambient.
    /// Must be cheap — it runs on every read; cache per-type metadata. The capability the filter needs to push
    /// down (so the facade can fail closed when the adapter can't isolate) is declared by RequiredCapability.
    Filter? ReadFilter(System.Type entityType);
    Koan.Data.Abstractions.Capabilities.CapabilityToken? RequiredCapability { get; }
}
```

### 2. The read-filter is *uniformly* contributor-driven; equality is the built-in default contributor

`RepositoryFacade`'s bespoke `ManagedReadFilter()` is **replaced** by folding every registered `IReadFilterContributor` (the same two-shaped application — `ApplyManaged` + `ScopedById`/`ScopedByIds`). The data core ships **one built-in contributor**, `ManagedEqualityReadContributor`, that yields `Filter.Eq(d.StorageName, d.ValueProvider())` for each managed descriptor whose **`AutoReadFilter`** is true — so:

- **Tenancy is byte-identical and stays one declaration.** Its `ManagedFieldDescriptor` (default `AutoReadFilter = true`) → the built-in contributor emits the same equality filter it does today. The read-filter now *flows through the seam* (golden) without tenancy changing a line.
- **A predicate axis (moderation) registers its own `IReadFilterContributor`** returning, e.g., `Filter.AnyOf("__mod_visibility", viewer.Clearances)` or `Filter.Ne("__mod_status", "hidden")`. If it also stamps a field, its descriptor sets `AutoReadFilter = false` (stamp + schema-column + cache-partition, but **no** auto-equality that would wrongly conjoin).

### 3. `ManagedFieldDescriptor` gains `bool AutoReadFilter = true`

The single additive member. `true` (default) preserves every existing axis (tenancy/classification) exactly. `false` means "stamp/schema/cache me, but I supply my own read predicate via an `IReadFilterContributor`." No other descriptor change; the write-stamp, schema-column, and cache-partition paths are untouched.

### 4. Fail-closed is preserved and generalised

`InspectManagedAdapter`'s fail-closed (an active managed scope on an adapter that can't isolate → throw) generalises: a contributor that yields a non-null filter **and** declares a `RequiredCapability` the adapter doesn't announce → fail closed, exactly as the equality path does today (the built-in equality contributor carries the descriptor's `RequiredCapability`, so tenancy's `RowScoped` fail-closed is unchanged). A predicate the adapter cannot push falls to the existing post-fetch/`NotSupported` path, never silently unfiltered.

### 5. Cache interaction (ties to gap B)

A non-equality axis (`AutoReadFilter = false`) **cannot** be a cache-key segment — an id-keyed cache namespace is equality-by-construction, and partitioning by the row's stamped scalar is the wrong operand for a viewer-context predicate. So such a descriptor signals **cache-exclusion** for its entity (the framework already has the precedent — `[Classified]` excludes via `StorageFieldTransformRegistry`; extend the exclusion to "has a non-equality managed axis"). Equality axes (tenancy) remain cache-partitioned as today. Detailed in the cache convergence work (gap B).

---

## Consequences

- **Tenancy becomes a *golden* example.** Its read-filter is now a registered contributor (the built-in equality one), not framework-derived — so "tenancy = pure registration over generic seams" is true for *every* surface, read-filter included. No behavior change, no new tenancy code.
- **A moderation capability rides as pure contributors.** Field-stamp (managed descriptor), read-visibility (`IReadFilterContributor` with a non-equality predicate), async-hop (carrier), fail-closed (guard) — zero core edits. The "would Moderation hit a wall?" test passes.
- **One read-filter mechanism.** The bespoke `ManagedReadFilter` is gone; reads fold a uniform contributor set. Adding a read-scoping axis is a registration.
- **Hot path.** Contributors are cached per type (like guards/managed fields); the no-contributor and no-ambient-value cases are the same empty fast paths. Equality stays a single `Filter.Eq` per axis.
- **Scope.** This is the read-filter seam only. The write-stamp/schema/carrier/key seams are already generic (unchanged). The cache convergence + non-equality cache-exclusion is gap B; storage blob-key isolation is gap C.

---

## Implementation (phased — TDD, ARCH-0079 real-`AddKoan()` specs, mutation, green-ratchet)

1. **Seam + built-in equality contributor (`Koan.Data.Core`).** Add `IReadFilterContributor` + `ManagedEqualityReadContributor` (reads the managed registry, honors `AutoReadFilter`, carries `RequiredCapability`); inject `IReadFilterContributor[]` into `RepositoryFacade` (like `_guards`); replace `ManagedReadFilter()` with the folded contributor set across `Get`/`GetMany`/`Query`/`Count`/`Delete`. Spec: an entity with the built-in equality contributor read-isolates **byte-identically** to today (re-run the existing managed-field/tenant read specs — they must pass unchanged).
2. **`AutoReadFilter` flag (`Koan.Data.Abstractions`).** Add to `ManagedFieldDescriptor` (default true). Spec: a descriptor with `AutoReadFilter=false` stamps + schemas but contributes **no** equality read-filter.
3. **Predicate-axis proof (`Koan.Data.Core` test).** A fake non-equality `IReadFilterContributor` (`Filter.Ne`/`Filter.AnyOf`) AND-folds into Query + the IDOR key-op lowering; an adapter that can't push it fails closed. The decisive Moderation test, made real on a fake axis (no Moderation module needed).
4. **Tenancy re-home verification (`Koan.Tenancy` test).** The flagship `AssertNoTenantLeak` passes unchanged with the read-filter flowing through the built-in contributor (proves the re-home is byte-identical through a real boot).
5. **(Gap B, separate slice)** cache fold convergence onto `AmbientAxisComposer` + the out-of-band evict-key scope fix + non-equality cache-exclusion.
