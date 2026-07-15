# ARCH-0100: The durable ambient carrier — slices that survive the async-hop

> **2026-07-15 amendment:** ARCH-0113 retains this decision's fail-closed capture, restore, and
> suppression behavior but moves the carrier beneath Data into Core. `EntityContext` is no longer the
> cross-pillar owner; Data, Tenancy, Jobs, Events, and Transport consume one Core-owned ambient
> context. The original implementation location below is historical.

**Status**: Proposed (2026-06-24) · adversarially reviewed (3 lenses, all *ratifiable-with-fixes* — folded below)
**Date**: 2026-06-24
**Deciders**: Enterprise Architect
**Scope**: Make the axis-generic ambient carrier (ARCH-0097) **survive a durable async-hop** — a background job (and, on the same mechanism, a message / outbox entry) captures the ambient typed slices at submit and **rehydrates them before the handler runs**, composed with the request-path guard to fail closed. This closes the "worker-fails-open hole": the request-path chokepoint (ARCH-0099 §1b) only covers the synchronous request; work that serializes and resumes elsewhere loses the ambient tenant (and classification) unless it is carried. This is the Phase-0 keystone of the SnapVault dogfood — its headline break-and-rebuild (in-memory worker → `Koan.Jobs`) is structurally unsafe without it.
**Related**: **ARCH-0097** (the axis-generic typed-slice carrier this extends across the hop) · **ARCH-0099 §1b/§7d** (the fail-closed request gate this composes with; the durable-carrier roadmap bullet this ratifies; stancl's `QueueTenancyBootstrapper` prior art) · **ARCH-0098** (classification — the second axis that rides the same carrier) · **JOBS-0005 / JOBS-0008** (the ledger + dispatch + chains this wires into) · **DATA-0105** (the contributor pipeline; the carrier is the durable counterpart of the in-memory slice snapshot) · **[koan-design-principles]** (conformity-by-design; hot-path discipline; fail-closed > remember-the-filter).

---

## Context

ARCH-0097 made `EntityContext` axis-generic: a cross-cutting concern (tenant, classification) rides as a registered immutable **typed slice** in `ContextState.Slices`, snapshotted once at the chokepoint and threaded down. That works **within one async context**. It does **not** survive a durable hop:

- `Koan.Jobs` persists a `JobRecord` at submit (`JobCoordinator.SubmitAsync` → `JobRecordFactory.Create` → `_ledger.Append`) and, later — different thread, different DI scope, possibly a different process or node after a restart — claims and executes it (`JobOrchestrator.ExecuteClaimedAsync`). The `AsyncLocal<ContextState?>` is gone across that gap.
- `JobRecord` today carries exactly **one** ambient field: `CorrelationId` (the trace id). It has **no carrier for the typed slices**. The orchestrator builds a fresh `JobContext` and invokes the handler with **no ambient tenant in scope**.

The consequence is the **worker-fails-open hole** named in the delight harvest as the existential threat to the isolation flagship. SnapVault is the textbook case: a photo upload enqueues AI analysis (fire-and-forget today), and that handler reads/writes `PhotoAsset` (tenant data) and a Weaviate vector. Migrate it to `Koan.Jobs` **without** a carrier and the handler runs under the dev-fallback tenant (dev) or fails the fail-closed guard (prod) — either way, the `__koan_tenant` write-stamp and every tenant-scoped read inside the handler are wrong. "Structurally unwriteable across tenants" becomes false the moment work crosses a queue.

Three load-bearing observations from the seam map (verified against the code) shape the design:

1. **The work-item is loaded *before* any context setup.** `ExecuteClaimedAsync` calls `binding.Load(rec.WorkId)` at the very top — and the work-item is tenant data. So rehydration must wrap **from before the load** through settle (which conditionally auto-saves the work-item), not merely around `binding.Execute`.
2. **The whole job ledger is infrastructure, not tenant data — and it is multi-entity.** Because rehydration is active during settle, the orchestrator's own ledger writes would otherwise be tenant-stamped by the restored ambient. This is not one entity: `JobRecord`, `JobMetric`, **`JobGateRecord`** (written by `SetGate` during settle), and **`JobClaimTicket`** (written at claim) are all `Entity<>`-backed ledger rows. `JobGateRecord`/`JobClaimTicket` are also *read at claim time on a worker thread with no ambient tenant* — so if they were tenant-scoped, claiming would throw under Closed posture and the cooperative gate would fail **open** under Open posture. All four must be ambient-exempt.
3. **`[HostScoped]` is a `Koan.Tenancy` type.** Using it on a `Koan.Jobs` entity would create a `Koan.Jobs → Koan.Tenancy` reference and make jobs *name an axis* — violating the very layering invariant this ADR depends on (ARCH-0097). The exempt marker must be axis-generic and live below tenancy.

---

## Decision

### 1. A slice is *carriable* iff its module registers a carrier seam

The carrier is a small **conformance-tested two-method seam** (the shape of `IStorageGuard`/`IWriteStamp` — not a declarative descriptor like `ManagedFieldDescriptor`, because `Restore` genuinely pushes an ambient scope). It is **axis-generic** and lives in `Koan.Data.Core` — because the registry's whole job is to push/read `EntityContext` slices, which live in `Koan.Data.Core`; it cannot live in `Koan.Core` without reaching back up:

```
public interface IAmbientSliceCarrier            // implemented by the owning module
{
    string AxisKey { get; }                       // stable opaque key, e.g. "koan:tenant" — the bag key
    string? Capture();                            // read the ambient slice → portable string (null = absent)
    IDisposable Restore(string captured);         // push the slice back onto EntityContext; disposed after settle
    IDisposable Suppress();                       // push an EXPLICITLY-CLEARED ambient (see §5 — don't inherit)
}
```

`Koan.Data.Core` owns an `AmbientCarrierRegistry` (a singleton built from the **DI-enumerable** set of carriers — see §2) with two verbs over **all** registered carriers:

- `IReadOnlyDictionary<string,string>? Capture()` — each carrier's non-null `Capture()`, keyed by `AxisKey`. Returns **`null`** (not an empty map) when no carrier yields a value — so the no-cross-cutting-slice common case allocates nothing and persists no field.
- `IDisposable Restore(IReadOnlyDictionary<string,string>? bag)` — for each key with a **registered** carrier, `Restore(value)`; returns one composite `IDisposable` that unwinds all pushed scopes in reverse. A `null`/empty bag restores nothing (see §5 for why that is still safe).

`Koan.Jobs` (and later `Koan.Messaging`) depends only on this registry — it **never names an axis**.

### 2. Discovery is DI-enumerable — which closes the fail-closed timing gap

Each owning module registers its carrier in its `Register(services)` via `services.TryAddEnumerable(ServiceDescriptor.Singleton<IAmbientSliceCarrier, …>())`, mirroring `IStorageGuard`. `AmbientCarrierRegistry` collects `GetServices<IAmbientSliceCarrier>()` once. Because all module `Register` methods run during DI build — **before** host start and **before** any runtime submit — the carrier set is fixed before `JobCoordinator` (a singleton) ever captures. There is no submit-time registration race and `[Before]`/`[After]` ordering is irrelevant. (This is the property the fail-closed guarantee in §5 silently depends on; pinning the mechanism makes it sound.)

### 3. Tenancy registers its carrier in `Koan.Tenancy` — reusing what already exists

The tenancy carrier is trivial because both halves already exist:

- `Capture()` serializes the **tri-state** `TenantContext` (tenant id · explicit host · unset) — not just the effective id — so the distinction between "submitted host-scoped" and "submitted with no scope" survives. The captured string **must lead with a carrier-owned version token** (see §6); an unknown/future token fails closed at `Restore` rather than mis-restoring.
- `Restore(s)` returns the **existing** `IDisposable` from `Tenant.Use(id)` (a concrete tenant) or `Tenant.None()` (explicit host). `Tenant.Use` already returns the `SliceScope` restore handle — the carrier reuses it verbatim.

Classification (ARCH-0098) registers its own carrier later. One mechanism, N axes.

### 4. The bag is an opaque, sparse, **nullable** field on `JobRecord` — never one column per axis

```
public Dictionary<string,string>? AmbientCarrier { get; set; }   // axisKey → captured slice; null when empty
```

A single sparse bag keeps `Koan.Jobs` axis-agnostic (one tenant column would couple it to tenancy; classification would force a second; the schema would grow per axis). The field is **null** when no slice was in scope (zero-allocation, absent on the persisted row — no `"AmbientCarrier":{}` on every job). Each carrier owns its key and serialization. **The bag is immutable after capture** — never mutated post-construction — and `JobRecord.Clone()` must deep-copy it (`c.AmbientCarrier = AmbientCarrier is null ? null : new(AmbientCarrier)`), matching how `Transitions` is already deep-copied, because the in-memory ledger stores and returns clones; a shared `Dictionary` instance would alias across jobs in the exact isolation path this ADR protects.

### 5. Capture-at-submit; restore-at-execute wrapping load-through-settle; fail-closed **composed** with the §1b guard

**Capture** in `JobCoordinator`, the same way and at the same place `Correlation()` is captured — on the submitting async context, before any scope change — threaded into `JobRecordFactory.Create`, symmetric with the correlation id. Covers all three submit paths (`SubmitAsync`, `SubmitManyAsync`, `TriggerAsync`). Capture is one registry call over already-snapshotted ambient state (no per-axis branching in the coordinator).

**Restore** in `JobOrchestrator.ExecuteClaimedAsync`, **at the top, before `binding.Load`**, as an `IDisposable` that stays open across load + execute + settle:

```
using var ambient = _carrier.Restore(rec.AmbientCarrier);   // before binding.Load
var workItem = await binding.Load(rec.WorkId, workerCt);    // reads the right tenant's partition / passes the read-filter
...
await binding.Execute(workItem, ctx, linked.Token);         // handler runs in the submitted ambient
await SettleSuccessAsync(...);                              // conditional auto-save of the work-item is tenant-correct
```

**The fail-closed guarantee is *composed*, not self-contained.** The carrier restores the submitted ambient; the *refusal* of an under-scoped write is the request-path guard's job (`TenantStorageGuard`, ARCH-0099 §1b). The three bag states are distinct and must be handled explicitly:

1. **`null`/empty bag** → every registered axis is **explicitly *suppressed*** (cleared via `IAmbientSliceCarrier.Suppress()`), **not left to inherit** the worker/drain thread's ambient. This matters because `DrainAsync` in `JobMode.Inline` runs *synchronously inside the submitter's* `Tenant.Use(...)` scope — a plain no-op restore would let an unscoped job inherit the submitter's tenant and write its partition (the leak the impl-review found). After suppression the handler runs with no ambient axis; a tenant-data write is then refused by `TenantStorageGuard` **under Closed posture**. **Under Open posture the dev-fallback tenant applies — expected dev behavior, not a leak.** (Scheduled/boot/`Trigger` submissions legitimately produce an empty bag; a scheduled job that touches tenant data must enter a tenant scope explicitly inside its handler. The carrier neither invents nor weakens an ambient that was never submitted.) With **no** registered carriers (an app without any cross-cutting module) the restore is a true allocation-free no-op.
2. **bag with a registered axis** → restore it; the handler runs in the submitted ambient.
3. **bag names an axis whose carrier is *unregistered* at execute (owning module absent on this node), or `Restore` throws** → the job is **dead-lettered with a named reason**. It never silently drops the axis and runs fail-open. This is the one fail-closed decision the carrier owns by itself.

**Invariant: a job never executes in a *weaker* ambient than it was submitted in** — where "weaker" means a captured axis silently absent. An empty submission is not weaker (nothing was captured); an unregistered captured axis is, and dies.

*Deferred hardening (not a v1 claim):* a captured tenant **deleted/suspended between submit and execute** is not yet validated at `Restore` — `Tenant.Use(id)` checks only non-emptiness, so such a job would run in an isolated **ghost** partition (no leak — the data stays in the dead tenant's own partition — but wasted work and an id-reuse-collision risk). The *correct* owner of this is tenant-lifecycle: deleting/suspending a tenant **cancels its outstanding jobs** as part of the P8 erasure/relocate saga, so the ghost case does not arise in correct operation. Existence-validation-at-restore (carrier looks up `TenantRecord.Status == Active`, else dead-letter) is a tracked follow-on, not promised here.

### 6. The job ledger is ambient-exempt infrastructure — via a generic marker, not `[HostScoped]`

All four `Koan.Jobs` ledger entities — `JobRecord`, `JobMetric`, `JobGateRecord`, `JobClaimTicket` — are marked **ambient-exempt** so the restored ambient never stamps them and claim-time reads never hit a tenant filter. The marker is a **generic, axis-agnostic signal in `Koan.Data.Abstractions`** (e.g. an `IAmbientExempt` marker interface — final name settled in Phase 3), **not** `Koan.Tenancy`'s `[HostScoped]`: jobs already reference `Koan.Data.Abstractions` (for `[Index]`), so this adds no tenancy edge and jobs still name no axis. Every ambient-axis seam excludes it: tenancy's managed-field `AppliesTo` and `TenantStorageGuard` treat a type as exempt if it carries `[HostScoped]` **or** implements the generic marker (a union — `[HostScoped]` stays tenancy's own control-plane marker; infra gets a tenancy-free one); classification's future carrier does likewise.

**Honest framing (correcting an earlier draft):** this marker **changes** behavior — it is *corrective*, not a codification of the status quo. Today nothing in `Koan.Jobs` is exempt, so a job submitted while a `Tenant.Use` scope is still on the stack **would** tenant-stamp its `JobRecord` once tenancy and jobs ship together. The marker *prevents* that. It is safe to introduce with **no data migration only because** tenancy contributors are unshipped — no tenant-stamped job rows exist yet to move. And "no row movement" holds for a precise reason worth stating: the tenant axis is a managed **field** (a `__koan_tenant` JSON-leaf/column injected via `ManagedFieldDescriptor.AppliesTo`), not a storage-set/partition particle — exempting it suppresses the field within the *same* physical table/collection; the storage-set name is unaffected.

### 7. Chains re-thread the bag; the orchestrator is a second capture site

Chain successors (`ctx.ContinueWith`, `[JobChain]` auto-advance, `OnFailure.Continue`) are built by the **orchestrator's own** `JobRecordFactory.Create` calls, **not** through `JobCoordinator` — so capture-at-submit (§5) does not fire for them. `JobRecordFactory.Create` therefore takes the `AmbientCarrier` as a parameter, and the orchestrator **propagates the parent's `rec.AmbientCarrier` verbatim** into every chain successor (re-capturing from the still-restored ambient would be equivalent at those sites, but propagation is unambiguous and independent of restore still being active). Both stages of a chain submitted under `Tenant.Use("acme")` thus observe acme.

### 7a. The coalesce/idempotency identity folds in the captured ambient

The carrier guarantees the *handler* runs in the submitted ambient — but a job can be dropped or mis-routed **at the submit gate, before any capture/restore**, if the dedup is ambient-blind. `[JobIdempotent]` coalesces a duplicate submit onto an active job by a key built from the work-item; the lookup (`FindActiveByCoalesceKey`) reads the **exempt** (globally-visible) `JobRecord`. So a tenant-blind key lets tenant B's idempotent submit collapse onto tenant A's queued job — B's work is dropped and runs once *in A's ambient against A's data*. The "same work" for two tenants is **different work**.

Fix (conformity-by-design): **fold the captured ambient bag into the stored `JobRecord.CoalesceKey`**, and compute the same fold for the dedup lookup. The dedup is then structurally ambient-scoped — no ledger query can forget the filter because the key itself encodes the axis. An unscoped/system submit (null bag) keeps its **global** coalesce identity (correct — a system singleton *should* coalesce across the host). This is axis-generic: the coordinator folds whatever the carrier captured, naming no axis.

### 7b. Resource gates (`[JobGate]`) are global by design, not per-tenant

A `[JobGate]` key and its backing `JobGateRecord` (exempt) are **global** — a tenant's cooperative backoff on resource key `"api"` defers *every* tenant's job keyed `"api"`. This is **intended**: a gate models a genuinely **shared** dependency (an external API's 429 should back everyone off, not just the tenant who hit it). It is not a data leak (no tenant data crosses). An app that wants a **per-tenant** gate includes the tenant in its `[JobGate]` key (the key is app-authored). The framework does **not** auto-fold the ambient into the gate key — doing so would break legitimate shared-resource coordination. (Contrast §7a: coalesce identity *is* auto-folded because cross-tenant coalescing is always wrong.)

### 8. Messaging / outbox ride the same carrier (follow-on, not a second design)

`Koan.Messaging` captures the same bag onto the message envelope / outbox row at publish and restores it at dispatch via the **same `AmbientCarrierRegistry`**. This ADR specifies the generic mechanism and ships the **jobs** half (the SnapVault-blocking one). Messaging is a follow-on slice on the identical contract — not a separate decision. (If messaging must stay free of a `Koan.Data.Core` dependency, the bare `IAmbientSliceCarrier` interface can later split into `Koan.Core` while the `EntityContext`-touching registry stays in `Koan.Data.Core`; deferred to the §8 follow-on.)

---

## Consequences

- **The isolation flagship survives the queue.** A tenant-scoped job reads and writes its own tenant across a durable hop, a process restart, a different node, and **a multi-stage chain**. `AssertNoTenantLeak` can assert over the **async path**, not just the request path — the proof the delight harvest demanded.
- **Axis-generic, one mechanism.** Tenancy and classification both ride it; `Koan.Jobs`/`Koan.Messaging` never name an axis. Adding a future axis is a carrier registration, never a change to jobs or messaging.
- **Fail-closed is honest and composed.** The carrier owns one fail-closed decision (unregistered captured axis → dead-letter); under-scoped writes are refused by the §1b guard (Closed posture); Open-posture dev-fallback is expected. No "remember to set the tenant in your handler" discipline for the captured case.
- **Hot path.** Capture/restore are one registry call each; the no-cross-cutting-slice case is `null` (no allocation, no field on the row). No per-op, per-axis branching in jobs.
- **Ledger plane-split is now structural** via a generic ambient-exempt marker — removing a latent bug class (infra rows leaking into tenant partitions; claim throwing/failing-open) and giving ARCH-0099 §7d's structural-plane-split intent a constructive first instance, *without* a `Koan.Jobs → Koan.Tenancy` edge.
- **Scope.** Jobs ships here; messaging/outbox is the named follow-on. Deleted-tenant existence-validation and revocation-propagation (ARCH-0099 §7d) stay roadmap, with the P8 cancel-on-delete saga named as the real owner of the ghost-job case.

---

## Implementation (phased — TDD, ARCH-0079 real-`AddKoan()` specs, mutation, green-ratchet per phase)

1. **Ambient-exempt marker + carrier registry (`Koan.Data.Abstractions` + `Koan.Data.Core`).** Add the generic exempt marker (`IAmbientExempt`) to `Koan.Data.Abstractions`; add `IAmbientSliceCarrier` + the DI-enumerable `AmbientCarrierRegistry` (`Capture`→nullable bag / `Restore`) to `Koan.Data.Core`. Data-core specs: two fake carriers round-trip a slice across a simulated async boundary; **`Capture()` allocates nothing / returns `null`** when the registry is empty or all carriers return null; an **unregistered** key on restore is surfaced (not silently dropped); restore disposes in reverse and isolates parallel contexts.
2. **Tenancy exemption union (`Koan.Tenancy`).** `TenantScopeMetadata.IsHostScopedType` (and the managed-field `AppliesTo`/guard) treat `[HostScoped]` **or** `IAmbientExempt` as exempt. Spec: an `IAmbientExempt` entity is never tenant-stamped and never tenant-filtered, same as `[HostScoped]`.
3. **Ledger exemption (`Koan.Jobs`).** Mark **all four** — `JobRecord`, `JobMetric`, `JobGateRecord`, `JobClaimTicket` — `IAmbientExempt`. Real-`AddKoan` spec under **Closed** posture: submit → defer (sets a gate) → claim a tenant-scoped job; assert none of the four rows carry `__koan_tenant` and that `ClaimNext`/`SetGate`/`ActiveGates` never throw the no-tenant guard.
4. **Tenancy carrier (`Koan.Tenancy`).** `TenantContextCarrier : IAmbientSliceCarrier` (versioned tri-state capture; `Restore` reuses `Tenant.Use`/`Tenant.None`). Specs: capture under `Tenant.Use("acme")`, restore in a clean context, assert `Tenant.Current`/`EffectiveTenantId()` match; host-scoped vs unset survive distinctly; an **unknown version token** fails closed (named dead-letter), never mis-restores.
5. **Capture + bag plumbing (`Koan.Jobs`).** Add nullable `AmbientCarrier` to `JobRecord`; deep-copy it in `Clone()` (spec: two clones do not alias the bag); capture in the three `JobCoordinator` submit paths + add the param to `JobRecordFactory.Create`. Spec: submit under `Tenant.Use("acme")`, assert `JobRecord.AmbientCarrier["koan:tenant"]` round-trips the tri-state; submit with no scope → `AmbientCarrier` is null on the row.
6. **Restore-at-execute + chains, fail-closed (`Koan.Jobs`).** Wrap `ExecuteClaimedAsync` load-through-settle in `_carrier.Restore(rec.AmbientCarrier)`; propagate the parent bag at the two chain-advance `JobRecordFactory.Create` sites; dead-letter on unregistered-axis/restore-failure. Specs: (a) handler observes the submitted tenant; (b) the work-item is loaded from and auto-saved to the correct tenant; (c) a **2-stage chain** under `Tenant.Use("acme")` — both stages observe acme and stamp acme; (d) a bag naming an unregistered axis dead-letters with a named reason (never fail-open); (e) a scheduled/`Trigger` job has a null bag and is unaffected.
7. **The SnapVault async-path proof (sample spec).** After the worker migration (conversion Phase 1), an `AssertNoTenantLeak`-shaped spec over the **job path**: Studio A's enqueued AI-analysis job writes only Studio A's `PhotoAsset`/vector; Studio B never sees it. This sample integration spec is the acceptance test for this ADR's delight.
8. **(Follow-on, separate slice)** Messaging/outbox capture+restore on the same registry; revisit the interface-home split only if messaging must stay `Koan.Data.Core`-free.
