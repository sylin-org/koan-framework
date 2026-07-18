# Koan.Identity — design & capability plan

> **Graduated to [SEC-0007](../decisions/SEC-0007-koan-identity-module.md) (Accepted — planned, 2026-06-27).** That ADR is the authoritative, operational, composition-ordered build spec. This document remains the longer-form capability/data-model/pain→delight reference the ADR points back to.

- Status: **Superseded by SEC-0007** (Draft 2026-06-26; graduated 2026-06-27)
- Scope: a new framework module `Koan.Identity` (+ `Koan.Identity.Web`) that owns the **person** (the durable identity), its **management/self-service surfaces**, and the **day-2 access primitives** — composing with the existing auth, authz, trust, and tenancy pillars rather than duplicating them.
- Inputs: the internal surface map (Explore), the module-boundary prior art (WorkOS/Clerk/Auth0/Keycloak/ASP.NET Identity/Stytch/Frontegg), and two web "delight" research passes — onboarding ([[tenancy-delight-research]] pass 1) and management/self-service/DX (pass 2). All cited in the memory notes + the workflow transcripts.
- Applies: `koan-design-principles` (fewer, more meaningful parts; conformity-by-design; fail-closed), `contributor-pipelines-never-bespoke`, `koan-ergonomics-first` (Reference = Intent), `break-and-rebuild-preferred`, `no-stopgaps`. Composes [[facet3-tenancy-design]], [[arch-0100-durable-ambient-carrier]], [[sec-0004-capability-authz-model]].

---

## 1. Why this module exists (the gap)

Koan today has **no durable user/identity entity.** Identity is 100% claims-driven and the "who" is a bare string everywhere it matters:

- `Membership.IdentityId` (`Koan.Tenancy`) — "user id or email", **not an FK to anything** (it has nothing to reference).
- `RefreshToken.Subject` / `AgentGrant.Subject` / `CurrentUserDto.Id` — all denormalized claim strings.
- `IUserStore` (`Koan.Web.Auth`) is an **empty `Exists()` stub** — the intended user store was never built.

Everything else is already built and stays: sign-in (`Koan.Web.Auth` OAuth/OIDC + cookie + `ExternalIdentity`), token issuance/validation (`Koan.Security.Trust`, `Koan.Web.Auth.Server`), standard .NET role claims with Entity-first global/tenant bindings (`IdentityRole` and `Membership.Roles`), the authorization floor (`Koan.Web` `[Access]` + `EntityAccess<T>` + `IAuthorize` + `EntityFloorAuthorizationProvider` + `AgentGrant`), and tenancy (`Koan.Tenancy` `TenantRecord`/`Membership`/`Invite` + the fail-closed ambient axis + the ARCH-0100 carrier).

**`Koan.Identity` supplies the missing keystone — the person — and the management/self-service experience that no .NET option ships, then composes with all of the above.**

## 2. The thesis: *identity is `Entity<T>` one layer down*

The day-2 delights the market charges for or structurally can't do are **near-free byproducts of Koan's substrate.** This is the strategic case for building it, and why Koan's version wins:

| Delight (research-ranked) | Why ~free for Koan |
|---|---|
| **App-owned, portable, no per-MAU** (the #1 dev lock-in fear) | Identities are `Entity<T>` in the app's own multi-provider store, stable GUID v7 ids → native FKs, one-command export/import (incl. portable PHC/bcrypt hashes), **no auth-storage adapter** (the abstraction that killed Lucia is structurally absent). |
| **Reference = Intent dual consoles** (the universal gap: ASP.NET Identity / Ory / NextAuth / Supabase ship no operator console) | `EntityController`/`EntityToolset` already generate CRUD over entities → referencing `Koan.Identity.Web` auto-mounts the operator admin **and** end-user self-service. |
| **Bidirectional access explainer** + self-explaining denials | The decision *is* an entity computation: `Membership.Roles` + `AgentGrant` query + `EntityAccess<T>.Constrain` → name the contributing rows; "revoke" = `Remove()`. Rides the SEC-0004 `gate·constrain·project` seam; one authoring vocabulary (`[Access]` string ↔ fluent `EntityAccess<T>` gate). |
| **Impersonation, safe-by-construction** | An `actor` claim rides the ARCH-0100 durable carrier alongside `sub`, fail-closed across async hops; "who may impersonate whom" is the same `Can(subject,action,resource)` check; dangerous verbs 403 while an actor claim is present. "God-mode" can't ship by accident. |
| **Audit-by-construction** | Identity/access mutations are entity writes → lifecycle events auto-emit before→after diffs; no "remember to log it". |
| **Atomic verifiable deprovisioning + roles that can't leak** | The fail-closed data axis (`DataAxis.AssertNoLeak`) + roles-on-`[HostScoped] Membership` → "X's effective access in tenant T" is a scoped query that cannot leak; "deactivated = cannot act" is verifiable (rhymes with the crypto-erasure-certificate flagship). |

**delight == moat**, and the moat is Koan's architecture.

## 3. Boundary — owns / composes / is NOT

**Owns (net-new):** `Identity` (person) + attached **factors** (verified emails, linked external identities, optional local credential, passkeys/MFA); sign-in→identity **reconciliation** (implements `IUserStore`); **groups**; durable **sessions/devices**; **impersonation** primitive; **API tokens** (`Entity<ApiToken>`); **audit** emission; the **effective-access resolver/explainer**; the **two consoles** (`Koan.Identity.Web`); a **global user↔role binding**.

**Composes with (no duplication):** `Koan.Web.Auth` (sign-in; relates `ExternalIdentity` → `Identity`), `Koan.Security.Trust` + `Koan.Web.Auth.Server` (tokens, the `actor` claim), standard .NET role claims projected from `IdentityRole` and `Membership.Roles`, `Koan.Web` `[Access]`/`EntityAccess`/`AgentGrant`/`IAuthorize` (authz floor), and `Koan.Tenancy` (`Membership`/`Invite`).

**Is NOT:** a standalone IdP/SSO server (that's `Auth.Server` + connectors); a new policy engine (the `[Access]`↔`EntityAccess` range already spans simple→fine-grained — the story is **evolve in place**, not Rego/Cedar); the tenancy control plane. *Resisting Keycloak-ification is the discipline that keeps it shippable.*

## 4. Decisions (settled with the architect)

- **D1 — Identity is the noun; membership = `identity × tenant`.** `Koan.Tenancy` keeps `Membership`; `Koan.Identity` gives `Membership.IdentityId` a real backing entity (soft-FK v1: string column + logical reference, no breaking migration).
- **D2 — Full management suite, phased.** All capabilities in §6 are in scope; §7 sequences them so each phase is green and dogfoodable.
- **D3 — One role vocabulary, two binding scopes.** Role keys use standard .NET string/claim semantics. `Koan.Identity` owns the **global** user↔role binding (no-tenancy case) + the effective-access resolver; `Membership.Roles` stays the **tenancy** binding. A governed definition catalog is deferred until an application needs definitions that constrain real grants or policies.
- **D4 — Optional local-credential factor.** Password + passkey as opt-in attached factors (off by default; external-IdP-only stays the zero-config default); portable PHC/bcrypt hashes as part of the anti-lock-in promise.
- **D5 — Person ≠ email.** One identity holds multiple **attached verified factors** (emails + SSO links + credentials) and many **revocable memberships**, with per-membership display overrides. Dissolves duplicate accounts, merge-hell, method-lockout, and departure-lockout at the root.
- **D6 — Compose, don't duplicate.** The authz floor, sign-in, token issuance, and tenancy already exist; `Koan.Identity` references them.
- **D7 — Graceful degradation by composition.** No-tenancy → users/groups/global-roles/permissions + consoles; `+Koan.Tenancy` → membership/grants/invites/affordances light up over the same entities.

## 5. Data model (new entities + relations)

- **`Identity`** (`Entity<Identity>`) — the person. `Id` (GUID v7, the canonical subject), `DisplayName`, `Picture`, `Status` (Active/Suspended/Deactivated), `Epoch` (revocation), `[Timestamp]` Created/Updated. The thing every existing `…Subject`/`IdentityId` string resolves to.
- **`IdentityEmail`** (`[Parent(Identity)]`) — a verified email factor (address, `Verified`, `Primary`). Multiple per identity; the join-matching key for domain routing.
- **`IdentityCredential`** (`[Parent(Identity)]`) — optional local password (PHC/bcrypt) or passkey/WebAuthn record (label, created, last-used, server-synced state).
- **`ExternalIdentity`** — *exists* (`Koan.Web.Auth`); now relates to `Identity` (provider + key hash + claims). Account-linking falls out of "many factors on one person".
- **`Group`** (`Entity<Group>`, `[Parent]`-nestable) — org-free grouping of identities for bulk role assignment; maps to IdP groups later via SCIM.
- **`Session`** (`Entity<Session>`, `[Parent(Identity)]`) — durable session/device record (device/browser/OS, approx city, first-seen/last-active, `IsCurrent` server-tagged) → the device list + "sign out everywhere-else".
- **`ApiToken`** (`Entity<ApiToken>`, `[Parent(Identity)]`) — scoped/named/`ExpiresAt`/last-used PAT; rotate = new row, scopes preserved, brief overlap.
- **`AuditEvent`** (append-only channel) — canonical `actor / action / target / before→after / context(IP,UA) / occurred_at`; emitted automatically by the lifecycle seam on identity/access mutations; optional hash-chaining; SIEM stream.
- **Reused:** standard role claims plus `IdentityRole`/`Membership.Roles` bindings, `AgentGrant` (impersonation requests + JIT/time-boxed grants + support access — all `Save()`-to-issue/`Remove()`-to-revoke/`Query()`-to-observe on one primitive), and `Membership`/`Invite` (tenancy binding).
- **New binding:** `IdentityRole` (`[Parent(Identity)]`) — the **global** user↔role binding for the no-tenancy case (catalog from `Auth.Roles`).

## 6. Capability spec (delight/pain → capability)

Tag: **[T]**able-stakes · **[D]**ifferentiator. Each kills a research-cited pain.

**A. Person core & factors** — `Identity` + attached factors + reconciliation; stable GUID v7; status+epoch. **[D]** app-owned/portable/no-per-MAU; **[D]** person≠email (no duplicate accounts / departure lockout).

**B. End-user self-service panel** — profile; **sessions & devices + "sign out everywhere-else"** (immediate, observable); MFA/passkey enroll with **pre-provisioned recovery** (codes + recovery contacts); **Security Checkup** traffic-light (2FA/recovery/unknown-sessions/breached-password, inline fixes); connected accounts (unlink-safe across **all** factor types); **API tokens**; **data export + verifiable-erasure delete**; **"this was me / wasn't me"** in-app activity (risk-tiered, not alert-fatigue). **[T]** features, **[D]** that it's generated.

**C. Operator console** — user list as an `Entity<T>` query (fuzzy/multi-field/custom-attr search, cursor paging); **uncapped bulk lifecycle** (suspend≠delete, preview+partial-failure, one audit batch); **lifecycle-aware delete** (detect dependents → reassign/anonymize/cascade, never a raw FK error); groups; role assignment. **[D]** (the ASP.NET-Identity / headless gap).

**D. Access model & explainability** — one standard role vocabulary bound globally or per-membership; **effective-permissions panel** (flatten additive grants to one human answer); **role-overlap detector** (catch role explosion pre-commit); **bidirectional explainer** ("can X do Y on Z?" + "why does X have access to Z?" → the exact binding, one-click revoke); **self-explaining denials/allows** on the SEC-0004 seam; **author-time lint** (always-allow/unreachable/shadowed fail to save) + **preview==production** simulator on the real engine; in-place RBAC→ABAC/ReBAC evolution (no enforcement-code change). **[D]**.

**E. Day-2 power** — **safe impersonation** (actor-claim + carrier + auto-banner + 403-on-dangerous + request→approve→time-boxed on `AgentGrant`, no-self-approval, user-revocable, dual-sided audit); **JIT/time-boxed grants** (no standing admin; customer-grantable read-only support access distinct from impersonation; pre-expiry one-click extend; break-glass); **tamper-evident change-audit** + customer-facing view + SIEM stream. **[D]**.

**F. Membership (tenancy-on)** — invites **bind to identity, not the email string** (inline collision reconciliation, no duplicate accounts); **safe-by-default domain routing** (DNS-verified domains only + existing-verified-member required + generic-provider blocked) + admin **sprawl-inventory**; seat=active-member; **atomic verifiable deprovisioning**; the per-tenant-configurable resolver (subdomain / `/t/{code}` / claim). **[D]**.

**G. Developer DX** — one-line drop-in login + self-service + admin (Reference = Intent); **layered customization** (theme → slot → headless → own-one-flow — dodging the ASP.NET all-or-nothing curse); dev-open/prod-closed posture mirroring tenancy; **offline pre-seeded dev users** ("sign in as alice@example.com" with no network — integration-tests-as-canon); auth schema/cookie/env names as a **stability contract** in `koan.lock.json` (no silent renames). **[D]**.

## 7. Phased build (full suite, sequenced for green + dogfoodable steps)

Each phase compiles green; SnapVault is the driving consumer; per-phase: TDD-where-it-pays → adversarial multi-lens review → integration-test-as-canon.

- **P0 — Person core + the seam.** `Identity` + `IdentityEmail` + factor model; reconciliation (implement `IUserStore`, relate `ExternalIdentity`); `Membership.IdentityId` soft-FK → `Identity`; dev-open/prod-closed posture + offline seeded dev users; `AuditEvent` channel scaffold.
- **P1 — The two consoles + sessions.** `Koan.Identity.Web` auto-mounts the operator console (search/bulk/lifecycle/groups) + the self-service panel (profile/sessions-devices/connected/tokens/export-delete); durable `Session`/device list + "sign out everywhere-else"; audit-by-construction live.
- **P2 — Access model & explainability.** `IdentityRole` global binding over standard role keys; effective-access resolver; bidirectional explainer; self-explaining denials/allows; author-time lint + real-engine simulator; groups→role mapping.
- **P3 — Day-2 power.** Safe impersonation (actor claim + carrier + banner + 403 + request/approve on `AgentGrant`); JIT/time-boxed grants + support access + break-glass; tamper-evident audit + SIEM stream; Security Checkup + recovery + risk-tiered activity; local-credential + passkey/MFA factors.
- **P4 — Membership (tenancy-on).** Invite-binds-to-identity + collision reconcile; the per-tenant resolver + safe domain routing + sprawl inventory; seat=active-member; atomic verifiable deprovisioning.
- **P5 — SnapVault dogfood + measure.** Wire studios onto it; demonstrate the moats (person-identity, atomic deprovisioning, instant switching, safe impersonation); record the before/after reduction.

## 8. Anti-patterns to honor (from the research)

Email-as-identity-key · default-to-create-new-workspace · paywall security (SSO tax) · fire-and-forget deprovisioning · God-mode/identity-swap impersonation (always wrap with an `actor` claim, never swap `sub`) · 404-instead-of-403 + bare "Access Denied" · UI affordances the backend rejects · additive-only perms with no effective view · role explosion as the growth path · per-MAU pricing · non-portable hashes / unstable ids · operational logs reused as audit / mutable audit · all-or-nothing UI customization · a bespoke auth-storage adapter layer (the Lucia lesson) · standing admin creds · never-expiring impersonation/elevation.

## 9. Open questions

- **Module naming/split:** `Koan.Identity` (headless core) + `Koan.Identity.Web` (consoles + drop-in UI) — confirm; and whether the drop-in *sign-in* UI lives here or in `Koan.Web.Auth`.
- **Identity-key vs verified-email matching:** identity keyed by stable GUID; domain-join matches on a *verified* `IdentityEmail`. Confirm reconciliation rules for collisions/merges.
- **MFA/passkey storage + recovery** depth in P3 (server-synced passkey state is load-bearing for the "no phantom credential" delight).
- **Sessions as entities vs the cookie:** how the durable `Session` relates to the existing ASP.NET cookie scheme (the cookie stays the transport; the `Session` row is the durable twin for the device list + revocation).
- **Graduate this doc** to a numbered ADR (SEC-0007 or ARCH-01xx) on acceptance.

## 10. Acceptance criteria (module)

1. A no-tenancy app references `Koan.Identity(.Web)` and gets users + groups + global roles + the operator console + self-service panel — zero hand-built UI.
2. Adding `Koan.Tenancy` lights up membership/invites/grants/affordances over the same entities — no code-path fork.
3. Identities (+ portable hashes + roles + tokens + audit) live in the app's own data store; one-command export/import; no per-MAU axis.
4. The bidirectional explainer, self-explaining denials, safe impersonation, and audit-by-construction work over the existing SEC-0004 seam + ARCH-0100 carrier + `DataAxis.AssertNoLeak`.
5. SnapVault dogfoods the moats with the flagship spec staying green.
