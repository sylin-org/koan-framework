# Tenancy prior-art findings — control planes + config/policy layering

> **Research input, not current product truth.** Proposed invitation, lifecycle, and configuration surfaces below
> remain prior-art observations only. See the [current Tenancy how-to](../guides/tenancy-howto.md) for supported APIs.

- Date: 2026-06-22
- Source: prior-art sweep workflow `wf_25fb6f0c-992` (5 clusters · 29 frameworks · real web + `gh` research → synthesis).
- Purpose: validate / challenge **ARCH-0099 §6** (control plane + the `Koan.Tenancy.Web` portal) and **§7** (the tenant configuration plane) against the field, and pull concrete refinements.
- Full raw output: workflow transcript `wf_25fb6f0c-992` (clusters: .NET multitenancy · B2B-auth orgs · config/flag layering · governed-override locks · SaaS/data-platform control planes).

## Verdict: validated, with a genuinely novel core

- **§6 plane split** ([HostScoped] control-plane is *not* tenant-scoped) is architectural **canon** — AWS SaaS Factory ("control-plane services are GLOBAL, not multi-tenant"), Nile/Neon catalogs, Temporal, django-tenants public schema, stancl central app.
- **§2b role-on-membership / one-identity-across-N-tenants** is the **unanimous** B2B shape — Clerk, WorkOS, Stytch, Auth0 Organizations, PropelAuth, keycloak-orgs. The StackExchange model is industry-standard.
- **§2 first-Owner one-shot-then-ignored** is exactly **Keycloak's** persisted claimed-state model the ADR cites.
- **Invite (token + expiry + accept-on-next-login)** is universal (Clerk/WorkOS/Auth0/Stytch/keycloak-orgs).
- **§7a layered resolution** (`effective = tenant-override-if-unlocked else default`) is the universal config-resolution shape (ABP 5-provider chain, Finbuckle, Orchard, Spring, Flagsmith, Azure).
- **§7a two-state mutability lock** is directly validated by **Spring Cloud Config `overrideNone`** (the identical operator-floor-wins vs operator-default-tenant-may-override toggle).
- **§7a honest-degrade-never-crash** is the field norm (Azure rejects write / keep-last-good; AWS keep-prior-version; Spring degrade-to-default).
- **§7b DNS-TXT-verified captured domains** *improves on* the field — WorkOS/keycloak-orgs verify, but django-tenants/stancl trust the configured row; the 0ktapus-takeover justification is correct; global single-org domain ownership is a hard WorkOS/Stytch invariant.
- **§1b fail-closed gate** is **contrarian-correct**: Nile ("no context = see ALL tenants", traded for analytics) and Rails acts_as_tenant fail open; Finbuckle "gives resolution but zero authorization."
- **§7's unification of the structural + config halves into ONE governed plane is genuinely differentiated** — nobody ships it.

## The keystone: Windows Group Policy is the 1:1 ancestor of the §7 lock

GP is the only system that is **structurally identical** to §7's mechanic *and* a checklist of its gaps. A GPO is declared-once + linked-to-a-scope (= declare-typed-key-once + per-tenant override); resolution is closest-scope-wins (= default → tenant-override); the lock is a single boolean that **inverts precedence** — **"Enforced" (No Override) == Koan `Locked`**, normal link == `TenantMayChange`. The invariant *"No Override always beats Block Inheritance"* (an Enforced parent pierces a child's block) **is** §7's policy-gate-above-tenant; *"a local GPO can specify neither Enforced nor Block"* is *"only the solution owner sets the lock."* It confirms the SHAPE is right — and names the three things §7 is missing (tri-state, clean-revert, RSoP).

Honorable mentions to borrow from: **Kubernetes ValidatingAdmissionPolicy** (cleanest declare-logic/param-values/binding-scope split + the strongest override-within-bounds via `matchResources ∩ matchConstraints` + `failurePolicy:Fail` = fail-closed as one field); **Intune two-plane** (Compliance unconditionally outranks Configuration = the model for §7b framework-axis invariants-regardless-of-lock).

## Refinements to ADOPT into §7 (the meat)

1. **Tri-state override representation** — distinguish *absent* (no opinion → inherit) from *explicitly-set-to-default* from *overridden* (GP's "Not Configured" vs "Disabled"). §7's binary present/absent can't say "tenant explicitly chose the default" → corrupts audit/provenance and drifts when the default changes.
2. **Bounded overrides on the lock** — alongside `Locked | TenantMayChange`, let the declaration carry a typed validity envelope (min/max/allowlist/schema) so *"TenantMayChange within [floor,ceiling]"* is expressible and out-of-bounds values are **structurally unrepresentable** (Kubernetes VAP, Cedar template placeholders, AWS AppConfig attribute constraints, Temporal's clamped 1–90d retention). Cedar's lesson: the bound must live **on the lock** — a deny-supreme floor admits no after-the-fact exception.
3. **First-class effective-value explainer (RSoP)** — the dominant operational pitfall across the whole lock cluster (GP needs RSoP; ABP has recurring "why is this tenant seeing the host value" with no provider trace, issues #18759/#2276). Ship `value = tenant-override / blocked by Locked floor in X / fell back to default Y / which layer won` as a boot-report + runtime query (fits self-reporting-infrastructure).
4. **Graduated enforcement mode (advise/dryrun → warn → enforce)** — for tightening a floor existing tenants already breach (Gatekeeper `enforcementAction`, VAP `failurePolicy`, AWS bake-time + auto-rollback, Unleash CR phases). The binary lock would hard-break a fleet; an observe-mode is needed (aligns with the memory's posture-change = migration-saga).
5. **Model §7b framework-axis invariants as a separate higher PLANE** that unconditionally outranks the ordinary config plane (Intune Compliance-beats-Configuration) — makes "you cannot unlock a security floor" *structural* (which plane it lives in), not a per-key "cannot unlock" bit the author must remember.

### Refinements to ADOPT into §6 / §2 (control plane)

6. **Time-box + visibly mark the prod bootstrap credential** — Keycloak 26's first-admin is ephemeral (2h), marked "temporary" everywhere, with an offline lockout-recovery command. Extend §1's dev-branded-ephemeral-key discipline to §2's prod one-time-token.
7. **WorkOS-style time-boxed, intent-scoped portal link** for sensitive prod ops (domain verification, provider config) — a 5-minute single-purpose delegation token beats a standing Owner-gated session.
8. **Structurally enforce the plane split** — §4a/§6 assert [HostScoped]; the field shows the split is *advisory* everywhere and leaks (AWS SBT/Neon builders "routinely leak tenant logic into control-plane services"). A control-plane entity acquiring a tenant particle should be a compile/boot **conformance failure**, not a convention.
9. **Carry tenant context into durable work-items + rehydrate** — stancl's QueueTenancyBootstrapper (tenant id rides the job payload). The §1b chokepoint protects the request path; jobs/messaging/outbox are the out-of-request hole (the Rails/Django worker-fails-open leak). Already in the memory handoff (P8/outbox-TenantId); the ADR should state it.
10. **Name the revocation-propagation contract** — Clerk roles-in-JWT and Supabase tenant_id-in-JWT both go stale until token refresh (removed member keeps access). Koan keeps membership as durable rows resolved per-request (good); wire the CAEP-epoch trust-fabric direction into §6/§7 explicitly.
11. **Two-speed enrollment ladder** for §7b registration posture — auto-**invite** (immediate) vs auto-**suggest**/request-to-join (admin-approval), richer than open|invite-only|domain-gated; enforce global single-org domain ownership.

## Anti-patterns to AVOID (each observed)

- **Client-side / out-of-band resolution** makes the lock advisory (Spring overrides-map "not enforceable"; Azure precedence in client `Select()` order). The lock is real only if the framework is the **single resolver** — keep §7 framework-owned, never expose a raw config path that bypasses it.
- **Fail-open on absent context** (Nile see-all, Rails unscoped/worker/console) — the canonical leak; no "no-tenant = see-all" escape hatch (cross-tenant = explicit audited P8 saga only).
- **Override-always-no-floor** (Finbuckle/Orchard/stancl/AWS-tier) — "a tenant-level appsettings silently overriding a security host key is undetectable" (Orchard). Don't weaken §7's lock for ergonomics.
- **Tattooing / dirty revert** (GP preferences, Intune CSPs persist after the profile is withdrawn) — withdrawing a tenant override or a lock must cleanly revert to the default with no residue (GP's wipe-and-reapply is the good model).
- **JWT-embedded role/tenant without a revocation channel** — keep roles as durable per-request rows; no mutable membership baked into a long-lived token without CAEP-epoch revocation.
- **Free-form unschematized settings sprawl** (PropelAuth/Auth0 org-metadata JSON, stancl data column) — typed-key + default is right; no untyped per-tenant blob alongside the governed plane.
- **Priority-effect floors a higher-priority ALLOW defeats** (Casbin `priority(p.eft) || deny`) — framework-axis floors must be deny-supreme / separate-plane, never first-match-priority.
- **Non-local precedence without introspection** (GP LSDOU, Gatekeeper audit-vs-admission) — don't grow past the 2-layer model without shipping the RSoP explainer first (ABP's opacity pitfall).
- **Heavyweight per-tenant containers as the default** (Orchard shell-per-tenant, Keycloak realm-per-tenant — don't scale past hundreds; the reason keycloak-orgs single-realm exists) — validates "tenant never in the table-name spine" + control-field default (§4a); container/db-per-tenant stays per-tenant opt-in placement (§3).

## Net

§6/§7 are on solid, validated ground; §7's governed-config primitive is the differentiator. The five §7 refinements (tri-state · bounded overrides · RSoP explainer · graduated enforcement · framework-axis-as-plane) turn the binary lock into the full Group-Policy-grade mechanic the field proves is needed. They are additive to the existing §7 shape, not a redesign.
