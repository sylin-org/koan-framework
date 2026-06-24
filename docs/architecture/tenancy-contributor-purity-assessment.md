# Tenancy contributor-purity assessment — is it a golden example?

- Date: 2026-06-24
- Standard (architect): tenancy MUST be a golden example of the contributor model — **zero bespoke tenancy code in the framework core**; tenancy = registered contributors over **generic** seams; the seams generic enough that a future **Moderation** capability (its own field + a context filter, possibly **non-equality** row-visibility) plugs in identically, never touching a core pipeline.
- Method: 4-auditor evidence-based sweep (`wf_8a2f7cf4-758`) — data filter/stamp, cache key, cross-assembly grep, and the Moderation thought-experiment. Every claim file:line-grounded.

## Bottom line

**Naming/branching purity: PASS.** There is **zero bespoke tenancy code** in any framework core assembly. Every `tenant` token outside `Koan.Tenancy` is a doc-comment/example (verified across `Koan.Core`, `Koan.Data.Abstractions`, `Koan.Data.Core`, `Koan.Cache*`, `Koan.Jobs`, connectors). **No** non-`Koan.Tenancy` assembly references `Koan.Tenancy` (no layering violation). Tenancy is pure registration (`KoanAutoRegistrar.cs:51-57`: one `ManagedFieldDescriptor` + `TenantStorageGuard` + `TenantContextCarrier`). The write-stamp, guard, carrier, key-fold (`AmbientAxisComposer`), schema-column, and `IAmbientExempt` seams are **truly generic and N-axis**. The data-core "0 tenant code refs" claim is **verified**.

**Genericity: the decisive gap — the read-filter seam is TENANT-SHAPED, not predicate-generic.** All four auditors converged here. A Moderation capability that isolates **by equality** (its own stamped field) rides every seam by pure registration. A Moderation capability whose read semantics are a **non-equality predicate** (row-visibility: `visibility IN viewer.clearances`, `status != banned`, `visible-to-context`) **cannot plug in** — it is blocked in core.

## The three gaps to reach "golden"

### A. The managed-field read-filter is equality-only (the load-bearing fix) — ✅ CLOSED (DATA-0106, 2026-06-24)
> Closed by the **separate `IReadFilterContributor` seam** (the architect's chosen design, not a `Func<Filter?>` on the descriptor). The equality read-filter is re-homed onto a built-in `ManagedEqualityReadContributor` (tenancy byte-identical), a predicate axis (moderation) registers its own contributor, and fail-closed/cache-exclusion ride the contributor union. Proven generic-for-Moderation on SQLite **and** MongoDB. See **DATA-0106** (Accepted + Implemented). The original analysis below stands as the rationale.
- `RepositoryFacade.ManagedReadFilter()` unconditionally emits `Filter.Eq(d.StorageName, ValueProvider())` (`RepositoryFacade.cs:157`); the write-verify is `field = @p` (`SqliteRepository.cs:860`); the IDOR key-lowering `ScopedById` AND-folds the same equality (`RepositoryFacade.cs:167-171`).
- `ManagedFieldDescriptor` carries **only** `ValueProvider: Func<object?>` (a scalar) — **no predicate member** (`ManagedFieldDescriptor.cs:35-42`). The framework owns the predicate shape and fixes it as scalar equality.
- The `Filter` model is already rich enough (`Ne`/`In`/`HasAny`/`AnyOf`/`Not` exist, `Filter.cs:24-67`) and `FieldPathResolver` already resolves a managed name → storage leaf (`FieldPathResolver.cs:50-60`). The wall is **purely** the missing descriptor seam + the hard-coded `Filter.Eq`.
- **Fix (additive, tenancy byte-identical):** add an optional read-predicate operand to `ManagedFieldDescriptor` — `Func<Filter?>? ReadPredicate` (default = `Filter.Eq(StorageName, ValueProvider())`) — that `ManagedReadFilter`/`ApplyManaged`/`ScopedById` honor; or a sibling `IReadFilterContributor` seam. This is the change that makes the model provably generic-for-Moderation. **A core-contract change → ADR + architect nod.**

### B. The cache hand-rolls its axis fold (an un-converged divergence) + an out-of-band evict bug
- `CachedRepository.AppendManagedScope` (`cs:388-394`) hand-rolls `::name=value` via a `StringBuilder` instead of delegating to the **one** ARCH-0096 engine — the **exact** divergence just fixed in `JobCoalesce`, un-fixed here. So `tenant` renders three ways across pillars (cache `::__koan_tenant=acme` vs job/storage `|tenant=acme`), defeating the convergence ARCH-0096 exists for. **Converge it onto `AmbientAxisComposer`/`IdentifierComposer`.**
- **Real bug:** the scope-fold lives only inside `CachedRepository`; `CacheKey.For` and `EntityCacheExtensions.Uncache/Flush` build keys **without** the managed-scope suffix (`CacheKey.cs:86-96`, `EntityCacheExtensions.cs:46,79`), so out-of-band `Uncache` **misses** the scoped key the decorator wrote. Move the scope fold into the shared `CacheKey` primitive.
- **Non-equality + cache:** a visibility predicate can't be a cache-key segment (keys are equality by nature), and there's **no** generic hook for a descriptor to opt its entity out of caching (the only exclusion keys off `StorageFieldTransformRegistry`/`[Classified]`, `cs:63,74`) → a non-equality managed axis would silently **cache-leak**. Add a descriptor signal that drives cache exclusion for non-equality axes.

### C. Storage blob-key isolation is unbuilt (axis-neutral, already queued as Phase-0 0.4)
- `AmbientAxisComposer`'s doc-comment claims "storage blob keys use it", but `Koan.Storage` has **zero** references to any ambient seam — so it's unbuilt for tenancy **and** Moderation. (The comment over-claims; soften to "will use".) Design: [tenancy-storage-vector-isolation-design.md](./tenancy-storage-vector-isolation-design.md).

## Minor smell

`Koan.Classification` exposes `IClassificationTenantAccessor` (names "Tenant" outside `Koan.Tenancy`, `cs:10-19`). Not a data-core violation (independent axis, Null default, no `Koan.Tenancy` dep), but it's a tenant-**shaped** accessor baked into a sibling capability rather than an axis-agnostic "key-bucket accessor". Worth genericising when classification is next touched.

## Synthesis

The contributor architecture is **real and clean** — tenancy genuinely is registration-only, no core pipeline names it, and most seams are N-axis generic. It is **~90% golden**. The remaining 10% is the difference between "generic over **which field**" (done) and "generic over **which predicate**" (gap A) — plus one un-converged cache fold (B) and the unbuilt storage prefix (C). Closing A makes the bold claim true: *any* data-segmentation capability — tenancy, classification, moderation — plugs in as pure contributors. A is the load-bearing one; B is worth doing regardless (active divergence + real evict bug); C is already planned.
