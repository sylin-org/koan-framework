# SEC-0008 — Access enforcement at the data layer (the ambient subject + the access read-axis)

> **R11 supersession amendment (2026-07-18):** The model-decorated ambient-subject implementation described below
> was retired before V1. `Koan.Data.Access`, `[AccessScoped]`, `Subject`, and durable arbitrary-filter carriage no
> longer exist. The surviving guarantee is smaller and more reusable: ordered `IWebContextContributor`s validate
> request evidence once after authentication and contribute typed Entity predicates through `WebContext.Where<T>`;
> Web projects those predicates into Data's existing `IReadFilterContributor` fold. SnapVault's `event` link is the
> consumer proof: the query value selects a durable grant but never authorizes by itself. This is request-lifetime
> read visibility, not headless/job authorization; durable work must establish or re-resolve its own service context.
> The original decision remains below as historical evidence for the superseded implementation.
>
> **R07 context amendment (2026-07-15):** Subject semantics and the access read-axis remain
> module-owned, but `Subject` now scopes `Koan.Core.Context.KoanContext` directly. Its
> `SubjectContextCarrier : IKoanContextCarrier` is registered independently from `AccessAxis`;
> `EntityContext.WithSlice` and Data-axis `.Carries(...)` are no longer current APIs. The original
> declaration and source citations below are retained as historical implementation evidence.

- Status: **Accepted — implementing (narrow increment)** · 2026-06-27
- Deciders: framework architect
- Supersedes/amends: amends **SEC-0004 §128** (resolves the deferred core-ward hoist of the read seam); resolves **ambient-charter Open Question #5** (the principal/subject slice). Builds on **DATA-0106** (the read-filter contributor seam), **ARCH-0101 §7** (the `IDataAxis` authoring surface), **ARCH-0102** (AODB push-down), **ARCH-0100** (the durable ambient carrier), **ARCH-0097** (the `EntityContext` one-carrier rule).
- Consumer of record: **SnapVault** (the studio↔client guest-gallery read path — the "real consumer" SEC-0004 §128 was waiting for).

## Context

Koan enforces **tenancy at the data layer**: every `Entity.Query()/.All()/.Get()` funnels through `RepositoryFacade`, which folds every registered `IReadFilterContributor` predicate into every read (`ReadScopeFold.Fold`, `RepositoryFacade.cs:155-205,261-271`) and lowers key-ops to scoped queries (IDOR defence). It is **fail-closed**: a predicate that cannot be pushed to the adapter throws (`RepositoryFacade.cs:195-205`). So a raw `Entity.Query()` returns only the ambient tenant's rows, with no controller in the loop.

Koan enforces **SEC-0004 access (the `Constrain` row-scoping) only at the web layer**: `EntityAccessConstrainHook<T> : IRequestOptionsHook<T>` fires solely inside `EntityEndpointService`. A raw `Entity.Query()` in a service, **job, or SSE handler composes the tenant filter but not the access filter** — it is structurally bypassable, because nothing below the web layer knows the subject. In Koan's own terms, **tenancy is fail-closed; access is remember-the-filter.** That asymmetry is the defect this ADR closes.

Three facts make the fix tractable and on-canon (all verified in source):

1. **The data-axis read seam is already generic and already multi-axis.** `IReadFilterContributor` never names "tenant"; `RepositoryFacade` folds a *plural* contributor list; its own doc-comment reserves the second slot for *"a future moderation capability's non-equality row-visibility predicate (`Filter.AnyOf(...)`, `Filter.Ne(...)`)"* (`IReadFilterContributor.cs:10-13`). **`SoftDeleteAxis` is a shipped, working second axis** of exactly that shape (`SoftDeleteAxis.cs`). Access is the third.
2. **The asymmetry is a deferral, not a decision.** SEC-0004 §128: *"Core-ward hoist of the seam (for jobs/bus) — ARCH-0092 landed it in `Koan.Web`; deeper hoist **deferred to a real consumer**."* The ambient charter parks the subject slice as Open Question #5. Moving the *read* half down **completes** canon.
3. **The only missing primitive is an ambient subject.** Tenancy's axis reads an ambient scalar (`TenancyAmbient.EffectiveTenantId()`); the data layer is principal-free (zero `ClaimsPrincipal` in `Koan.Data.*`). Access needs an ambient *who*, mirrored from the tenant slice.

The decisive consumer argument: **SnapVault's guest read path includes jobs and SSE** (a guest-triggered job reads photos). A web-layer hook *structurally cannot* scope a job; only the ambient subject + the ARCH-0100 carrier can. The data-layer axis is therefore not merely cleaner — it is the only mechanism that closes the guest-job/SSE leak.

## Decision

**1. Access read row-scoping rides the data-axis seam as a third axis (after `tenant`, `soft-delete`), driven by a new ambient *subject*.** Any `Entity.Query()/.All()/.Get()` of an access-scoped entity is inherently scoped, fail-closed, and IDOR-safe — in controllers, services, jobs, and SSE alike. The data core stays subject-agnostic; no registered axis ⇒ empty fold ⇒ byte-identical no-op (Reference = Intent).

**2. It is a *split*, not a relocation.** Only the read-row-scoping moves down. These stay at the web/MCP surface (SEC-0004 unchanged):
- the coarse allow/deny **gate** (401/403 — a response concern; ARCH-0092 "one door"),
- the per-row **`can:[]` projection** (a response-shaping concern),
- the **write create-stamp / freeze-ownership** (a payload concern the data layer cannot do).

One `EntityAccess<T>`/`Owner` declaration can feed both halves; the *general* migration of `EntityAccessConstrainHook` to the axis is deferred (see Scope).

**3. The narrow increment is opt-in and pushable.** An entity opts in with `[AccessScoped(field, scopePrefix)]`; the axis builds a **pushable `Filter.In`** from the ambient subject's snapshot. Off-by-default; only opted-in entities pay any cost.

## Mechanism

### The ambient subject (mirrors `TenantContext`)
- **`SubjectContext`** — immutable record on the one `EntityContext` carrier (charter L1). Four ambient states:
  - *absent* (no slice) → an access-scoped read **fails closed** (deny-all), posture-gated dev fallback like tenancy;
  - *System* (`Subject.System()`, the `Tenant.None()` analogue) → **no access constraint** (elevation for reconcilers, seeders, system jobs);
  - *Unconstrained* (`Subject.Use(id)`) → a known subject with **full access** (e.g. a tenant operator — tenancy already isolates them);
  - *Constrained* (`Subject.Use(id, scopes)`) → limited to `scopes` (a guest).
- **`Subject`** static facade: `Current`, `Use(id, scopes?)`, `System()`, mirroring `Tenant`.
- **`SubjectContextCarrier : IAmbientSliceCarrier`** (`AxisKey "koan:subject"`) — versioned capture/restore/suppress via `EntityContext.WithSlice<SubjectContext>`, so the submitter's subject rides the durable async hop (ARCH-0100). **This is what makes a guest-triggered job inherently scoped.**
- **Populated at the edge**: the web/MCP auth middleware sets the subject per request (constrained for a guest, unconstrained for an operator); outside a web app, code sets it explicitly (`using (Subject.Use("p", scopes)) { … }`) exactly as `Tenant.Use` works today.

### The access axis (mirrors `SoftDeleteAxis`)
```csharp
public sealed class AccessAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("access")
        .AppliesTo(static t => AccessScopedMetadata.IsAccessScoped(t))   // opt-in: the type carries [AccessScoped]
        .Reads(static t => AccessAmbient.ReadFilter(t))                  // subject snapshot → pushable Filter (or null/deny)
        .Carries(new SubjectContextCarrier());
}
```
`AccessAmbient.ReadFilter(t)` per the four states above: *absent* → deny-all (a `Filter.In(field, [])`-shaped match-nothing, posture permitting); *System/Unconstrained* → `null`; *Constrained* → `Filter.In(attr.Field, subject.Scopes.Where(prefix).Strip())` (empty scopes → match-nothing → deny). `.Reads` is a valid Shared-mode plane on its own (no `.Field` stamp — the axis filters an existing field like `eventId`, it does not write an invisible column).

### Recursion guard — by construction (opt-in)
The access predicate is built **at the edge** by reading grant rows once (the snapshot), never per query. The grant/control-plane tables are simply **not `[AccessScoped]`**, so the axis never fires on them — the access analog of grants being `[HostScoped]` for tenancy. No `IAmbientExempt` gymnastics, no re-query, no recursion, and the read-path predicate is a cheap pure set-membership test (hot-path discipline preserved).

### Pushability, cache, fail-closed
- **Pushable only.** The predicate is `Filter.In` (`FilterOperator.In` — pushes on relational/document/vector), never a `ClrFilter` (the non-pushable residual that fail-closes on every adapter, `RepositoryFacade.cs:195-205`). A future Expression-sourced predicate must pass a boot-time pushability gate (fail loud at boot, never closed at runtime).
- **Cache exclusion** is automatic for opted-in types (`DelegatingReadFilterContributor.ExcludesFromCache => Applies`) — a viewer-context result cannot share an equality cache key. Paid only by `[AccessScoped]` entities; everything else keeps its cache.
- **Fail-closed on absent subject** for access-scoped entities — the canonical default; legitimate full-access code declares `Subject.System()`/`Subject.Use(id)`.

## Module

A new `Koan.Data.Access` module (mirrors `Koan.Data.SoftDelete`): `SubjectContext`, `Subject`, `SubjectContextCarrier`, `AccessScopedAttribute` + `AccessScopedMetadata`, `AccessAmbient`, `AccessAxis`. Reference = Intent: referencing it enables data-layer access scoping; not referencing it leaves every seam empty. It takes **no dependency on `Koan.Identity`/`Koan.Web`** — the subject is a string id + scope-token set, populated by whatever edge the app uses (graceful degradation, the tenancy pattern).

## Scope

**In (this increment):** `Koan.Data.Access` (subject + carrier + `[AccessScoped]` + the access axis, fail-closed, pushable, opt-in) + tests; **SnapVault as the consumer** — `PhotoAsset [AccessScoped(Field="EventId", ScopePrefix="event:")]`, the guest edge populates `Subject` scopes from `GalleryGrant`, and the guest read path becomes the axis (the planned web `IRequestOptionsHook` is dropped).

> **`Field` = the CLR property name** (e.g. `nameof(PhotoAsset.EventId)`). `AccessScopedMetadata` normalizes it to the actual property name at first touch and **fails loud at boot** if it names no public property — because the relational filter translators emit the field name verbatim, a wrong-cased `Field` would otherwise silently deny-ALL on case-sensitive relational JSON paths (caught in the SEC-0008 review). Follow-ons (tracked, not in this increment): the **write-half** guard (a constrained subject's writes stay surface-enforced — the data axis is read-only), a **cross-adapter** `Filter.In(field, [])` row in the FilterConvergence oracle, and the **vector** read path (off the `IReadFilterContributor` fold today — a tripwire for any future broad migration).

**Deferred (consumer-driven, per §128):** the **general** migration of `EntityAccessConstrainHook`'s Expression-sourced `Constrain` to the axis (the web hook stays for non-opted entities — fully backward-compatible); the **write half** (create-stamp/freeze stays at the surface); the **`IDataService.Direct()` raw-SQL path** (RLS is the backstop — a Koan-side axis reaches the facade, not raw SQL).

## Consequences

- A raw `Entity.Query()` of an access-scoped entity is inherently scoped everywhere, including jobs/SSE (the SnapVault guest-job leak is closed structurally).
- The framework gains a reusable ambient **subject** (the charter's missing axis) — useful beyond access (audit actor, future axes).
- Cost: opted-in entities lose shared-key caching; full-access internal code must declare `Subject.System()`/`Subject.Use(...)` (the same elevation tax tenancy already imposes via `Tenant.None()`).
- The web `Constrain` hook shrinks over time to projection-only as entities migrate to the axis (fewer, more meaningful parts).

## Risks & mitigations

- **Elevation becoming god-mode** — `Subject.System()` is the only bypass; the tri-state distinguishes *absent* (deny) from *System* (elevated) so "no subject" never silently means "all access." Keep `System()` narrow, greppable, audited.
- **Double-narrowing** for an entity that has both `[AccessScoped]` and a web `EntityAccess<T>` — the two AND-compose idempotently (both narrow to the same owner set); an opted-in entity should drop the web read-`Constrain` to avoid redundant work.
- **Pushability regressions** — In-only for the narrow increment; a boot-time gate guards the general migration.
- **`Direct()` / raw SQL** — out of reach; RLS remains the backstop. The honest claim is "any query *through the facade* is access-safe," not "any query."

## Alternatives rejected

- **Web-only + lint/tripwires** — directly violates *structural > disciplinary* and *fail-closed > remember-the-filter*; a lint is the remember-the-filter failure dressed up. (No-stopgaps rule.)
- **Fail-closed data guard only** (refuse access-scoped reads without a subject, no row-scoping below the web) — closes the silent-leak but not the row-safety; half a loaf, and still needs the subject carrier.
- **Go-broad now** (migrate all SEC-0004 `Constrain` to the axis immediately) — largest blast radius; deferred until SnapVault proves the shape, exactly the consumer-driven sequencing §128 asked for.

## References

`src/Koan.Data.Core/Pipeline/IReadFilterContributor.cs:10-13,27,49` · `ReadScopeFold.cs:29-40` · `RepositoryFacade.cs:155-205,261-271` · `src/Koan.Data.Core/Axes/{IDataAxis,Axis,DelegatingReadFilterContributor}.cs` · `src/Koan.Data.SoftDelete/SoftDeleteAxis.cs` (exemplar) · `src/Koan.Tenancy/{TenantAxis,TenantContext,TenantContextCarrier,TenancyAmbient}.cs` (mirror) · `src/Koan.Data.Abstractions/{IAmbientExempt.cs,Filtering/Filter.cs}` · `src/Koan.Data.Core/Ambient/IAmbientSliceCarrier.cs` · SEC-0004 §128 · `docs/architecture/ambient-context-charter.md` (Q5).
