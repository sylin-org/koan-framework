---
type: RFC
domain: core
title: "Koan Multi-Tenancy — External Design Review, Round 2"
audience: [frontier-models, external-architects]
status: open-for-review
last_updated: 2026-06-21
---

# Koan Multi-Tenancy — External Design Review, Round 2

> **Historical review input only.** This June 2026 RFC pressure-tests a proposed surface broader than Koan V1.
> Current supported behavior is defined by the [Tenancy package contract](../../src/Koan.Tenancy/README.md), the
> [Tenancy Web contract](../../src/Koan.Tenancy.Web/README.md), and generated product truth.
>
> **This is the second round of review.** Round 1 (the original RFC) drew three independent frontier
> reviews; their feedback converged sharply and reshaped the design. This document presents **what
> changed**, asks you to **analyze those changes** (did the reductions lose anything? do the corrections
> fully close the round-1 holes? what *new* holes do the changes introduce?), and asks you to
> **pressure-test one freshly-designed piece** — the PII/PHI data-classification mechanism — against
> prior art. As before: be adversarial, be specific, and **continue the delight investigation** now
> that the foundation is firmer.
>
> If you reviewed round 1, focus on the deltas. If you're new, §1 is a 60-second primer and the
> original RFC has full background.

---

## 0. What we're asking you this round

1. **Analyze the changes (§3–§4).** The 20 round-1 proposals were distilled to **7 load-bearing
   primitives + 2 seams**. Did that reduction lose anything important, or over-collapse distinct
   concerns? Are the **6 honesty corrections** (§3) sufficient, or do they reveal the design was
   over-promising in ways that aren't fully fixed yet?
2. **Find the *new* holes the changes introduce.** Reframing `Relocate`/`Erase` as sagas, adding the
   migration fan-out runner, the connection broker, and especially the **data-classification mechanism
   (§5)** — each adds surface. Where does the *new* surface break? Rank by severity.
3. **Pressure-test the PII/PHI design (§5).** This is the one genuinely-new piece since round 1. We
   grounded it in prior art (data-privacy vaults, split control/data plane, classification-driven
   policy). Is the mechanism right? What's missing? Is it correctly scoped (tenancy-adjacent capability
   vs. tenancy-core)?
4. **Continue the delight investigation.** Round 1 surfaced strong delights (the isolation test-kit,
   tenant branching, the leak-siren). With the foundation now firmer, **what's the next layer of
   delight** — especially around the data-classification mechanism and the operator story?

Reply structure: **(a)** analysis of the changes · **(b)** new holes (ranked) · **(c)** the PII/PHI
mechanism critique · **(d)** delight (round 2) · **(e)** what we're still not seeing.

---

## 1. 60-second primer (skip if you saw round 1)

**Koan** is an entity-first .NET 10 application meta-framework. You call static methods on entities
(`Todo.Get(id)`, `todo.Save()`); adding a package reference auto-activates a capability ("Reference =
Intent"); the same entity code runs across SQL/NoSQL/Vector/JSON providers. **The fact that matters:
Koan owns *every* backend pillar in one runtime** — data, web, cache, vector, jobs, messaging, storage,
auth, AI, observability — so tenancy can be a property of the runtime, not a predicate every query must
remember. **Tenancy is the flagship slice of an ambient-context primitive.** It's pre-1.0, single-author,
no backward-compat constraint — propose the *right* thing, and treat scale claims skeptically.

---

## 2. What round 1 validated (so you know the stable base)

All three reviewers independently validated: the core thesis (own-every-axis → runtime property);
**identity global / membership per-tenant / roles on the membership**; rejecting a "tenant-zero" control
plane; the **tenant gate evaluated before the role check**; immutable tenant id + mutable alias codes
(the code *is* the entity key → O(1) resolve + global-uniqueness-by-key); same-DX for reads/writes; and
**tenant survives the async hop** (Koan owns jobs+messaging, so the tenant auto-flows into background
work — the single biggest edge over ORM-bolted libraries). **None of this changed.** The deltas below
sit on top of this validated base.

---

## 3. Change-set A — six honesty corrections

Round 1 caught six overstated claims. Each is now stated precisely (Koan has a "no boot-lies" /
self-reporting principle; the design must not over-promise):

| Was claimed | Now stated as |
|---|---|
| "Same DX across all modes" | "Same *entity code* across modes; **operational migration DX is placement-aware**, orchestrated by a fan-out runner (§4)." |
| "You cannot forget the boundary" | "...except one explicit, audited, **fail-closed** escape, itself constrained at the data chokepoint so it can't silently become the forgotten boundary." |
| "Lifecycle fans out across every axis" (Erase) | "**Erase is a verifiable state machine** (quiesce → fan-out → verify → certify); fan-out is eventually consistent within a *quiesced* window, then verified." |
| "Relocate as a verb" | "A verb backed by an **explicit saga** with a defined consistency model (quiesce / copy / atomic cutover / verify / rollback)." |
| "The operator console exists by default" | "Per-tenant operational *data* is collected by default (the runtime tags every op); the **console is a projection** you build on it." |
| "Addresses scaling cliffs" | "Makes substrate **choice and movement** cheap and visible; does **not** repeal the cliffs (pool fan-out, catalog bloat)." |

**Question for you:** are these corrections *enough*, or do any of them (especially Erase and Relocate)
still hide a distributed-systems problem we're waving at with the word "saga"?

---

## 4. Change-set B — 20 proposals → 7 load-bearing primitives + 2 seams

Applying Koan's "fewer but more meaningful parts" discipline, the reviewers' ~20 proposals collapsed to
seven primitives, each of which **dogfoods an existing pillar** (so none is net-new infrastructure):

| | Primitive | Dogfoods | Composes into |
|---|---|---|---|
| **P1** | **Chokepoint guard** — read-filter + write-guard at the lowest data-adapter level (**not** `Entity.Save`, which is bypassable); RLS is the backstop, not the primary guard | data pillar | all enforcement · the `Tenant.None()` constraint · Suspend |
| **P2** | **Tenant state machine** — `Provisioning/Active/Suspended/Relocating/Erasing/Erased`, enforced *at the chokepoint* | jobs | Suspend · Erase · Relocate · billing-block |
| **P3** | **`Suspend`-as-quiesce** — a real enforcement state (block-writes / block-all) | P1/P2 | the quiesce step Erase + Relocate both need |
| **P4** | **Logical Export/Import** — dump/upsert a tenant's entities via the entity model | entity model | backup/restore · `Provision(from:)` branching · snapshot · BYOC (later) |
| **P5** | **Migration fan-out runner** — placement-aware, resumable, **canary-capable**, version-skew-tolerant | jobs ledger | schema migrations across N tenants · part of Relocate · drift detection |
| **P6** | **Connection broker** — per-tenant routing, pool governance, **guaranteed session-state reset** (fail the process, never return a tainted connection), and **honest boot-report limits** ("db-per-tenant: max recommended tenants = N given pool size M") | data + capability model + self-reporting | db-per-tenant viability · the RLS connection-poisoning fix |
| **P7** | **Isolation test-kit** — run the entity surface under mismatched tenant contexts, assert the guard fired; property-based fuzz in CI; in-memory N-tenant simulation | the integration-test canon | `AssertNoTenantLeak` · `[TenantIsolated]` · `Tenant.Simulate` · **the "prove it" answer to "why Koan over homegrown RLS"** |

**Two seams** (defined now, behavior deferred so we don't preclude): a nullable **`ParentTenantId`** on
the registry (flat v1, hierarchy later without a data migration), and **`[ProjectedToHost]`** (the
declared cross-tenant read-model seam — the coherence channel syncs tenant-scoped → host read model for
fleet analytics).

**Also adopted from round 1:** the `Tenant.None()` escape is fail-closed at the chokepoint with an
escape *taxonomy* (`[HostScoped]` = quiet legitimate system work; `Tenant.None()` = loud audited ad-hoc
escape); a **tenancy activation gradient** (`off → warn → enforce`, so Reference=Intent doesn't
instant-cliff every un-scoped op); **tenant-scoped config that composes with the capability model** (a
tenant carries a *capability profile* — feature flags / limits / plan gates); an **observability
cardinality strategy** (tenant as a 100%-fidelity trace *attribute*, but a metric *label* only for top-N
+ a per-tenant debug flag); and a **leak-siren** (a guard rejection emits a structured
`TenantBoundaryViolation` security event with forensics, not just a throw).

**Deferred as roadmap (built on the primitives, not built yet):** Provision-as-a-pipeline, trial-TTL
tenants, branching/snapshot, act-as-as-`sudo`, drift detection, dev-mode tenant switcher.
**Scoped out of v1 (north-stars the primitives *enable*):** BYOC-via-Relocate and full
canary-deploy-by-tenant.

**Question for you:** is the 7-primitive reduction honest, or did we collapse genuinely-distinct concerns
to make the count look small? Specifically — is P5 (migration fan-out) really one primitive, or is
"orchestrate schema changes across a heterogeneous fleet with canary + rollback + version-skew" three
primitives wearing a trenchcoat?

---

## 5. Change-set C (NEW) — the PII/PHI data-classification mechanism

Round 1's sharpest new hole: our control plane holds human PII (`Identity`) centrally, which can violate
a tenant's regional residency (a tenant's product data is correctly EU-pinned, but the human's
email/name sits in a US control plane). We investigated prior art before designing.

### What the prior art says

- **Data-privacy vaults are a mature, named category** — Skyflow, Very Good Security, Basis Theory,
  Evervault, Protegrity, HashiCorp. The pattern: isolate sensitive fields in a separate vault, replace
  them with format-preserving **tokens** in the app's own stores, detokenize on authorized read. Effect:
  downstream stores fall **out of compliance scope**. Applied *selectively* to sensitive fields, not all
  data.
- **Split control/data plane is the residency standard** — the global control plane holds **non-PII**
  tenant metadata; PII and product data live in **regional data planes**. ("A regional cell uses separate
  control-plane tables for tenant metadata devoid of PII.") This is exactly the round-1 "split identity"
  idea, and it's the convergent industry pattern — not a hack.
- **Classification-driven enforcement is the norm** — define PII/PHI/PCI tiers via tags/attributes;
  centrally declare classification; auto-enforce isolation/encryption/routing by tier (AWS Macie, Azure
  Purview, Protegrity, the data-catalog vendors).
- **The clinching argument against a single hard rule:** **HIPAA requires PHI retention** (you often
  *cannot* delete it), while **GDPR mandates right-to-erasure**. The same framework must serve both —
  which is impossible under one absolute rule and only works with **per-classification, per-jurisdiction
  policy**. (One nice consequence: erasure is *easier* with a vault — delete the vault record and every
  token dangles, erased everywhere at once.)
- **Field-level encryption** (MongoDB Queryable Encryption, per-tenant keys + blind-index HMAC for
  searchability) is the middle option between co-location and full vaulting.

### The proposed Koan mechanism — declarative data classification

Make sensitivity a **declarative axis**, the same way Koan already makes other concerns declarative
(Reference=Intent, capability attributes, `[Access]` gates). The developer classifies; the application
declares its posture; the framework enforces — **same entity code regardless of posture**:

```csharp
public class Patient : Entity<Patient>
{
    public string MedicalRecordId { get; set; }      // not sensitive
    [Phi] public string Diagnosis { get; set; }       // classified
    [Pii] public string Email { get; set; }           // classified
}
```

```jsonc
// app-level posture — the "medical-grade vs photo-site" decision, as configuration
"Koan:DataClassification": {
  "Pii": "Isolate",        // CoLocate | FieldEncrypt | Isolate(vault) | RegionPin
  "Phi": "RegionPin",      // medical app → PHI must stay in-region AND isolated
  "Retention": { "Phi": "retain", "Pii": "erasable" }  // HIPAA-forever vs GDPR-erasable
}
```

- A **photo site** sets everything to `CoLocate` → zero overhead, one table, exactly today's behavior.
- A **medical app** sets `Phi: RegionPin + Isolate` → the framework routes `[Phi]` fields to an isolated,
  region-pinned store (a vault or a regional data plane), tokenizes them in the primary store, and
  applies retention rules — **with no change to `Patient` code**.
- **`Identity` PII is just `[Pii]`-classified data** → the "split identity" behavior (global opaque anchor
  + isolated/regional PII detail) is the *enforcement result* of `Isolate`/`RegionPin` on identity's PII
  fields. **No special identity hack** — one mechanism.
- This is the **same-DX invariant extended from isolation-mode to sensitivity**: the posture is config;
  the code is frozen. It **composes with tenancy** — tenant *placement* (which region/substrate) and data
  *classification* (which sensitivity tier) are orthogonal axes.
- Because Koan **owns every axis**, classification can flow into cache (don't cache `[Phi]` plaintext),
  search/vector (tokenize before indexing), logs/telemetry (redact classified fields), and erasure
  (vault-delete cascades) — the same structural advantage as tenancy itself.

### The open questions on this mechanism (we want your take)

1. **Scope.** Is data-classification *part of* the tenancy facet, or a **sibling capability** that
   tenancy/residency *composes with*? It was surfaced by the residency hole, but it's arguably its own
   concern (sensitivity ≠ tenancy). Our lean: sibling capability, tightly composed. Right call?
2. **Vault: build, integrate, or abstract?** Should Koan ship a built-in vault adapter, integrate
   external vaults (Skyflow/VGS) behind a capability, or just provide the classification + routing seam
   and let the vault be a pluggable adapter (Reference = Intent)? Our lean: the seam + pluggable adapters.
3. **Searchability.** `Isolate`/`FieldEncrypt` breaks `WHERE email LIKE ...`. Do we expose blind-index /
   queryable-encryption as a capability, or honestly declare classified fields non-queryable (and surface
   it in the boot report / capability model)?
4. **The anchor must not leak.** If identity's PII is isolated but a *token/anchor* derived from it
   crosses borders, does that token itself constitute PII transfer? (Format-preserving tokens are
   designed to avoid this, but the framework must guarantee the anchor is opaque.)
5. **Detokenization latency & cache scope.** A vault read on every PII access adds latency; caching the
   plaintext pulls the cache back *into* compliance scope. How should the framework default here?

---

## 6. The state of the open forks (for completeness)

- **PII residency** → resolved into the §5 classification mechanism (was the round-1 open fork).
- **Tenant hierarchy** → v1 flat; `ParentTenantId` seam reserved.
- **Connection-resolution seam** → being designed now, anchored by P6 (the connection broker).
- **SSO/SCIM** → boundary held (Koan owns the event hooks — `OnIdentityLinked`/`OnMembershipRevoked` —
  not the SAML/OIDC protocol).
- **Ambient carrier naming** → internal; the tenant developer surface is `Tenant` regardless.

---

## 7. Delight — round 2

Round 1's top delights are adopted: the **isolation test-kit** (turn "trust us" into "here's the proof,
regenerated every build" — the answer to "why Koan"), **tenant branching/snapshot**, the **leak-siren**,
**act-as-as-`sudo`**, and trial-TTL tenants. We now want the *next* layer, especially around the new
classification mechanism and day-2 operations:

- What's delightful about **classification** beyond compliance relief? (e.g., "flip `Phi: Isolate` and the
  framework re-routes existing data via the migration runner + tells you exactly what moved" — a
  one-config-line path to HIPAA-readiness?)
- What's the **operator** delight when sensitivity, residency, and tenancy compose — e.g., a single fleet
  view showing each tenant's region × isolation posture × compliance status?
- Is there a delight in making **erasure provable** (a GDPR "certificate of deletion" the framework
  generates because it owns every axis and can verify the fan-out)?
- The **"magic moment"** question stands: with this firmer foundation, what's the single thing that makes
  a team say "wait, *that's* all it takes" — and would it make them choose Koan over Postgres-RLS +
  Skyflow + a homegrown layer?

---

## 8. Summary of the deltas since round 1

1. Six over-promises corrected toward honesty (§3).
2. 20 proposals → **7 load-bearing primitives + 2 seams**, each dogfooding an existing pillar (§4).
3. `Relocate` and `Erase` reframed as **explicit sagas/state-machines** with consistency models.
4. The PII residency fork **resolved into a declarative data-classification mechanism** grounded in prior
   art (vaults, split-plane, classification policy) — identity PII becomes ordinary classified data (§5).
5. The validated base (identity/membership, gate-above-role, async-hop, immutable-id) is **unchanged**.

**Tell us where the changes are wrong, what new problems they create, and what delight we still haven't
imagined. Thank you.**
