# ARCH-0090: Auth + data surface trim — five public-surface decisions

**Status**: Accepted (2026-06-17)
**Date**: 2026-06-17
**Deciders**: Enterprise Architect
**Scope**: Resolves assessment card **S4** ("auth + data surface trim — one ADR session", depended on E5). Five public-surface trim decisions across the auth and data pillars, each re-derived empirically (a 4-reader surface-mapping workflow) before deciding. The card's premises were **two-of-four stale** (Vector facade "merge"; Vector-workflow "cut" framing) — corrected here.
**Related**: WEB-0071 (E5 auth engine swap — the connectors are now thin post-swap) · DATA-0053 (per-adapter connection factories — `Koan.Data.Direct` relational sessions) · DATA-0103 (E6 ES/OS vector repo consolidation — *below* the Vector facade) · the C9 / external-consumer-gate lesson (PROGRESS Divergence log, 2026-06-14).

---

## Cross-cutting constraint — the external-consumer gate

Every package named here is `KoanPackageKind=Periphery` (published NuGet). **"Zero in-repo consumers" does NOT mean safe-to-delete** — an in-repo grep is blind to downstream-repo consumers (the C9 systemic blind spot). Any package-id retirement therefore carries an external-break risk and should be accompanied by a changelog/migration note. Where the architect accepted a hard delete (Data.Direct), that trade-off is explicit below.

---

## Decision 1 — `Koan.Data.Direct`: **fold-hard into Core, delete the package**

`Koan.Data.Direct` is a raw-SQL escape hatch (`IDirectDataService.Direct(...)` → session/transaction over ADO.NET, DATA-0053 connection factories). The **binding contracts already live in `Koan.Data.Core`** (`Koan.Data.Core.Direct.*`: `IDirectDataService`/`IDirectSession`/`IDirectTransaction`, and `IDataService.Direct(...)`); only the ~450-LOC implementation lives in the separate package, which has **zero real in-repo consumers** (the only call site is `DataService.Direct()` forwarding to the DI-resolved impl, which today throws "AddKoanDataDirect() required" when the package is absent — a half-dead member).

**Decision:** move the implementation (`DirectSession`/`DirectTransaction`/`DirectDataService`) into `Koan.Data.Core`, register `IDirectDataService` **by default** in Core's data registration (so `IDataService.Direct(...)` works out-of-the-box — Reference=Intent; it naturally `NotSupported`s on non-relational stacks since the connection factories are only present when a relational adapter is referenced), and **delete the `Koan.Data.Direct` project** from the solution. The `AddKoanDataDirect()` registration method is removed (not shimmed).

**External-break note (accepted):** downstream apps that `PackageReference Sylin.Koan.Data.Direct` break at restore, and any explicit `services.AddKoanDataDirect()` call no longer compiles. But `data.Direct(...)` *call sites* keep working (the contract is in Core and is now registered by default — strictly better than today). A changelog migration note ("remove the package reference + the `AddKoanDataDirect()` call; `.Direct()` now works by default") is the mitigation. The defensive-publication framing (the "Direct API escape hatch") is preserved — the feature stays, only its packaging changes.

## Decision 2 — Auth provider connectors: **shrink the clone bodies, keep the per-provider packages**

After E5 (WEB-0071) moved the OAuth2/OIDC engine to maintained ASP.NET handlers, the **Discord / Google / Microsoft** connectors are ~77-LOC near-identical clones: one `internal sealed IAuthProviderContributor` returning a static `ProviderOptions` dict + a registrar + an `[assembly: AuthProviderDescriptor]`. They have no live in-repo consumers (only archived samples).

**Decision:** collapse the three clone bodies behind a **shared helper** (the per-provider file becomes the literal endpoints/scopes/icon + a one-line registration), but **keep the three package identities**. This preserves **Reference=Intent** (an app references `Koan.Web.Auth.Connector.Google` precisely to opt into Google) and avoids an external break — collapsing them into one always-on built-in contributor was rejected because it would silently give every auth app google/microsoft/discord defaults (erasing the opt-in signal) and break the three package ids.
- **Oidc** connector: keep — it is the generic disabled-by-default "bring-your-own-OIDC" escape hatch, not a clone.
- **Test** connector: keep, untouched — it is the 1230-LOC dev OIDC/OAuth2 IdP and the canonical ARCH-0079 auth fixture (4 live sample/test consumers).

## Decision 3 — `Koan.Web.Auth.Roles`: **keep separate**

The DB-backed role layer (`Role`/`RoleAlias`/`RolePolicyBinding : Entity<T>`, the store contracts, `RolesAdminController`, `AdminBootstrapContributor`, `AddKoanWebAuthRoles`) has zero in-repo consumers and a clean one-way edge (Roles→Auth). Folding it into `Koan.Web.Auth` was considered and **rejected**: it would pull a hard `Koan.Data.Core` dependency into `Koan.Web.Auth`, which is deliberately **data-store-agnostic**. That layering property is worth more than retiring one zero-consumer package; the fold's only benefit (one fewer package) does not justify coupling the auth pillar to the data layer. **No change.**

## Decision 4 — Vector-workflow subsystem: **cut**

`IVectorWorkflow<T>` / `VectorWorkflow<T>` / `IVectorWorkflowRegistry` / `VectorProfiles` / `VectorWorkflowOptions` / `Workflows/VectorWorkflowRegistry` is an embed→save orchestration layer wired into `VectorData<T>.Save`/`SaveMany` (gated by `VectorWorkflow<T>.IsAvailable()`) and unit-tested — but **never activated in practice**: no sample registers a profile, and `Koan:Data:Vector:EnableWorkflows` defaults off. It is speculative generality.

**Decision:** **cut** it — remove the workflow branches from `VectorData<T>.Save`/`SaveMany` (the direct-persist path remains, unchanged), delete the workflow types (`VectorWorkflow`, `VectorWorkflowOptions`, `Workflows/`, the three abstractions, `VectorProfiles`, the workflow registration in `ServiceCollectionVectorExtensions`, the workflow bits in `EntityVectorExtensions`), the `EnableWorkflows` option, and the `VectorWorkflow.Spec` test. This is a public surface on the published `Koan.Data.Vector` package, so an external-consumer check + a changelog note accompany it (per the cross-cutting gate).

## Decision 5 — `Vector<T>` / `VectorData<T>` facades: **keep both** (premise stale)

The card's "facade merge" premise assumed redundant twins. Empirically they are **not twins**: `Vector<T>` is the user-facing transaction-aware orchestration facade (partition / capabilities / `EnsureCreated` / `Flush` / `Stats` / hybrid + options Search), and `VectorData<T>` is the persistence engine it delegates into (metadata normalization, dual-store consistency, the `VectorEntity` record that is part of `Vector<T>`'s contract). Critically, **`Vector<T>.IsAvailable` and `Vector<T>.Search(float[], VectorRetrieveOptions, ct)` are frozen *name-bound reflection targets*** in the AI pillars (`ChainExecutor` RAG retrieve + `EntityToolGenerator`) — a merge that drops the `Vector<T>` name or retypes `Search` would silently break RAG + tool-gen at runtime with no compile error. **Decision: keep both; reject the literal merge.** Document the facade/engine boundary instead.

---

## Consequences

- **Net trim** is modest and honest: one project deleted (`Koan.Data.Direct`, folded into Core) + one subsystem cut (vector-workflow), an auth-connector DRY pass, and two *non-actions* where the card's premise was stale or the layering cost too high (Vector facade keep; Roles keep). This mirrors the C-series finding that "trim" cards routinely over-target — the empirical map is the corrective.
- `IDataService.Direct(...)` goes from a half-dead throwing member to a default-registered, working feature.
- `VectorData<T>.Save` loses an unused branch; the direct-persist path is unchanged.
- External-break surface: the `Sylin.Koan.Data.Direct` package id (deleted) and the vector-workflow public types (cut) — both carry changelog migration notes; the auth-connector package ids and all Roles/Vector-facade types are preserved.
