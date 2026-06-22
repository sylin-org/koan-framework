# ARCH-0099: Tenancy realignment — dev-open/prod-closed posture, first-owner onboarding, the mandatory mode ladder, and tenant-as-native-container

- Status: Accepted (2026-06-22)
- Deciders: Enterprise Architect
- Supersedes / amends: **ARCH-0095** (tenancy — posture default, the owner-admin rejection, the mode-ladder priority), **ARCH-0096 §5** (tenant is now a *name* particle, leading), **DATA-0105 §1** (tenant storage isolation is the per-adapter native container, not a relational schema-qualifier only). Reconciles these against **DATA-0094** (native partition container — Accepted, and the more general model). Grounded in prior-art research (workflow `wf_94dbd26f-e67`, 4 web-verified clusters) + a code-and-docs audit (`wf_c789391c-72e`).
- Related: ARCH-0098 (classification — the other Facet-3 axis), ARCH-0094 (Adapter Forge / Conformance Gate = P7), ARCH-0086 (`[KoanDiscoverable]`), the Ambient Context Charter.

## Context

Three canon positions were re-examined with the architect and found wrong or drifted:

1. **Fail-closed-always** for tenancy contradicted Koan's Reference = Intent / zero-ceremony DX and, per prior art, is the posture developers *rage-disable* (every large open-data breach — MongoDB, the 2024 Firebase ~125M-record leak, Elasticsearch — shares the shape "open-default needing opt-in-to-secure"; the fix is not "closed everywhere" but "open in dev, closed in prod, ambiguity → closed").
2. **The owner-admin rejection** (ARCH-0095 §6.8 fork 4) conflated a real security hole with a legitimate onboarding need.
3. **The mode ladder** (ARCH-0095 §2) was treated as "shared-row is v1, the rest deferred"; it is **mandatory feature surface**. And its storage-isolation rung had drifted into an inconsistency: **DATA-0094 (Accepted)** maps the isolation boundary onto each adapter's *native container* (name-suffix / scope / database / schema, store-universal), while **DATA-0105 §1 + ARCH-0096 §5 (Proposed, June)** narrowed the *tenant* axis to "cache-key particle only + relational schema-qualifier," explicitly "tenant is not a name particle." The June docs narrowed the May model without reconciling. This ADR reconciles them in favor of the general (DATA-0094) model.

## Decision

### 1. Posture: open in dev, closed in prod — derived from `KoanEnv`, never a flag

The tenancy/isolation posture is **computed once at boot from the in-core `KoanEnv` snapshot** (`KoanEnv.IsDevelopment`/`.IsProduction` — never a raw `ASPNETCORE_ENVIRONMENT` read; this is the snapshot-ambient-once canon), with the ASP.NET rule **verbatim: the predicate is a positive `IsDevelopment()`, and unset/ambiguous resolves to Production/closed.** The resolved posture is printed in the boot report.

- **OPEN in dev** (detected Development only): auto-provision one dev tenant (smart-named from the git/machine user — `leo@acme.dev` → "Acme", not `default`), auto-trust the loopback caller as that tenant's **Owner**, tenant resolution falls back to the dev tenant (no day-one 403), and auto-mint a **branded, per-machine ephemeral** signing key (the `django-insecure-` brand trick — every dev relaxation is marked so the prod guard and `git diff` catch it).
- **HARD-FAIL at prod boot** (refuse to boot — the small set where absence is directly exploitable): tenancy active with no real resolver (Claim/Host/Header) configured · the branded dev key/dev-tenant marker still present · a "dev/open" flag set while `KoanEnv` says Production · no signing secret where the gate needs one.
- **WARN loudly, never block** (blocking the soft stuff is what drives the rage-disable): an **OPEN SURFACES census** in the boot report — every entity/surface served without a tenancy/auth gate.
- **Refuse with a guided diagnostic** (Redis protected-mode quality, not a bare 403): the fail-closed chokepoint (slice 1b) names the exact attribute/config to add.
- **No single cross-environment "tenancy off" flag.** Relaxation is *derived* from env detection (nothing to set, nothing to leave on) and structurally inert in prod. This replaces the `TenancyMode.Off`-default (which contradicted Reference = Intent): referencing `Koan.Tenancy` **activates** it, posture-by-env.

### 2. Owner-admin disentangled — reject the backdoor, build first-owner onboarding

These are different things; the rejection does not conflict with the onboarding need:

- **(a) Rejected, stays dead:** a *permanent, standing, cross-tenant* "tenant-zero master" that can read into any tenant at any time — the exact isolation hole the gate exists to prevent.
- **(b) Built:** the **first user becomes Owner of *their own* tenant** (dev: the auto dev tenant; prod: a tenant created through provisioning) — a **role-on-membership**, **zero** cross-tenant reach. The universal convention (Clerk/Stytch/PropelAuth/Ghost); not a backdoor.

Mechanics: first-Owner is a **one-shot bootstrap** (fires only when no Owner exists; once claimed, ignored — Keycloak's model; claimed-state persisted). In prod the claim window is gated behind a pre-seeded `Koan:Tenancy:BootstrapAdminEmails` allowlist **or** a one-time token printed to the host log, with a **host-only CLI seed** as the zero-web-attack-surface escape hatch. Any genuine cross-tenant op (provision/relocate/erase) runs as an **explicit audited P8 saga**, never an ambient principal.

### 3. The mode ladder is mandatory feature surface (all three rungs)

| Model | Boundary | Mechanism | Status |
|---|---|---|---|
| **Different databases** | connection | per-tenant connection routing (**P6 connection broker** — the one genuinely-new infra piece) | required; unbuilt |
| **Different storages (container-per-tenant)** | storage object | the tenant renders a **native container** per adapter (§4) — store-universal | required; primitive landed (§5), wiring next |
| **Same storage (control field)** | one row | the `__koan_tenant` discriminator + mandatory filter (P1) | **shipped + proven** (4 stores) |

Placement is **per-tenant** (heterogeneous — "acme" on a dedicated DB while B–Z share), held in the control-plane registry; the *mechanisms* land first and are config-selectable app-wide, with per-tenant heterogeneity + `Relocate` arriving with the control-plane entities + P8.

### 4. Tenant storage isolation is the per-adapter native container (supersedes DATA-0105 §1 / ARCH-0096 §5)

Adopt **DATA-0094's** model for the tenant axis: the boundary maps onto each adapter's **native** isolation primitive, store-universal, exactly as partition already does:

- **Name-encoding stores** (relational tables, **Mongo collections**, Redis keys, Json dirs) → the tenant renders a **leading container particle** by **tenant id** (`2a6v7.Todo` — Mongo collection prefix; on relational the `.` flattens to the adapter separator, `2a6v7_Todo`, still a valid isolated table). **Tenant id, not code** — the id is immutable; naming by a mutable code orphans storage on rename.
- **Native-container stores** (Couchbase **scope**, a future Mongo **database**-per-tenant, Postgres **schema**, graph **namespace**) → routed natively via the DATA-0094 `EncodePartitionInName = false` / native-container path; no name particle, the adapter routes.

This **supersedes** ARCH-0096 §5's "tenant is a cache-key particle, not a name particle" and DATA-0105 §1's "4a is a schema-qualifier only, name-particles reserved for partition suffixes." The empirical objection that drove the June narrowing (the `.` flattens; the composer was append-only) is resolved: the composer now has positions (§5), and per-adapter rendering handles the flatten. ARCH-0095's *"tenant never enters the table-name **spine**"* still holds — the tenant is a **leading namespace particle**, not the `{model}` spine.

#### 4a. The storage picture (ratified) — tenancy-on ≠ particles-everywhere

A particle is the *container-mode* rendering only. The default rung when tenancy is on is the **control field** (shared-row), which adds **no name particle at all** — just the invisible `__koan_tenant` discriminator. And the **control-plane is exempt from every rung** (the `[HostScoped]` exemption the gate already uses): the registry entities (`Tenant`, `Membership`, …) live in the root/host scope, never prefixed. Every tenant's data is treated identically — **the app owner's own tenant is a normal tenant, not special** (no master powers; that is the rejected backdoor, §2).

| State | Tenant data | Control-plane (`[HostScoped]`) |
|---|---|---|
| `Koan.Tenancy` **not referenced** | `Todo` (byte-identical to today) | — |
| referenced · **default** (control-field) | `Todo` + invisible `__koan_tenant` field | `Tenant` / `Membership` (host scope, unprefixed) |
| referenced · **container** mode | `2a6v7.Todo` (leading tenant-id particle) | `Tenant` / `Membership` (host scope, unprefixed) |

So: no module ⇒ nothing; module on ⇒ the shadow field by default (names unchanged); the leading prefix appears only when a tenant (or the app) is placed in container mode; and the control-plane never gets a particle in any state.

### 5. Positional particles — the missing primitive (LANDED)

`IdentifierComposer` was append-only (every particle trailed the anchor) — it could not express a leading container prefix. **Added** (`a46c11fc`): `Particle.Position` (Leading/Trailing, default Trailing) + an optional per-particle `Separator` override; the composer builds `[leading] anchor [trailing]`, the byte-clamp preserves the leading prefix and hashes the full id (clamped tenant containers never collide). Byte-identical for the existing trailing partition; 19 composer specs + data-naming 40/40; the leading-vs-trailing invariant mutation-killed.

**Next wiring (not this ADR):** build the `IParticleContributor` discovery seam (ARCH-0096 §2 designed it; partition is hand-wired today) so `Koan.Tenancy` *registers* a leading tenant-container particle — the same registration shape classification and partition use.

## Consequences

- Tenancy's default flips from `Mode=Off` to **active-posture-by-env** (Reference = Intent). A non-tenant app that doesn't reference the module is unaffected; one that references it gets dev-open/prod-closed.
- The mode ladder is one capability with three rungs behind one contributor seam: P1 control-field (shipped), the leading-container particle / native-container (primitive landed, wiring next), the P6 connection broker (required, unbuilt — the genuinely-new infra).
- Onboarding (first-owner, dev auto-seed) and lifecycle still depend on the control-plane `Tenant`/`Membership` entities (zero-code today); dev seeds them in-memory gated to `IsDevelopment()` until the durable entities land.
- Three docs stop disagreeing: this ADR is the single source of truth; ARCH-0095/0096 and DATA-0105/0094 carry pointers here.

## Build order

1. **Posture seam** — `TenancyPosture` computed once from `KoanEnv` (resolve Open/Closed, the hard-fail vs warn pre-flight, branded-marker detect, the boot line) + dev auto-seed (dev tenant + Owner membership + branded ephemeral key, in-memory, `IsDevelopment()`-gated) + the Redis-style refusal diagnostic. Delivers day-one delight, fail-closed in prod by construction.
2. **`IParticleContributor` discovery seam** + wire `Koan.Tenancy` to register the **leading tenant-container particle** (id-based) → container-per-tenant on name-encoding stores. Prove with the cross-adapter oracle.
3. **Durable control-plane** (`[HostScoped]` `Tenant` + `Membership` + `koan:owner` role) + the prod first-claim flow (allowlist/token/CLI).
4. **P6 connection broker** (database-per-tenant) — re-derive empirically from the data-core connection-resolution code first.
5. Native-container routing for the remaining stores (Couchbase scope already; Mongo database, Postgres schema) behind the same contributor.

## Risks / open

1. **P6 connection broker** is the one genuinely-new infra piece (per-tenant routing + pool governance + session-reset + credential/KMS seam); empirically re-derive before building.
2. **Per-tenant heterogeneous placement** needs the control-plane registry; until it exists, mode selection is app-wide config.
3. **The `.` flatten on relational** means a leading tenant container reads `2a6v7_Todo` there (still isolated) while Mongo keeps `2a6v7.Todo`; the per-adapter rendering is the seam, conformance-gated by P7.
