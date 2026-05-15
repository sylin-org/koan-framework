# ARCH-0078: Stale-While-Revalidate as Explicit Opt-In

**Status**: Accepted
**Date**: 2026-05-15
**Deciders**: Enterprise Architect
**Scope**: `Koan.Cache.*` read semantics
**Related**: ARCH-0075 (Koan.Cache pillar), ARCH-0076 (decorator order)

---

## Context

The cache pillar inherited stale-while-revalidate (SWR) semantics from the pre-v0.7 design, where the cache was a single-tier read-through fronting a slow backing store. In that world, "serve the previous value while we refresh" was a reasonable default — it traded staleness for latency.

The v0.7 reshape (ARCH-0075) changed the cache's identity. It is now a **transparent freshness layer** for `Entity<T>` operations: writer write-through to L1+L2, peer evict on broadcast, defense-in-depth L1 TTL ceiling. The whole point of the architecture is that reads return **fresh** values, with the coherence channel — not staleness — handling cross-node consistency.

SWR survived the reshape as a default-on consistency mode (`CacheReadOptions.Default.Consistency = StaleWhileRevalidate`). It is currently exposed through:

- `ICacheEntryBuilder<T>.AllowStaleFor(TimeSpan)` (per-entry)
- `[Cacheable(AllowStaleForSeconds = N)]` (per entity)
- `[CachePolicy(AllowStaleFor = ...)]` (per policy)
- `MemoryCacheAdapterOptions.EnableStaleWhileRevalidate = true` (adapter default)
- `RedisCacheAdapterOptions.EnableStaleWhileRevalidate = true` (adapter default)
- `CacheConstants.Configuration.Memory.EnableStaleWhileRevalidate`
- `CacheStoreCapabilities.SWR` flag
- `CacheFetchResult.StaleUntil`
- `CacheConsistencyMode.StaleWhileRevalidate` (enum value)

The integration test surface added in M4+ exposed two problems:

1. **Contract gap.** The store layer (Memory, Redis) implements SWR correctly: keys past absolute TTL but within the SWR window return `Hit` with the stale value. But reads through `ICacheClient` → `LayeredCache` do not surface those stale hits — somewhere between the store boundary and the typed entry builder, the stale signal is dropped. Unit tests with hand-rolled DI graphs missed this; the first integration test hit it.
2. **Default-on conflicts with the cache's identity.** The cache markets itself as "fresh through coherence." Defaulting reads to SWR teaches users the opposite — that `.Get()` may return stale values they didn't ask for. The two adapter-level toggles (`EnableStaleWhileRevalidate`) make this worse: a user who explicitly sets `AllowStaleFor(0)` on an entry can still observe stale values if the adapter's global flag is enabled.

### Forces

1. **The cache's value proposition is freshness.** Asymmetric coherence (writer write-through + peer evict + always-broadcast) is the freshness story. SWR is the opposite philosophy.
2. **Graceful degradation matters in some cases.** Read-mostly catalogs that prefer stale data over `503` during a Redis outage are a legitimate use case. SWR has value — as an **explicit, per-call opt-in**, not a default.
3. **Default-on with redundant gates is the worst combination.** Adapter-level `EnableStaleWhileRevalidate` flags create three places where SWR can be turned on: the entry, the policy, the adapter. Reasoning about which wins is impossible without reading source.
4. **Removing the primitive entirely would lose a useful feature.** Store-level SWR plumbing (`StaleUntil` on envelopes, the read-then-evict-on-expiry pattern) is sound. The bug is at the orchestration layer, not the store layer.

---

## Decision

SWR becomes a **per-entry / per-policy explicit opt-in**. The default cache contract is **"fresh or null"**.

### What changes

1. **`CacheReadOptions.Default` consistency mode** → `Strict` (was `StaleWhileRevalidate`).
2. **`CacheClient.CreateEntry<T>` default options** → `Strict` (was `StaleWhileRevalidate`).
3. **Adapter-level toggles are removed:**
   - `MemoryCacheAdapterOptions.EnableStaleWhileRevalidate` — deleted.
   - `RedisCacheAdapterOptions.EnableStaleWhileRevalidate` — deleted.
   - `CacheConstants.Configuration.Memory.EnableStaleWhileRevalidate` — deleted.
4. **Store-level SWR honor:** Memory and Redis stores no longer consult an adapter flag. They serve stale values **iff** `CacheReadOptions.AllowStaleFor` propagated from the caller is non-null and the entry is within the staleness window. No flag, no stale read.
5. **LayeredCache → CacheClient read path is repaired.** The current contract drop (where stale hits at the store layer don't surface to `ICacheClient.Get`) is fixed. After repair, an explicit `.AllowStaleFor(TimeSpan)` produces stale reads end-to-end.

### What stays (the opt-in tunable)

The following surface remains and is documented as the **explicit SWR opt-in**:

| API | Scope | Example |
|---|---|---|
| `ICacheEntryBuilder<T>.AllowStaleFor(TimeSpan)` | Per-call | `client.CreateEntry<T>(key).WithAbsoluteTtl(60s).AllowStaleFor(30s).Get(...)` |
| `[Cacheable(60, AllowStaleForSeconds = 30)]` | Per-entity | Entity-wide SWR for the type |
| `[CachePolicy(AbsoluteTtl = ..., AllowStaleFor = ...)]` | Per-policy | Power-user / multiple policies per type |

The rule: **non-null `AllowStaleFor` is the master signal**. If it's set, the read may return stale values within the window; if not, reads return null past absolute TTL.

`CacheConsistencyMode.StaleWhileRevalidate` is retained as an enum value but is no longer the default. Setting `AllowStaleFor` implicitly sets the consistency mode to SWR for that read.

### Boot report visibility

The cache pillar's boot report gains a per-policy line whenever `AllowStaleFor` is configured:

```
Policy:Product            AbsoluteTtl=300s  AllowStaleFor=60s  [SWR opt-in]
Policy:Category           AbsoluteTtl=120s                     [strict]
```

This makes the opt-in discoverable to ops without grepping source.

---

## Consequences

### Positive

- **Default behavior aligns with the cache's identity.** "Fresh through coherence" is the contract every user gets unless they explicitly choose otherwise.
- **One opt-in surface, not three.** `AllowStaleFor` is the only way to enable SWR. No adapter flags, no ambient configuration.
- **Reasoning becomes local.** A reader of `MyEntity` code can determine the staleness contract by reading the `[Cacheable]` attribute. No need to check `appsettings.json` or adapter source.
- **The integration-test gap closes.** The repaired LayeredCache→CacheClient path means `.AllowStaleFor(...)` works end-to-end, which makes the contract enforceable in tests.

### Negative

- **Breaking change.** Users who relied on the default-on SWR (without ever calling `AllowStaleFor`) will see different behavior: reads past TTL return null instead of stale values. Mitigation: ARCH-0075 framing already declared the cache pillar as a v0.7 greenfield with no existing users; this is the right window for the change.
- **Adapter flag removal is a config-shape change.** Any external config (env vars, `appsettings.json`) referencing `Cache:Memory:EnableStaleWhileRevalidate` or `Cache:Redis:EnableStaleWhileRevalidate` will be silently ignored after this change. Mitigation: greenfield, no existing config to migrate.
- **`CacheConsistencyMode.PassthroughOnFailure` is now orphaned.** It was never wired and is not part of this decision; left in place pending a separate decision on whether to implement it.

### Neutral

- The `CacheConsistencyMode` enum survives. We could collapse it to a `bool AllowStale` on read options, but the enum gives room for future modes (e.g., `PassthroughOnFailure`) without re-shaping the contract.

---

## Implementation order

1. **Repair the contract** — find and fix the LayeredCache→CacheClient SWR drop point. This is a prerequisite: making SWR opt-in only matters if the opt-in actually works.
2. **Flip the default** — change `CacheReadOptions.Default.Consistency` and `CacheClient.CreateEntry` initial options to Strict.
3. **Remove adapter toggles** — delete `EnableStaleWhileRevalidate` properties + constants + store-level checks.
4. **Boot report** — surface `AllowStaleFor` per policy in the registrar's Describe output.
5. **Tests** — re-enable the skipped `Stale_entries_expire_after_allowance` (rewritten to prove opt-in works); add a negative test (default config returns null past TTL).
6. **Docs** — update `docs/reference/data/cache.md`, `.claude/skills/koan-caching/SKILL.md`, and the `CLAUDE.md` cache utilities section.

---

## Notes for reviewers

- This decision sits cleanly inside the v0.7 break-and-rebuild window per ARCH-0075. No external users.
- The integration-test suite landed in the same branch surfaced the contract bug. That alignment — finding the bug *and* deciding the contract in one branch — keeps the design and the proof together rather than punting either.
