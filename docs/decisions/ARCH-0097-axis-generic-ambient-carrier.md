# ARCH-0097: The axis-generic ambient carrier — typed slices, not named fields

**Status**: Proposed (2026-06-22)
**Date**: 2026-06-22
**Deciders**: Enterprise Architect
**Scope**: Make the ambient routing carrier (`EntityContext`) **axis-generic**: a cross-cutting concern (tenant, classification, …) rides as a **registered typed slice from its own module**, not as a named field baked into `Koan.Data.Core`. This is the load-bearing slice of the Facet-3 ambient unification, and the prerequisite for tenancy (and classification) being a **separate module that provides contributors** rather than code living in the data core.
**Related**: [Ambient Context Charter](../architecture/ambient-context-charter.md) (the typed-slices vision this implements — Law L3) · **DATA-0105** (the storage-composition contributor pattern — contributors read declared ambient slices) · **ARCH-0095** (tenant is the first typed slice; the developer surface lives in `Koan.Tenancy`) · **[koan-design-principles]** (conformity-by-design; hot-path discipline).

---

## Context

Facet 3 calls for **one** ambient carrier with **typed slices**. The carrier today (`EntityContext.ContextState`) instead bakes cross-cutting axes as **named fields**: slice 1a added `Tenant`, and `CacheBehavior` was already there. Each named axis drags its module's types *into* `Koan.Data.Core` — `ContextState` `using`s `Koan.Data.Core.Tenancy`, the data core knows what a tenant is, and the whole point of the contributor pipeline (tenancy = registration, not core code) is undermined. The carrier must not name an axis it does not own.

Two kinds of dimension must be distinguished:

- **Intrinsic routing dimensions** — `Source`, `Adapter`, `Partition`, `Transaction`. These *are* `Koan.Data.Core` concerns (they route an operation to a store). They stay named fields.
- **Cross-cutting axes** — tenant, classification, and (today) cache behavior. These belong to other modules and must ride generically.

---

## Decision

### 1. `ContextState` carries an immutable map of typed slices

Alongside the intrinsic routing fields, `ContextState` holds an **immutable, type-keyed slice map**. A cross-cutting concern stores its own immutable slice object; the data core never names the type.

### 2. `EntityContext` exposes a generic, axis-agnostic slice API

```
public static T? GetSlice<T>() where T : class;          // read the current slice (null = absent)
public static IDisposable WithSlice<T>(T? slice) where T : class;  // push (or clear) + restore-on-dispose
```

Slices are **inherited-unless-overridden** exactly like the routing dimensions: a nested `EntityContext.With(partition: …)` carries the ambient slices through, and `WithSlice` pushes a new immutable context restored on dispose. The data core's `With(...)` no longer has a `tenant` parameter.

### 3. The module owns the slice type **and** the ergonomic surface

A cross-cutting module defines its slice type and its developer-facing surface on top of the generic API. Tenancy (`Koan.Tenancy`, ARCH-0095) defines `TenantContext` (the slice) and `Tenant.Use(id)` / `Tenant.None()` / `Tenant.Current` / the `.WithTenant(…)` extension — all thin wrappers over `EntityContext.WithSlice`/`GetSlice`. The data core never references any of it.

### 4. Absent module ⇒ absent slice ⇒ no-op (structural)

A concern with no registered slice is simply not present in the map — there is no per-op "is tenancy on?" branch in the carrier, and no coupling. This is the carrier half of the contributor pattern's *Reference = Intent / absent = no-op* property (DATA-0105).

---

## Consequences

- **Tenancy and classification decouple from `Koan.Data.Core`.** `TenantContext` moves out of `ContextState` into `Koan.Tenancy` as a slice; the data core stops `using` tenancy.
- **One ambient primitive.** The carrier is the single typed-slice mechanism the Charter described; the named routing fields remain because they are genuinely intrinsic.
- **Hot path.** `GetSlice<T>` is a dictionary lookup on the already-snapshotted ambient state; the common case (no cross-cutting slice) is an empty immutable map — cheap and allocation-free to carry. Slices are snapshotted once at the chokepoint and threaded down with the rest of the context (DATA-0105 hot-path discipline).
- **Scope.** This is the **minimal** Facet-3 ambient slice — enough to host tenant (and next classification). `CacheBehavior` can migrate to a slice later; the full seven-mechanism unification remains a future ADR. Migrating `CacheBehavior` now is out of scope (it is not part of the tenancy decoupling).

---

## Implementation

1. Add the slice map to `ContextState`; add `GetSlice`/`WithSlice` + a lightweight restore scope to `EntityContext`; remove the `tenant` parameter/field and the `Koan.Data.Core.Tenancy` dependency.
2. `Koan.Tenancy` rewires `Tenant.Use/None/Current` + `.WithTenant` onto the slice API.
3. A data-core spec proves the generic slice mechanism (push/read/inherit-across-`With`/restore/parallel-isolation); the tenancy specs move to `Koan.Tenancy` and prove `Tenant` over the slice.
