# DATA-0106: The read-filter contributor seam — predicate-generic read scoping

**Status**: **Accepted + Implemented** (2026-06-24; on `dev`) · ADR adversarially reviewed (2 lenses) AND the impl diff adversarially reviewed (4 lenses → 2 HIGH + 1 MEDIUM, all verified + folded — see the Implementation note)
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
    /// Must be cheap — it runs on every read; cache per-type metadata.
    Filter? ReadFilter(System.Type entityType);
    /// The isolation capability the contributor needs the adapter to announce (the SAME nullable Capability type
    /// ManagedFieldDescriptor.RequiredCapability uses — Koan.Core.Capabilities). The FACADE owns the throw (§4);
    /// the contributor only declares.
    Capability? RequiredCapability { get; }
}
```
The contributor returns data (a `Filter`) + a declaration (`RequiredCapability`); it has no channel to throw — **the facade owns fail-closed** (§4), exactly as it owns the `IStorageGuard` loop.

### 2. The read-filter is *uniformly* contributor-driven; equality is the built-in default contributor

`RepositoryFacade`'s bespoke `ManagedReadFilter()` is **deleted** and replaced by one `ReadScopeFilter()` that folds every registered `IReadFilterContributor`. **Invariant: `grep ManagedReadFilter` returns zero after the change** — the folded filter threads through the *same* helpers (`ApplyManaged`, `ScopedById`, `ScopedByIds`) at **all eight** current call sites: `Get`, `GetMany`, `Query`, `Count`, `Delete`, **`DeleteMany`**, **`DeleteAll`**, **`RemoveAll`** (the last three are the cross-scope mass-delete leak class — omitting them regresses to an unscoped wipe). The fold is explicit: collect each contributor's non-null `ReadFilter`; **zero survivors → `null`** (wholly unfiltered, the existing fast path); **one → that filter**; **many → `Filter.All(survivors)`** — so the helpers receive a single `Filter?` byte-identical to today (no 1-element `AllOf`, no `null` operand).

The data core ships **one built-in contributor**, `ManagedEqualityReadContributor`, that **reproduces today's `ManagedReadFilter` tri-state verbatim**: iterate the managed descriptors with `AutoReadFilter == true`; per descriptor, `ValueProvider()` null ⇒ skip (off/host); accumulate `Filter.Eq(StorageName, value)` only for non-null values; return `null` when nothing accumulates; unwrap a single predicate. So:

- **Tenancy is byte-identical and stays one declaration.** Its `ManagedFieldDescriptor` (default `AutoReadFilter = true`) → the built-in contributor emits the same equality filter it does today. The read-filter now *flows through the seam* (golden) without tenancy changing a line.
- **A predicate axis (moderation) registers its own `IReadFilterContributor`** returning, e.g., `Filter.AnyOf("__mod_visibility", viewer.Clearances)` or `Filter.Ne("__mod_status", "hidden")`. If it also stamps a field, its descriptor sets `AutoReadFilter = false` (stamp + schema-column + cache-partition, but **no** auto-equality that would wrongly conjoin).

### 3. `ManagedFieldDescriptor` gains `bool AutoReadFilter = true`

The single additive member. `true` (default) preserves every existing axis (tenancy/classification) exactly. `false` means "stamp/schema/cache me, but I supply my own read predicate via an `IReadFilterContributor`." No other descriptor change; the write-stamp, schema-column, and cache-partition paths are untouched.

### 4. Fail-closed is preserved and generalised — triggered by *any* contributor, enforced by pushdown

**The decisive correction (review CRITICAL+HIGH).** Today's fail-closed (`InspectManagedAdapter`/`RequireManagedAdapter`) is gated on `_managed.Count > 0` — so a contributor with **no** managed descriptor (a pure predicate axis) would bypass it entirely and run unfiltered or *post-fetched* on a non-isolating adapter. **Post-fetching an isolation filter is itself a leak** — it fetches cross-scope rows into process memory and skews `Count`. So:

- **Trigger moves off `_managed.Count > 0` onto "any active contributor yields a non-null filter."** The adapter-inspection runs over the contributor *union*.
- **The isolation filter MUST be enforced at the store, never as an in-memory residual.** The facade fail-closes (the existing `InvalidOperationException`) when the folded filter is non-null and **any** of: (a) a contributor declares a `RequiredCapability` the adapter doesn't announce; (b) the folded filter is **not fully pushable** by the adapter — checked via the existing `FilterSupport` oracle (the same one `FilterSplitter` uses), so an operator the adapter can't push fails closed rather than silently residual-filtering; (c) the adapter is not `IQueryRepository` (key-ops lower to scoped queries).
- **Bias-to-strict:** a read-scoping contributor that yields a filter but declares **no** capability and isn't provably pushable **fails closed** — a `null` `RequiredCapability` is *not* a free pass. An axis that scopes reads without an isolation guarantee is a misconfiguration.
- Tenancy is unchanged: the built-in equality contributor carries the descriptor's `RowScoped`, equality is pushable, so the existing fail-closed fires identically. The read-side throw (previously only structurally implied) gets an explicit spec (a *read*, not just a write, on a non-isolating adapter under an active equality contributor throws).

### 5. Cache-exclusion for non-equality axes ships **with** this ADR (not deferred)

A non-equality axis **cannot** be a cache-key segment — an id-keyed cache namespace is equality-by-construction, and partitioning by the row's stamped scalar is the wrong operand for a viewer-context predicate (it would serve a hidden row to a viewer who shouldn't see it). The review showed this can't wait for gap B: `CachedRepository.AppendManagedScope` appends an equality segment for **every** managed descriptor regardless of `AutoReadFilter`, so a stamping non-equality axis would silently mis-partition `[Cacheable]` reads the moment DATA-0106 lands. So the minimal cache-exclusion ships **in this change set**:

- `AppendManagedScope` **skips** any descriptor with `AutoReadFilter == false` (no equality segment from a non-equality axis).
- `_excludeFromCache` **OR-includes** "the type has any `AutoReadFilter == false` managed descriptor" — the whole entity is cache-excluded (the framework already has the exclusion precedent: `[Classified]` via `StorageFieldTransformRegistry`).
- **Mixed entity (an equality axis + a non-equality axis): exclusion wins for the whole entity** — correctness dominates a partition that would omit the visibility axis — and a first-use **diagnostic** is logged (`[Cacheable] X excluded: non-equality managed axis <name>`) so the lost cache is visible, not silent.

Equality axes (tenancy) remain cache-partitioned exactly as today. The fuller cache convergence (the hand-rolled `AppendManagedScope` fold onto `AmbientAxisComposer` + the out-of-band evict-key scope bug where `CacheKey.For`/`Uncache` omit the suffix) remains **gap B** — but the *correctness*-critical exclusion is here.

---

## Consequences

- **Tenancy becomes a *golden* example.** Its read-filter is now a registered contributor (the built-in equality one), not framework-derived — so "tenancy = pure registration over generic seams" is true for *every* surface, read-filter included. No behavior change, no new tenancy code.
- **A moderation capability rides as pure contributors.** Field-stamp (managed descriptor), read-visibility (`IReadFilterContributor` with a non-equality predicate), async-hop (carrier), fail-closed (guard) — zero core edits. The "would Moderation hit a wall?" test passes.
- **One read-filter mechanism.** The bespoke `ManagedReadFilter` is gone; reads fold a uniform contributor set. Adding a read-scoping axis is a registration.
- **Hot path.** Contributors are cached per type (like guards/managed fields); the no-contributor and no-ambient-value cases are the same empty fast paths. Equality stays a single `Filter.Eq` per axis.
- **Scope.** The read-filter seam + the *correctness*-critical non-equality cache-**exclusion** (so a stamping non-equality axis can't mis-partition a `[Cacheable]` read). The write-stamp/schema/carrier/key seams are already generic (unchanged). The fuller cache **convergence** (hand-rolled fold → `AmbientAxisComposer`) + the out-of-band evict-key bug stay gap B; storage blob-key isolation is gap C.

---

## Implementation (phased — TDD, ARCH-0079 real-`AddKoan()` specs, mutation, green-ratchet)

1. **Seam + built-in equality contributor (`Koan.Data.Core` / `Koan.Data.Abstractions`).** Add `IReadFilterContributor` (`Filter? ReadFilter(Type)` + `Capability? RequiredCapability`) + `ManagedEqualityReadContributor` (reproduces `ManagedReadFilter`'s tri-state verbatim, honors `AutoReadFilter`, carries the descriptor's `RequiredCapability`). Inject `IReadFilterContributor[]` into `RepositoryFacade` (like `_guards`); add `ReadScopeFilter()` (drop-null/single-survivor/`Filter.All` fold). **Replace `ManagedReadFilter()` at ALL EIGHT call sites** — `Get`/`GetMany`/`Query`/`Count`/`Delete`/`DeleteMany`/`DeleteAll`/`RemoveAll` — threading the folded filter through the unchanged `ApplyManaged`/`ScopedById`/`ScopedByIds`. **Grep gate: zero remaining `ManagedReadFilter` references.** Spec: the existing managed-field/tenant read specs pass **byte-identical**, AND the off path (no ambient value) issues the SAME single adapter call with no predicate (zero delta both on and off).
2. **`AutoReadFilter` flag (`Koan.Data.Abstractions`).** Add to `ManagedFieldDescriptor` (default true). Spec: a descriptor with `AutoReadFilter=false` **still** stamps (`CurrentManagedValues`) + serializes the column, contributes **no** equality read-filter, and is **excluded from the cache** (see step 5).
3. **Fail-closed over the contributor union (`Koan.Data.Core`).** Move the adapter-inspection trigger off `_managed.Count>0` onto "any active contributor yields a filter"; fail closed on unmet `RequiredCapability`, on a folded filter not fully pushable (`FilterSupport`), or non-`IQueryRepository`; a null-capability read-scoping contributor fails closed (bias-to-strict). Specs: (a) a **read** (not just a write) on a non-isolating adapter under an active equality contributor **throws**; (b) a pure predicate contributor (no managed field) on a non-isolating adapter **throws** (the CRITICAL — never silently post-fetches).
4. **Predicate-axis proof + IDOR (`Koan.Data.Core` test).** A fake non-equality `IReadFilterContributor` (`Filter.Ne`/`Filter.AnyOf`) AND-folds into `Query`/`Count` **and** the IDOR key paths: assert a wrong-context **Get-by-id returns null** and a non-owned **Delete-by-id does not delete** and **DeleteMany/DeleteAll/RemoveAll** stay scoped. The decisive Moderation test on a fake axis (no Moderation module needed).
5. **Cache-exclusion (`Koan.Cache`, same change set).** `AppendManagedScope` skips `AutoReadFilter==false` descriptors; `_excludeFromCache` OR-includes "type has any `AutoReadFilter==false` managed axis"; first-use diagnostic. Specs: a `[Cacheable]` entity with a non-equality axis is **not cached** (and still read-isolated via the predicate); a mixed (equality+non-equality) `[Cacheable]` entity is **excluded** yet still tenant-isolated.
6. **Tenancy re-home verification (`Koan.Tenancy` test).** The flagship `AssertNoTenantLeak` passes unchanged with the read-filter flowing through the built-in contributor — and **add the missing scoped `DeleteMany`/`DeleteAll` assertions** (only `RemoveAll` is asserted today, so a delete-path regression is currently silent).
7. **(Gap B, separate slice)** the fuller cache fold convergence onto `AmbientAxisComposer` + the out-of-band evict-key scope fix.

---

## Implementation note (2026-06-24, `dev` — as shipped)

Delivered exactly as specced, plus two corrections from a **4-lens adversarial review of the impl diff** (the review pattern that caught the ARCH-0100 CRITICAL). All findings verified high-confidence and folded; both HIGHs were RED-verified (the new test fails without the fix) before GREEN.

**As-built surfaces:** `IReadFilterContributor` (`Filter? ReadFilter(Type)` + `Capability? RequiredCapability` + `bool ExcludesFromCache(Type) => false`) and the built-in `ManagedEqualityReadContributor` in `Koan.Data.Core.Pipeline`; `ManagedFieldDescriptor.AutoReadFilter = true`; `RepositoryFacade.ReadScopeFilter()` replacing `ManagedReadFilter` at all 8 sites (grep-zero); fail-closed `InspectScopeAdapter` over the managed-descriptor **and** contributor union + the §4b `FilterSplitter` pushability check; cache-exclusion in `CachedRepository`.

**Review fix 1 (HIGH) — raw/CAS fail-closed rode the contributor union.** `GuardRawAgainstActiveScope` (QueryRaw/CountRaw) and `ConditionalReplaceAsync` still gated on `HasManaged && CurrentManagedValues() is not null` — so a *pure* predicate axis (a moderation `IReadFilterContributor` with **no** managed field, which §2 blesses) bypassed isolation on the opaque-SQL and CAS paths. Fixed by adding `RepositoryFacade.IsReadScoped()` (folds every contributor's `ReadFilter`) and tripping both gates on `IsReadScoped() || (HasManaged && CurrentManagedValues() is not null)`. Tenancy is byte-identical (its equality contributor yields a filter exactly when a managed value is active).

**Review fix 2 (HIGH) — cache-exclusion rode only the managed registry.** `NonEqualityManagedAxis` read only `ManagedFieldRegistry`, so a `[Cacheable]` entity scoped *only* by a pure predicate contributor was cached by id (no scope segment) and leaked across viewers on a hit. Fixed by adding `IReadFilterContributor.ExcludesFromCache(Type)` (default `false`; a predicate axis returns `true` for its types), injecting `IEnumerable<IReadFilterContributor>` into `CachedRepository`, and OR-ing it into `_excludeFromCache`. The two layers can no longer diverge.

**Review fix 3 (MEDIUM) — per-read `FilterSplitter.Split` on the equality hot path.** The equality (tenancy) shape is static per (type,adapter), so its pushability is settled **once** at construction (`EqualityShapeIsPushable`) and the per-read Split is skipped (`_skipReadPushabilityCheck`) when the only active scope is the built-in equality contributor — byte-identical to the pre-DATA-0106 read cost. A predicate axis (dynamic shape) keeps the per-read Split.

**Cross-adapter agnosticism proven (the predicate plane is engine-generic, not just relational).** The decisive Moderation proof — a fake non-equality axis (`__mod_status != "hidden"`, `AutoReadFilter=false`) folding into Query/Count + the get-by-id/delete-by-id IDOR lowering + DeleteMany/DeleteAll/RemoveAll — passes **identically** on **SQLite** (relational, JSON-envelope, `json_extract … <> @p`) and **MongoDB** (document/BSON, `$ne`, non-Newtonsoft), through the *same* `FieldPathResolver` with no adapter-specific read branch. The §4b pushability fail-closed is facade-generic (proven on SQLite via an `IgnoreCase` predicate the relational caps can't push).

**Green (all through real `AddKoan()` boots, ARCH-0079):** data-core 264, Koan.Tenancy 81 (incl. the flagship `AssertNoTenantLeak` + new scoped DeleteMany/DeleteAll + two cache-exclusion proofs), SQLite connector 10, Mongo connector 25, cache topology 50, JSON 7, InMemory 33 — and the data-core 255 baseline stayed byte-identical through the re-home.
