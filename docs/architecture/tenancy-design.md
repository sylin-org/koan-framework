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

> **What this is.** The implementation-ready design for first-class multi-tenancy in Koan. Tenancy is
> the **flagship typed slice** of the Ambient primitive defined by the
> [Ambient Context Charter](./ambient-context-charter.md); this document is the concrete model the
> charter's laws and truth-test are applied to. It depends on the ambient carrier (the charter) and on
> [SEC-0004](../decisions/) (capability authz floor), [DATA-0104](../decisions/DATA-0104-generic-entity-storage-naming.md)
> (storage-name grammar), [DATA-0077](../decisions/) (`PartitionNameValidator` identifier alphabet),
> [JOBS-0005](../decisions/) (durable jobs), and the [WEB-0068](../decisions/) read-path predicate machinery.
>
> **Status: design-only.** Captured in prep for implementation. Forks marked **[SETTLED]** are ratified
> by the architect (this session); **[OPEN]** items are still to decide. Nothing here is built yet.

The two mandates carry from the charter: **Simplification** ("same developer experience regardless of
tenancy mode") and **Delight** ("what would developers *and operators* love to have?"). Greenfield,
break-and-rebuild is desired. The design is grounded in a four-lens harvest of real practitioner pain
(41 sourced pains; see [Appendix A](#appendix-a--the-pain-harvest)).

---

## 1. Principles (the load-bearing decisions)

1. **Same DX across every mode.** The developer writes `Todo.Get(id)` / `todo.Save()` identically whether
   the tenant is pooled, schema-isolated, or on a dedicated database. The mode is **configuration + a
   per-tenant registry strategy**, never code. This is the invariant everything else serves.
2. **Isolation is a sliding boundary at four depths.** Deployment → connection → schema → row. The
   framework's job is to let you slide the boundary by config while the entity code stays frozen.
3. **Identity is global; membership is per-tenant; roles live on the membership.** One human, N tenants
   (the StackExchange/Slack model). The `User` is **not** a tenant-scoped entity.
4. **Tenant identity is an immutable surrogate.** Physical storage binds to an immutable `id`
   (`a1b2c3`); human-facing `codes` are mutable aliases. A rename is a metadata change, never a
   re-keying migration.
5. **Tenant is ambient (isolation); principal is explicit (authority); membership is the bridge.** The
   tenant slice flows via the ambient carrier. The principal does **not** auto-flow (no invisible
   authority). Authorization = `Ambient.Tenant` (ambient) × explicit principal → resolve membership.
6. **Fail-closed, secure-by-default, structurally enforced.** No tenant on a scoped entity → throw.
   Entities are tenant-scoped by default once tenancy is on (`[HostScoped]` opts out). A forgotten
   predicate must be *structurally impossible*, not a discipline (the #1 harvested pain).
7. **The control plane is dogfooded Koan.** The registry, identity, and lifecycle are `[HostScoped]`
   `Entity<T>` + `IKoanJob<T>` — so the operator inherits Koan's entire surface (query, project, audit,
   coherence, MCP) over the fleet for free.
8. **No scope contamination.** The root/control-plane store holds **only** control-plane data. Every
   tenant's product data — including the default/master tenant's — lives in its own placement.
9. **Koan owns every axis.** Tenant flows into data, cache, vector, search, jobs, messaging, and
   observability; lifecycle verbs (provision/erase/relocate) fan out across all of them. This is the
   structural advantage no single-ORM library has.

---

## 2. The isolation mode ladder  [SETTLED]

The boundary sits at one of four depths. Mode 1 is out of scope (a deployment topology needing zero
framework support); modes 2–4 are same-runtime tenancy.

| Mode | Boundary depth | Isolation mechanism | Koan strategy |
|---|---|---|---|
| **1 — Silo** | deployment | separate process/infra | **out of scope** (zero framework) |
| **2/3 — Database-per-tenant** | connection | per-tenant connection, **mapped or templated** | route connection by `Tenant.Current.Id` |
| **4a — Schema-per-tenant** | one DB, native schema | DB schema = namespace prefix → `acme.todo` | route schema/namespace by tenant |
| **4b — Shared-schema** | one table | discriminator column + mandatory filter (+ RLS) | inject tenant predicate at the repository |

**Two refinements that were settled:**

- **Modes 2 and 3 are one strategy** ("database-per-tenant") differing only in connection sourcing
  (explicit per-tenant config block vs. a `{tenant}`-substituted template).
- **Tenant never enters the table-name spine.** The `{tenant}-{model}` sketch would collide with
  [DATA-0104](../decisions/DATA-0104-generic-entity-storage-naming.md)'s `-` = spine separator. Instead
  tenant routes to a **native boundary**: 4a uses the database's own schema (which rides DATA-0104's `.`
  namespace separator natively — `acme.todo`), 4b uses a discriminator column (name stays `todo`). The
  just-settled storage grammar stays clean.

**Heterogeneous by design.** Placement is per-tenant data, not a global switch: tenant "acme" (premium)
can be on a dedicated database while tenants B–Z share a schema. The registry holds each tenant's
strategy; the operator can change it (see [§7 Relocate](#7-lifecycle-operations--settled)).

**Honest limit:** the ladder makes substrate *choice* and *movement* cheap and visible; it does **not**
repeal physics. Database-per-tenant still fans out connection pools; schema-per-tenant still bloats the
Postgres catalog past a few hundred tenants. The design *steers* (defaults to shared-schema, the
scalable mode) and makes the upgrade a verb — it does not eliminate the cliff.

---

## 3. The developer surface  [SETTLED]

```csharp
// Read / scope — ambient, AsyncLocal, auto-restoring (per the charter's carrier)
Tenant.Current                       // the rich object {Id, Codes, Name}; throws if fail-closed & unset
using (Tenant.Use("a1b2c3")) { ... } // explicit scope (admin, jobs, tests, support act-as)
using (Tenant.None()) { ... }        // the ONE loud, audited escape to host/control-plane scope

// Lifecycle (framework verbs — same-DX means same provisioning; each is an IKoanJob)
await Tenant.Provision("a1b2c3");    // create placement, ensure schema, seed default membership
await Tenant.Relocate("a1b2c3", to); // move substrate (pooled→silo), entity code frozen
await Tenant.Erase("a1b2c3");        // GDPR — fan out across every tenant-scoped axis
await Tenant.Rename("a1b2c3", newCode: "mslp", newName: "Microslop");

// Classification — secure-by-default once tenancy is ON
public class Todo : Entity<Todo> { }              // tenant-scoped automatically
[HostScoped] public class TenantRecord : ... { }  // system/registry entities opt out
```

The rich `Tenant.Current` object exposes `.Id` (physical, framework-only), `.Codes.Current` (canonical
link), and `.Name` (display). **App code never reads `.Id`** — the framework's physical axis extracts it;
the rich object is an app-space convenience.

**Sane defaults:** tenancy **OFF** by default (single-tenant apps pay nothing). When ON: secure-by-default
(scoped unless `[HostScoped]`), fail-closed, default resolution chain subdomain→header→JWT-claim, default
mode `SharedSchema` (works on one connection, upgrade by config). The boot report prints the posture —
`Tenancy: SharedSchema · query-filter+RLS · fail-closed · 3 tenants` — making isolation **verifiable at
startup**, not discovered at breach.

---

## 4. The control-plane data model  [SETTLED]

All `[HostScoped]`, living in the root store ([§1.8 no contamination](#1-principles-the-load-bearing-decisions)).

```csharp
[HostScoped] sealed class Tenant : Entity<Tenant, string>   // Id = the immutable "a1b2c3"
{
    public string Name { get; init; }                 // mutable display
    public TenantPolicy Policy { get; init; }         // joinMode, exclusive, defaultRoles
    public TenantPlacement Placement { get; init; }   // substrate, region, tier, dataSourceRef ← a REF, never the secret
    public TenantStatus Status { get; init; }         // Provisioning|Active|Suspended|Relocating|Erasing|Erased
    public bool IsDefault { get; init; }              // routing pointer — bare domain lands here; NO extra powers
    [Timestamp] DateTimeOffset CreatedAt { get; init; }
    [Timestamp(OnSave = true)] DateTimeOffset UpdatedAt { get; init; }
}

[HostScoped] sealed class TenantCode   : Entity<TenantCode, string>   // Id = the code → O(1) resolve + global-unique
{ public string TenantId; public CodeKind Kind; }                     //   Kind = Current | Previous
[HostScoped] sealed class TenantDomain : Entity<TenantDomain, string> // Id = the domain → global-unique capture map
{ public string TenantId; public DomainVerification Verification; public bool Exclusive; }

[HostScoped] sealed class Identity   : Entity<Identity>   { public string Subject; public string Email; /* IdP refs */ }
[HostScoped] sealed class Membership : Entity<Membership> { public string IdentityId; public string TenantId;
                                                            public string[] Roles; public MembershipStatus Status; }
[HostScoped] sealed class Invite     : Entity<Invite>     { /* email, tenantId, roles, token, expiresAt, status */ }

[HostScoped, AppendOnly] sealed class AuditEntry : Entity<AuditEntry>
{ public string ActorId; public string Action; public string? TargetTenantId; public AuditScope Scope; [Timestamp] DateTimeOffset At; }

[HostScoped] sealed class TenantOperation : Entity<TenantOperation>, IKoanJob<TenantOperation>  // resumable lifecycle op
{ public string TenantId; public OperationKind Kind; public OperationStatus Status; public int Cursor; /* store N of M */ }
```

**The decisions baked into this model:**

1. **Codes & domains are keyed entities** [SETTLED, fork 1]. `Id = the code/domain`, so resolution is an
   O(1) keyed `Get` and **global uniqueness is the key constraint** (race-safe, no TOCTOU). Fully
   normalized: `TenantCode` is the sole source of truth; the *current* code is the row with
   `Kind = Current`; `previous` rows are live redirects. (Optional micro-opt: a denormalized
   `Tenant.CurrentCode` scalar synchronized by `Rename` — defaulted off to honor single-source-of-truth.)
2. **Secrets are referenced, never stored.** `Placement.dataSourceRef` is a *name* resolved through
   Koan's existing connection/secret machinery. The registry can be queried/exported/MCP-exposed without
   surfacing a connection string.
3. **Membership is host-stored, tenant-filtered.** The operator reads it unfiltered (the onboarding
   cross-tenant lookup needs that); a *tenant*-admin reading "my members" gets the same entity,
   tenant-filtered by the [WEB-0068](../decisions/) read-path predicate machinery. The control plane
   reuses the data plane's enforcement — no parallel admin model.
4. **Operator-view vs tenant-view is field projection.** The operator sees the full `Tenant` row
   (placement, policy, cost); a tenant-admin sees a projection (name, codes, their config). One row, two
   faces.
5. **Lifecycle ops are `IKoanJob`s** [SETTLED, fork 2]. Provision/Relocate/Erase inherit durability,
   retry, a ledger, the conveyor (fan across stores one cursor at a time), and visibility from JOBS-0005
   — directly answering the harvested "no per-tenant migration ledger / no partial-failure recovery /
   fleet stranded mid-migration" pain.
6. **The registry is live truth, coherence-invalidated.** Editing a `Tenant` row changes fleet behavior
   on the next request (cached + evicted across nodes via the coherence channel). No redeploy.
7. **Audit is configurable, default light** [SETTLED, fork 3]. `Koan:Tenancy:Audit = Mutations` (default:
   lifecycle verbs, act-as, placement/policy/membership changes) `| Full` (adds every cross-tenant read).
8. **No master backdoor** [SETTLED, fork 4]. The default/master tenant is `IsDefault: true` — a routing
   pointer with **zero special data powers**. Cross-tenant power is host-plane only. The "tenant-zero"
   model (control-plane-as-a-tenant) is explicitly **rejected** — it re-creates the `AppHost` split-brain
   Facet 3 is killing.

### Worked example — code lifecycle (Microslop's two rebrands)

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

---

## 5. Resolution & onboarding  [SETTLED]

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

---

## 6. Authorization model  [SETTLED]

Three tiers: **platform operator** (host scope — all tenants/registry/fleet) · **tenant admin** (one
tenant; manages its members/roles/config; blind to others) · **tenant user** (one tenant, role-bounded).

**The rule that makes it safe:** the **tenant gate is prior to and independent of the role check**.
Authorization evaluates (1) does the principal hold a membership in the resolved tenant? — fail-closed,
deny if not — *then* (2) does their role in *this* tenant permit the action. You cannot role your way
across a tenant boundary; tenant is an isolation axis **above** roles.

**Composition:** this extends, not replaces, the SEC-0004 `IAuthorize` capability floor — the floor gates
ops by capability; tenancy adds a *prior* membership gate and qualifies every capability check by the
resolved tenant. *(The exact SEC-0004 seam to be re-derived from code before implementation.)*

**Two security properties that fall out** (each closes a harvested critical/high pain):

- **Membership resolved per request, server-side, never trusted from the token.** An org-switch
  re-resolves authority (no stale-claim privilege window); a removed membership denies on the *next*
  request (no deprovisioning lag waiting for token expiry).
- **The request-tenant is a routing hint, authorized against the principal's memberships** — never
  authority in itself. Closes the "tenant id from URL/header not token" IDOR/BOLA class.

---

## 7. Lifecycle operations  [SETTLED]

Each is an `IKoanJob` (`TenantOperation`) — durable, cursored, resumable, audited:

- **Provision** → create placement, ensure schema on the connection (no migration-history replay — Koan
  derives schema from the entity model), seed the buyer's default membership, → `Active`.
- **Relocate** (pooled → dedicated) → copy across stores cursored; cutover flips the registry; the
  runtime re-routes to the new `dataSourceRef`; entity code never moved. *Honest: the data copy is real
  work; what's removed is the code/routing re-architecture (the harvested "6–12 month" pain), not the
  bytes.*
- **Erase** (GDPR) → fan out across every tenant-scoped axis Koan knows (data, cache, vector, search,
  blobs) → emit an audit deletion certificate. *Honest: backups remain retention-window expiry, not
  surgical.*
- **Suspend** → set `Status = Suspended`; because tenant + membership resolve per request against a
  fail-closed gate, every request denies on the **next call** — atomic, no token-expiry wait.

---

## 8. The operator / service-owner console  [SETTLED — direction]

The host-face **projection** of the control-plane entities (not a product to build — `EntityController`
+ host-scope authz over the §4 entities). What the operator sees, each row a harvested pain killed:

| Sees… | Kills | Severity |
|---|---|---|
| **Fleet** — per tenant: codes.current, name, substrate, region, tier, status, members | capacity planning / manual rebalancing | medium |
| **Per-tenant health / SLIs** | "one tenant's pain is invisible in aggregates" | medium |
| **Per-tenant cost / consumption** — measured (runtime tags every op), not proxy-guessed | "cost attribution impossible on shared resources" | **high** |
| **Noisy-neighbor finder** — who's hammering the shared store now | "hunt the offender via pg_stat_activity" (Cloudflare) | **high** |
| **Placement & relocate** | "pooled→silo is a 6–12 month re-architecture" | **high** |
| **Lifecycle** — provision/deprovision/erase/restore | GDPR erasure, one-call provisioning | high |
| **Access across tenants** — who can reach what; atomic revoke | "deprovisioning incomplete — access outlives the event" | **high** |
| **Domain-capture approvals** | "verified-domain capture takeover vector" | high |
| **Posture & drift** — enforcement mode, fail-closed status, per-tenant config divergence | "config drift / pressure to fork the codebase" | medium |

**Delights:** trustworthy *measured* cost/health/noisy-neighbor (Koan owns the axes); relocate-as-a-button
(the buyer can self-serve a tier upgrade that requests the placement); the console *exists by default* (a
projection, not a build, in the self-reporting lineage); fluid **audited act-as** (`using
(Tenant.Use(x))`) to reproduce a tenant's bug as that tenant, then back out.

**Guardrails:** no backdoor through the master tenant; every cross-tenant op is explicit, host-scope, and
audited; an **unmistakable scope indicator** ("acting in HOST scope" vs "acting AS tenant X") prevents the
confused-deputy by making scope *visible*, not by trusting discipline.

---

## 9. Enforcement mechanics  [PARTIALLY SETTLED — detail is the next design task]

Decided shape (the developer-facing half we have **not** yet fully detailed):

- **Read-filter + write-guard at the repository chokepoint.** Reads inject the tenant predicate; **writes
  stamp-and-verify** tenant on every Save/Remove and reject on mismatch. *The write half is a delta from
  the harvest — writes are the worse leak (cross-tenant corruption, not just exposure).*
- **RLS backstop** (Postgres/SQL Server) for the one surface the structural floor can't reach:
  `IDataService.Direct(...)` / raw SQL / bulk ops. Named explicitly as the non-structural escape; covered
  by RLS + an explicit tenant-scope, never silently.
- **Multi-axis auto-flow.** The tenant token rides into cache keys (`CacheKey` already takes `partition`),
  coherence, vector/search collection keys, job payloads (auto-captured at submit, fail-closed-restored at
  execute), message envelopes, and observability labels (opt-in/sampled — cardinality cost is real).
- **The cross-tenant escape must bind to the query / materialize-in-scope**, dodging ABP's real bug
  (`Disable` scope expiring before a deferred `IQueryable` runs → silent empty results).

**Open detail:** exactly how the read-filter + write-guard + RLS compose invisibly at the chokepoint, and
the precise current connection-resolution seam in the data core (to be re-derived empirically before
implementation — it is the one genuinely-new piece of infrastructure).

---

## 10. Settled forks & open questions

**[SETTLED] this session:**
- Scope boundary flowing-only; tenant = first-class slice with isolation teeth (charter §6).
- Immutable tenant `id` + mutable `codes{current/previous}`; rename = metadata.
- Codes/domains as separate keyed entities, normalized, `Kind=Current|Previous` (fork 1).
- Lifecycle ops as `IKoanJob` (fork 2).
- Audit configurable, default light (fork 3).
- No scope contamination; root store = control plane only; `IsDefault` = routing pointer, no powers;
  reject tenant-zero (fork 4).
- Identity-global / membership-per-tenant / roles-on-membership; tenant gate above roles.
- Mode ladder (silo out-of-scope / db-per-tenant / schema-per-tenant / shared-schema); tenant never in
  the table-name spine; heterogeneous registry.
- Domain capture requires DNS-TXT verification.

**[OPEN]:**
- **Enforcement mechanics detail** (§9) — the next design task.
- **The ambient carrier's own name** — charter Q1 (`Ambient` recommended; the `Koan.Context` collision is
  stale/confirmed-absent). Tenancy's developer surface is `Tenant` regardless.
- **Connection-resolution seam** — re-derive from code before implementing db-per-tenant.
- **Tenant hierarchy** (sub-tenants / nested isolation) — deferred; v1 is flat.
- **SSO/SCIM brokering, invite edge cases** — model the membership shape now; per-IdP brokering is
  above-layer / v2, not a v1 claim.
- **Surgical per-tenant backup/restore** — acknowledged hard; retention-window for v1.

---

## Appendix A — the pain harvest

Four blind lenses (web research), 41 sourced pains, condensed to the clusters that shaped the design.
Severity/frequency and sources are in the harvest record (session artifact); the high-signal clusters:

1. **The forgotten-boundary leak** *(critical, all 4 lenses — the #1)*: forgotten read filter, **writes
   bypass the filter** (worse — corruption), raw-SQL/bulk bypass, IDOR-from-URL, tenantId-from-header.
   → §6 (gate above roles, request-tenant-as-hint), §9 (read-filter + write-guard + RLS).
2. **Tenant lost across the async hop** *(ubiquitous, 3 lenses)*: jobs/queues/webhooks lose context →
   wrong/host tenant. → §9 multi-axis auto-capture+fail-closed-restore (Koan owns jobs + bus — structural
   win).
3. **Infra-below-the-query leaks** *(high)*: cache key forgot the tenant; pooled-connection session-state
   reuse; options cached for the wrong tenant; **sticky resolver** (Finbuckle #840). → immutable
   AsyncLocal carrier (no mutable holder to mis-scope) + multi-axis flow.
4. **Scaling cliffs** *(high)*: db-per-tenant connection explosion; schema-per-tenant catalog bloat;
   pool-incompatible-with-db-per-tenant. → §2 honest-limit; default shared-schema, steer placement.
5. **The pooled→silo migration** *(high)*: 6–12 month re-architecture. → §7 Relocate verb (code frozen).
6. **Fleet migrations / drift** *(high)*: N-database fan-out, partial failure, snowflake drift, no ledger.
   → §4.5 lifecycle-as-jobs (resumable, cursored); no migration-history replay (schema derived).
7. **Cross-tenant admin/reporting fights isolation** *(medium)*: the legitimate 5%; ABP `Disable`
   deferred-query trap. → §8 host console (explicit/audited); §9 materialize-in-scope escape.
8. **Operator blind spots** *(high)*: cost attribution, per-tenant SLA, noisy-neighbor finder, residency,
   GDPR erasure, one-call provisioning. → §8 console (measured, not guessed — Koan owns the axes).
9. **Identity** *(critical/high)*: same-user-many-tenants, org-switch = silent privilege change, global
   roles = inflation, deprovisioning incomplete, domain-capture takeover, invite edge cases. → §3/§5/§6
   (membership-carries-role, resolved-per-check, DNS-verified capture).

**The validation:** practitioners independently re-derived our settled decisions
(membership-carries-role, resolved-per-check, verified-domain, fail-closed, multi-axis flow) as the fixes
they reached the hard way. The design is the convergent answer, made structural.

**Six deltas the harvest forced** (now folded above): write-path enforcement (§9), the Direct/raw-SQL
named escape (§9), tenant-lifecycle verbs (§3/§7), membership-resolved-per-request as a load-bearing
invariant (§6), the deferred-query escape guard (§9), region/placement + per-tenant config as registry
fields (§4).
