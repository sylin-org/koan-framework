---
type: ARCHITECTURE
domain: core
title: "Redesign Completion Ledger — the grand autonomous run"
audience: [architects, ai-agents]
status: active
last_updated: 2026-06-22
---

# Redesign Completion Ledger

> **The durable orchestration plan + status ledger for the autonomous run** that drives the remaining Koan
> redesign — **Facet 3** (ambient + tenancy), **Facet 4**, and the cross-cutting capabilities
> (storage-composition pipeline, classification, Adapter Forge) — to **FULL implementation + exhaustive
> tests.** No time or effort limit; the objective is to come out the other side with every capability the
> ADRs describe **implemented and tested.** This ledger survives compaction — **update the status table as
> work proceeds.** It references, and does not restate, the per-ADR designs and the memory anchors.

## Mandate (architect, 2026-06-22)

Establish all necessary ADRs (capturing the scope gathered so far); **iterate over each facet**, doing
**side-discovery** as necessary to identify opportunities for *"fewer but more meaningful moving pieces"*;
then **implement** and write **exhaustive tests**. No limits on time or effort. Adjust ADRs and this plan
as discovery warrants.

## Governing canon (apply to every decision)

- **[[koan-design-principles]]** — conformity-by-design (structural > disciplinary) · aggressive/layered
  memoization · descriptor-not-callback seams · hot-path discipline.
- **[[koan-redesign]]** — fewer-but-more-meaningful parts · consolidation = deletion · dogfood-gated
  convergence (≥2 existing consumers) · green ratchet. Target map: [foundation-consolidation-plan.md](./foundation-consolidation-plan.md).
- **Method + boundaries** — [[koan-architect-working-style]] (verify empirically; re-derive agent/review
  findings against current source before coding) · [[no-stopgaps-full-implementation]] ·
  [[break-and-rebuild-preferred]] · [[koan-ergonomics-first-no-csharp-ceremony]] ·
  **[[persona-separation-no-gposingway]]** (never the user-platform names). Commit on `dev`, **don't push**.
  Newtonsoft canonical. **Ultracode is on → author Workflows for substantive phases.**

## Method — per facet / capability

1. **Side-discovery** — a convergence survey (Workflow): what bespoke parallel implementations could this
   facet collapse onto one meaningful primitive? (The storage pipeline collapsed partition/timestamps/
   identity; classification reuses it; the ambient carrier collapses 7 mechanisms.) Output: the convergence
   opportunities to fold into the ADR.
2. **ADR(s)** — establish or adjust, capturing scope; **adversarial review (Workflow)** before ratification
   (this caught real canon-errors twice on DATA-0105).
3. **Phased TDD** — RED → GREEN → **mutation-check**; **ARCH-0079 real-store integration spec** every step.
4. **Exhaustive tests** — isolation/no-leak proofs across adapters, **allocation benchmarks (0-bytes/op)**,
   the conformance kit, the full **green ratchet**.
5. **Persist** — memory + this ledger + commit.

## ADR inventory (the "necessary ADRs")

| ADR | Scope | Status |
|---|---|---|
| ARCH-0084 | Unified capability model (Facet 1) | ✅ Accepted |
| ARCH-0086 | `KoanModule` boot primitive (Facet 2) | ✅ Accepted |
| [ambient-context-charter](./ambient-context-charter.md) | Facet-3 ambient truth-test (11 Laws) | ⚠️ charter only — **needs an Ambient-unification ADR** (collapse the 7 ambient mechanisms onto one carrier; tenancy rode `EntityContext` directly, the full unification is unbuilt) |
| ARCH-0094 | The Adapter Forge | ✅ Accepted — **implement** (queued post-tenancy) |
| ARCH-0095 | Tenancy | ✅ Accepted — **§2 erratum filed** (4a = Route schema-qualifier, not a name particle; net-new Route machinery) |
| **ARCH-0096** | Identifier-composition primitive (anchor + ordered particles) | ✅ **Authored (Proposed)** — empirically-scoped: data+cache = the dogfood-2; vector already converged; jobs/blob = same-shape follow-ons; tenant = cache-key particle; `Koan.Core.Naming` home (layering verified acyclic) |
| **DATA-0105** | Storage-composition contributor pipeline | ✅ **Finalized (Proposed, revised ×2)** — consumes ARCH-0096; descriptor-not-callback; **6 stages (Key dropped)**; 3 memo planes; sync applicators; must-fixes i–xi + upgrades A–D folded; 3-lens spot-check ship-ready |
| **Classification** | The data-classification axis (`[Pii]`/`[Phi]`/`[Secret]`, layered policy) | ☐ **TO AUTHOR** — folded in ARCH-0095/tenancy-design today; it is a sibling capability and the pipeline's 2nd consumer → own ADR |
| **Facet 4** | (undefined here) | ☐ **TO SCOPE** via side-discovery against [foundation-consolidation-plan.md](./foundation-consolidation-plan.md) → ADR(s) |

## The sequence (dependency-ordered; refine via side-discovery)

1. **Foundational ADRs** — author **ARCH-0096** + final-revise **DATA-0105** + the **ARCH-0095 §2 erratum**.
2. **Storage pipeline impl** — **phase 0a** standalone memo/determinism fixes (`ProjectionResolver`/
   `IndexMetadata`[+determinism @:35 & :41]/`AdapterNaming.GetOrCompute`/base-name→(Type,adapter)) → the
   pipeline model (descriptor base · 3-plane cache · structural closure via typed-slice · deterministic
   ordering · 0-alloc benchmark · `IsInvariantOnly` fast path · off=structurally-absent) → convergence
   (write-stamp; name-particle re-homing partition onto ARCH-0096, **data + cache first**; schema-column).
3. **Tenancy = registration** on the pipeline — the 8 primitives (P8 internal), kernel, control-plane keyed
   entities, membership-gate-above-roles, lifecycle sagas, **erasure certificate** — + **exhaustive tests**
   (`AssertNoTenantLeak` across adapters, SQLite-first no-Docker proof; two-shaped read-filter incl.
   get-by-id; durable-carrier stamping).
4. **Classification axis** — side-discovery → ADR → impl (the layered policy; encrypt/tokenize/mask as
   Serialize contributors; the serialization/record hook for bare-entity stores; the 2nd pipeline consumer
   that retro-justifies the read-filter/serialize seams).
5. **Ambient unification** — the charter → ADR → collapse the 7 ambient mechanisms onto one carrier (promote
   `EntityContext`; re-home AI scopes + cache behavior; delete `CacheScope`/`_override`; typed slices).
   *Sequencing open:* tenancy already rode `EntityContext`; re-derive whether this lands before, with, or
   after the pipeline via side-discovery (it is Facet 3's core, tenancy is its flagship slice).
6. **Adapter Forge** (ARCH-0094) — impl; the **Conformance Gate = the pipeline's structural check + the
   tenancy P7 isolation kit**; agent-authored conformance-gated adapters.
7. **Facet 4** — scope via side-discovery → ADR(s) → impl + tests.
8. **Cross-cutting completion** — observability/measured-cost (tenancy §11), the durable-carrier schema
   (messaging outbox `TenantId`, DLQ classified-stripping), the read-guard collapse (tenant filter +
   SEC-0004 `Constrain` + WEB-0068 into one ordered chain), the compliance-posture boot report.

## Side-discovery checkpoints (the convergence harvest)

At each facet boundary, before the ADR, survey for "fewer but more meaningful parts": the latent twins the
storage pipeline already revealed (the 4 identifier-composing surfaces; the 3 read-guard mechanisms; the
durable-carrier stampers; the per-entity manifest serving hot-path + boot-report + Forge fingerprint). Each
opportunity that clears the dogfood gate (≥2 existing consumers) folds into the facet's ADR as a deletion.

## Test objective (exhaustive)

Every capability: an ARCH-0079 real-store integration spec + a mutation-check; the isolation/no-leak proofs
across **every** adapter (not a sample); the 0-bytes-per-op allocation benchmarks; the capability-honesty +
conformance kit; the green ratchet stays green throughout. "Done" = the capability the ADR describes is
implemented and its tests pass on real stores.

## Status ledger (running — update as work proceeds)

- ✅ **Tenancy design** — ARCH-0095 (3 external review rounds, unanimous ship). Slices **1a** (ambient
  `Tenant` carrier) + **1b** (fail-closed chokepoint gate) committed (TDD+mutation, data-core suite 174/174).
- ✅ **Pivot** to the storage-composition contributor pipeline; exhaustive inventory + memoization survey.
- ✅ **DATA-0105** drafted + **two** full adversarial review rounds; **cross-pillar decision ratified** (full
  cross-pillar axis-composition primitive). **Design principles → [[koan-design-principles]].**
- ✅ **Foundational ADRs DONE** — empirical survey (6 Explores) re-grounded the cross-pillar scope (vector
  already converged onto `StorageNameGenerator`; the real dogfood-2 is data-name + cache-key; a 5th instance
  `JobTypeBinding.CoalesceKey` + blob-binding are same-shape follow-ons; tenant is a cache-key particle, not a
  name particle). **ARCH-0096** authored (identifier-composition primitive, `Koan.Core.Naming`, descriptor
  model, 3-plane memo, layering verified acyclic). **DATA-0105** finalized (6 stages, Key dropped, descriptor
  model, all must-fixes i–xi + upgrades A–D). **ARCH-0095 §2 erratum** filed. 3-lens spot-check (coherence /
  layering / adversarial-factual) = unanimous ship-ready, 0 blocking.
- ✅ **Phase 0a DONE** — the 4 extracted standalone memo/determinism fixes, each TDD'd green-ratchet:
  `IndexMetadata` determinism (`:35` Guid group-key → attribute-position; `:41` Dict iteration → explicit
  insertion order) + Type-cache (`11e4439f`); `ProjectionResolver` Type-cache (`f2638aa0`);
  `AdapterNaming.GetOrCompute` per-`ServiceProvider` factory-lookup cache via `ConditionalWeakTable`
  (`81f2f2e1`); base-anchor split off partition in `StorageNameGenerator` (`28b30a42`). Data-core suite
  174 → **190/190** (16 new specs). Each behaviour-preserving except the determinism fix (a correctness win).
- ◐ **NEXT: Phase 0b** — the **ARCH-0096 engine** in `Koan.Core.Naming` (`IdentifierComposer` + `Particle`/
  `CompositionPolicy` readonly structs + `ParticleDescriptor` `[KoanDiscoverable]` discovery + the 3-plane
  cache + the 0-alloc benchmark) **then** the **DATA-0105 descriptor base** (`IStorageContributor`, plane tree,
  structural closure, deterministic ordering, `OFF=structurally-absent` + `IsInvariantOnly` fast path).
- ☐ Phase 1 write-stamp · 2 name-particle · 3 schema-column · 4 tenancy=registration+SQLite proof ·
  5 classification · ambient unification · Adapter Forge · Facet 4 · cross-cutting completion.

> Full per-area detail + the DATA-0105 review punch-list (must-fixes i–xi, upgrades A–D, opportunities):
> memory **[[facet3-tenancy-design]]** (the anchor). Tenancy spec: [tenancy-design.md](./tenancy-design.md).
