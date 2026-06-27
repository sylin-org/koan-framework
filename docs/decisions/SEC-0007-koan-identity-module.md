# SEC-0007 — Koan.Identity: the durable person, its management surfaces, and the day-2 access primitives

- **Status:** Accepted — planned (build not started); supersedes the draft design doc [koan-identity-design.md](../architecture/koan-identity-design.md)
- **Date:** 2026-06-27
- **Deciders:** framework architect
- **Related:** [SEC-0001](SEC-0001-fleet-identity-and-trust-fabric.md) (trust fabric, `KoanIdentity`/`Identity.Current`, ES256 issuer, epoch revocation), [SEC-0002](SEC-0002-unified-authorization-model.md) (`IAuthorize` seam), [SEC-0003](SEC-0003-dev-and-shared-secret-identity.md) (dev identity, fail-closed boot), [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) (gate·constrain·project — the authz floor), [SEC-0005](SEC-0005-governed-agent-access-grants-audit-door.md) (`AgentGrant`/audit/door), [SEC-0006](SEC-0006-embedded-oauth-authorization-server.md) (OAuth AS), [ARCH-0086](ARCH-0086-koan-module.md) (`KoanModule`), [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md) (exposure / `EntityToolset`), [ARCH-0099](ARCH-0099-tenancy-realignment.md) + [ARCH-0100](ARCH-0100-durable-ambient-carrier.md) (tenancy, the durable carrier). Evidence: the two delight-research passes + module-boundary prior art (see [koan-identity-design.md](../architecture/koan-identity-design.md) §Inputs).

## Context

Koan has **no durable user/identity entity.** Identity is 100% claims-driven and the "who" is a bare string everywhere it is referenced: `Membership.IdentityId` (`Koan.Tenancy`, "user id or email", FK to nothing), `RefreshToken.Subject` (`Koan.Web.Auth.Server`), `AgentGrant.Subject` (`Koan.Web`), `CurrentUserDto.Id` (`Koan.Web.Auth`). `IUserStore` (`Koan.Web.Auth`) is an empty `Exists()` stub — the user store was never built.

Everything *around* the person is already built and **stays**: sign-in (`Koan.Web.Auth` OAuth/OIDC + `Koan.cookie` session + `ExternalIdentity`/`IExternalIdentityStore`), token issuance/validation (`Koan.Security.Trust` ES256 + `KoanIdentity`/`Identity.Current`; `Koan.Web.Auth.Server` OAuth AS), the role **catalog** (`Koan.Web.Auth.Roles`: `Role`/`RoleAlias`/`RolePolicyBinding` + `RolesAdminController`), the **authorization floor** (`Koan.Web` `[Access]` + `EntityAccess<T>` + `IAuthorize` + `EntityFloorAuthorizationProvider`; `AgentGrant` for revocable per-subject capabilities), and tenancy (`Koan.Tenancy` `TenantRecord`/`Membership`/`Invite` + the fail-closed ambient axis + the ARCH-0100 carrier).

Two web "delight" research passes (onboarding; management/self-service/DX) plus module-boundary prior art (WorkOS/Clerk/Auth0/Keycloak/ASP.NET-Identity/Stytch/Frontegg) converge: the day-2 identity-management delights the market **charges for or structurally cannot do** are **near-free byproducts of Koan's substrate** — because *identity is just `Entity<T>` one layer down.* What is missing is the **person** and the **management/self-service experience** no .NET option ships.

## Decision

Add **`Koan.Identity`** (headless core) and **`Koan.Identity.Web`** (consoles + drop-in UI) — the module that owns the durable **person**, its **factors**, its **management/self-service surfaces**, and the **day-2 access primitives**, and **composes** with the auth/token/authz/tenancy pillars rather than duplicating them.

Framing: **identity is the noun; membership = `identity × tenant`.** `Koan.Tenancy` keeps `Membership`; `Koan.Identity` gives `Membership.IdentityId` a real backing entity. **Graceful degradation by composition:** referencing `Koan.Identity(.Web)` yields users/groups/global-roles/permissions + the operator & self-service consoles; adding `Koan.Tenancy` lights up membership/grants/invites/affordances **over the same entities** — no code-path fork.

### The load-bearing decisions

**D1 — `Identity` is a durable `Entity<Identity>` (GUID v7), the canonical subject; the ambient claims view stays in Trust.** The person record lives in the app's own data store. The existing ambient `Koan.Security.Trust.Identity.Current` / `KoanIdentity` struct (the request's `ClaimsPrincipal` view) is unchanged; reconciliation bridges the two (claims `sub` → `Identity.Id`). Naming: the durable entity is `Koan.Identity.Identity` (the person); the ambient accessor remains `Koan.Security.Trust.Identity.Current` (the principal). They are different concerns and must not be merged (see Open Questions for the residual naming wrinkle).

**D2 — App-owned, portable, no per-MAU (the anti-lock-in moat).** Identities, factors, credentials (portable PHC/bcrypt hashes), roles, sessions, tokens, and audit are all `Entity<T>` over the multi-provider data abstraction with stable GUID v7 ids → native FKs, one-command export/import, **no bespoke auth-storage adapter layer** (the abstraction that killed Lucia is structurally absent — storage is solved one layer down). Cost scales with infra the team already owns; there is **no per-user license axis**, stated in the boot report.

**D3 — One role catalog, two binding scopes.** The catalog stays in `Koan.Web.Auth.Roles`. `Koan.Identity` adds a **global** user↔role binding (`IdentityRole`) for the no-tenancy case + the effective-access resolver; `Membership.Roles` stays the **tenancy** binding. One catalog, bound globally *or* per-membership — never two divergent catalogs (WorkOS/Auth0 prior art).

**D4 — Optional local-credential factor, off by default.** External-IdP-only stays the zero-config default; password + passkey/WebAuthn are **opt-in** attached factors. Hashes are portable PHC/bcrypt (D2). The person≠email model needs multiple factor types regardless.

**D5 — Person ≠ email.** One `Identity` holds multiple **attached verified factors** (`IdentityEmail` + `ExternalIdentity` links + `IdentityCredential`) and many **revocable memberships**, with per-membership display overrides. This dissolves duplicate accounts, account-merge hell, wrong-method lockout, and departure-lockout at the root.

**D6 — Compose, don't duplicate.** The authz floor (SEC-0004), sign-in (`Koan.Web.Auth`), token issuance (SEC-0001/0006), and tenancy (ARCH-0099/0100) already exist and are referenced — not reimplemented. `Koan.Identity` provides the `IUserStore` implementation that `Koan.Web.Auth` already calls.

**D7 — Reference = Intent dual consoles.** Referencing `Koan.Identity.Web` auto-mounts the **operator** admin console and the **end-user** self-service panel, generated over the identity entities via the `EntityController`/`EntityToolset` ergonomic (ARCH-0092) — closing the universal "ships no console" gap (ASP.NET Identity, Ory, NextAuth, Supabase). Customization is **layered** (theme → slot → headless → own-one-flow), dodging the ASP.NET all-or-nothing curse.

**D8 — Safe-by-construction power verbs.** Impersonation carries an `actor` claim (separate from `sub`) on the ARCH-0100 durable carrier, fail-closed across async hops; an always-on banner auto-injects; dangerous identity verbs 403 while an actor claim is present; "who may impersonate whom" is the **same** `Can(subject, action, resource)` check (SEC-0004), never bespoke. Impersonation, JIT/time-boxed elevation, and customer-grantable support access are **all** `AgentGrant` (Save=issue / Remove=revoke / Query=observe / `ExpiresAt`=time-box). Identity/access mutations **auto-emit** before→after `AuditEvent`s (they are entity writes) on a dedicated append-only channel; "God-mode" cannot ship by accident.

**D9 — Not a policy engine, not an IdP server, not Keycloak.** The `[Access]` ↔ fluent `EntityAccess<T>` range already spans simple→fine-grained; the story is **evolve in place** (add a condition/grant to an existing role, no enforcement-code change), not a new Rego/Cedar engine. SSO/OAuth server stays `Koan.Web.Auth.Server` + connectors. Tenant control plane stays `Koan.Tenancy`. Resisting scope creep is the discipline that keeps this shippable.

## Build in composition order (each layer composes on the prior; following it top-to-bottom yields a working system)

> Each layer = a phase (P0…P5). Per layer: TDD-where-it-pays → adversarial multi-lens review → an ARCH-0079 integration spec through real `AddKoan()`. Each layer compiles green and is independently dogfoodable.

### Layer 0 — Person core + the seam *(P0; foundation everything composes on)*
- **Project:** `src/Koan.Identity/Koan.Identity.csproj`; add to `Koan.sln`. `SecIdentityModule : KoanModule` (`Id = "Koan.Identity"`) — Reference = Intent registration; no manual ceremony.
- **Entities:** `Identity` (`Entity<Identity>`, GUID v7; `DisplayName`, `Picture`, `Status` {Active|Suspended|Deactivated}, `Epoch`, `[Timestamp]` Created/Updated); `IdentityEmail` (`[Parent(typeof(Identity))]`; `Address`, `Verified`, `Primary`).
- **Reconciliation:** implement `IUserStore` (currently a stub in `Koan.Web.Auth`) so the sign-in callback **upserts** an `Identity` from claims and **relates** the `ExternalIdentity` (provider+keyhash) to it; `Identity.Id` == the claims `sub`. Multi-provider sign-in → one person.
- **Seam fill:** `Membership.IdentityId` becomes a soft-FK to `Identity.Id` (string column + logical reference — **no breaking migration**). `[Parent]` navigation where it pays.
- **Posture + DX:** dev-open/prod-closed mirroring tenancy; pre-seeded offline dev users (`sign in as alice@example.com`, no network — integration-tests-as-canon).
- **Audit channel scaffold:** `AuditEvent` (append-only; `actor`/`action`/`target`/`before→after`/`context`/`occurred_at`).
- **Acceptance:** an app references `Koan.Identity`, signs in, and a durable `Identity` row exists that `Membership.IdentityId` resolves to; tests run offline with seeded users.

### Layer 1 — The two consoles + sessions/devices *(P1; the visible payoff, over Layer-0 entities)*
- **Project:** `src/Koan.Identity.Web/Koan.Identity.Web.csproj` (Reference = Intent → auto-mounts the surfaces).
- **Operator console:** user list as an `Entity<T>` query (contains/fuzzy + multi-field AND/OR + custom-attribute search + sort + cursor paging); **uncapped bulk lifecycle** (suspend≠delete; preview+partial-failure; one audit batch row); **lifecycle-aware delete** (detect dependents → reassign/anonymize/cascade, never a raw FK error); groups.
- **Self-service panel (end-user twin):** profile; **sessions & devices** + "sign out everywhere-**else**"; connected accounts (unlink-safe across **all** factor types); API tokens; **data export + verifiable-erasure delete**.
- **Entities:** `Group` (`Entity<Group>`, `[Parent]`-nestable); `Session` (`Entity<Session>`, `[Parent(Identity)]`; device/browser/OS, approx city, first-seen/last-active, `IsCurrent` server-tagged — the durable twin of the `Koan.cookie` transport; revocation kills server tokens synchronously); `ApiToken` (`Entity<ApiToken>`, `[Parent(Identity)]`; scoped/named/`ExpiresAt`/last-used; rotate = new row, scopes preserved, brief overlap).
- **Audit-by-construction live:** identity mutations emit `AuditEvent`s via the lifecycle seam.
- **Acceptance:** reference `Koan.Identity.Web` → secured operator console + self-service panel render with zero hand-built UI; "sign out everywhere-else" is immediate + observable.

### Layer 2 — Access model & explainability *(P2; composes on Layer-0 identity + the existing catalog/floor)*
- **Global binding:** `IdentityRole` (`[Parent(Identity)]`) — the no-tenancy user↔role binding over the `Koan.Web.Auth.Roles` catalog (D3).
- **Effective-access resolver:** flatten `IdentityRole` (global) + `Membership.Roles` (per-tenant) + `AgentGrant` + `EntityAccess<T>.Constrain` into one human-readable "X can do A,B,C on these resources"; role-overlap detector (catch role explosion pre-commit).
- **Bidirectional explainer:** forward ("can X do Y on Z?") + reverse ("why does X have access to Z?" → the exact contributing row) with one-click revoke (`Remove()` the grant). Rides the SEC-0004 `gate·constrain·project` seam.
- **Self-explaining denials/allows:** the same evaluation emits a decision trace (deciding term + the input that failed) inline on the wall, in the console, and via an MCP/CLI `explain` verb.
- **Author-time safety:** lint the `[Access]`/`EntityAccess<T>` declaration at build (always-allow/unreachable/shadowed fail to save); a real-engine playground/simulator (preview == production); the access-shape surfaced in `koan.lock.json` so a silent widening shows in a PR diff.
- **Acceptance:** "who has access to Z and why" is a query; a denied request explains itself; a dangerous rule fails to compile.

### Layer 3 — Day-2 power *(P3; composes on Layers 0–2 + AgentGrant + the carrier)*
- **Safe impersonation (D8):** `actor` claim on the ARCH-0100 carrier; auto-injected banner; 403 on dangerous verbs while impersonating; request→approve→time-boxed on `AgentGrant` (mandatory reason+ticket, no-self-approval, user-revocable, dual-sided audit). Gate via `Can(...)`.
- **JIT / time-boxed grants:** no standing admin; customer-grantable read-only support access distinct from impersonation; pre-expiry one-click extend; documented break-glass path.
- **Tamper-evident change-audit:** the `AuditEvent` channel gets optional hash-chaining + a customer-facing in-portal view + self-configured SIEM streaming.
- **Account security factors (D4):** local-credential + passkey/WebAuthn (server-synced state — no phantom credential); MFA; **pre-provisioned recovery** (codes + recovery contacts); **Security Checkup** traffic-light landing; risk-tiered "this was me / wasn't me".
- **Acceptance:** impersonation is attributed/banner'd/time-boxed/revocable and structurally cannot be "God-mode"; audit is tamper-evident; recovery is set up before it's needed.

### Layer 4 — Membership (tenancy-on) *(P4; lights up when `Koan.Tenancy` is referenced)*
- **Invite binds to identity, not the email string:** at accept-time resolve to the signed-in identity, detect alias/dotted-gmail/collisions before creating a second account, reconcile inline.
- **The tenant-resolution module:** per-tenant-configurable carrier (subdomain `{code}.host` / path `/t/{code}` / claim — config on `TenantRecord`), the `ITenantResolver` concrete slice + the `IKoanWebPipelineContributor` middleware at `AfterAuthentication` wrapping the request in `Tenant.Use(...)`; **safe-by-default domain routing** (DNS-verified domains only + existing-verified-member required + generic-provider blocked) + admin sprawl-inventory.
- **Seat = active-member; atomic verifiable deprovisioning:** "deactivated = cannot act" across data/storage/cache/sessions (the fail-closed axis + carrier + `Session` revocation), with a per-user receipt.
- **Acceptance:** two studios are isolated end-to-end through the UI; an invite never spawns a duplicate; deprovisioning is atomic + verifiable; the flagship spec stays green.

### Layer 5 — SnapVault dogfood + measure *(P5)*
- Wire SnapVault studios onto `Koan.Identity`; demonstrate the moats (person-identity, atomic deprovisioning, instant switching, safe impersonation); the flagship tenancy spec stays green; record the before/after reduction. See [[snapvault-conversion]].

## Data model (summary)

New: `Identity`, `IdentityEmail`, `IdentityCredential`, `Group`, `Session`, `ApiToken`, `AuditEvent`, `IdentityRole`. Relates to existing `ExternalIdentity` (now → `Identity`). Reuses: `Role`/`RoleAlias`/`RolePolicyBinding` (catalog), `AgentGrant` (impersonation/JIT/support — one primitive), `Membership`/`Invite` (tenancy binding). Full field list + the capability→pain mapping: [koan-identity-design.md](../architecture/koan-identity-design.md) §5–§6.

## Consequences

- **Net-new pillar** with a clean seam (`IUserStore` + `Membership.IdentityId`), composing with every existing auth/authz/trust/tenancy surface; no framework rework of those.
- **The keystone:** Auth proves *who*, `Identity` is the durable *person*, Tenancy decides *which tenants*, the `[Access]` floor decides *what they can do* — today "who" is a bare string; making it real enriches everything downstream.
- **delight == moat:** app-owned data, generated dual consoles, free bidirectional explainability, safe-by-construction power verbs, audit-by-construction — structurally cheap for Koan, charged-for or impossible elsewhere.
- **Scope risk** (Keycloak-ification) is bounded by D9 and the layered phasing; P0–P1 already deliver a believable, dogfoodable cut.

## Alternatives considered

- **No module — keep identity claims-only.** Rejected: leaves `Membership.IdentityId` a bare string forever; no console, no self-service, no person≠email, no app-owned moat.
- **Bolt identity onto `Koan.Web.Auth`.** Rejected: conflates sign-in (transient principal) with the durable person + management surfaces; the two have different lifecycles. `Koan.Identity` *provides* the `IUserStore` `Auth` calls.
- **Org-mandatory (Stytch-B2B/Frontegg) model.** Rejected: forces tenancy assumptions into the user core; breaks the no-tenancy degradation. We use the WorkOS/Clerk org-free-core + additive-membership seam.
- **A new ABAC/ReBAC policy engine.** Rejected (D9): the `[Access]`↔`EntityAccess<T>` range + `AgentGrant` already span the need; evolve in place.

## Anti-patterns to honor

Email-as-identity-key · default-to-create-new-workspace · paywall security (SSO tax) · fire-and-forget deprovisioning · God-mode/identity-swap impersonation (never swap `sub`; always wrap with `actor`) · 404-instead-of-403 + bare "Access Denied" · UI affordances the backend rejects · additive perms with no effective view · role explosion as the growth path · per-MAU pricing · non-portable hashes/unstable ids · operational logs reused as audit / mutable audit · all-or-nothing UI customization · a bespoke auth-storage adapter (the Lucia lesson) · standing admin creds · never-expiring impersonation/elevation.

## Open questions

- **Naming wrinkle:** the durable entity `Koan.Identity.Identity` coexists with the ambient `Koan.Security.Trust.Identity` static accessor. Confirm whether to rename the entity (e.g. `Person`) or keep `Identity` with FQN discipline. (Leaning: keep `Identity`; the ambient accessor is the claims *view* of the durable person.)
- Identity-key vs verified-email matching rules for collision/merge.
- `Session`-vs-cookie relationship (cookie stays transport; `Session` is the durable twin for the device list + revocation).
- MFA/passkey + recovery storage depth (server-synced passkey state is load-bearing).
- This ADR is **Accepted-planned**; flip to **implemented** per layer as P0…P5 land.
