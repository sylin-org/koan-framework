# ARCH-0101: The data-axis model — segmentation as multi-plane contributors, with a premium authoring layer

**Status**: Accepted (shape ratified by the Enterprise Architect, 2026-06-24; implementation phased below)
**Date**: 2026-06-24
**Deciders**: Enterprise Architect
**Scope**: Establish that **all data-segmentation** — tenancy, classification, moderation, soft-delete, and any future axis — is expressed as **registered contributors over well-defined composition planes**, with **zero bespoke axis code in the framework core**. Provide a **premium `[DataAxis]` authoring layer** (sugar over the conformant seams), **query self-reporting** (`.Explain()`), and a **one-line isolation proof**; and add the two planes the architect negotiation surfaced — the **container-name particle** and the **operation-semantics override**. The standard (canon): a future capability plugs in as pure contributors, the framework **refuses to let it leak**, and **authoring it delights**.
**Related**: **ARCH-0096** (identifier engine — the name/key plane) · **DATA-0105** (storage-composition contributor pipeline — the stamp plane) · **DATA-0106** (read-filter contributor seam — the predicate plane) · **ARCH-0100** (durable carrier — the async-hop plane) · **ARCH-0097** (ambient typed slice) · **ARCH-0098/0099** (classification / tenancy — the first axes) · **ARCH-0094** (Adapter Forge / the conformance gate) · **[koan-design-principles]** (conformity-by-design; descriptor-not-callback; fail-closed; self-reporting) · **[tenancy-golden-contributor-standard]**.

---

## Context

The contributor-purity audit ([tenancy-contributor-purity-assessment.md](../architecture/tenancy-contributor-purity-assessment.md)) found tenancy is ~90% a golden contributor example — **zero bespoke tenancy code in core**, pure registration, N-axis generic seams for stamp/guard/carrier/key/schema — **except** the read-filter, which was equality-shaped (DATA-0106 closes that). The architect negotiation then extended the model:

- **An axis is a *multi-plane* bundle, and the *mode* selects the planes.** The same tenant axis manifests as a record field + read-filter (shared mode), a container-name particle (separate-container mode, `T1-Todo`), or connection routing (separate-database mode).
- **Soft-delete revealed a plane the taxonomy lacked** — it doesn't just stamp + filter, it *changes what `Delete` means* (physical remove → `__deleted = true`).
- **Authoring should be premium** — declaring an axis should feel like declaring intent, the framework should refuse to ship a leak, and a query should explain itself.

This ADR ratifies that full shape. It is the cohesive model; DATA-0106 is one plane's detailed (already-reviewed) seam.

---

## Decision

### 1. The law: segmentation = contributors over planes

Every composition **plane** has exactly **one engine** and **one DI-enumerable contributor seam**. The data core (and every consuming pillar) **never names an axis** — absent module ⇒ empty seam ⇒ structural no-op (Reference = Intent). An axis **registers into the planes its mode requires**; nothing else changes. This is the conformity-by-design invariant: a segmentation concern that can't ride a plane means the *plane* is wrong (genericise it), never that core gets axis-named code.

### 2. The plane catalog

| Plane | Engine | Contributor seam | Status |
|---|---|---|---|
| Data source (separate-DB) | `EntityContext` source/adapter routing (DATA-0077) | ambient routing | exists |
| **Container-name particle** (separate-container) | ARCH-0096 `IdentifierComposer` | **`IStorageNameParticleContributor`** (§3) | **new** |
| Partition particle | ARCH-0096 | DATA-0077 partition | exists |
| Record managed **field** (stamp) | managed-field stamp + serialize | `ManagedFieldDescriptor` (DATA-0105) | exists |
| Read **filter** (predicate) | predicate AND-fold | `IReadFilterContributor` (DATA-0106) | building |
| **Operation-semantics override** | facade chokepoint rewrite (declarative) | **`.OnDelete(...)` declaration** (§4) | **new** |
| Async-hop carrier | `AmbientCarrierRegistry` | `IAmbientSliceCarrier` (ARCH-0100) | exists |
| Cache-key particle / blob-key particle | `AmbientAxisComposer` (ARCH-0096) | managed-derived / storage seam | exists / gaps B,C |

### 3. The container-name particle plane (new seam)

`StorageNameGenerator` already composes via ARCH-0096 but hardcodes the *only* particle as partition (`[new Particle(0, "partition", …)]`). Add a DI-enumerable **`IStorageNameParticleContributor`** (`Particle? Particle(Type entityType)`) folded alongside partition. Mode-3 `T1-Todo#partition` becomes pure registration: tenancy contributes `new Particle(100, "tenant", id, Leading, "-")`. **"Tenant never in the spine"** is upheld — the spine is the *anchor* (`Todo`), untouched; the tenant is a separable leading particle around it.

### 4. The operation-semantics override plane (new, declarative + bounded)

An axis may change what an operation *means* — the canonical case is soft-delete (`Delete` ⇒ `__deleted = true`). This is the most powerful plane, so it is **declarative, not an arbitrary interceptor** (descriptor-not-callback, conformance-checkable): an axis declares `OnDelete(Logical.SetTrue("__deleted"))` — *data*, the framework knows "delete = set this field." The facade applies it at the chokepoint.

- **The escape verb** — `.HardDelete()` (a C# 14 extension member from the module) rides an ambient **bypass slice that is *plane-specific***: it bypasses **only** the soft-delete override. **All other isolation is RETAINED** — `.HardDelete()` still goes through the tenant/moderation read-scoping (the IDOR lowering), so a hard-delete can only physically remove a row the caller can *see*. A naive "bypass all interception" flag would be a cross-tenant delete hole; the bounded design forbids it by construction.
- **Soft-delete is the canonical reference module** (`Koan.Data.SoftDelete`): `[SoftDelete]` opt-in + `.Field("__deleted")` + `.OnDelete(Logical.SetTrue)` + `.Reads(hide-deleted)` + the `.HardDelete()`/`.Restore()`/`.WithDeleted()` verbs — ≈10 lines, pure contributors.

### 5. Per-entity activation is the `AppliesTo` plane

An axis's scope is one knob: **broad** (tenant: `Entity.Without<HostScoped>()`) or **attribute-gated opt-in** (soft-delete: `Entity.With<SoftDelete>()`, moderation: `Entity.With<Moderatable>()`). `[SoftDelete]` on an entity *is* the conditional activation — same mechanism, narrower predicate.

### 6. Modules extend the entity verb surface

Axis modules add verbs to `Entity<T>` via **C# 14 extension members** — the established Koan idiom (`.Uncache()`, `.Job`/`.Jobs`, `.Copy()/.Move()`). `Koan.Data.SoftDelete` adds `.HardDelete()`/`.Restore()`/`Todo.WithDeleted()`. Reference = Intent reaches the call site.

### 7. The `[DataAxis]` premium authoring layer — sugar over the conformant seams

A discovered axis type (`[DataAxis]`, like `IKoanJob`) with `Declare(Axis builder)` and smart defaults: a `.Field(name, valueProvider)` infers **stamp + equality read-filter + cache-key particle + index + fail-closed** (you opt *out* loudly, never *in* silently); a `.Reads(predicate)` supplies a non-equality predicate; `.Carries(slice)` wires the async-hop; `.OnDelete(...)` declares an operation override. **Mode is config** (`Shared | Container | Database`) — the *same* axis code maps to field+filter / name-particle / routing. **Critically, `[DataAxis].Declare(...)` EXPANDS to the exact conformant seams** (`ManagedFieldDescriptor` + `IReadFilterContributor` + `IStorageNameParticleContributor` + `IAmbientSliceCarrier` + the `OnDelete` declaration) — **both/and**: the raw seams stay the canon a power author can drop to; the premium layer is the delightful default.

### 8. Safety by construction

- **Fail-closed is the default.** A `.Field(...)` defaults to `FailClosed(RowScoped)`; opting out is explicit and loud.
- **The boot refuses a leaky axis** (DATA-0106 bias-to-strict, surfaced as a startup error): an axis that read-scopes an entity routed to an adapter that can't satisfy its isolation capability **does not boot** — with a fix-it message. You cannot silently ship a leak.
- **Non-equality axes auto-exclude from cache** (DATA-0106 §5 — a viewer-predicate can't be an equality cache key).
- **An analyzer** flags a stamp-without-read or read-without-stamp; the **conformance gate** (ARCH-0094) treats "claims isolation it can't deliver" as a build failure.

### 9. Self-reporting: `.Explain()` (query-RSoP)

Any query renders the per-axis fold + provenance + pushdown status + fail-closed satisfaction — the honest projection, the *same* self-reporting machinery as the boot report (which also lists active axes and their planes). The magic is inspectable; a reviewer reads the whole isolation story in one place.

### 10. Proof: `DataAxis.AssertNoLeak<T>()`

One assertion generates the cross-axis isolation proof — two contexts per axis × {read · get-by-id IDOR · scoped delete · async-hop · cache} — through a real `AddKoan()` boot (ARCH-0079). Generalizes the flagship `AssertNoTenantLeak` to every axis.

### 11. Conventions

- **Flatten structured fields** (`__mod_author`, `__mod_status`) rather than nested (`__mod.author`) — they filter + index; `FieldPathResolver` resolves single-segment managed names today (multi-segment is a later option, not a v1 need).
- **An axis's side-data is its own entity**, not a bespoke plane — moderation history is a `ModerationEvent : Entity<ModerationEvent>` riding the same contributors (tenant-scoped for free), placed via the same partition/name primitives.

---

## Consequences

- **Tenancy, classification, moderation, soft-delete are all pure contributors** — zero bespoke axis code in core; a new axis is a `[DataAxis]` type. The "would Moderation hit a wall?" test passes by construction.
- **The framework refuses to ship a leak and explains itself** — fail-closed default + boot-refusal + `.Explain()` + `AssertNoLeak` make multi-tenant anxiety ("did I forget a filter somewhere?") structurally answerable: *there is nowhere to forget it*.
- **Conformity-by-design is preserved while authoring delights** — the premium layer is sugar that expands to the conformant seams; both the power-user path and the conformance story stay clean.
- **The data core stays axis-agnostic** — every plane folds a contributor set, naming nothing.
- **Scope discipline** — the operation-override plane is declarative + bounded (no god-interceptor); the bypass is plane-specific (no cross-tenant escape).

---

## Implementation (phased — each phase: TDD, ARCH-0079 real-`AddKoan()` specs, per-seam adversarial review, mutation, green-ratchet)

- **Phase A — read-filter seam (DATA-0106). ✅ DONE (2026-06-24, `dev`).** The predicate plane: `IReadFilterContributor` + built-in `ManagedEqualityReadContributor` + `AutoReadFilter` + fail-closed-over-the-union (+ `ExcludesFromCache` + `IsReadScoped` raw/CAS gate + hot-path memoization, from the impl-diff adversarial review) + the non-equality cache-exclusion. *The foundation.* Proven adapter-agnostic on SQLite **and** MongoDB (the relational and document families). Full detail in the DATA-0106 Implementation note.
- **Phase B — container-name particle seam (§3). ✅ DONE (2026-06-24, `dev`).** `IStorageNameParticleContributor` + `StorageNameParticleRegistry` folded into `StorageNameGenerator` via the ONE ARCH-0096 `IdentifierComposer`. Implemented as a **static registry** (the declared `ManagedFieldRegistry`/DATA-0105 §4 deviation from "DI-enumerable" — the composer is static, cached, reached deep in data *and* vector naming where no DI scope exists, and a signature change would break every caller and miss the cache key). **Security-critical:** the ambient axis value is folded into the name cache key (`(provider,entity,partition,axisKey)`) so a per-container name can never cache and serve across tenants — mutation-verified. A **3-lens impl-diff adversarial review (`wf_1c43dcc1-d57`) folded an injectivity guard + 2 conformity fixes** (all latent — no production name-particle contributor ships yet, but real seam gaps): (1) a container-name particle value must map **injectively** to its storage token (`PartitionTokenPolicy.IsInjective`, the ONE shared rule `PartitionNameValidator` now delegates to) — a lossy/case-folded value (`acme/east`, or `Acme` on a case-folding store) **fails closed** rather than collapse two scopes into one physical container; (2) the registry's `Gather` is lock-free (volatile snapshot), mirroring `ManagedFieldRegistry`'s hot-path memo; (3) `Register` dedups by logical `Axis` id, not CLR type. Spec: mode-3 emits `T1-Todo#partition` (leading axis particle, anchor untouched), host emits `Todo` (byte-identical); SQLite integration proves per-container isolation (write T1 / read T2 → not found); injectivity fail-closed proven (lossy + case-fold). Green: data-core 271 (byte-identical re-home + delegation), SQLite 11, Mongo naming 2.
- **Phase C — operation-semantics override (§4) + `Koan.Data.SoftDelete`. ✅ DONE (2026-06-24, `dev`).** Declarative `OperationOverrideDescriptor` (Field + OnDeleteValue + AppliesTo) in a static `OperationOverrideRegistry`; the facade rewrites `Delete`/`DeleteMany`/`DeleteAll`/`RemoveAll` to load the VISIBLE (read-scoped) rows and re-persist with the field set, through a new **unguarded operation-override write channel** (`ManagedFieldWriteScope.Overrides` — injected into the record but NOT conflict-guarded, since a mutable state field changing by design must not be guarded; **isolation Current wins on any key collision** so an override can never clobber a tenant stamp). The plane-specific bypass (`OperationOverrideBypass`, ridden by `.HardDelete()`) skips ONLY the override — the physical delete is STILL read-scoped (IDOR retained). `Koan.Data.SoftDelete` is the canonical reference module: `[SoftDelete]` opt-in + the invisible `__deleted` managed field (AutoReadFilter=false, absent on normal writes) + a NULL-safe hide-deleted read contributor (`AnyOf(__deleted IS NULL, __deleted != true)`) + the `Delete⇒__deleted=true` override + `.HardDelete()`/`.Restore()`/`T.WithDeleted()` (C# 14 extension members). Enabling-primitive blast radius is minimal: relational injects from `Effective` (guard unchanged on `Current`); Mongo splits inject(`Effective`)/guard(`Current`). Spec: `Delete` soft-removes (hidden from every read site yet physically present under `WithDeleted`), `Restore` re-shows, `HardDelete` physically removes but only a visible row, `DeleteAll`/`RemoveAll` soft-remove the visible set. **A 4-lens impl-diff adversarial review (`wf_17a6b8a6-538`) folded 4 fixes** (the "override re-stamps tenant" abuse was REFUTED — `Effective` is isolation-wins): (1) the soft-delete managed field + read contributor declare **`RowScoped`** so a `[SoftDelete]` entity on a non-isolating adapter (JSON/InMemory) **fails closed cleanly** ("does not announce") instead of an opaque managed-field mid-read throw; (2) **`.HardDelete()` enters `WithDeleted`** so it can purge an already-soft-deleted row (the recycle-bin purge); (3) the bypass is **target-scoped to `(type, id)`** (not a process-wide flag) so it cannot leak into a cascade/lifecycle delete of a different entity (the ADR §4 "bounded by construction" invariant); (4) `_deleteOverride` resolved once at facade construction (no per-delete registry lock). Green: SoftDelete 7, tenancy 84 (incl. the **tenant × soft-delete two-axis isolation proof** + the JSON fail-closed), + byte-identical regression (data-core 271, sqlite 11, mongo 25).
- **Phase D — the `[DataAxis]` premium layer (§7).** Discovered axis type + `Declare(builder)` + smart defaults + mode-as-config; expands to the Phase-A/B/C seams. Spec: a `[DataAxis]` and the equivalent raw-seam registration produce byte-identical behavior.
- **Phase E — self-reporting (§8/§9).** `.Explain()` query-RSoP + the boot-report axis listing + the boot-refuses-leaky-axis pre-flight.
- **Phase F — `DataAxis.AssertNoLeak<T>()` (§10).** The generalized cross-axis proof harness; re-express `AssertNoTenantLeak` over it.
- **Then:** gap B (cache fold convergence onto `AmbientAxisComposer` + the out-of-band evict-key bug), gap C (storage blob-key + Weaviate vector isolation, SnapVault Phase-0 0.4/0.3), then the SnapVault conversion (the dogfood acceptance suite).
