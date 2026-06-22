---
type: RFC
domain: core
title: "Koan Multi-Tenancy — External Design Review, Round 3 (Final)"
audience: [frontier-models, external-architects]
status: open-for-review
last_updated: 2026-06-21
---

# Koan Multi-Tenancy — External Design Review, Round 3 (Final)

> **This is the closing adversarial pass before the design hardens into an ADR and implementation.**
> Two prior rounds plus a four-persona delight harvest have converged it — the last round produced *zero*
> design reversals, only refinements. So we are **not** fishing for more features. We are asking for the
> things we'd **regret not addressing at v1**, the reasons a team would **not** adopt, and one honest
> self-check: a framework whose entire thesis is *"fewer but more meaningful parts"* has, across three
> rounds, grown to **eight primitives + a classification axis + a layered policy engine + erasure
> certificates.** Did we rebuild the monster we set out to kill? Tell us straight.
>
> If you reviewed earlier rounds, focus on §3 (deltas) and §5 (the closing questions). If you're new, §2
> is the converged picture and the round-1/round-2 RFCs hold full background.

---

## 0. What we're asking this final round

1. **Last-call holes — the regret class.** Not "what's missing" (we've heard a lot); specifically:
   *what will we wish we had designed in at v1 because retrofitting it at v1.4 will hurt?* The
   data-model decisions, the seams, the invariants that are cheap now and expensive later.
2. **Critique the newest piece: the layered classification policy (§3.1).** It's the only substantial
   thing that changed since round 2. Is the *declared-fact vs. layered-handling* split airtight? Is the
   *solution-level mutability lock* the right safety mechanism? Does *identity-PII-as-solution-level-only*
   actually resolve the multi-tenant-user conflict, or hide a new one?
3. **Verdict on the three still-open decisions (§4).** Migrations (additive-only vs. expand/contract),
   the P8 saga coordinator, and the adoption surface. We have leans; we want your call.
4. **The why-NOT.** We've collected a lot of "why Koan." Now argue the other side: **what stops a team
   from adopting**, what's the deal-breaker, where does the "owns every axis" thesis become a liability
   rather than an advantage?
5. **The coherence / accretion self-check.** Is the *developer-facing* surface still small, or has the
   design accreted? Is "fewer but more meaningful parts" still true, or aspirational?
6. **The final 10% of delight.** With the whole design now visible, is there a delight we're
   *structurally positioned for* and still haven't named?

Reply structure: **(a)** last-call/regret holes · **(b)** layered-policy critique · **(c)** open-decision
verdicts · **(d)** why-not / adoption barriers · **(e)** coherence check · **(f)** final delight.

**Context that frees the critique:** Koan is pre-1.0, single-author, .NET-only, not yet battle-tested at
scale. No backward-compat constraint. Be harder on us this round than the last two — a closing review
that only confirms is a wasted review.

---

## 1. 60-second primer

**Koan** is an entity-first .NET 10 application meta-framework. Static methods on entities
(`Todo.Get(id)`, `todo.Save()`); a package reference auto-activates a capability ("Reference = Intent");
the same entity code runs across SQL/NoSQL/Vector/JSON. **The load-bearing fact: Koan owns *every*
backend pillar in one runtime** — data, web, cache, vector, jobs, messaging, storage, auth, AI,
observability — so a cross-cutting concern can be a property of the runtime instead of a predicate every
query must remember. Tenancy is the flagship slice of an ambient-context primitive.

---

## 2. The converged design (stable across the last round)

**Validated base (unchanged through all rounds):** identity is global / membership is per-tenant / roles
live on the membership · the tenant gate is evaluated *before* the role check · membership is resolved
per request, never trusted from the token · immutable tenant id + mutable alias codes (the code *is* the
entity key → O(1) resolve + global-uniqueness-by-key) · same DX for reads/writes · **tenant survives the
async hop** (auto-flows into jobs/messaging because Koan owns them).

**The 8 load-bearing primitives** (each dogfoods an existing pillar — not net-new infrastructure):

| | Primitive | Role |
|---|---|---|
| P1 | Chokepoint guard | read-filter + write-guard at the lowest data-adapter level (RLS is the backstop) |
| P2 | Tenant state machine | `Provisioning/Active/Suspended/Relocating/Erasing/Erased`, enforced at the chokepoint |
| P3 | Suspend-as-quiesce | a real enforcement state; the quiesce step Erase + Relocate need |
| P4 | Logical Export/Import | tenant data via the entity model → backup, branching, snapshot |
| P5 | Migration fan-out (a: executor, b: fleet orchestrator) | placement-aware, resumable, canary |
| P6 | Connection broker | per-tenant routing, pool governance, guaranteed session reset, honest boot-report limits |
| P7 | Isolation test-kit | assert-no-leak + property-based fuzz, regenerated every build |
| P8 | Saga coordinator | phase gates / compensation / rollback; the four lifecycle sagas compose from it |

**Plus** two seams (`ParentTenantId` for deferred hierarchy; `[ProjectedToHost]` for cross-tenant
read-models), and a **data-classification axis** (a *sibling* capability, detailed in §3.1).

**Honesty posture (a deliberate feature):** the design states its limits — it does not repeal the
scaling cliffs (db-per-tenant pool fan-out, schema-per-tenant catalog bloat); Relocate is a write-suspend
maintenance window, not magic 2PC; Erase is a verifiable state machine with a defined partial-failure
path, eventually-consistent within a quiesced window; backups age out by retention, not surgical delete;
RLS is the named non-structural backstop for raw-SQL/Direct access.

**Control plane:** registry/identity/membership/lifecycle are `[HostScoped]` entities + jobs in a root
store holding *only* control-plane data; no "tenant-zero"; cross-tenant power is host-plane-only and
audited.

---

## 3. Deltas since round 2

### 3.1 Decision resolved — the layered classification policy (critique this hardest)

Round 2 left open: how to reconcile data classification (isolate PII/PHI) with the AI/vector pillar and
with "medical-grade vs. photo-site" reality. The resolution is a **three-tier layered policy**, built on
one distinction:

- **Classification is a declared FACT.** `[Pii]` / `[Phi]` on a field asserts *"this is sensitive data."*
  No config layer can un-declare it. It is truth, set by the developer.
- **Handling is layered POLICY.** *How* that fact is treated (co-locate / field-encrypt / isolate-in-vault
  / region-pin / embeddable / retention) is resolved across three tiers, with a lock.

```csharp
// DEVELOPER — declares facts, and hints desires
public class Patient : Entity<Patient> {
    [Phi] public string Diagnosis { get; set; }              // FACT
    [Phi(Embeddable = true)] public string Notes { get; set; } // FACT + a HINT ("I'd like to embed this")
    [Pii] public string Email { get; set; }                  // FACT
}
```
```jsonc
// SOLUTION OWNER — sets the default AND the mutability lock (the safety mechanism)
"Koan:DataClassification": {
  "Phi": { "posture": "Isolate",  "mutability": "CannotChange" },  // a floor tenants cannot breach
  "Pii": { "posture": "Isolate",  "mutability": "MayChange" }      // tenants may relax this one
}
```
```jsonc
// TENANT — consulted ONLY where the solution marked the item "MayChange"
"Koan:Tenant:photo-co:DataClassification": { "Pii": { "posture": "CoLocate" } }
// → photo-co's PII co-locates (permitted); but PHI stays Isolate everywhere (locked).
```

The resolution and its properties:
- **Effective handling = resolve(developer hint → tenant override [only if unlocked] → solution default + lock).**
- **The mutability lock is "policy gate above tenant"** — it mirrors the "tenant gate above roles" rule:
  the solution owner sets a floor a tenant cannot relax. Without it, a tenant could downgrade a medical
  solution's protection — a compliance hole.
- **Lock default is classification-aware:** sensitive (`[Pii]`/`[Phi]`) → *locked + protected* by default
  (the owner must deliberately open it); non-sensitive config → open by default.
- **Identity-PII handling is solution-level ONLY** — never tenant-overridable. The identity is a *global,
  shared* record, so the platform governs how it handles a human's PII; one tenant can't relax it. This
  resolves the multi-tenant-user conflict (a human in an EU medical tenant *and* a US photo tenant) —
  their shared identity is governed by the platform, not by whichever tenant they're acting in. (Residency
  still follows the identity's own home region.)
- **Denied hints degrade honestly at runtime — never a build error** (preserving same-DX: the same code
  ships everywhere). `[Phi(Embeddable = true)]` under a locked `Isolate` posture is simply *not* embedded,
  and the boot report says exactly why:
  ```
  Classification: Patient.Notes [Phi] — requested Embeddable=true; DENIED by solution policy
                  [Phi: Isolate, Locked]. Field excluded from the vector/AI axis.
  ```
- **This is the answer to the round-2 classification×AI tension:** embeddability is no longer a global
  framework call — each deployment answers it by policy, and the AI pillar honors the resolved posture.
- **It generalizes:** "layered policy with solution-level locks" is simply *how every tenant-overridable
  setting works* (the tenant capability profile, with the solution owner deciding which knobs turn). PII
  is the highest-stakes instance, not a special case.

### 3.2 The delight north-stars (from the four-persona harvest)

A blind harvest across developer / architect / operator / competitive lenses produced **zero reversals**
and crowned, unanimously, one flagship:

- **The erasure certificate** — a cryptographically-signed `Tenant.Erase()` receipt with per-axis purge
  counts (rows, cache, vectors, search, blobs, logs). The competitive point: every incumbent proves
  deletion from the DB; **none can prove it from the other axes, because they don't own them.** Timed to
  the Feb-2026 EDPB enforcement shift (controller must now *demonstrate* disposition). It turns the
  scariest unprovable compliance task into a build artifact.
- **Persona magic moments:** developer — *"the leak you literally cannot write"*; architect — *"the
  day-one tenancy decision stops being a decision"* (isolation is a reversible dial); operator — *"at 3am,
  'which tenants are affected?' is already answered"* (the tenant is auto-stamped on every axis).
- **Competitive grounding:** RLS has ~8 cataloged silent-leak modes and is row-only; GoodRx's in-house
  vault cost 6 months / 10 engineers / 18-month MVP (classification-as-a-posture-flag erases that
  build-vs-buy); Clerk teams "migrate out ~month nine" when residency is demanded (the relocate dial);
  Neon branches the DB only (Koan branches vectors/blobs/cache/jobs too).

### 3.3 Refinements folded in

- **Observability cardinality split:** tenant as a *forensic* trace attribute (sampled, always present)
  vs. a *SLO* metric label that's bounded and tiered by the registry's tier field.
- **The registry is the canary-cohort selector** — tenant-scoped feature flags + canary-by-cohort fall
  out of the registry being live, coherence-invalidated truth.
- **Auto-masking extends to the search index and the log line**, not just API responses — minimum-necessary
  as a runtime property, composing with the capability authz projection.

---

## 4. The three still-open decisions — your verdict wanted

1. **Migrations.** Additive-only in v1 (the canary *refuses* a detected-breaking change), with
   expand/contract documented as the pattern for breaking changes? Or commit to automating expand/contract
   in the framework now (so version-skew during a canary is safe by construction)? — *Our lean:
   additive-only v1.* The risk: a developer writes a breaking change, the canary runs old+new schema
   simultaneously, and data corrupts silently. Is "refuse breaking changes in the canary" a sufficient
   guardrail, or a false promise?
2. **P8 saga coordinator as the 8th primitive.** Relocate, Erase, posture-migration, and schema-fan-out
   are all sagas. Name one coordinator they compose from? — *Our lean: yes — it consolidates four ad-hoc
   state machines into one.* Or does a general saga primitive over-generalize four genuinely-different
   consistency models into a leaky abstraction?
3. **Adoption surface.** Does tenancy *core* (P1–P3, P7) work at the `Koan.Data` level, with the
   multi-axis flow as additive value when you adopt more pillars (no separate "Lite" SKU, consistent with
   Reference = Intent)? — *Our lean: yes, graceful layering.* Or does the value proposition collapse
   below a critical mass of adopted pillars, making partial adoption a trap?

---

## 5. The closing questions (what this round is really for)

- **(a) The regret class.** What's cheap to design in now and expensive to retrofit later? Candidates we
  suspect: the audit-event schema, the classification policy's resolution precedence, the saga
  coordinator's compensation contract, the connection broker's secret/credential model. What else?
- **(b) Layered-policy critique.** §3.1 — break it.
- **(c) Open-decision verdicts.** §4 — your call on each, with reasoning.
- **(d) Why-NOT / adoption barriers.** Argue against adoption. The candidates: **the "second framework"
  problem** (you must buy the whole Koan model); **.NET-only** in a polyglot world; **single-author /
  unproven-at-scale maturity risk**; **the owns-every-axis lock-in** (the advantage is also the
  dependency — what happens when a team wants Koan's tenancy but Pinecone's vectors, or a managed Redis
  Koan doesn't have an adapter for?); **operational surface** (8 primitives + a vault integration + a
  classification engine is a lot to run). Which of these is the *real* deal-breaker, and is any of them
  fatal?
- **(e) Coherence / accretion.** The honest one. We started at "collapse the backend into fewer, more
  meaningful parts." We now have 8 primitives, a classification axis, a 3-tier policy engine, sagas, and
  certificates. **Distinguish the developer-facing surface from the internal surface:** a developer still
  writes `Todo.Get()`, `[Pii]`, `using (Tenant.Use(x))`, and a config block — is *that* still small? Or
  has the conceptual load actually grown past what the thesis promised? Where, specifically, would you cut?
- **(f) The final delight.** With everything visible, the structurally-possible delight we still haven't
  named.

---

## 6. The arc (so you can judge the trajectory)

- **Round 1:** validated the thesis + the forks; corrected 6 over-promises; 20 proposals → 7 primitives.
- **Round 2:** surfaced the 8th primitive (saga coordinator); resolved PII into a classification mechanism;
  3 more honesty corrections; split P5.
- **Delight harvest (4 personas):** zero reversals; erasure certificate as the cross-persona flagship.
- **Round 3 (this one):** the layered policy resolves the last open mechanism; we want the regret-class
  holes, the why-not, and the accretion check before we commit.

**Be hard on us. A closing review that only confirms is a wasted review. Thank you.**
