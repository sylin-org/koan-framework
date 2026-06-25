---
type: ARCHITECTURE
domain: cache
title: "Cache scope-key convergence (redesign gap B)"
audience: [architects, ai-agents]
status: active
last_updated: 2026-06-24
---

# Cache scope-key convergence — the out-of-band evict bug

> **Gap B of the [redesign completion ledger](./redesign-completion-ledger.md).** A focused convergence +
> latent-bug fix, not a new ADR: it implements an already-decided fix on top of ARCH-0096
> (`AmbientAxisComposer`), DATA-0105/§3.2 (the managed-scope cache partition) and DATA-0106 §5 (the
> non-equality cache exclusion). Records the one real design decision — **where the scope-fold lives** — so a
> future reader does not "correct" it back to the ledger's literal first phrasing.

## The bug (security-relevant correctness)

A `[Cacheable]` entity carrying an **equality** managed axis (the tenant `__koan_tenant` discriminator) is
cached under a **scoped** key. The read path partitions the key so one tenant's cached row is never served to
another (proven by `AssertNoTenantLeakSpec.Cacheable_entity_does_not_leak_through_the_cache_key`). But the
scope segment lived in the key on **only the read path** — `CachedRepository.AppendManagedScope`. The
out-of-band evict sites built a **different, scope-less _and_ partition-less** key:

| Site | Key it built | Matches the read-path entry? |
|---|---|---|
| read path (`CachedRepository`) | `{TypeName}:{Partition}:{Id}::__koan_tenant={t}` | — (the authority) |
| `CacheKey.For(type,id,partition)` | `{TypeName}:{Partition}:{Id}` | only for a **non-axis** entity |
| `EntityCacheExtensions.Uncache` / `EntityCacheHandle.Flush` | `{TypeName}:{Id}` | **never** (also drops `{Partition}`) |

So `entity.Uncache()` / `EntityCache<T,K>.Flush(id)` **silently no-op'd**: they removed a key nothing was
stored under. This is broader than tenancy — the `{Partition}` miss means out-of-band evict has been a no-op
for **every** default-templated `[Cacheable]` entity since the canonical template gained `{Partition}`.

## The fix — one canonical scoped-key builder, one composer

`Koan.Cache.Abstractions` deliberately carries only Microsoft.Extensions references — it cannot see
`ManagedFieldRegistry` (`Koan.Data.Abstractions`) or `AmbientAxisComposer` (`Koan.Core`). So `CacheKey.For`
**cannot** fold the scope without coupling the cache contract surface to the data pipeline. The ledger's first
phrasing ("`CacheKey.For` gains the scope segment") is therefore not implementable as written without inverting
a clean boundary.

**Decision:** the scope-fold lives **one layer up**, in `Koan.Cache` (which already references both
`Koan.Core` and `Koan.Data.Abstractions`), in a single public primitive both the read path and the evict path
call — `Koan.Cache.Keys.ScopedEntityCacheKey`:

- `ScopedEntityCacheKey.AppendScope(baseKey, entityType)` — fold the managed **equality** axes onto an
  already-formatted base key. The **read path** entry point: `CachedRepository` keeps template-formatting the
  base (so a custom `[CachePolicy]` template is honoured) and delegates only the scope-fold here.
- `ScopedEntityCacheKey.For(entityType, id, partition)` — the full canonical scoped key: `CacheKey.For` for
  the base (the canonical default-template shape) then `AppendScope`. The **out-of-band evict** entry point;
  `Uncache`/`Flush` route through it, reading `{Partition}` from `EntityContext.Current`.

Both paths call the **same** `AppendScope`, so read and evict agree on the scope suffix by construction. The
scope-fold itself delegates to the **one** ARCH-0096 composer — `AmbientAxisComposer.Append(base, bag,
Trailing, "::")` over a bag of `{StorageName → ValueProvider()}` for the equality descriptors — so the cache
key renders the axes through the same engine as job-coalesce and storage-name composition, not a bespoke
concat ([[koan-design-principles]]: one composer / conformity-by-design).

### What is preserved exactly

- **Byte-identical cached key.** For the single registered equality field (tenant),
  `AmbientAxisComposer.Append(base, {__koan_tenant→t}, Trailing, "::")` ⇒ `base::__koan_tenant=t` — identical
  to the prior `AppendManagedScope`. Off / no managed field ⇒ the bag is empty ⇒ the base returns unchanged
  (0-alloc fast path). The read-path cached key does not move.
- **DATA-0106 §5 non-equality exclusion stays.** A non-equality axis (`AutoReadFilter == false`) or a
  pure-predicate `IReadFilterContributor` still excludes the whole type from caching (`_excludeFromCache` in
  `CachedRepository`), so `AppendScope` only ever folds equality axes — a viewer-context predicate's scalar is
  never a cache-key segment.

### What changes (the bug fix)

- The **evict key** now equals the read-path key for default-templated entities: it gains `{Partition}`
  (fixing the universal no-op) and the equality scope segment (fixing the cross-tenant case). `Uncache`/`Flush`
  now actually evict.

### Folded from the impl-diff adversarial review

The convergence was reviewed adversarially (`wf_6de73ebb-7fe`, 4 refute-by-default lenses + a verify pass; 6 of 11
findings confirmed). Three were folded as fixes; three are documented contracts/limitations.

**Fixed:**
- **Null/default id must not throw (MEDIUM).** Routing the evict sites through `CacheKey.For` (which throws on a
  null id) changed `Uncache`/`Flush` from a benign no-op to an `ArgumentNullException` for a string-keyed entity
  with an unset id. The evict sites now guard the default key (`EqualityComparer<TKey>.Default.Equals(id,
  default)`) and no-op — parity with the write path's `IsDefaultKey` skip.
- **Culture-invariant id token (LOW).** `CacheKey.For` now renders an `IFormattable` id culture-invariantly,
  matching the read path's `CacheKeyTemplate`. Without it, a negative-int / `DateTime` key under a non-invariant
  process culture would key differently on read vs evict — the very no-op gap B fixes.
- **Verbatim partition token (LOW).** `CacheKey.For` no longer trims the partition. The read path's template
  stores `EntityContext.Partition` raw; trimming on the evict side both diverged from it (a no-op for a
  surrounding-whitespace partition) and risked collapsing two distinct partitions onto one cache key.

**Documented (not changed):**
- **Out-of-scope evict is a no-op (LOW, by contract).** `Uncache`/`Flush` read the scope from the *ambient*
  `EntityContext` at evict-time; calling them for a tenant-scoped entity without the original `Tenant.Use(…)`
  active builds a `…::__koan_tenant=_` key that does not match the cached entry, so nothing is evicted (a stale
  cache, never a cross-tenant *hit* — distinct scopes never compute equal keys). This is the documented contract
  ("call under the same scope"); the `bool` return signals the no-op. The dominant async path is covered by
  ARCH-0100 (the durable carrier rehydrates the scope inside the job).
- **A tenant literally named `_` (LOW, pre-existing, dev-only).** A tenant whose surrogate id is exactly `_`
  collides with the no-tenant sentinel under dev-open posture. Pre-existing (the prior `AppendManagedScope` used
  the same `?? "_"` fallback), dev-only, and requires a hand-chosen `_` id the framework never mints. A
  `TenantContext.For` sentinel-rejection is the proper home, out of scope here.
- **Multi-equality-axis segment order (LOW, latent).** With a second equality managed axis, the cache-segment
  order is axis-ordinal (the composer's order), not the managed-field priority order. Harmless (an opaque,
  ephemeral key; read and evict still agree by construction) and only the tenant equality axis exists today.

### Known limitation (pre-existing, documented — not a regression)

Out-of-band `Uncache`/`Flush` are static and have no access to a per-entity custom `[CachePolicy(KeyTemplate=…)]`;
they target the **default** template shape (`CacheKey.For`). A `[Cacheable]` entity with a custom key template
is evicted by tag (`EntityCache<T,K>.FlushAll()`, which is unaffected — it flushes by the `{TypeName}` tag),
not by `Uncache`/`Flush`. This matched prior behaviour (out-of-band evict never honoured custom templates).

## Proof

- `tests/Suites/Tenancy/.../CacheEvictKeyConvergenceSpec.cs` (ARCH-0079, real `AddKoan()` + SQLite + Koan.Tenancy):
  same-tenant `Uncache`/`Flush` now removes the scoped entry (RED before the fix — `Exists` stayed true); a
  cross-tenant evict leaves the other tenant's entry intact; a `[HostScoped]` non-axis `[Cacheable]` entity is
  evicted via the partition-aware key (the universal `{Partition}` fix).
- Byte-identical regression: data-core off-proof, tenancy, SoftDelete, the Data.Axes suites, and the cache
  suite stay green; the read-path cached key is unchanged.
