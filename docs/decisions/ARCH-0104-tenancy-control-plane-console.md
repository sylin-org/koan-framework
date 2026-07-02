---
id: ARCH-0104
slug: tenancy-control-plane-console
domain: Architecture
status: Accepted
date: 2026-07-01
title: Tenancy control-plane operator console (the host-face "0.1 portal")
---

## Context

ARCH-0099 designed the multi-tenancy control plane as dogfooded `[HostScoped]` `Entity<T>` rows
(`TenantRecord`, `Membership`, `Invite`) in the root/host scope, and named a **host-face
operator/service-owner console** as a *projection* over that data — fleet / health / measured-cost /
noisy-neighbor / placement / relocate / lifecycle / access / posture. The design was explicit that the
console is "not a build" yet; the readiness assessment flagged it as the missing **"0.1 portal"**.

Today the control-plane **data layer exists** (`src/Koan.Tenancy/ControlPlane/`: `TenantRecord`,
`Membership`, `Invite`, `TenantBootstrap`, `TenantStatus`/`InviteStatus`, posture) but there is **no
operator surface at all** — an operator cannot see the tenant fleet, cannot suspend/reactivate a tenant,
cannot audit who did what. `Koan.Identity.Web` already proves the pattern for a Reference = Intent
console over `[HostScoped]` entities (operator + self-service controllers, role-gated, entity-first
reads, auto-mounted). This ADR builds the tenancy counterpart and stands up the minimal backing data the
richer surfaces need.

## Decision

### 1. New package `Koan.Tenancy.Web` (Reference = Intent)

A new Periphery package, parallel to `Koan.Identity.Web`, referencing **`Koan.Web` + `Koan.Tenancy` +
`Koan.Jobs`** and **not** `Koan.Identity` — `Membership.IdentityId` stays a string soft-FK, so the
console needs no identity dependency and the layering stays clean (the tenancy core still references
neither sibling). Referencing the package auto-mounts (`AddKoanControllersFrom<T>`) the operator API and
the bundled UI. The headless core keeps working without it.

### 2. The host-operator principal — explicit, never a master backdoor

A new well-known host role **`TenancyRoles.Operator = "koan:tenancy-operator"`**. It is a *global/host*
role, granted out-of-band (e.g. an `IdentityRole` binding or config), and is **never derived from any
tenant membership** — this is the design's "no master backdoor / master tenant is an `IsDefault` routing
pointer with zero special powers." The controllers gate on it (`[Authorize(Roles = …)]`). Under the
tenancy **dev-open** posture the console dev-seeds the grant for the loopback dev caller (Reference =
Intent, no ceremony); under **prod-closed** posture the grant must be explicit and access **fails
closed**.

### 3. Backing data (the "also build missing backing" scope)

- **`TenantAuditEntry`** — a new `[HostScoped]`, `IAmbientExempt`, append-only entity in **`Koan.Tenancy`
  core**: `Actor`, `Action`, `TenantId?` (null = fleet-wide), `Summary`, `[Timestamp] At`. Every console
  mutation writes one → "explicit + audited cross-tenant" by construction. It lives in the core (pure
  data, no new dependency) as the design's control-plane `AuditEntry`.
- **`TenantOperation : Entity<TenantOperation>, IKoanJob<TenantOperation>`** — in **`Koan.Tenancy.Web`**:
  the durable, resumable, audited lifecycle-operation ledger the design calls for (lifecycle ops are
  `IKoanJob`s → free resumability/audit from JOBS-0005). v1 implements **control-plane Erase** (the one
  genuinely fan-out op) — deletes a tenant's memberships + invites + the record and returns a
  removed-row count report. This is the honest first step toward the design's signed **erasure
  certificate**; `Provision`/`Relocate` are deferred. The job type lives in the web tier so the tenancy
  core takes no `Koan.Jobs` edge.

### 4. Surfaces (host-face projection + guarded actions)

- **Fleet roster** — tenant · status · seat count · pending-invite count · posture.
- **Tenant drill-in** — members (identity + roles), invites (pending/expired).
- **Guarded actions** — create/rename, suspend/reactivate (status), invite/revoke-invite, revoke-seat
  (membership), and **erase** (two-step confirm → submits a `TenantOperation` job).
- **Operations feed** — `TenantOperation` rows (queued/running/completed) with the removed-row report.
- **Audit log** — the `TenantAuditEntry` stream (fleet-wide and per-tenant).
- **Act-as** — an audited, explicit "operate as {tenant}" toggle that records intent and drives an
  **unmistakable scope banner** in the console.

### 5. Guardrails (design canon)

- Explicit operator role — **no master backdoor**; the role is never tenant-derived.
- Every mutation is **audited with the acting subject** (`TenantAuditEntry.Actor`).
- **Unmistakable scope indicator** whenever acting-as a tenant.
- Erase is **two-step + control-plane-only** in v1 (never a one-click destructive fleet action).
- Prod posture **fails closed** on a missing operator grant.

## Honest v1 boundaries (documented, not faked)

- Surfaces with **no backing data** — measured-cost, noisy-neighbor, placement, relocate — are **omitted,
  not fabricated**.
- **Erase is control-plane-only** (memberships/invites/record). The tenant's *product* data (blobs /
  vectors / rows) fan-out erase and the cryptographically-signed erasure certificate are the full saga
  (ARCH-0099 P5b/P8), deferred.
- **Act-as** records intent + shows the scope banner in the console; wiring the operator's *ambient*
  tenant across the whole application (via the existing resolution carriers) is a follow-on.
- No separate `TenantCode`/`TenantDomain` keyed entities — `Code` stays a field on `TenantRecord`;
  domain routing is unchanged.
- **Invite delivery** is a follow-on: the opaque accept token is minted and stored but **never shipped to
  the browser** (not even on create); an explicit "reveal invite link" operator action is deferred.
- **Owner-revoke serialization is per-node** (the `IKeyedLeaseGate`); a cross-node distributed guard is a
  follow-on (a control-plane console is typically single-instance).
- The **fleet-wide audit view** loads-then-caps at 200; store-level time-ordered pagination is a follow-on
  (the per-tenant view is index-pushed-down; control-plane audit volume is operator-action-driven, low).

## Review (adversarial, folded)

Two parallel adversarial reviews (security + correctness/idiom) ran on the diff. Confirmed and folded:
**(HIGH)** the invite `Role` accepted any string — an operator could mint `koan:tenancy-operator` onto a
membership (which the tenant-resolution role-projection would then honor), a lateral escalation that breaks
"never derived from a tenant membership"; fixed by `TenancyRoles.IsReservedHostRole` guarding the invite
path (service + a clean controller 400). **(HIGH)** `TenantDetailDto` serialized the raw `Invite.Token`
bearer credential to every operator browser; fixed with a token-less `InviteViewDto`. **(MEDIUM)** the
last-owner guard had a TOCTOU race (two concurrent owner-revokes could both pass); fixed by serializing
owner revokes per tenant through `IKeyedLeaseGate` with the re-count inside the lease. **(MEDIUM)** the
prod actor fallback collapsed to a bare `"operator"`; fixed with a richer claim chain + a self-announcing
`"operator (unattributed)"` sentinel. **(LOW)** added a strict same-origin CSP on the console UI, a
`[Index]` on `TenantAuditEntry.TenantId`, an idempotent policy registration, and the erase save→submit→audit
ordering. A claimed HIGH — that `TenantOperation` needed `IAmbientExempt` or the erase job would dead-letter
under an act-as ambient — was **re-derived as overstated**: `TenantScopeMetadata` treats `[HostScoped]` as an
exemption equal to `IAmbientExempt`, so `[HostScoped]` alone suffices; proven by a real worker-dispatched
erase submitted under an act-as tenant ambient (`Erase_job_dispatched_under_an_act_as_tenant_ambient_completes_via_the_worker`).

## Consequences

- The first operator-facing surface for tenancy; the tenancy counterpart to `Koan.Identity.Web`.
- Establishes the explicit **host-operator role** and proves the **`IKoanJob`-backed lifecycle** model.
- Off by default (Reference = Intent — absent unless referenced). Adds a `Koan.Jobs` edge to the tenancy
  **web** tier only; the tenancy core stays lean (Core + Data + one new pure-data entity).
- Composes ARCH-0099 (control plane), ARCH-0100 (ambient carrier — the erase job reads `[HostScoped]`
  rows, so it is `IAmbientExempt`), SEC-0007 (the `Koan.Identity.Web` console pattern), JOBS-0005 (the
  lifecycle job).

## Verification

- ARCH-0079 integration spec (real `AddKoan()`): roster projection; each action writes a
  `TenantAuditEntry`; the operator role gates the surface (fail-closed without it); the erase
  `TenantOperation` removes the control-plane rows and reports counts.
- Full `Koan.sln` build green; the existing Tenancy suites stay green (additive).
