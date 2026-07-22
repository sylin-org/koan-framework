---
type: WORK
domain: framework
title: "R12-05 - Prove the Public 0.20 Consumer Journey"
audience: [architects, maintainers, release-engineers, ai-agents]
status: completed
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: public NuGet template install, generation, restore, build, and SQLite runtime
---

# R12-05 — Prove the public 0.20 consumer journey

- Tranche: `T7B — public product maturity`
- Status: `completed`
- Depends on: passed R12-01 through R12-04
- Unlocks: genuine public use and focused product-maturity corrections
- Owner: the shipped template source owns the package-first consumer expression; standard NuGet owns resolution

## Meaningful outcome

A new developer can install Koan from NuGet.org, generate either starter, restore it cleanly, and run
useful SQLite-backed code without repository access or Koan-specific release machinery.

## Public promise

The beginner path is ordinary .NET:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
dotnet run --project TodoApi
```

The generated projects use standard NuGet `0.20.*` patch-floating references. NuGet therefore selects
the latest compatible 0.20 fix without crossing into 0.21. Applications that require exact
reproducibility can use ordinary NuGet locking.

No release manifest, token replacement, generated-project correction, Koan version API, or extra
configuration participates. The content-only template package is the sole source owner.

## Correction

Public `Sylin.Koan.Templates 0.20.5` generated bounded references beginning at an unpublished
`0.20.0`, so restore selected the available 0.20.4 packages but emitted NU1603. The template source
now uses `0.20.*` directly. This expresses the intended compatibility policy and removes the false
warning without adding a moving part.

## Public evidence — 2026-07-21

- PR `#94` merged to `main` as `cfb60f848653686278a1976dcacc71386f4cb19e`.
- `Release packages` run `29796113330` packed and published successfully.
- NuGet.org indexed `Sylin.Koan.Templates 0.20.6`.
- A fresh custom template hive installed exactly 0.20.6 from NuGet.org and generated both `koan-web`
  and `koan-console`.
- Both generated projects contained only `0.20.*` Koan references.
- Fresh NuGet.org-only restores used a clean package directory and completed without NU1603 or any
  other warning.
- The web project built in Release with zero warnings and zero errors.
- The console project selected local SQLite and passed Entity save, load, and query.
- The preceding live web proof passed SQLite-backed REST create/read and
  `/.well-known/Koan/facts`.
- Focused verification for the fallback fix passed:
  `SqliteConfigurationTruthSpec` (5/5) and `ServiceDiscoveryPlanSpec` (15/15).

No full release ratchet was rerun. The evidence is intentionally limited to the affected public
consumer path.

## Separate product rough edge

Resolved in this tranche (2026-07-21): local SQLite auto-fallback now enters discovery as an
adapter-owned candidate and no longer reports a misleading correction flow before selecting
`.koan/data/Koan.sqlite`.

## Architecture checkpoint — 2026-07-21

**Task:** Make SQLite autonomous fallback selection truthful (selected and explained as local intent) while keeping startup behavior and ownership unchanged.

**Application intent:** When auto-discovery is chosen for SQLite, startup should deterministically select the embedded file path as the fallback and report it as a legitimate path, without misleading configuration/correction errors.

**Public expression:** A default SQLite host remains:

```csharp
KoanEnv: Test or Development
Koan:Data:Sources:Default:sqlite:ConnectionString = auto
```

and resolves to `Data Source=.koan/data/Koan.sqlite` with no required external provider.

**Guarantee/correction:** `SqliteDiscoveryAdapter` now contributes an embedded-file discovery candidate owned by the adapter. `ServiceDiscoveryCoordinator` can therefore report selection rather than rejection when all external candidates are unavailable. Fallback remains:

- strict for explicitly configured required inputs,
- local and automatic when no explicit path is selected,
- and unchanged when discovery is explicitly disabled or coordinator unavailable.

**Complete intent surface:** No API surface change. No runtime behavior changes outside SQLite autonomous selection. No changes to release tooling or core discovery semantics.

**Public concepts:** Koan keeps startup as a pure .NET `AddKoan()` plus NuGet package reference; discovery explanation now follows ordinary "selected optional discovery candidate" behavior.

**Docs read:**

- `docs/initiatives/koan-v1/work-items/r12/R12-05-public-consumer-journey.md`
- `docs/initiatives/koan-v1/work-items/r12/R12-06-publish-and-observe-first-wave.md`
- `src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs`
- `src/Koan.Core/Orchestration/ServiceDiscoveryCoordinator.cs`
- `src/Koan.Core/Orchestration/Composition/ServiceDiscoveryRuntime.cs`
- `src/Connectors/Data/Sqlite/Discovery/SqliteDiscoveryAdapter.cs`
- `src/Connectors/Data/Sqlite/SqliteOptionsConfigurator.cs`
- `src/Connectors/Data/Sqlite/README.md`

**Code read:** See list above.

**Reusing:** Existing discovery model (`DiscoveryCandidate`, `DiscoveryCandidatePriority`, `ServiceDiscoveryCoordinator`) and existing SQLite connection builder in `SqliteOptionsConfigurator`.

**Creating new:**

| New code | Location | Why |
| --- | --- | --- |
| `embedded-default` runtime discovery candidate | `SqliteDiscoveryAdapter` | Makes fallback deterministic as a selected candidate without adding a new core choke point. |
| `DefaultSource` fallback constant | `Sqlite` infrastructure constants | Removes duplicated hard-coded path literals in the same connector boundary. |

**Coalescence:** Closest existing pattern was adapter-owned discovery candidates with adapter-specific normalization. SQLite now follows that pattern and removes the coordinator-level false-negative interpretation by making its local fallback part of discovery resolution rather than a configurator-side compensation.

**Ergonomics:** No new user-facing settings, no new API, no new external dependency. Startup remains automatic and explainable from normal discovery facts.

**Constraints satisfied:**

- Standard .NET and existing Koan discovery contracts remain primary.
- No changes to runtime contracts beyond SQLite selection path.
- No new tests are added until you request suite execution.
- `dev` changes remain non-publication scope.

**Risks:** If an explicit coordinator implementation suppresses adapter-specific candidates or rewrites planned candidates, the fallback may not be selected. That is a coordinator integration risk, not SQLite-specific; this change makes that risk visible by requiring adapter ownership for fallback selection.

## Acceptance

1. The install/generate/run path uses public packages and ordinary .NET commands.
2. Both templates restore from NuGet.org without warnings.
3. Generated references remain within the compatible 0.20 line.
4. Web build and SQLite-backed Entity behavior pass.
5. Publication remains exclusively owned by a resulting push to `main`; `dev` activity publishes
   nothing.
