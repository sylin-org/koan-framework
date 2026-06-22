---
type: RFC
domain: core
title: "Koan Multi-Tenancy — Request for External Design Review"
audience: [frontier-models, external-architects]
status: open-for-review
last_updated: 2026-06-21
---

# Koan Multi-Tenancy — Request for External Design Review

> **You are being asked, as a frontier AI model / external architect, to review a multi-tenancy
> design for a .NET application framework called Koan.** This document is fully self-contained: it
> explains what Koan is, the resources the design builds on, and the complete tenancy plan. We want
> your **honest, adversarial evaluation** — not validation theater. Disagree where warranted, find the
> holes, and (most important) **investigate and propose what "delight" should mean for this feature
> set.** What we want most is to be *surprised* by something we missed.

---

## 0. What we're asking you to do

Three things, in order of how much we value them:

1. **Investigate & propose delight (highest value).** Beyond correctness — what would make developers
   *and operators* genuinely **love** doing multi-tenancy on this framework? What is the "I can't
   believe it's this easy" moment? What would make a team *choose* Koan specifically for a
   multi-tenant SaaS? Research prior art (ABP, Finbuckle, Nile, Neon, Clerk/WorkOS, Auth0
   Organizations, AWS/Azure SaaS guidance, Stripe-style APIs, anything) and bring ideas we haven't had.
   Our own delight thesis is in §7 — **go beyond it, and challenge it.**
2. **Find the holes.** Where does this design fail — at scale, under adversarial conditions, in real
   SaaS operations, in edge cases? Rank by severity. Be specific and concrete.
3. **Validate the structural claims & the forks.** Which of our claims hold up and which are
   overstated? Where would you have decided a fork differently, and why? (Forks are listed in §6.)

Please structure your reply as: **(a)** delight proposals (ranked, with reasoning), **(b)** concrete
holes (ranked), **(c)** fork-by-fork take, **(d)** validation summary, **(e)** "what you should be
asking but aren't."

**Context that frees the design:** Koan is pre-1.0, has a single author/consumer, and is *not* yet
battle-tested at scale. There is **no backward-compatibility constraint** — break-and-rebuild toward
"fewer but more meaningful parts" is explicitly desired. So do not temper proposals to fit an existing
API; propose the *right* thing. Conversely, treat scale claims skeptically — this hasn't run at 10,000
tenants.

---

## 1. What Koan is

Koan is an opinionated **application meta-framework** on **.NET 10** whose thesis is to collapse the
entire backend stack behind one **entity-first** programming model. Three ideas define it:

**a) Entity-first, no ceremony.** You model data as entities and call static methods on them. There
are no repositories, no `DbContext`, no manual data-access layer:

```csharp
public class Todo : Entity<Todo> { public string Title { get; set; } }

var t = await Todo.Get(id);
await t.Save();
var open = await Todo.Query(x => !x.Done);
```

**b) "Reference = Intent."** Adding a NuGet package reference *is* the configuration. A source-generated
registrar auto-activates the capability at boot — no `services.AddX()` wiring. Reference
`Koan.Data.Postgres` and Postgres is live; reference the Redis cache adapter and L2 caching activates.
The canonical bootstrap is one line (`AddKoan()`).

**c) Multi-provider transparency.** The *same* entity code runs over SQL, NoSQL, Vector, JSON, or
in-memory stores. Adapters announce their **capabilities** (a typed capability model — e.g. "I support
pushdown filtering with these operators," "I support conditional/CAS writes," "I support TTL
indexes"), and the framework composes against what's announced, degrading honestly when a capability
is absent.

### The fact that matters most for tenancy

**Koan owns every axis of the backend in one runtime** — data, web, cache, vector search, background
jobs, messaging, media, blob storage, auth, AI, orchestration, and observability are all first-party
pillars under the same entity model and the same boot/registry. This is the structural difference from
every existing multi-tenancy library (ABP, Finbuckle, apartment, django-tenants, Hibernate filters),
each of which is *bolted onto a single ORM* and can therefore only see the query layer. As you'll see,
a large fraction of real-world tenancy pain is "the library couldn't reach past the ORM." Koan can.

### The ambient pattern Koan already has

Koan already contains the industry-convergent "ambient context" pattern, in a primitive called
`EntityContext`: an **immutable** record flowed via `AsyncLocal`, *derived-on-write* (you don't mutate
it; you produce a new one), with **nearest-wins shadowing** and **auto-restore on scope exit**:

```csharp
using (EntityContext.With(partition: "archive")) {
    // reads/writes in here use the "archive" partition; restored on dispose
}
```

This is the same shape as Go's `context.Context`, OpenTelemetry's Context/Baggage, Java's
`ScopedValue`, Rails `CurrentAttributes`, and ABP's `CurrentTenant.Change()`. Tenancy's carrier is a
**generalization of this existing, proven primitive** — not an invention. (Generalizing it is a
separate in-flight workstream called "Facet 3 / Ambient"; tenancy is its flagship consumer.)

---

## 2. The resources tenancy builds on

These existing Koan mechanisms are the seams the design leverages — they're why we believe this is
mostly *wiring tenant into load-bearing chokepoints* rather than building new infrastructure:

| Resource | What it gives tenancy |
|---|---|
| **The storage-name chokepoint** | Every adapter derives physical names (table/collection/key/index) through one resolver. One edit reaches all adapters at once. |
| **`Entity<T>` + surrogate keys** | The surrogate-key discipline (immutable machine IDs, not human strings) — applied to tenant identity. |
| **Background-jobs pillar** | Durable, retryable, **resumable/cursored** jobs with a ledger. Tenant lifecycle ops (provision/relocate/erase) become jobs and inherit all of that for free. |
| **Cache keys + coherence channel** | Cache keys already carry a partition axis; a cross-node invalidation channel already exists. Tenant rides both. |
| **Read-path predicate machinery** | A per-read-surface filter injection point (used today for row-visibility/authorization). Reused for the tenant read-filter. |
| **Capability-based authorization floor** | An existing `Can(subject, action, resource)` authz model ("gate · constrain · project") where the rule *is* the collection filter. Tenancy composes a prior membership gate above it. |
| **Self-reporting boot** | Pillars describe their capabilities in a structured boot report. Tenancy posture (mode, enforcement, fail-closed, tenant count) prints at startup → verifiable in application space. |

---

## 3. The mandate

Two requirements govern every decision:

- **Simplification** — "fewer but more meaningful moving parts."
- **Delight** — "what would developers love to have?"

And one **load-bearing invariant**:

> **The developer experience MUST be identical regardless of tenancy mode.** Whether a tenant is
> pooled (shared table), schema-isolated, or on a dedicated database, the developer writes
> `Todo.Get(id)` / `todo.Save()` *identically*. The mode is a deployment/configuration choice, never a
> code change.

---

## 4. The tenancy design

### 4.1 Isolation is a sliding boundary at four depths

| Mode | Boundary | Mechanism | In scope? |
|---|---|---|---|
| **Silo** | deployment | separate process/infra, same code | **out of scope** — needs zero framework support |
| **Database-per-tenant** | connection | per-tenant connection (mapped or templated) | yes |
| **Schema-per-tenant** | one DB, native schema | DB schema as namespace prefix (`acme.todo`) | yes |
| **Shared-schema** | one table | discriminator column + mandatory filter (+ RLS backstop) | yes |

Placement is **per-tenant data, not a global switch** — tenant "acme" (premium) can be on a dedicated
database while tenants B–Z share a schema. The framework's job is to let you **slide the boundary by
configuration while the entity code stays frozen**, and to make *moving* a tenant between modes a verb
rather than a re-architecture.

> **Honest limit (we are not claiming to repeal physics):** database-per-tenant still fans out
> connection pools; schema-per-tenant still bloats the database catalog past a few hundred tenants. We
> make the substrate *choice* and *movement* cheap and visible, and we default to shared-schema (the
> scalable mode). We do **not** eliminate the scaling cliffs.

### 4.2 The developer surface

```csharp
Tenant.Current                        // the rich object {Id, Codes, Name}; throws if unset & fail-closed
using (Tenant.Use("a1b2c3")) { ... }  // explicit scope (admin, jobs, tests, support "act-as")
using (Tenant.None()) { ... }         // the ONE loud, audited escape to host/control-plane scope

await Tenant.Provision("a1b2c3");     // create placement, ensure schema, seed default membership
await Tenant.Relocate("a1b2c3", to);  // move substrate (pooled → silo); entity code never changes
await Tenant.Erase("a1b2c3");         // GDPR — fan out deletion across every tenant-scoped axis

public class Todo : Entity<Todo> { }             // tenant-scoped automatically when tenancy is ON
[HostScoped] public class TenantRecord { ... }   // system/control-plane entities opt out
```

Defaults: tenancy **OFF** by default (single-tenant apps pay nothing); when **ON** → secure-by-default
(scoped unless `[HostScoped]`), **fail-closed** (no tenant on a scoped entity → throw), default mode
shared-schema, and the boot report prints the posture.

### 4.3 Tenant identity: immutable surrogate + mutable aliases

A tenant has an **immutable `id`** (e.g. `a1b2c3`) that *all* physical storage binds to, and **mutable
human-facing codes**. A rename is therefore a metadata change, never a re-keying migration:

```
Tenant     { Id="a1b2c3", Name="Microslop" }     // Name & codes mutable; Id never changes
TenantCode { Id="mslp",  TenantId="a1b2c3", Kind=Current }     // the code IS the entity key
TenantCode { Id="msft",  TenantId="a1b2c3", Kind=Previous }    // old codes still resolve → 301 redirect
```

Because the *code* is the entity key, resolution (`code → tenant`) is an O(1) keyed lookup and **global
uniqueness across all tenants is enforced by the key constraint** (race-safe, no application check).
Domains for "domain capture" follow the same pattern (`TenantDomain.Id = the domain`).

### 4.4 Identity is global; membership is per-tenant; roles live on the membership

The single most important structural decision, forced by the "one human in many tenants"
(StackExchange / Slack / GitHub-org) reality. The user is **not** a tenant-scoped entity:

```csharp
[HostScoped] Identity   { Id, Subject, Email }                 // global "who" — one per human
[HostScoped] Membership { Id, IdentityId, TenantId, Roles[] }  // the join — one per (human, tenant)
[HostScoped] Tenant     { Id, Codes, Name, Policy, Placement } // the registry
```

Your role in tenant A lives on your tenant-A membership and says nothing about tenant B.

### 4.5 Onboarding, resolution, and tenant claiming

Two resolutions intersect: the **request** axis (which tenant is this request for —
subdomain/header/route) and the **identity** axis (which tenants can this human enter). Landing is the
intersection; a request-tenant is always **authorized against the principal's memberships** before
access — never trusted as authority in itself.

Tenant claiming policy: `open` (self-serve) / `invite` / `domainCapture`. Domain capture
(`@microslop.com → tenant`) requires **DNS-TXT-verified domain ownership** (the verification is itself
a background job) — never self-asserted, because unverified capture is a tenant-takeover vector.

### 4.6 Authorization: tenant gate above roles

Authorization evaluates **(1)** does the principal hold a membership in the resolved tenant? —
fail-closed, deny if not — **then (2)** does their role in *this* tenant permit the action. You cannot
"role" your way across a tenant boundary; tenant is an isolation axis **above** roles. Membership is
**resolved per request, server-side, never read from the token** — which closes both the
"org-switch leaves stale admin claims" and the "deprovisioned user keeps access until token expiry"
classes.

### 4.7 The control plane is dogfooded framework primitives

The registry, identity, and lifecycle are all `[HostScoped]` entities and jobs, living in a root store
that holds **only** control-plane data (no tenant's product data — not even the default tenant's). So
the operator inherits the framework's entire surface (query, projection, audit, coherence) over the
fleet **for free**. Lifecycle operations (`Provision`/`Relocate`/`Erase`/`Suspend`) are durable,
resumable, cursored **jobs** — so partial-failure recovery, a per-tenant migration ledger, and
progress visibility come from the jobs pillar, not from hand-rolled migration scripts.

### 4.8 The operator / service-owner view

The platform operator works in **host scope** and sees the fleet as a **projection** of the
control-plane entities (not a separate product to build): per-tenant substrate, health, **measured**
cost/consumption (the runtime tags every operation with the ambient tenant), a noisy-neighbor finder,
placement + relocate, lifecycle, access-across-tenants, and posture. A "master/origin" tenant (e.g.
the public flagship product) sits in that fleet as **just another row** with `IsDefault: true` — a
routing pointer with **zero special data powers** (cross-tenant power is host-plane only). Operators
can **act-as** a tenant (audited) to reproduce a support issue.

### 4.9 Enforcement mechanics (the half still being detailed — your critique especially wanted here)

- **Read-filter + write-guard at the repository chokepoint.** Reads inject the tenant predicate;
  **writes stamp-and-verify** the tenant on every save/delete and reject on mismatch (write leaks are
  worse than read leaks — they corrupt, not just expose).
- **RLS backstop** (Postgres/SQL Server) for the one surface the structural floor can't reach — raw
  SQL / direct data access / bulk operations. Named explicitly as the non-structural escape.
- **Multi-axis auto-flow.** The tenant token rides into cache keys, coherence, vector/search keys, job
  payloads (captured at submit, fail-closed-restored at execute), message envelopes, and observability
  labels (opt-in/sampled, because tenant-label cardinality has real cost).
- **Open:** the precise composition at the chokepoint, and the current connection-resolution seam in
  the data core (which per-tenant routing must hook into) — being re-derived from code now.

---

## 5. The grounding: a practitioner pain harvest

The design is not armchair work — it was built against a four-lens survey of **41 sourced, real-world
multi-tenancy pains** (from ABP/Finbuckle/EF Core, Rails/Django/Laravel/Hibernate, AWS/Azure SaaS
architecture guidance, and Auth0/WorkOS/Okta identity). The high-signal clusters:

1. **The forgotten-boundary leak** (rated *critical*, appeared in **all four lenses** — the #1 pain):
   a forgotten read filter, *writes bypassing the filter*, raw-SQL bypass, tenant-id-from-URL (IDOR),
   tenant-id-from-header-not-token. → addressed by structural read-filter + write-guard + RLS + the
   "request-tenant authorized against membership" rule.
2. **Tenant context lost across the async hop** (*ubiquitous*, three lenses): jobs/queues/webhooks
   lose the tenant and run as the wrong/host tenant. → addressed by auto-capture-and-fail-closed-restore
   (Koan owns the job + bus pillars — a structural advantage).
3. **Scaling cliffs** (connection-pool explosion, catalog bloat, pooling-incompatible-with-db-per-tenant).
   → acknowledged honest limits; default to shared-schema, make placement movable.
4. **The pooled→silo migration** ("a 6–12 month re-architecture; the highest-margin upsell the
   architecture can't cheaply deliver"). → addressed by `Relocate` as a verb + heterogeneous registry.
5. **Operator blind spots** (cost attribution, per-tenant SLA, noisy-neighbor, residency, GDPR erasure,
   one-call provisioning). → addressed by the operator console as a projection (measured, not guessed).
6. **Identity** (same-user-many-tenants, org-switch privilege change, global-roles privilege inflation,
   incomplete deprovisioning, domain-capture takeover). → addressed by identity-global / membership-per-
   tenant / roles-on-membership / resolved-per-request / DNS-verified capture.

Notably, practitioners independently arrived at our settled decisions (membership-carries-role,
resolve-per-request, verified-domain, fail-closed, multi-axis flow) as the fixes they reached *the hard
way*. **If you think that convergence is misleading or that we pattern-matched ourselves into a local
optimum, say so** — that's exactly the kind of challenge we want.

---

## 6. The settled decisions and open questions (challenge any of these)

**Settled:**
- Same-DX-across-modes is the governing invariant.
- Mode ladder as in §4.1; tenant never enters the table-name string (routes to a native boundary or a
  column).
- Identity global / membership per-tenant / roles on membership.
- Immutable tenant `id` + alias codes as keyed entities (O(1) resolve, unique-by-key).
- Tenant is ambient (isolation); the principal is explicit (authority); membership is the bridge; the
  tenant gate is prior to the role check.
- Control plane = host-scoped entities + jobs; no scope contamination; `IsDefault` is a routing
  pointer with no special powers (we explicitly rejected a "tenant-zero" model where the control plane
  is itself a tenant).
- Lifecycle ops are durable jobs; audit is configurable (default light); domain capture requires DNS
  verification.

**Open:**
- Enforcement-mechanics composition (§4.9) and the connection-resolution seam.
- The ambient carrier's own name (`Ambient`?) — the tenant developer surface is `Tenant` regardless.
- How far the ambient primitive should absorb the app's root service provider.
- Tenant **hierarchy** (sub-tenants / nested isolation) — currently deferred; v1 is flat. *Should it
  be?*
- SSO/SCIM brokering and invite-flow edge cases — currently treated as above-the-framework / a later
  concern. *Is that the right boundary?*
- Surgical per-tenant backup/restore — acknowledged hard; v1 uses retention-window expiry.

---

## 7. Our delight thesis (please go beyond this and challenge it)

We include our current thinking deliberately — **so you can argue with it, not anchor to it.** Tell us
where it's wrong, what's missing, and what the *better* delight would be.

**The meta-thesis.** Almost every multi-tenancy pain in the harvest is "the library could only see the
query layer." Because Koan owns *every* axis, **tenant becomes a property of the runtime, not a
predicate you remember.** That's the engine; the moments below are consequences.

1. **"You cannot forget the boundary."** Read + write enforcement is structural at the chokepoint;
   a forgotten `WHERE` is impossible, not a discipline. The leak class that dominates real breaches
   simply can't be written.
2. **"Tenant survives the async hop — automatically."** No serializing the tenant into every job
   payload by hand; it auto-flows into jobs/messaging and fail-closed-restores.
3. **"The mode is a config line; moving a tenant is a verb."** The pooled→silo upsell becomes
   `Relocate`, not a re-architecture; entity code never moves.
4. **"Lifecycle fans out across every axis."** `Erase(tenant)` actually erases — data *and* cache
   *and* vector *and* search *and* blobs — because the runtime knows all of them.
5. **"Identity is global; authority is per-tenant and re-checked every request."** Same-user-many-
   tenants is the native shape; org-switch and deprovisioning are safe by construction.
6. **"The operator console exists by default."** Per-tenant cost/health/noisy-neighbor are *measured*
   (the runtime tags every op), not reconstructed from proxy metrics — and the console is a projection,
   not a product you build.
7. **"Rename is free; the master tenant dogfoods the same isolation it sells."** Trust by self-use.

**Where we think delight has limits (tell us if we're wrong to concede these):** we don't repeal the
scaling physics; cross-tenant reporting under physical isolation still needs a separate read-model;
per-tenant metric cardinality has a real cost; SSO/SCIM brokering is above our layer.

### The delight questions we most want you to investigate

- What is the **single magic moment** — the thing that makes someone say "wait, *that's* all it takes?"
  Is it the first `Tenant.Provision`? The boot report? The first time a relocate "just works"? Something
  we haven't named?
- What would make a team **choose Koan for multi-tenancy specifically**, over building on Postgres RLS +
  a homegrown layer, or over ABP/Finbuckle/Nile/Neon/Clerk?
- What's the **"oh no, a leak" moment** turned into delight — i.e., how should the framework behave the
  instant something *would* cross a tenant boundary?
- What does delight look like for the **operator** (the SaaS company), not just the developer?
- What's delightful about **day 2** — migrations, upgrades, debugging a single tenant's issue,
  onboarding the 1,000th tenant — not just day 1?
- Is there a **delight we're structurally positioned for and haven't realized** because we're too close
  to it?

---

## 8. Reference summary (for quick grounding)

- **Koan** = entity-first .NET 10 meta-framework; package-reference-as-configuration; multi-provider;
  owns all backend pillars in one runtime.
- **Tenancy = the flagship slice of an ambient-context primitive.** Same DX across pooled / schema /
  database isolation. Immutable tenant id + alias codes. Identity global, membership per-tenant.
  Control plane = host-scoped entities + jobs. Structural read+write enforcement + RLS backstop +
  multi-axis auto-flow. Operator console as a projection.
- **The bet:** owning every axis lets tenant be a runtime property rather than a remembered predicate —
  turning the genre's worst pains (forgotten filters, lost context in jobs, GDPR fan-out, untrustworthy
  cost attribution, the pooled→silo re-architecture) into things that are structural, automatic, or a
  single verb.

**Thank you. We want your sharpest thinking — especially on delight.**
