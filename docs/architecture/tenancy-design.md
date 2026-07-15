---
type: ARCHITECTURE
domain: core
title: "Koan Tenancy — Design (Facet 3 flagship slice)"
audience: [architects, developers, ai-agents]
status: draft
last_updated: 2026-06-21
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-21
  status: design-only
  scope: docs/architecture/tenancy-design.md
---

# Koan Tenancy — Design (Facet 3 flagship slice)

> **Current context ownership (2026-07-15):** The packaging and module-ownership rule below still
> applies, but the generic logical-flow seam is now `Koan.Core.Context.KoanContext`, not Data's
> `EntityContext`. Tenancy registers `TenantContextCarrier : IKoanContextCarrier` independently from
> its Data axis. See [ARCH-0113](../decisions/ARCH-0113-entity-capability-communication.md). Treat later
> references to a Data-owned generic ambient carrier as historical design text.

> **Packaging (canon, 2026-06-22).** Tenancy ships as a **separate `Koan.Tenancy` module**, not as code in
> `Koan.Data.Core`. The data core exposes **generic, tenancy-agnostic seams** — the axis-generic ambient carrier
> ([ARCH-0097](../decisions/ARCH-0097-axis-generic-ambient-carrier.md)) and the storage-pipeline contributor
> seams (`IStorageGuard` / `IWriteStamp` / read-filter / schema-column / particle,
> [DATA-0105 §0](../decisions/DATA-0105-storage-composition-contributor-pipeline.md)). `Koan.Tenancy` **provides
> the contributors** and **owns the developer surface** (`TenantContext`, `Tenant.Use`/`Tenant.None`/
> `Tenant.Current`, `.WithTenant`, `[HostScoped]`). **Reference = Intent**: referencing `Koan.Tenancy` lights
> tenancy up; not referencing it leaves the seams empty (structural no-op). A grep for "tenant" in
> `Koan.Data.Core` returns nothing — that invariant is the conformity guarantee. Everything below describes the
> tenancy *behaviour*; read the module boundary onto it.

> **What this is.** The implementation-ready design for first-class multi-tenancy in Koan, after a
> three-round external review (three frontier models per round) plus a four-persona delight harvest —
> unanimous verdict: **ship the ADR**. Tenancy is the **flagship typed slice** of the Ambient primitive
> defined by the [Ambient Context Charter](./ambient-context-charter.md); this document is the concrete
> model the charter's laws and truth-test are applied to. The full review trail — the convergence map, the
> honesty corrections, the negotiation, and the round-by-round deltas — lives in
> [tenancy-external-review-findings.md](./tenancy-external-review-findings.md) (the source of truth this
> spec folds in). It depends on the ambient carrier (the charter) and on
> [SEC-0004](../decisions/) (capability authz floor),
> [DATA-0104](../decisions/DATA-0104-generic-entity-storage-naming.md) (storage-name grammar),
> [DATA-0077](../decisions/) (`PartitionNameValidator` identifier alphabet),
> [JOBS-0005](../decisions/) (durable jobs), the [WEB-0068](../decisions/) read-path predicate machinery,
> [ARCH-0079](../../tests/README.md) (integration tests as canon), the [ARCH-0084](../decisions/)
> capability model, and [ARCH-0094](../decisions/ARCH-0094-adapter-forge.md) (the Adapter Forge — the
> companion to the external-infra delegation seam).
>
> **Status: design-only.** Nothing here is built yet. Forks marked **[SETTLED]** are ratified by the
> architect; **[DECIDED]** items were closed by the external review's verdict; **[OPEN]** items remain.
> The ADR that records this design is [ARCH-0095](../decisions/ARCH-0095-tenancy.md).

The two mandates carry from the charter: **Simplification** ("same developer experience regardless of
tenancy mode") and **Delight** ("what would developers *and operators* love to have?"). Greenfield,
break-and-rebuild is desired — and the **go-to-market is greenfield-only**: tenancy is pitched as the
*Day-0 foundation for your next SaaS*, **not** a migration path onto an existing codebase (§13). The design
is grounded in a four-lens harvest of real practitioner pain (41 sourced pains; see
[Appendix A](#appendix-a--the-pain-harvest)) and hardened by the external review (see
[Appendix B](#appendix-b--the-external-review-trail)).

**A governing discipline (adopted from the review).** *Attributes and verbs are expensive forever; config
knobs are cheap and discoverable. Bias every new surface toward config.* This rule is why the developer
surface stayed exquisitely small through three rounds (≈4 attributes, 3 accessors, 3 verbs, 1 config
block; entity code unchanged) and why the one entity policy-hint that crept in was cut (§5).

---

## 1. Principles (the load-bearing decisions)

1. **Same DX across every mode.** The developer writes `Todo.Get(id)` / `todo.Save()` identically whether
   the tenant is pooled, schema-isolated, or on a dedicated database. The mode is **configuration + a
   per-tenant registry strategy**, never code. This is the invariant everything else serves — with three
   honest asterisks the review forced (§1a).
2. **Isolation is a sliding boundary at four depths.** Deployment → connection → schema → row. The
   framework's job is to let you slide the boundary by config while the entity code stays frozen.
3. **Identity is global; membership is per-tenant; roles live on the membership.** One human, N tenants
   (the StackExchange/Slack model). The `User` is **not** a tenant-scoped entity.
4. **Tenant identity is an immutable surrogate.** Physical storage binds to an immutable `id`
   (`a1b2c3`); human-facing `codes` are mutable aliases. A rename is a metadata change, never a
   re-keying migration.
5. **Tenant is ambient (isolation); principal is explicit (authority); membership is the bridge.** The
   tenant slice flows via the ambient carrier. The principal does **not** auto-flow (no invisible
   authority). Authorization = `Tenant.Current` (ambient) × explicit principal → resolve membership.
6. **Fail-closed, secure-by-default, structurally enforced.** No tenant on a scoped entity → throw.
   Entities are tenant-scoped by default once tenancy is on (`[HostScoped]` opts out). A forgotten
   predicate must be *structurally impossible*, not a discipline (the #1 harvested pain). Enforcement lives
   at the **chokepoint** (the lowest data-adapter layer), never at `Entity<T>.Save()` (§12, P1).
7. **The control plane is dogfooded Koan.** The registry, identity, and lifecycle are `[HostScoped]`
   `Entity<T>` + `IKoanJob<T>` — so the operator inherits Koan's entire surface (query, project, audit,
   coherence, MCP) over the fleet for free, and there is **no second, weaker admin framework to certify**.
8. **No scope contamination.** The root/control-plane store holds **only** control-plane data, and is
   independently *placeable* (own substrate/region) so operator-projection queries can't degrade premium
   tenants. Every tenant's product data — including the default/master tenant's — lives in its own placement.
9. **Koan owns every axis.** Tenant flows into data, cache, vector, search, jobs, messaging, and
   observability; lifecycle verbs (provision/erase/relocate) fan out across all of them; **every durable
   serialized carrier** (audit log, messaging outbox, dead-letter queue, event store) carries tenant and
   honors classification (§6c). This is the structural advantage no single-ORM library has — and the basis
   of the flagship **erasure certificate** (§10).
10. **Owns every axis is also the lock-in risk.** The thesis that makes Koan tenancy uniquely capable —
    it owns every backend pillar — is the fatal adoption barrier for a team mandated to run un-owned infra
    (a specific vector DB, an enterprise bus). The answer is the **external-infra delegation seam** (§6e):
    carry tenant + classification across the boundary to un-owned infra *without failing open*, softening
    "owns every axis" to "**coordinates** every axis, even un-owned ones." The companion capability is the
    Adapter Forge ([ARCH-0094](../decisions/ARCH-0094-adapter-forge.md)).

### 1a. The "same DX" claim, made honest

The review validated same-DX for **entity read/write code** and corrected three overstatements; the boot
report states each precisely (the no-boot-lies principle):

- **Migration DX is placement-aware.** Entity code is uniform across modes; *operational* migration DX
  varies wildly by placement, and the framework **orchestrates the fan-out** (§9, P5) rather than pretending
  it's free.
- **Read performance scales with classification posture.** A `[Pii]` field under `Isolate` carries a
  detokenization cost a `CoLocate` field does not (§5). Same code, different latency — boot-reported.
- **Cross-entity operations are not mode-invariant.** Joins and multi-entity transactions behave
  differently across substrates (a shared-schema join is one query; a db-per-tenant "join" across two
  tenants is not a join at all). The boot report names which cross-entity guarantees hold under the
  configured placement.

The review forced **six** further overstatements honest, each made precise at its mechanics section rather
than here: *"you cannot forget the boundary"* → the escape **is** the gap, constrained at the chokepoint
(§12); *Erase is instant* → an eventually-consistent state machine within a quiesced window, then verified
(§9); *Relocate is a verb* → a saga with an explicit consistency model (§9); *the operator console exists
by default* → the **data** does, the console is a projection (§11); *addresses scaling cliffs* → makes
substrate choice and movement cheap, does not repeal physics (§2). The no-boot-lies principle requires each
to be stated, not glossed.

---

## 2. The isolation mode ladder  [SETTLED]

The boundary sits at one of four depths. Mode 1 is out of scope (a deployment topology needing zero
framework support); modes 2–4 are same-runtime tenancy.

| Mode | Boundary depth | Isolation mechanism | Koan strategy |
|---|---|---|---|
| **1 — Silo** | deployment | separate process/infra | **out of scope** (zero framework) |
| **2/3 — Database-per-tenant** | connection | per-tenant connection, **mapped or templated** | route connection by `Tenant.Current.Id` (P6 broker) |
| **4a — Schema-per-tenant** | one DB, native schema | DB schema = namespace prefix → `acme.todo` | route schema/namespace by tenant |
| **4b — Shared-schema** | one table | discriminator column + mandatory filter (+ RLS) | inject tenant predicate at the chokepoint (P1) |

**Two refinements that were settled:**

- **Modes 2 and 3 are one strategy** ("database-per-tenant") differing only in connection sourcing
  (explicit per-tenant config block vs. a `{tenant}`-substituted template).
- **Tenant never enters the table-name spine.** The `{tenant}-{model}` sketch would collide with
  [DATA-0104](../decisions/DATA-0104-generic-entity-storage-naming.md)'s `-` = spine separator. Instead
  tenant routes to a **native boundary**: 4a uses the database's own schema (which rides DATA-0104's `.`
  namespace separator natively — `acme.todo`), 4b uses a discriminator column (name stays `todo`). The
  storage grammar stays clean.

**Heterogeneous by design.** Placement is per-tenant data, not a global switch: tenant "acme" (premium)
can be on a dedicated database while tenants B–Z share a schema. The registry holds each tenant's
strategy; the operator can change it (§9, `Relocate`).

**Honest limit:** the ladder makes substrate *choice* and *movement* cheap and visible; it does **not**
repeal physics. Database-per-tenant still fans out connection pools (P6 publishes the honest ceiling —
"max recommended tenants = N given pool size M"); schema-per-tenant still bloats the catalog past a few
hundred tenants. The design *steers* (defaults to shared-schema, the scalable mode) and makes the upgrade
a verb — it does not eliminate the cliff.

---

## 3. The eight load-bearing primitives  [DECIDED]

The reviews proposed ~20 features. Applying the redesign discipline — *don't add surface; add a few
meaningful parts that many things compose from* — they reduce to **eight primitives**. This is the spine:
build these, and the flashy delights become roadmap built *on* them. Each dogfoods an existing pillar, so
none is net-new infrastructure — the lone exception being P6's per-tenant connection-routing seam (§12),
the one genuinely-new piece, which must be re-derived empirically from the data-core code first.

| Primitive | What it is | Composes into | Rides |
|---|---|---|---|
| **P1 · Chokepoint guard** | read-filter + **write-guard** at the lowest data-adapter layer (not `Entity.Save`); RLS is the backstop | all enforcement; `Tenant.None()` constraint; `Suspend` | data pillar |
| **P2 · Tenant state machine** | `Provisioning/Active/Suspended/Relocating/Erasing/Erased/EraseFailedStuck`, enforced *at the chokepoint* | `Suspend`, `Erase`, `Relocate`, billing-block | data + jobs |
| **P3 · `Suspend`-as-quiesce** | a real enforcement state (block-writes / block-all) at the chokepoint | the quiesce step `Erase` & `Relocate` both need | data |
| **P4 · Logical Export/Import** | dump/upsert a tenant's entities via the entity model (JSON/BSON); token handling on restore defined | backup/restore · `Provision(from:)` · snapshot · branch · merge · BYOC | entity model |
| **P5a · Migration executor** | apply one migration to one substrate; idempotent, resumable | the unit P5b fans out | jobs |
| **P5b · Fleet orchestrator** | ordering, canary gates, rollback policy, version-skew tolerance — a saga | schema migrations · part of `Relocate` · drift detection · posture-migration | jobs + P8 |
| **P6 · Connection broker** | per-tenant routing, pool governance, **guaranteed session reset**, honest boot-report limits, the **credential/KMS seam** | db-per-tenant viability · RLS-poisoning fix · key rotation | data + capability model + self-reporting |
| **P7 · Isolation test-kit** | run the entity surface under mismatched tenant contexts, assert the guard fired; property-based fuzz; N-tenant sim | `AssertNoTenantLeak` · `[TenantIsolated]` · `Tenant.Simulate` · the "prove it" delight · **= the Conformance Gate's first incarnation** | ARCH-0079 TestKit |
| **P8 · Saga coordinator** *(internal)* | phase gates, compensation, rollback over the jobs ledger; orchestrates **only** P3/P4/P6 — **no user business logic** | `Relocate`, `Erase`, posture-migration, schema fan-out | jobs ledger |

**P4 is more load-bearing than first rated** — logical Export/Import + a cross-axis snapshot is the
substrate for an entire "data-as-versioned-artifact" delight class (snapshot / branch / diff / merge,
§15). Elevate it.

**P7 is the positioning win.** It converts "trust our isolation" into "here's the proof, regenerated
every build" — the answer to "why Koan over homegrown RLS." It is also, structurally, the **first
incarnation of the Adapter Forge's Conformance Gate** ([ARCH-0094](../decisions/ARCH-0094-adapter-forge.md));
the same artifact (1) keeps the ARCH-0084 capability model honest today, (2) is the v1 isolation +
classification proof, and (3) gates agent-authored adapters tomorrow. Built once.

**P8 is internal.** It is *not* a developer-facing primitive and *not* a general workflow engine (that's
Temporal/MassTransit's job). It orchestrates only **idempotent, compensable framework primitives**
(P3/P4/P6) and carries no user logic. The coordinator owns the phase lifecycle; **each saga owns its own
consistency semantics** — that division *is* the anti-leak boundary. The developer surface stays
`Tenant.Erase()`, never `ISaga.Step()`.

### 3a. The "tenancy kernel" and the "Magic Cliff"  [DECIDED]

Adoption is **graceful layering — no separate SKU** (consistent with Reference = Intent: each pillar is
opt-in). But two things are named and tested as first-class:

- **The tenancy kernel = P1–P3 + P7 at the `Koan.Data` level.** Referencing only `Koan.Data` gives you the
  chokepoint guard, the state machine, suspend-as-quiesce, and the isolation test-kit — **~80% of the
  value** (the leak you can't write, fail-closed, prove-it). This is a supported configuration.
- **The Magic Cliff** is documented honestly: the *flagship* delight — tenant **survives the async hop**
  (jobs, messages, webhooks) — requires `Koan.Jobs` + `Koan.Messaging`. Below the cliff you have safe
  synchronous isolation; above it you have the full multi-axis guarantee. Reference = Intent makes the
  climb a package reference, and the boot report states which side of the cliff the app is on.

---

## 4. The developer surface  [SETTLED]

```csharp
// Read / scope — ambient, AsyncLocal, auto-restoring (per the charter's carrier)
Tenant.Current                       // the rich object {Id, Codes, Name}; throws if fail-closed & unset
using (Tenant.Use("a1b2c3")) { ... } // explicit scope (admin, jobs, tests, support act-as)
using (Tenant.None()) { ... }        // the ONE loud, audited escape to host/control-plane scope

// Lifecycle (framework verbs — same-DX means same provisioning; each is an IKoanJob / saga)
await Tenant.Provision("a1b2c3");    // create placement, ensure schema, seed default membership
await Tenant.Relocate("a1b2c3", to); // SAGA — quiesce / copy / cutover / verify / rollback (§9)
await Tenant.Erase("a1b2c3");        // STATE MACHINE — quiesce → fan-out → verify → certify (§9/§10)
await Tenant.Rename("a1b2c3", newCode: "mslp", newName: "Microslop");

// Cross-substrate admin query — the sanctioned fan-out (so operators never drop to raw SQL, §12)
await Tenant.FanOutQuery<Invoice>(q => q.Where(i => i.Overdue), across: TenantSet.All);

// Classification — declared FACTS on the entity (handling is policy, resolved elsewhere, §5)
public class Todo : Entity<Todo> { }              // tenant-scoped automatically
[HostScoped] public class TenantRecord : ... { }  // system/registry entities opt out

public class Patient : Entity<Patient>
{
    [Phi]    public string Diagnosis { get; init; }   // a fact: this is PHI
    [Pii]    public string Email     { get; init; }   // a fact: this is PII
    [Secret] public string ApiKey    { get; init; }   // write-only / masked-read (§5)
}
```

The rich `Tenant.Current` object exposes `.Id` (physical, framework-only), `.Codes.Current` (canonical
link), and `.Name` (display). **App code never reads `.Id`** — the framework's physical axis extracts it;
the rich object is an app-space convenience.

**Sane defaults:** tenancy **OFF** by default (single-tenant apps pay nothing). When ON: secure-by-default
(scoped unless `[HostScoped]`), fail-closed, default resolution chain subdomain→header→JWT-claim, default
mode `SharedSchema` (works on one connection, upgrade by config).

**The activation gradient (no instant cliff).** Reference = Intent + fail-closed means referencing the
package would otherwise throw on every un-scoped op the moment it's added. The mode ladder
`off → warn → enforce` defuses this: first activation defaults to **`warn`** (logs every un-scoped op with
the fix), an explicit config flip moves to **`enforce`**. The boot report prints the posture —
`Tenancy: SharedSchema · query-filter+RLS · enforce · fail-closed · 3 tenants` — making isolation
**verifiable at startup**, not discovered at breach.

**The surface stayed small (coherence verdict).** Across three review rounds the dev-facing surface held at
≈4 attributes (`[HostScoped]`, `[Pii]`/`[Phi]`/`[Secret]` as sugar over `[Classified]`), 3 accessors
(`Current`/`Use`/`None`), 3 verbs (`Provision`/`Relocate`/`Erase`, with `Rename`/`Suspend` as registry
ops), and one config block. The accretion is **inherent and lives in the host-plane** (sagas, broker,
state machines) — acceptable, because the developer never sees it.

---

## 5. The classification axis  [SETTLED — layered policy]

Classification is an **orthogonal capability that tenancy composes with** — not tenancy-core. It extends
"same DX" from the *isolation* dimension to the *sensitivity* dimension: classify a field once and the
whole compliance surface (logs, cache, search, erasure, masking, residency) updates itself. A photo-site's
caption is `CoLocate` (zero cost); a clinic's diagnosis is `Phi: RegionPin + Isolate`. **Same entity code.**

### 5a. One extensible axis, not N attributes

A **category** is `{ name, default-posture, applicable-handlings, retention-default }`. Built-in
well-known bundles ship as sugar; apps define their own (MNPI, ITAR, TradeSecret) in config:

```csharp
[Pii]  field   // sugar over [Classified("Pii")]
[Phi]  field   // sugar over [Classified("Phi")]
[Pci]  field
[Secret] field
[Classified("MNPI")] field   // app-defined category, declared in config
```

Two fact-families drive different handling:

- **Sensitivity** — PII / PHI / PCI / Secret / biometric → isolate / encrypt / mask / tokenize / region-pin.
- **Lifecycle** — retention/TTL, immutable/append-only → purge / reject-update. Several **already exist**
  as Koan primitives: `[Timestamp]`, `[AppendOnly]`, `[Index(Ttl = true)]`.

**`[Secret]` is the strongest add beyond the PII/PHI/PCI trio** — credentials-as-data (a tenant's payment
key, SMTP password, webhook secret), near-universal in SaaS. It carries a **distinct handling primitive PII
lacks: write-only / masked-read** — `Set` works; `Get`/serialize returns a mask, never the plaintext, by
default.

### 5b. Classification rides the capability model

A category **announces its required handlings** (tokenize / field-encrypt / mask / write-only / redact-in-
logs / exclude-from-embedding / region-pin); adapters **announce support**; the framework **composes or
fails-closed** on a capability mismatch (the ARCH-0084 pattern, the no-capability-lies rule). This is the
same honesty mechanism the Conformance Gate enforces (P7 / ARCH-0094).

### 5c. The layered policy — FACT vs HANDLING

The entity declares a **fact**; *handling* is **policy resolved in layers**, never on the entity:

1. **Solution config = posture + capabilities** (the floor):
   ```jsonc
   "Koan:Classification:Phi": {
     "posture": "Isolate",          // CoLocate | FieldEncrypt | Isolate(vault) | RegionPin
     "retention": "7y",
     "allowEmbedding": true,        // a capability, not an entity hint (see the cut, §5e)
     "embeddingStrategy": "ScrubAndEmbed",
     "mutability": "CannotChange"   // the policy gate above tenants (§5d)
   }
   ```
2. **Tenant config = overrides** where the solution left the posture unlocked.
3. ~~Developer entity hint~~ — **cut** (§5e).

### 5d. The mutability lock = policy-gate-above-tenant

The solution owner sets a posture default **and a mutability lock** (`CannotChange` | `MayChange`). A
locked posture is a floor a tenant **cannot relax** — the exact mirror of *tenant-gate-above-roles* (§8):
the solution sets a boundary tenants live inside. The lock default is **classification-aware** (sensitive
categories lock by default; non-sensitive open by default). This generalizes to all tenant-overridable
config — the tenant carries a *capability profile with locks*, not free rein.

**Identity-PII is special:** its **handling rules are solution-level only** (a global shared identity
record; a tenant cannot relax how a human's PII is protected). But its **residency shards by the
identity's own home region** (§6d) — not the solution's region and not the tenant's. Home region is
user-assertable/correctable; changing it triggers an identity-PII relocation saga.

### 5e. The cut — `[Phi(Embeddable = true)]` does not exist  [DECIDED]

Embeddability is **handling (policy), not a fact**. Putting it on the entity would couple the domain model
to the AI pillar and violate Reference = Intent (a silently-denied attribute). So:

- **Entity = facts only** (`[Pii]`/`[Phi]`/`[Secret]`).
- **Intent to embed is the *existing* `[Embedding]`** attribute — no new surface.
- If the resolved policy denies embedding for a `[Phi]` field → **`CapabilityDeniedException`** at the AI
  call site + a boot-report line + P7's **`AssertEmbeddable`** catches the dev/prod divergence in CI.

**Classified fields are excluded from the AI/semantic stack by default.** A tokenized/encrypted `[Phi]`
value is semantically meaningless, so it cannot be embedded unless config opts in (`allowEmbedding: true`
with an `embeddingStrategy` such as `ScrubAndEmbed`). This is a **real capability limitation of a framework
that owns the AI pillar** — and the boot report surfaces it honestly (the no-boot-lies principle applied to
a *downside*): an app sees exactly which classified fields are excluded from embedding and why. This was
the **deepest tension** the external review surfaced (classification × the AI/vector pillar, the sole
architect-decision of round 2); the fact-vs-policy cut above is its resolution.

This sharpens the fact-vs-policy split and honors "bias to config, not attributes."

### 5f. Classification handling details (folded from the review)

- **Searchable-equality is first-class.** `[Pii, Searchable]` → an automatic **blind-HMAC equality index**
  (login flows need exact-match on an isolated email); `LIKE`/range are honestly **denied + boot-reported**.
- **Plaintext lives in a request-scoped identity map** (AsyncLocal, discarded at request end) — **never the
  distributed cache** (which would pull plaintext into compliance scope). This also fixes the
  detokenization N+1 *within* a request: batch-detokenize at the query chokepoint for a result set.
- **Opaque high-entropy tokens are the default;** FPE (format-preserving) is explicit-flagged (FPE leaks on
  small domain spaces).
- **"Tokenized ≠ compliant."** GDPR Recital 26: pseudonymized data is still PII for the *controller* who
  holds the detokenization key. Tokenization = blast-radius reduction + *processor* residency, **not**
  controller-obligation elimination. The boot report says so.
- **Relations crossing a classification boundary are soft-enforced.** A co-located FK pointing at a vaulted
  row → a dangling token → **graceful redaction**, never a null-ref panic.
- **Propagation model (v1): per-leaf-field only.** Classification on complex types / collections / nav
  properties is defined-but-restrictive in v1 (unambiguous, relaxable later).
- **Key management is a seam, not a build.** Per-tenant keys; rotation is a P5b/P8 job via the **KMS adapter
  seam** (§6c). Crypto-shred (destroy the key) is the fast path for `Erase`.
- **P4 restore must define token handling.** Cross-environment restore of tokenized data is an industry
  nightmare; the export/import contract (P4) picks **re-tokenize vs decrypt-on-export** *explicitly* rather
  than leaving dangling tokens that resolve against the wrong vault.

---

## 6. The control-plane data model  [SETTLED]

All `[HostScoped]`, living in the root store ([§1.8 no contamination](#1-principles-the-load-bearing-decisions)),
which is independently placeable.

```csharp
[HostScoped] sealed class Tenant : Entity<Tenant, string>   // Id = the immutable "a1b2c3"
{
    public string Name { get; init; }                 // mutable display
    public string? ParentTenantId { get; init; }      // SEAM: v1 always null (flat); enables IN(self+ancestors) later
    public TenantPolicy Policy { get; init; }          // joinMode, exclusive, defaultRoles, capability profile + locks
    public TenantPlacement Placement { get; init; }    // substrate, region, tier, dataSourceRef, credentialRef ← REFs, never secrets
    public TenantStatus Status { get; init; }          // Provisioning|Active|Suspended|Relocating|Erasing|Erased|EraseFailedStuck
    public string Tier { get; init; }                  // SLA tier — drives the observability cardinality split (§11)
    public bool IsDefault { get; init; }               // routing pointer — bare domain lands here; NO extra powers
    [Timestamp] DateTimeOffset CreatedAt { get; init; }
    [Timestamp(OnSave = true)] DateTimeOffset UpdatedAt { get; init; }
}

[HostScoped] sealed class TenantCode   : Entity<TenantCode, string>   // Id = the code → O(1) resolve + global-unique
{ public string TenantId; public CodeKind Kind; }                     //   Kind = Current | Previous
[HostScoped] sealed class TenantDomain : Entity<TenantDomain, string> // Id = the domain → global-unique capture map
{ public string TenantId; public DomainVerification Verification; public bool Exclusive; }

[HostScoped] sealed class Identity   : Entity<Identity>   { public string Subject; public string HomeRegion; /* anchor + IdP refs */ }
[HostScoped] sealed class Membership : Entity<Membership> { public string IdentityId; public string TenantId;
                                                            public string[] Roles; public MembershipStatus Status; }
[HostScoped] sealed class Invite     : Entity<Invite>     { /* email, tenantId, roles, token, expiresAt, status */ }

[HostScoped, AppendOnly] sealed class AuditEntry : Entity<AuditEntry>   // the versioned audit envelope (§6c)
{ public int SchemaVersion; public AuditEventType Type; public AuditActor Actor;       // identity + membership + scope
  public string? TargetTenantId; public AmbientSnapshot Ambient; public long CausalSeq; // causal order, not just wall-clock
  public IDictionary<string,string> Extensions; [Timestamp] DateTimeOffset At; }

[HostScoped] sealed class TenantOperation : Entity<TenantOperation>, IKoanJob<TenantOperation>  // resumable lifecycle op (P5/P8)
{ public string TenantId; public OperationKind Kind; public OperationStatus Status; public int Cursor; /* N of M */ }
```

**The decisions baked into this model:**

1. **Codes & domains are keyed entities** [SETTLED, fork 1]. `Id = the code/domain`, so resolution is an
   O(1) keyed `Get` and **global uniqueness is the key constraint** (race-safe, no TOCTOU). Fully
   normalized: `TenantCode` is the sole source of truth; the *current* code is the row with
   `Kind = Current`; `previous` rows are live redirects.
2. **Secrets are referenced, never stored.** `Placement.dataSourceRef` / `credentialRef` are *names*
   resolved through the credential seam (§6c). The registry can be queried/exported/MCP-exposed without
   surfacing a connection string or key.
3. **Membership is host-stored, tenant-filtered.** The operator reads it unfiltered; a *tenant*-admin
   reading "my members" gets the same entity, tenant-filtered by the [WEB-0068](../decisions/) read-path
   predicate machinery. The control plane reuses the data plane's enforcement — no parallel admin model.
4. **Operator-view vs tenant-view is field projection.** One row, two faces (composes the SEC-0004
   `can:[]` projection).
5. **Lifecycle ops are `IKoanJob`s** [SETTLED, fork 2]. They inherit durability, retry, a ledger, the
   conveyor (fan across stores one cursor at a time), and visibility from JOBS-0005 — answering the
   harvested "no per-tenant migration ledger / no partial-failure recovery / fleet stranded mid-migration."
6. **The registry is live truth, coherence-invalidated.** Editing a `Tenant` row changes fleet behavior on
   the next request. No redeploy. The registry is also the **canary-cohort selector** (§11).
7. **Audit is configurable, default light** [SETTLED, fork 3]. `Koan:Tenancy:Audit = Mutations` (default) `| Full`.
8. **No master backdoor** [SETTLED, fork 4]. The default/master tenant is `IsDefault: true` — a routing
   pointer with **zero special data powers**. The "tenant-zero" model is explicitly **rejected** — it
   re-creates the `AppHost` split-brain Facet 3 is killing.
9. **`ParentTenantId` is a seam, not behavior** [SETTLED]. v1 is flat; the nullable column ships now so
   hierarchy is retrofittable as `IN (self + ancestors)` with **no later data migration** — *provided the
   discriminator stays a plain `tenant_id` column* (never a composite/path key). That plain discriminator
   is the second precondition that keeps the retrofit free; both ship in v1.

### 6a. Worked example — code lifecycle (Microslop's two rebrands)

```
Onboard as "msft":
  Tenant     { Id="a1b2c3", Name="Microsoft", Status=Active }
  TenantCode { Id="msft",  TenantId="a1b2c3", Kind=Current }

Rename msft → m-sft  (Rename job: check-unique, write Current, demote old, audit, evict cache):
  TenantCode { Id="msft",  TenantId="a1b2c3", Kind=Previous }   // still resolves → redirects
  TenantCode { Id="m-sft", TenantId="a1b2c3", Kind=Current }

Rename m-sft → mslp,  Name → "Microslop":
  Tenant     { Id="a1b2c3", Name="Microslop" }
  TenantCode { Id="msft",  TenantId="a1b2c3", Kind=Previous }
  TenantCode { Id="m-sft", TenantId="a1b2c3", Kind=Previous }
  TenantCode { Id="mslp",  TenantId="a1b2c3", Kind=Current }
```

All old codes still resolve (→ redirect to current); the storage `a1b2c3` never moves. A collision
(`acme` tries to claim "msft") is caught O(1) by the key and rejected with an actionable error
("held by tenant a1b2c3 as a previous alias; release first") — never a silent overwrite.

### 6b. Tenant-scoped configuration & the leak siren

- **Tenant-scoped config** (feature flags, plan limits, branding) rides `Tenant.Policy` and **composes the
  capability model** — a tenant carries a *capability profile* (elegant reuse, not a new store).
- **The leak siren:** a guard rejection emits a structured `TenantBoundaryViolation` audit/security event
  with forensics (ambient tenant, target, entity, **call site**, stack) — not just a throw. It is
  **rate-limited/aggregated** ("100 violations for tenant X in 1s") so a looping cross-tenant-write bug
  can't turn the security mechanism into an availability DDOS.

### 6c. Durable serialized carriers must carry tenant + honor classification at v1  [DECIDED — #1 regret class]

The review's highest-regret cluster: **anything serialized to durable storage** must be tenant-aware and
classification-safe from v1, or retrofitting is a pillar rewrite.

- **Audit-event versioned envelope** (above): a closed, minor-version-extensible event taxonomy + actor
  (identity + membership + scope) + target + ambient-context snapshot + **causal ordering** (not just
  wall-clock) + a forward-compat extension bag. *The erasure certificate's credibility rests on the audit
  trail's integrity.*
- **Messaging outbox `TenantId`-partitioning:** the outbox table carries `TenantId` and the dispatcher
  filters by it — else tenant A's event fires tenant B's handler (cross-tenant event poisoning).
  Retrofitting this is a messaging-pillar rewrite.
- **Classified-field stripping in DLQ / retry-ledger / event-store:** an erased tenant's plaintext PII/PHI
  sitting in a dead-letter queue legally **voids the erasure certificate**. Durable carriers strip or
  blind-encrypt classified fields *before* durable storage.

**The credential / key seam (on P6 + Placement, now).** First-class, vault-backed:

- **`ICredentialProvider`** — resolves the connection credential on pool-create and on auth-failure-retry
  (so a rotated DB password doesn't strand a tenant).
- **A pluggable KMS key-ring** for classification encryption + **tenant-scoped key rotation** (a P5b/P8
  job) + **cryptographic-shred** (destroy the key = fast erase).
- The **erasure certificate carries a Key-ID + a verification endpoint** — else certificates become
  unverifiable after the first key rotation.

Define the seams now; implement rotation later. Koan does not build a KMS — it integrates one (the
external-infra delegation pattern, §6e).

### 6d. Identity-PII residency = per-home-region  [DECIDED — correction]

"Identity-PII handling is solution-level" was right; "...therefore solution-region" was wrong. Split them:
**handling rules** (isolate/encrypt) are solution-governed (§5d), but **residency shards by the identity's
own home region** — else John (EU) joining a US-pinned solution lands his PII in a US vault (a GDPR
violation) and the platform is forced to a lowest-common-denominator tier. Home region is
**user-assertable/correctable**; changing it triggers an identity-PII relocation saga (P5b/P8).

### 6e. The external-infra delegation seam  [SETTLED — the lock-in answer]

The capability model must carry tenant context + classification across a boundary to **un-owned infra**
(a specific vector DB, an enterprise bus) **without silently failing-open**. This softens "owns every
axis" to "**coordinates** every axis, even un-owned ones, via adapters" — and is the technical half of the
greenfield-lock-in mitigation (§13). Its companion capability is the **Adapter Forge**
([ARCH-0094](../decisions/ARCH-0094-adapter-forge.md)): an agent authors a conformance-gated adapter for
the un-owned seam, and the **Conformance Gate (= P7) proves the adapter carries tenant + honors
classification** before it ships. Tenancy is the Forge's pilot and highest-stakes instance.

### 6f. `[ProjectedToHost]` — the cross-tenant read-model seam  [SEAM NOW]

The principled answer to legitimate cross-tenant reporting: a declared read-model that a coherence channel
syncs from tenant-scoped → host read model. Define the attribute/seam now; the sync engine can land later.
(Analytics over *classified* fields runs into the residency tension — see §5f / §11.)

---

## 7. Resolution & onboarding  [SETTLED]

Two resolutions intersect — the **request** axis (which tenant is this request for) and the **identity**
axis (which tenants can this human enter):

```
www.service.com        → no route signal → Tenant.Query(IsDefault==true)         → default/master tenant
mslp.service.com       → code "mslp"     → TenantCode.Get("mslp") → {a1b2c3,Current} → land
www.service.com?t=msft → code "msft"     → TenantCode.Get("msft") → {a1b2c3,Previous}
                                           → find Kind=Current → 301 → mslp.service.com
```

**Onboarding (identity → tenant):**

1. **Authenticate** → global identity; principal established, *no tenant yet*.
2. **Candidate tenants** → `Membership.Query` for this identity (privileged/host scope — cross-tenant) +
   apply the tenant's claiming policy.
3. **Land** → request targets a tenant: verify a membership exists (or policy auto-provisions) → else
   deny. No target, one membership → straight in. Many → tenant picker. No membership, open-join tenant →
   self-serve create.

**Tenant claiming policy** (`Tenant.Policy`): `joinMode = open | invite | domainCapture`,
`verifiedDomains`, `exclusive`, `defaultRoles`.

- **domainCapture** auto-binds matching-email sign-ins (`alice@microslop.com` → `TenantDomain.Get` →
  a1b2c3); `exclusive` then rejects that identity from joining other tenants.
- **Security [SETTLED]:** domain capture is a takeover vector (0ktapus/AiTM). `verifiedDomains` must be
  **DNS-TXT proven** (the verification is itself an `IKoanJob` polling for the record, flipping
  `Pending → Verified`) — never self-asserted. Capture fires only on `Verified`.

**Own the identity event hooks** [SETTLED]: the SSO/SCIM *brokering* boundary is correct (above-layer / v2),
but Koan owns the events the IdP drives Koan's Membership through — `OnIdentityLinked`,
`OnMembershipRevoked`, membership CRUD, domain-verification.

---

## 8. Authorization model  [SETTLED]

Three tiers: **platform operator** (host scope — all tenants/registry/fleet) · **tenant admin** (one
tenant; manages its members/roles/config; blind to others) · **tenant user** (one tenant, role-bounded).

**The rule that makes it safe:** the **tenant gate is prior to and independent of the role check**.
Authorization evaluates (1) does the principal hold a membership in the resolved tenant? — fail-closed,
deny if not — *then* (2) does their role in *this* tenant permit the action. You cannot role your way
across a tenant boundary; tenant is an isolation axis **above** roles. This is the same shape as the
classification **mutability lock** above tenant overrides (§5d) — a floor a lower layer can't breach.

**Composition:** this extends, not replaces, the SEC-0004 `IAuthorize` capability floor — the floor gates
ops by capability; tenancy adds a *prior* membership gate and qualifies every capability check by the
resolved tenant. *(The exact SEC-0004 seam to be re-derived from code before implementation.)*

**Two security properties that fall out** (each closes a harvested critical/high pain):

- **Membership resolved per request, server-side, never trusted from the token.** An org-switch
  re-resolves authority (no stale-claim privilege window); a removed membership denies on the *next*
  request (no deprovisioning lag). This closes the IDOR-from-URL and JWT-stale-claim breach classes by
  design.
- **The request-tenant is a routing hint, authorized against the principal's memberships** — never
  authority in itself. Closes the "tenant id from URL/header not token" IDOR/BOLA class.

---

## 9. Lifecycle operations as sagas  [reframed — DECIDED]

The review corrected the verb-pretense: `Relocate` and `Erase` are **not atomic verbs** but explicit,
auditable processes with defined consistency models. All ride the jobs ledger; the multi-phase ones
compose **P8** (internal coordinator) over **P3/P4/P6**.

- **Provision** → create placement, ensure schema on the connection (no migration-history replay — Koan
  derives schema from the entity model), seed the buyer's default membership, → `Active`. The *shape* is a
  composable pipeline (`.Seed<>().RunStep<>().Notify<>()`, each stage a durable job), not a monolith.
- **Relocate** (pooled → dedicated) → a **saga** with a defined consistency model:
  *quiesce (P3 write-suspend) → copy across stores cursored (P4) → atomic registry cutover → verify →
  rollback-on-failure*. **Honest: "zero-downtime-to-read, blocked-to-write maintenance window"** during
  cutover — no magic 2PC across substrates. The data copy is real work; what's removed is the
  code/routing re-architecture (the harvested "6–12 month" pain), not the bytes.
- **Erase** (GDPR) → a **verifiable state machine**: *quiesce → fan out across every tenant-scoped axis
  (data, cache, vector, search, blobs, **and the durable carriers** §6c) → verify → certify* (§10). The
  fan-out is **eventually consistent within the quiesced window, then verified** — a write cannot sneak
  into cache after erase because the tenant is quiesced first. A `Verify`-fails path leads to a terminal
  **`EraseFailedStuck`** with a defined recovery (retry/re-queue the failed axis); **never a silent
  `Certify`** (Erase is a one-way door). **Crypto-shred** (destroy the per-tenant key, §6c) is the fast
  path for the classified axes.
- **Suspend** → set `Status = Suspended`; because tenant + membership resolve per request against a
  fail-closed gate, every request denies on the **next call** — atomic, no token-expiry wait. Doubles as a
  blast-radius brake / reversible offboard (P3).
- **Posture migration** (a classification policy change) → **also a saga** (§9a).

### 9a. Any effective-policy change is a migration saga, bidirectionally  [DECIDED — correction]

A classification lock change (`MayChange` → `CannotChange`) **or** a tenant override, in **either
direction** — relax *or* **tighten** (`CoLocate` → `Isolate`) — is a **data migration, not an instant
runtime switch**. Else the read-filter fails-open or throws against physically-mismatched data. Changing
the effective posture triggers P5/P8 to vault + backfill *before* enforcing the new posture. Enterprises
will demand the tighten path; v1 must have it.

### 9b. The saga compensation contract  [DECIDED — fold in]

P8's contract, defined now so the lifecycle sagas are uniform:

- **Per phase: undo-vs-forward** — each phase declares whether its compensation rolls back or rolls
  forward.
- **Compensation can fail** → a `stuck` terminal state (no infinite retry), surfaced to the operator.
- **Per-tenant mutex** — one lifecycle saga per tenant at a time; plus a cross-tenant pool-contention rule
  so a fleet relocate can't starve live traffic.
- **Structured per-phase events** into the audit envelope (§6c).
- **Erasure batching is a saga parameter** (batch size, backoff, **noisy-neighbor circuit-breaker**) — a
  framework knob, not application logic.

---

## 10. The erasure certificate  [flagship delight]

The cross-persona flagship of the entire design — **the artifact only a runtime that owns every axis can
produce.** Every incumbent proves deletion from the database; **none** can prove it from
cache/vector/search/logs/blobs (RLS = DB-only, a vault = vault-only, an identity provider =
identity-only). Market timing makes it the deal-maker: the **Feb-2026 EDPB Coordinated Enforcement** report
shifted the erasure burden-of-proof to the controller to *demonstrate* disposition (a ~€160k fine where
"deletion logs" were ruled insufficient). The certificate turns the scariest unprovable compliance task
into a build artifact.

**What it is:** a **cryptographically-signed** record emitted at the end of the `Erase` state machine,
carrying:

- **Per-axis disposition counts** (rows / cache keys / vectors / search docs / blobs / audit-stripped /
  outbox-stripped / DLQ-stripped / retry-ledger-stripped / event-store-stripped — every durable carrier of
  §6c rolls up here).
- **Honest axis classes** — it distinguishes **surgically-purged** axes from **async-purging-with-ETA**
  (vector/search surgical delete can take hours — the "lingering ghost") from **retention-window backup**
  (expires, not surgical).
- **Retention exceptions** — what is *legally retained* and why (HIPAA-must-retain vs GDPR-must-erase is a
  per-classification policy, not an absolute; §5).
- **The signing Key-ID + a verification endpoint** (§6c) so the certificate is verifiable after key
  rotation.

**Why it is uniquely Koan:** owns-every-axis + the durable-carrier discipline (§6c) means there is no axis
the certificate can't speak to. Its credibility rests on the audit envelope's integrity — which is why
§6c is the #1 v1 regret class.

---

## 11. The operator / service-owner console  [SETTLED — direction]

The host-face **projection** of the control-plane entities (not a product to build — `EntityController` +
host-scope authz over the §6 entities; the *data* exists by default, the console is a projection). Each
row a harvested pain killed:

| Sees… | Kills | Severity |
|---|---|---|
| **Fleet** — per tenant: codes.current, name, substrate, region, tier, status, members | capacity planning / manual rebalancing | medium |
| **Per-tenant health / SLIs** | "one tenant's pain is invisible in aggregates" | medium |
| **Per-tenant cost / consumption** — measured (runtime tags every op), not proxy-guessed | "cost attribution impossible on shared resources" | **high** |
| **Noisy-neighbor finder** — who's hammering the shared store now | "hunt the offender via `pg_stat_activity`" | **high** |
| **Placement & relocate** | "pooled→silo is a 6–12 month re-architecture" | **high** |
| **Lifecycle** — provision/deprovision/erase/restore + the **erasure certificate** | GDPR erasure proof, one-call provisioning | high |
| **Access across tenants** — who can reach what; atomic revoke | "deprovisioning incomplete — access outlives the event" | **high** |
| **Domain-capture approvals** | "verified-domain capture takeover vector" | high |
| **Posture & drift** — enforcement mode, fail-closed status, per-tenant config divergence, classification drift | "config drift / pressure to fork the codebase" | medium |
| **The 3D fleet compliance matrix** — Tenant × Region × Isolation × Classification + compliance status | "the compliance spreadsheet drifts from reality" | high |

**The 3D fleet compliance matrix** is generated truth (can't drift like a maintained spreadsheet) and
doubles as a sales asset. **Compliance posture self-assessment lands in the boot report** ("HIPAA-
compatible: PHI retained, PII erasable, audit ON") — a continuously-true attestation (**drift becomes a
diff, not an audit-time surprise**), the self-reporting principle applied to compliance.

**Observability cardinality split** [SETTLED]:

- **Forensic cardinality** — tenant is a **trace attribute**, always present, sampled. 100% fidelity for
  "which tenant did this request belong to."
- **SLO cardinality** — tenant is a **metric label** only for the top-N / SLA'd tenants (**tiered by the
  registry's `Tier` field**); the long tail buckets. Plus a per-tenant debug flag. This bounds metric
  cardinality without losing forensic fidelity.

**Auto-masking is a runtime property** — classification flows to *presentation*: an admin sees masked, a
doctor sees plaintext, an integration sees the token. It composes the SEC-0004 `can:[]` projection and the
classification axis, and **extends to the search index + the log line**, not just the API response — a
wedge RLS/vault-only solutions structurally can't match: **RLS is column-blind**, so HIPAA minimum-necessary
is hand-rolled per-endpoint DTOs today, whereas Koan's masking is a runtime property of the field's
classification.

**Delights:** trustworthy *measured* cost/health/noisy-neighbor (Koan owns the axes); relocate-as-a-button;
the console exists by default; fluid **audited act-as as first-class `sudo`** (`using (Tenant.Use(x))` —
time-boxed, logged, with a visual indicator and auto-expiry) to reproduce a tenant's bug as that tenant,
then back out.

**Guardrails:** no backdoor through the master tenant; every cross-tenant op is explicit, host-scope, and
audited; an **unmistakable scope indicator** ("acting in HOST scope" vs "acting AS tenant X") prevents the
confused-deputy by making scope *visible*, not by trusting discipline.

---

## 12. Enforcement mechanics  [now detailed — P1]

The structural floor. The detail the round-1 draft deferred:

- **Read-filter + write-guard at the chokepoint (P1).** The guard lives at the **lowest data-adapter
  layer**, not `Entity<T>.Save()` — else direct/raw access bypasses it. **Reads** inject the tenant
  predicate; **writes stamp-and-verify** tenant on every Save/Remove and reject on mismatch. *The write
  half is a delta from the harvest — writes are the worse leak (cross-tenant corruption, not just
  exposure).* Null tenant = host/shared (clean modeling of platform rows under one filter). In `enforce`
  mode a tenant-scoped op with no tenant in scope throws a **fail-loud, fix-naming** error (charter L6/D4) —
  e.g. *"no tenant in scope for a tenant-scoped `Todo` read; wrap in `using (Tenant.Use(id))`, configure
  tenant resolution, or mark the entity `[HostScoped]`"* — never a bare exception or a cryptic null deep
  in business logic.
- **The `Tenant.None()` escape is constrained, not free.** It is the *one* loud, audited escape to host
  scope. A tenant-scoped **write** under `None()` still throws unless an explicit, source-gen-flagged
  **`[AllowUnscopedWrite]`** capability is present. `[HostScoped]` = quiet legitimate system work;
  `Tenant.None()` = loud audited ad-hoc escape. The escape binds to the query / materializes in scope —
  dodging the deferred-`IQueryable` trap (a disabled scope expiring before the query runs → silent empty
  results).
- **The shared-schema discriminator is an invisible, framework-managed shadow field** [SETTLED]. A
  tenant-scoped entity carries **no tenant property on its POCO** — which is what keeps tenancy
  *secure-by-default and structurally impossible to forget* (charter L8): you cannot forget to declare
  what you never declare. The adapter persists and filters a **hidden tenant discriminator** at the storage
  layer (a column alongside `id`; a Mongo/JSON field), driven by the ambient tenant — the guard stamps it
  on write and filters reads by it. Adapters **announce tenant-isolation support as a capability**
  (ARCH-0084); under `enforce`, a tenant-scoped entity routed to an adapter that does **not** announce
  support **fails closed** (boot-reported), never silently fail-open — the same no-capability-lies contract
  the Conformance Gate (P7) verifies. Rejected alternatives (a marker interface / a `TenantEntity<T>` base):
  both are opt-in, so a forgotten marker reintroduces the leak.

  > **ERRATUM (2026-06-22) — "a column alongside id" is re-derived to "a managed field in the persisted record".**
  > Empirical re-derivation + adversarial review ([the managed-field design memo](./tenancy-managed-field-design.md))
  > established that relational adapters persist **only `(Id, Json)`**, so the discriminator is **not** a sibling
  > column but an **invisible framework-managed field injected into the persisted record** (the Json envelope /
  > a BSON element / a JSON-file key), filtered via managed-aware field-resolution, with an **optional indexed
  > computed column** as a Schema-stage optimization. The shadow-field *intent* (no POCO property,
  > secure-by-default, can't-forget, capability-gated fail-closed) is unchanged. Two corrections to the
  > sequencing below: (a) the **flagship no-leak proof runs on SQLite first** (no-Docker, the natural `(Id,Json)`
  > envelope), not the JSON-file adapter; (b) enforcement spans **planes** — the repo chokepoint (read predicate
  > + write **stamp-AND-verify** via a conflict-aware upsert), the **cache key** (the managed axis enters
  > `CacheKey.For`), the **vector** path (fail-closed on a non-isolating adapter), and **raw/Direct** (out of
  > scope for the predicate; **RLS is the named backstop**, landing with the capability, not after). The
  > managed-field mechanism is added to the §14 settled forks.
- **RLS backstop** (Postgres/SQL Server) for the surface the application-level floor can't reach:
  `IDataService.Direct(...)` / raw SQL / bulk ops. Named explicitly as the non-structural escape; covered
  by RLS + an explicit tenant-scope, never silently. **RLS session reset is guaranteed at the physical
  pool layer** (P6): `SET LOCAL tenant` is reset on pool return (DISCARD ALL / reset-reusable where
  possible; teardown only on reset-failure) — **fail the process, never return a tainted connection.** This
  closes the #1 cross-tenant-leak class the review cited (connection-state poisoning).
- **`Tenant.FanOutQuery<T>`** is the sanctioned cross-substrate admin query (§4) so operators never drop to
  raw SQL and bypass P1.
- **Multi-axis auto-flow.** The tenant token rides into cache keys (`CacheKey` already takes `partition`),
  coherence channels, vector/search collection keys, **connection-pool session vars** (the guaranteed RLS
  reset above — the charter L9 axis), **job payloads** (auto-captured at submit, fail-closed-restored at
  execute), **message envelopes** (+ the outbox `TenantId` partition §6c), and observability labels (the
  cardinality split §11). This is the **Magic Cliff** boundary (§3a): the job/message hops need
  `Koan.Jobs` + `Koan.Messaging`.

**The connection-resolution seam** is the one genuinely-new piece of infrastructure. It must be
**re-derived empirically from the data-core code before implementing db-per-tenant** (P6) — the existing
adapter/connection wiring is the load-bearing risk, and the design must bind to what is actually there, not
to an assumed shape. This is the first task of the phased TDD (§14 [OPEN]).

**Charter compliance.** The tenant slice surfaces in the carrier's `Ambient.Describe()` (charter L11) so
"which tenant is live, and who set it" is answerable in seconds — the isolation-axis answer to the "where
was this set?" delight criterion (D6). The chokepoint guard preserves the charter's R3 guarantees:
*writes-always-invalidate-cache* composes with the tenant-keyed cache key (a tenant write invalidates only
its own tenant's cached entry); Source-XOR-Adapter and partition validation are unaffected; and the tenant
filter is a **separate, named** predicate that never widens isolation as a side effect of an unrelated
filter (charter L8/C9).

---

## 13. Adoption & go-to-market  [DECIDED]

- **GTM is greenfield-only.** The owns-every-axis thesis is both the advantage and the fatal adoption
  barrier for teams with legacy or corporate-mandated infra. Tenancy is pitched as the **"Day-0 foundation
  for your next SaaS,"** **not** a migration path onto an existing codebase. The technical complement that
  loosens this is the external-infra delegation seam (§6e) + the Adapter Forge — a team can run Koan
  tenancy *with* their mandated Pinecone/Kafka.
- **Trust/maturity** (single-author, "who do I call at 2am") is the other top barrier — answered by
  dogfood + open governance + a published security/test track record (P7 is a piece of that evidence).
- **The kernel + Magic Cliff** (§3a) is the adoption on-ramp: start with `Koan.Data` for 80%, climb to the
  full guarantee by package reference.

---

## 14. Settled forks, closed decisions & open questions

**[SETTLED] forks (this session):**
- Scope boundary flowing-only; tenant = first-class slice with isolation teeth (charter §6).
- Immutable tenant `id` + mutable `codes{current/previous}`; rename = metadata.
- Codes/domains as separate keyed entities, normalized, `Kind=Current|Previous` (fork 1).
- Lifecycle ops as `IKoanJob` (fork 2); the multi-phase ones as sagas over P8.
- Audit configurable, default light (fork 3).
- No scope contamination; root store = control plane only, independently placeable; `IsDefault` = routing
  pointer, no powers; reject tenant-zero (fork 4).
- Identity-global / membership-per-tenant / roles-on-membership; tenant gate above roles.
- Mode ladder; tenant never in the table-name spine; heterogeneous registry.
- Domain capture requires DNS-TXT verification.
- Classification = layered policy (fact vs handling); mutability lock above tenant; one extensible axis.
- `ParentTenantId` seam (flat v1); `[ProjectedToHost]` seam; external-infra delegation seam.

**[DECIDED] by the external review (verdict: ship):**
- **D1 — Migrations are additive-only in v1.** A breaking change is a **declaration**
  (`[BreakingMigration]` — the canary *refuses* it), not a detection problem; the documented escape is a
  new entity version (`PatientV2`). Watch **semantically-breaking-additive** (NOT NULL without default,
  new enum value).
- **D2 — P8 stays internal** (not a dev primitive, not a workflow engine; orchestrates only P3/P4/P6;
  no user logic).
- **D3 — Graceful layering, no Lite SKU**; name + test the **tenancy kernel** (P1–P3 + P7 @ `Koan.Data`);
  document the **Magic Cliff**.
- **The cut** — `[Phi(Embeddable=true)]` does not exist (§5e).
- **The four corrections** — durable-carrier schema (§6c); credential/KMS seam (§6c); identity-residency
  per-home-region (§6d); policy-change-is-a-migration-saga (§9a).
- **GTM greenfield-only** (§13).

**[OPEN]:**
- **The ambient carrier's own name** — charter Q1 (`Ambient` recommended; the `Koan.Context` collision is
  stale/confirmed-absent). Tenancy's developer surface is `Tenant` regardless.
- **Connection-resolution seam** — re-derive from code before implementing db-per-tenant (§12, first TDD
  task).
- **The exact SEC-0004 seam** for the membership gate (§8) — re-derive from code.
- **Tenant hierarchy behavior** — seam shipped (§6.9); behavior deferred, v1 flat.
- **SSO/SCIM brokering** — membership shape modeled now; per-IdP brokering above-layer / v2.
- **Surgical per-tenant backup/restore** — acknowledged hard; retention-window for v1.
- **Classification × analytics residency** — `[ProjectedToHost]` over classified fields needs a
  cross-region detokenization fan-out that fights the pinning it enforced (§5f); v1 excludes classified
  fields from host projection by default.

---

## 15. Roadmap (built on P4 — north-stars, not v1)

These are scope-out for v1; the v1 primitives make them *tractable*. They cluster on **P4 (logical
Export/Import) + cross-axis snapshot** — the "data-as-versioned-artifact" substrate:

- **Point-in-time snapshot query** — `Tenant.Snapshot(id, at: t)` → a read-only cross-axis time-travel view
  ("show me what the customer saw at 2pm yesterday"). True point-in-time is the hard part.
- **Merge / Split — M&A as a verb** — `Tenant.Merge(A, B, target)` resolves surrogate-key collisions,
  re-assigns vault tokens, maps audit logs, signs an M&A compliance certificate.
- **The Git model for data** — `Tenant.Branch` + `Tenant.Diff` + `Tenant.Checkout`: branch, modify, diff
  the cross-axis delta, apply back as an audited compensating transaction. (Wedge: branch-the-DB-only tools
  can't branch vectors + blobs + cache; a half-branch of an AI app is no branch.)
- **Trial tenants with TTL** — `Provision(ttl:)` auto-schedules `Erase`, cancelled on conversion (P2 + jobs).
- **Tenant branching / snapshot provisioning** — `Provision(from:, Snapshot)` (P4 + cross-axis snapshot).
- **Schema drift detection** — the framework knows each tenant's expected schema → flags drift (P5).
- **Tenant-aware dev mode** — a dev tenant-switcher, color-coded by context.
- **BYOC via `Relocate`** — "the hardest B2B objection in one method call." A massive distributed-systems +
  trust-boundary undertaking; build P4 + the Relocate saga so BYOC is *tractable* later, do not build it
  in v1.
- **Full tenant-aware canary / per-tenant logic versioning** — the ambient carries a version slice; v1
  subset is **cohort canary via the registry** (the registry is the canary-cohort selector, §6.6 — cohort
  selection, not full logic-versioning). Defer the full canary-by-tenant.

---

## Appendix A — the pain harvest

Four blind lenses (web research), 41 sourced pains, condensed to the clusters that shaped the design.
Severity/frequency and sources are in the harvest record (session artifact); the high-signal clusters:

1. **The forgotten-boundary leak** *(critical, all 4 lenses — the #1)*: forgotten read filter, **writes
   bypass the filter** (worse — corruption), raw-SQL/bulk bypass, IDOR-from-URL, tenantId-from-header.
   → §8 (gate above roles, request-tenant-as-hint), §12 (read-filter + write-guard + RLS).
2. **Tenant lost across the async hop** *(ubiquitous, 3 lenses)*: jobs/queues/webhooks lose context →
   wrong/host tenant. → §12 multi-axis auto-capture+fail-closed-restore (the Magic Cliff structural win).
3. **Infra-below-the-query leaks** *(high)*: cache key forgot the tenant; pooled-connection session-state
   reuse; options cached for the wrong tenant; sticky resolver. → immutable AsyncLocal carrier + P6 session
   reset + multi-axis flow.
4. **Scaling cliffs** *(high)*: db-per-tenant connection explosion; schema-per-tenant catalog bloat. → §2
   honest-limit; default shared-schema; P6 publishes the honest ceiling.
5. **The pooled→silo migration** *(high)*: 6–12 month re-architecture. → §9 Relocate saga (code frozen).
6. **Fleet migrations / drift** *(high)*: N-database fan-out, partial failure, snowflake drift, no ledger.
   → §9 lifecycle-as-sagas (P5a/P5b/P8, resumable, cursored); no migration-history replay (schema derived).
7. **Cross-tenant admin/reporting fights isolation** *(medium)*: the legitimate 5%; deferred-query trap.
   → §11 host console (explicit/audited); §12 materialize-in-scope escape + `FanOutQuery`.
8. **Operator blind spots** *(high)*: cost attribution, per-tenant SLA, noisy-neighbor finder, residency,
   GDPR erasure, one-call provisioning. → §11 console (measured, not guessed).
9. **Identity** *(critical/high)*: same-user-many-tenants, org-switch = silent privilege change, global
   roles = inflation, deprovisioning incomplete, domain-capture takeover, invite edge cases. → §4/§7/§8.

**The validation:** practitioners independently re-derived our settled decisions (membership-carries-role,
resolved-per-check, verified-domain, fail-closed, multi-axis flow) as the fixes they reached the hard way.
The design is the convergent answer, made structural.

---

## Appendix B — the external-review trail

Three rounds (three frontier models each) + a four-persona delight harvest, fully recorded in
[tenancy-external-review-findings.md](./tenancy-external-review-findings.md). The arc:

- **Round 1** — validated the thesis and all settled forks; corrected **6 overstated claims** toward
  honesty (§1a); collapsed ~20 proposals to **7 load-bearing primitives + 2 seams + 1 new fork (PII
  residency)**.
- **PII-residency investigation** — resolved to the declarative **classification axis** (§5), grounded in
  data-privacy-vault prior art and the HIPAA-retain-vs-GDPR-erase contradiction that proves
  per-classification policy is mandatory.
- **Round 2** — surfaced the **8th primitive (P8 saga coordinator)**; split P5 into executor + orchestrator;
  crowned the **erasure certificate** the flagship; named the **classification × AI** tension; demanded 3
  more honesty corrections.
- **Delight harvest** (dev / architect / operator / competitive) — zero design reversals; unanimous
  flagship = the erasure certificate; mapped each primitive to a persona-grounded delight + competitive
  grounding (EDPB-2026, the in-house-vault build cost, the month-nine migration-out, branch-the-DB-only).
- **Round 3 (final)** — unanimous **ship**; **4 corrections + 1 cut + 3 closed decisions + greenfield GTM**;
  the #1 regret class = durable carriers must carry tenant + classification at v1 (§6c).
- **Post-review** — classification as one extensible axis (not N attributes); `[Secret]` =
  write-only/masked-read; the Adapter Forge spun off as the companion capability
  ([ARCH-0094](../decisions/ARCH-0094-adapter-forge.md)).

**Coherence verdict (all three, final round):** the developer surface stayed exquisitely small; the thesis
holds externally; the accretion is inherent and host-plane. **Verdict: ship the ADR (ARCH-0095).**
