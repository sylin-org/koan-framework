---
type: ARCHITECTURE
domain: core
title: "Koan Tenancy — External Review Findings & Negotiation"
audience: [architects, ai-agents]
status: proposed
last_updated: 2026-06-21
validation:
  status: design-only
  scope: docs/architecture/tenancy-external-review-findings.md
---

# Koan Tenancy — External Review Findings & Negotiation

> Three frontier models independently reviewed [tenancy-external-review-rfc.md](./tenancy-external-review-rfc.md),
> each structured (a) delight (b) holes (c) forks (d) validation (e) blind-spots. This document
> synthesizes their feedback (convergence = strongest signal), distills it into a small set of
> **load-bearing primitives**, lists the **honesty corrections** they forced, and records the
> **adopt / defer / reject negotiation** against Koan's principles. Status **proposed** — pending
> architect ratification before folding into [tenancy-design.md](./tenancy-design.md).

---

## 1. Convergence map (ranked by signal strength)

Where multiple independent reviewers landed on the same point, treat it as a finding, not an opinion.

| # | Finding | Models | Type | Severity |
|---|---|---|---|---|
| 1 | **Schema-migration fan-out is unaddressed** — rolling a migration across N tenants (schema/db-per-tenant) is the first wall at 50+ tenants; needs ordering, partial-failure/resume, canary, rollback-per-tenant, version skew | **A · B · C** | hole | **critical** |
| 2 | **Testing/isolation-proof is the biggest gap *and* the biggest delight** — there's no way for a dev to *prove* isolation; this is the answer to "why Koan over homegrown RLS" | **A · C** | hole + delight | **critical** |
| 3 | **`Relocate` is a saga, not a verb** — multi-phase (extract→create→copy→cutover); the consistency model during the transition window (writes mid-move, cache on old substrate, rollback) is undefined → split-brain risk | **A · C** | hole + overstated-claim | **critical** |
| 4 | **The connection-resolution seam is *the* load-bearing risk** — pool exhaustion at scale, credential/secret rotation, failover; needs a first-class connection broker + honest boot-report limits | **A · C** (+ B) | hole | **high** |
| 5 | **RLS connection-state poisoning** — `SET LOCAL tenant` not cleared on pool return → next request inherits prior tenant; brittle under concurrency/cancellation; must reset at the physical pool layer (fail the process, never return a tainted connection) | **B** | hole | **critical** |
| 6 | **`Tenant.None()` is the forgotten boundary** — devs will wrap slow/failing ops in it; must be fail-closed at the chokepoint + a taxonomy (loud ad-hoc escape vs. quiet `[HostScoped]` system work) | **A · C** | hole + overstated-claim | **high** |
| 7 | **`Erase` is eventually consistent** — fans across non-transactional axes; a write can sneak into cache after erase ran; GDPR is legally binding → must be a verifiable state machine + quiesce first | **A · B** | hole | **high** |
| 8 | **Tenant hierarchy: defer, but don't preclude** — keep v1 flat; add a nullable `ParentTenantId` + keep the discriminator a plain `tenant_id` so `IN (self+ancestors)` is retrofittable | **A · B · C** | fork | — |
| 9 | **SSO/SCIM boundary is right — but own the event hooks** (`OnIdentityLinked`, `OnMembershipRevoked`, membership CRUD, domain-verification) | **A · C** | fork | — |
| 10 | **"Same-DX across modes" is overstated for migrations** — entity code is uniform; *operational* migration DX varies wildly by placement | **A · C** | overstated-claim | — |
| 11 | **Observability cardinality needs an explicit strategy** — tenant as trace *attribute* (100% fidelity) but not a metric *label* by default; top-N labelled, rest bucketed; per-tenant debug flag | **A · C** | blind-spot | — |
| 12 | **Identity (PII) vs. data residency** — `[HostScoped]` Identity in a central control plane may sit in a different region than a tenant's EU product data → GDPR violation | **B** | hole | **high** |
| 13 | **Guard at the chokepoint, not `Entity<T>.Save()`** — else direct/raw access bypasses it; RLS is the *backstop*, not the primary guard | **A** | fork/validation | — |
| 14 | **Tenancy activation cliff** — Reference=Intent + fail-closed means referencing the package instantly throws on every un-scoped op; needs a `warn → enforce` gradient | **C** | hole | medium |
| 15 | **`Suspend` semantics undefined** — block-writes-allow-reads vs block-all; must be enforced at the data chokepoint, not web middleware | **A** | blind-spot | medium |

**Validated as genuinely sound (all three, strongly):** the core thesis (own-every-axis → tenant is a
runtime property, not marketing); identity-global/membership-per-tenant; tenant-survives-the-async-hop
(the biggest competitive edge); reject-tenant-zero; tenant-gate-prior-to-role; immutable-id + keyed-codes;
same-DX for reads/writes; the pain-harvest convergence is real signal, not a local optimum.

**Overstated claims to correct (see §3):** same-DX-for-migrations · "cannot forget the boundary"
(the escape *is* the gap) · Erase-fans-out (eventually consistent) · Relocate-as-a-verb · "operator
console exists by default" (the *data* does; the console is a projection you build) · "addresses scaling
cliffs" (handles *choice + movement*, not the cliffs).

---

## 2. The load-bearing primitives (the "fewer but more meaningful parts" lens)

The reviews proposed ~20 features. Applying the redesign discipline (don't add surface; add a few
meaningful parts that many things compose from), almost all of them reduce to **seven primitives**.
This is the negotiation's spine — build these, and the flashy delights become roadmap built *on* them:

| Primitive | What it is | Composes into |
|---|---|---|
| **P1 · Chokepoint guard** | read-filter + write-guard at the lowest data-adapter level (not `Entity.Save`); RLS is the backstop | all enforcement; `Tenant.None()` constraint; `Suspend` |
| **P2 · Tenant state machine** | `Provisioning/Active/Suspended/Relocating/Erasing/Erased`, enforced *at the chokepoint* | `Suspend`, `Erase`, `Relocate`, billing-block |
| **P3 · `Suspend`-as-quiesce** | a real enforcement state (block-writes / block-all) at the chokepoint | the quiesce step `Erase` & `Relocate` both need |
| **P4 · Logical Export/Import** | dump/upsert all of a tenant's entities via the entity model (JSON/BSON) | backup/restore · `Provision(from:)` branching · snapshot · BYOC migration |
| **P5 · Migration fan-out runner** | placement-aware, job-orchestrated, resumable, canary-capable, version-skew-tolerant | schema migrations · part of `Relocate` · drift detection |
| **P6 · Connection broker** | first-class data-pillar component: per-tenant routing, pool governance, guaranteed session reset, honest boot-report limits | db-per-tenant viability · RLS-poisoning fix |
| **P7 · Isolation test-kit** | run the entity surface under mismatched tenant contexts, assert the guard fired; property-based fuzz; in-memory N-tenant sim | `AssertNoTenantLeak` · `[TenantIsolated]` · `Tenant.Simulate` · the "prove it" delight |

Each dogfoods an existing pillar (P5/P2/P3 ride **jobs**; P4 rides the **entity model**; P7 rides the
**ARCH-0079 TestKit**; P6 extends the **data pillar** + **capability model** + **self-reporting**), so
none is net-new infrastructure — they're tenant-aware wiring of load-bearing chokepoints. That is the
principle-aligned shape.

---

## 3. Honesty corrections (fold into the design + the RFC's claims)

Koan's self-reporting/no-boot-lies principle demands these claims be made precise:

- **"Same DX across modes"** → *"same entity code across modes; operational migration DX is
  placement-aware, and the framework orchestrates the fan-out (P5)."*
- **"You cannot forget the boundary"** → *"...except one explicit, audited, fail-closed escape, which
  is itself constrained (P1) so it can't silently become the forgotten boundary."*
- **"Lifecycle fans out across every axis" (Erase)** → *"Erase is a verifiable state machine
  (quiesce → fan-out → verify → certify); the fan-out is eventually consistent within a quiesced window,
  then verified."*
- **"Relocate as a verb"** → *"a verb backed by an explicit saga with a defined consistency model
  (quiesce / copy / atomic cutover / verify / rollback)."*
- **"The operator console exists by default"** → *"per-tenant operational data is collected by default;
  the console is a projection built on it."*
- **"Addresses scaling cliffs"** → *"makes substrate choice and movement cheap and visible; does not
  repeal the cliffs."*

---

## 4. The negotiation (adopt / seam-now / roadmap / architect-decision / scope-out)

### 4a. Adopt into v1 (high-convergence, principle-aligned, fills a real gap)

1. **P1–P7 the load-bearing primitives** (§2). These are the design's new spine.
2. **`Relocate` and `Erase` reframed as sagas** (P2 + P3 + P5) with explicit consistency models — the
   #3/#7 corrections. No-stopgaps demands the saga, not the verb-pretense.
3. **`Tenant.None()` constrained + the escape taxonomy** (P1): fail-closed at the chokepoint (host scope
   touches only `[HostScoped]`; a tenant-scoped write under `None()` still throws unless an explicit,
   source-gen-flagged `[AllowUnscopedWrite]` capability is present). `[HostScoped]` = quiet legitimate
   system work; `Tenant.None()` = loud audited ad-hoc escape.
4. **Migration fan-out runner** (P5) over the jobs ledger — the #1 convergent hole.
5. **Connection broker** (P6) + **boot-report honesty** ("db-per-tenant: max recommended tenants = N
   given pool size M") + **guaranteed RLS session reset** (fail the process, never a tainted connection).
6. **Isolation test-kit** (P7) — `AssertNoTenantLeak`, `[TenantIsolated]` theory attribute, property-based
   fuzz in CI. This *is* the "why Koan over homegrown RLS" answer; ARCH-0079 makes it canon.
7. **Identity event hooks** (#9): keep the SSO/SCIM boundary, own `OnIdentityLinked` /
   `OnMembershipRevoked` / membership CRUD / domain-verification so the IdP drives Koan's Membership.
8. **Suspend defined + chokepoint-enforced** (#15, P3): a first-class enforcement state, the quiesce
   primitive lifecycle needs.
9. **Tenancy activation gradient** (#14): a mode ladder `off → warn → enforce` so Reference=Intent
   doesn't instant-cliff; default to `warn` on first activation, explicit flip to `enforce`.
10. **Observability cardinality strategy** (#11): tenant = trace *attribute* always; metric *label* only
    for top-N + a per-tenant debug flag; bucket the rest. Rides Koan.Observability.
11. **Tenant-scoped configuration** (Model C blind-spot): per-tenant feature flags / limits / plan gates
    / branding ride `Tenant.Policy` and **compose with the capability model** (a tenant carries a
    capability profile — elegant reuse).
12. **Leak siren** (Model A): a guard rejection emits a structured `TenantBoundaryViolation` audit/security
    event with forensics (ambient tenant, target, entity, stack), not just a throw. Cheap, high-value.

### 4b. Seam now, behavior deferred (don't preclude)

13. **Tenant hierarchy** (#8, all three): v1 flat; add nullable `ParentTenantId` to the registry now;
    keep the discriminator a plain `tenant_id`. No behavior, no later data migration.
14. **`[ProjectedToHost]`** (Model A): the declared cross-tenant read-model seam (coherence channel syncs
    tenant-scoped → host read model) — the principled answer to cross-tenant reporting. Define the
    attribute/seam; the sync engine can land later.

### 4c. Roadmap delights (build on the v1 primitives; flag, don't build yet)

15. **`Provision` as a composable pipeline** (Model C): `.Seed<>().RunStep<>().Notify<>()`, each stage a
    durable job — adopt this *shape* for Provision (better than a monolith).
16. **Trial tenants with TTL** (Model A): `Provision(ttl:)` auto-schedules `Erase`, cancelled on
    conversion. Builds on P2 + jobs.
17. **Tenant branching / snapshot** (Model A): `Provision(from:, Snapshot)` — builds on P4 (logical) +
    cross-axis snapshot (the meta-framework superpower; true point-in-time is the hard part).
18. **Act-as as first-class `sudo`** (Models B, C): time-boxed, logged, visual indicator, auto-expiry.
19. **Schema drift detection** (Model C): the framework knows each tenant's expected schema → flag drift.
    Builds on P5.
20. **Tenant-aware dev mode** (Model C): a dev tenant-switcher, color-coded by context.

### 4d. Architect decisions required (not features — design forks)

21. **Identity/PII residency** (#12, Model B — the sharpest new issue). The `[HostScoped]` control plane
    holds human PII centrally, which can violate a tenant's regional residency. **Proposed direction
    (architect to ratify):** *split identity* — a **global minimal anchor** (opaque id + auth subject,
    no PII) lives central; **regional identity-detail** (email/name/PII) lives in the identity's home
    region, referenced by the anchor. This honors the surrogate-key discipline (the global record is
    just an opaque key) and makes the control plane **regionally shardable**. Alternative: a fully
    region-sharded control plane with a thin global identity→home-region router. **Open.**
22. **Control-plane placement** (#... Model B noisy-neighbor): the root store already holds only
    control-plane data (no contamination) — confirm it is independently *placeable* (own substrate/region)
    so operator-projection queries can't degrade premium tenants. Mostly falls out of existing decisions.

### 4e. Scope-out of v1 (acknowledge as north-stars the primitives *enable*, don't build)

23. **BYOC via `Relocate`** (Model B — "the hardest B2B objection in one method call"): genuinely
    compelling, but a massive distributed-systems + trust-boundary undertaking (a Koan node in the
    customer's VPC, control plane spanning trust domains). **Build P4 + the Relocate saga so BYOC is
    *tractable* later; do not build BYOC in v1.**
24. **Full tenant-aware canary deploys / per-tenant logic versioning** (Models B, C): the ambient carries
    a version slice; the capability model announces "Domain Logic v2." Powerful but it's *application*
    versioning, a larger scope than data tenancy — possibly its own facet. The v1 subset is tenant-scoped
    feature flags (#11). Defer the full canary-by-tenant.

---

## 5. Net effect

The external reviews did **not** overturn the design — they validated the structural thesis and the
settled forks, and corrected six overstated claims toward honesty. Their twenty proposals collapse to
**seven load-bearing primitives + two seams + one genuinely-new architect fork (PII residency)**, every
one of which dogfoods an existing pillar. The single largest addition — the **isolation test-kit (P7)** —
is also the strongest *positioning* win: it converts "trust our isolation" into "here's the proof,
regenerated every build," which is the answer to the reviewers' "second-framework / why-Koan" question.

**Next:** architect ratifies §4 (especially the residency fork §4d-21) → fold the adopted set into
[tenancy-design.md](./tenancy-design.md) → then enforcement-mechanics detail (now anchored by P1/P6).

---

# Round 2 — deltas (3 more frontier reviews on the round-2 RFC)

The round-2 RFC (the 6 corrections + 7 primitives + the classification mechanism) drew 3 more reviews.
They validated the round-1 changes, demanded **3 further honesty corrections**, surfaced an **8th
primitive** (a saga coordinator — the "fewer but more meaningful parts" answer to four ad-hoc sagas),
**unanimously** crowned the **erasure certificate** as the flagship delight, and exposed the **deepest
tension** (classification vs. the AI/vector pillar).

## R2.1 — Unanimous (all 3 reviewers) — act on these

| Finding | Type |
|---|---|
| **P5 is not one primitive — split it** (≥2: executor + fleet-orchestrator; the orchestrator is a saga; one reviewer says 3) | correction |
| **The Erasure Certificate** — cryptographically-signed, per-axis counts, retention exceptions — is THE round-2 delight + the "why-Koan" artifact for regulated SaaS | delight |
| **"Atomic cutover" / "saga" still hand-waves CAP** — Relocate needs a P3 write-suspend window (honest: "zero-downtime-read, blocked-to-write"); Erase needs a defined partial-failure path (no silent `Certify`) | correction |
| **3D fleet compliance matrix** (Tenant × Region × Isolation × Classification + compliance status) | delight |

## R2.2 — The 8th primitive: a saga/lifecycle coordinator (the key structural catch)

Relocate, Erase, posture-migration, and schema-fan-out are **all sagas**, currently designed ad hoc.
Name **P8 · saga coordinator** (phase gates, compensating actions, rollback policy) over the jobs
ledger; the four lifecycle sagas compose from it. This is the round-1 "20→7 primitives" discipline
applied to the corrections themselves. **Running count: 8 primitives (P1–P8) + 2 seams.**

## R2.3 — Three more honesty corrections

- **Relocate** "atomic cutover" → "a **P3 write-suspend (quiesce) window** during cutover;
  zero-downtime-to-read, **blocked-to-write maintenance window**." No magic 2PC across substrates.
- **Erase** → add a partial-failure resolution path: a `Verify`-fails terminal (`EraseFailedStuck`) with
  a defined recovery (retry/re-queue the failed axis); never silently `Certify`. Erase is a one-way door.
- **Same-DX** gains two more asterisks: "(1) migration DX is placement-aware; **(2) READ PERFORMANCE
  scales with classification posture** (detokenization); **(3) cross-ENTITY operations** (joins,
  multi-entity transactions) are **not mode-invariant** across substrates — boot-reported."

## R2.4 — P5 split + the expand/contract correction

- **P5a · migration executor** — apply one migration to one substrate; idempotent, resumable.
- **P5b · fleet orchestrator** — ordering, canary gates, rollback policy, version-skew; a saga, composes P8.
- **Version-skew REQUIRES expand/contract** (old+new schema run simultaneously). The framework must
  either enforce/automate expand/contract OR **restrict v1 to additive migrations only** — else a
  breaking change silently corrupts data mid-canary. **[ARCHITECT DECISION 1]**

## R2.5 — Classification refinements (from the critique)

- **Searchable-equality is first-class**, not "non-queryable by default": `[Pii, Searchable]` → auto
  blind-HMAC index (login flows need it); `LIKE`/range honestly denied + boot-reported.
- **Plaintext lives in a request-scoped identity map** (AsyncLocal, discarded at request end) — NEVER the
  distributed cache (which would pull it into compliance scope); this also fixes the detokenization N+1
  *within* a request. Batch-detokenize at the query chokepoint for result sets.
- **Opaque high-entropy tokens default; FPE is explicit-flagged** (FPE leaks on small domain spaces).
- **"Tokenized ≠ compliant"** (GDPR Recital 26: pseudonymized data is still PII for the *controller* who
  holds the detokenization key; tokenization = blast-radius reduction + *processor* residency, NOT
  controller-obligation elimination) — the boot report must say so.
- **Soft-enforce relations crossing a classification boundary** (FK co-located → vaulted breaks DB
  integrity); dangling tokens → graceful redaction, not a null-ref panic.
- **Identity-PII residency = the identity's OWN home region** (distinct from tenant residency) — resolves
  the multi-tenant-user conflict (a user in EU tenant A + US tenant B).
- **Key management**: per-tenant keys; rotation is a P5b/P8 job via a **KMS adapter seam** (don't build a KMS).
- **P4 Export/Import must define token handling on restore** (re-tokenize vs decrypt-on-export — cross-env
  restore of tokens is an industry nightmare).

## R2.6 — The deepest tension: classification × the AI/vector pillar  [ARCHITECT DECISION 2]

Tokenized `[Phi]` can't be embedded (a token is semantically meaningless) → classification **excludes
classified fields from the AI/semantic stack**, a real limitation given Koan owns AI. And
`[ProjectedToHost]` analytics over classified fields needs a cross-region detokenization fan-out that
**violates the very pinning it enforced.** Options: (a) exclude-by-default + boot-report (honest);
(b) vault-side embedding (hard); (c) embed-plaintext, store the vector classification-aware (the vector
is *derived* from PII — jurisdictionally debatable). **Lean: (a) exclude-by-default + an explicit
`Embeddable` opt-in** that surfaces the jurisdictional implication.

## R2.7 — Other new holes (adopt)

- **Connection broker pool starvation** (P6): reset session state *without teardown* where possible
  (DISCARD ALL / reset-reusable); teardown only on reset-failure.
- **Audit/leak-siren cardinality DDOS**: a looping cross-tenant-write bug emits hundreds of siren events →
  the security mechanism becomes an availability vector. **Rate-limit/aggregate** ("100 violations for
  tenant X in 1s"); add the **call site** to the siren payload.
- **Vector/search erase is slow** ("lingering ghost" — surgical delete from vector DBs is hard; fan-out
  may take hours): the Erase certificate distinguishes synchronously-purged axes from
  async-purging-with-ETA.
- **Classification posture migration is itself a saga** (flip CoLocate→Isolate migrates millions of rows;
  in-flight reads) — composes P8 + P5.

## R2.8 — More round-2 delights (adopt)

- **Compliance posture self-assessment in the boot report** ("HIPAA-compatible: PHI retained, PII
  erasable, audit ON") — the magic-moment delight + the self-reporting principle.
- **"Flip the config / compliance time-machine"** (Day-200 HIPAA via a config change + the
  posture-migration saga).
- **Context-aware auto-masking** (classification flows to *presentation*: admin sees masked, doctor sees
  plaintext, integration sees token) — composes with the SEC-0004 `can:[]` projection.
- **Classification drift detection** (plaintext lingering pre-migration → boot-report/health check).

## R2.9 — Architect decisions surfaced (round 2)

1. **Expand/contract vs additive-only migrations in v1** (version-skew correctness). *Lean:* additive-only
   v1 + the canary refuses a detected-breaking change; expand/contract documented for breaking changes.
2. **Classified × AI/vector** (exclude-by-default vs embed-plaintext-derived-vector). *Lean:*
   exclude-by-default + explicit `Embeddable` opt-in.
3. **P8 saga coordinator** as the 8th primitive. *Lean:* YES (the principled answer to four ad-hoc sagas).
4. **Adoption surface / "Koan.Tenancy.Lite"** — does tenancy core (P1–P3, P7) work at the Koan.Data level,
   multi-axis being additive value? *Lean:* YES, graceful layering, no separate SKU (consistent with
   Reference = Intent — each pillar is opt-in).

## R2.10 — Net (round 2)

The design held; the reviews drove it toward honesty and surfaced the **8th primitive (saga coordinator)**
as the unifying structure for the lifecycle sagas, plus the **erasure certificate** as the flagship
regulated-SaaS delight (the artifact that turns "owns every axis" into auditor-grade proof). The
classification mechanism is validated (sibling capability, seam + adapters, searchable-equality,
request-scoped plaintext, opaque tokens) with one deep open tension (classification × AI). **Running:
8 primitives (P1–P8) + 2 seams + 4 round-2 architect decisions.** Next: architect ratifies the round-2
decisions + the accumulated round-1 negotiation → fold all into tenancy-design.md → enforcement mechanics.
