# ARCH-0085 — Versioning, compatibility ranges, and version automation

**Status**: Accepted
**Date**: 2026-06-01
**Deciders**: Enterprise Architect
**Scope**: NuGet package version *numbers* and dependency *compatibility* across the Koan monorepo
**Related / supersedes**: extends **ARCH-0082** (two-tier versioning) and **closes its deferred Non-goal #1** ("No NuGet-side compatibility constraints in this ADR"). Builds on ARCH-0083 (operational workbooks).

---

## Context

ARCH-0082 adopted two-tier versioning (kernel lockstep + periphery independent) to stop version-inflation spam. It explicitly deferred the NuGet-side compatibility story. That deferral produced a production incident:

> A consumer (Gposingway) with `Sylin.Koan.Cache` at `Version="*"` resolved the published `Koan.Cache 0.8.1` against `Koan.Data.Abstractions 0.8.2`. `0.8.1` predates the DATA-0096 unified-pipeline migration (`c5017fea`), which **deleted** `ILinqQueryRepository<,>` from the kernel and replaced it with `IQueryRepository<,>`. `CachedRepository<,>`'s closure still referenced the deleted type → `TypeLoadException` on every decorated repository access.

Three failures stacked:

1. **Floor-only dependencies.** Periphery→kernel deps emit `>= X` (the `dotnet pack` default for a ProjectReference). A *breaking* kernel bump silently satisfies a stale periphery package's floor. NuGet checks version floors, never ABI.
2. **No coordinated rebuild on a kernel break.** The breaking wave bumped some periphery packages (Data.Core→0.9.0, Web→0.8.3) by hand and **missed others** (Cache stayed 0.8.1).
3. **Tooling that can't be run.** `Update-Versions.ps1` applies literal SemVer (`feat!→major→1.0.0`), contradicting the repo's pre-1.0 "breaking = minor" convention — so `versions.props` is hand-edited, which is how Cache was missed.

## The reframe

Versioning conflates two **independent** axes:

- **Axis 1 — the version number** ("*my* code changed"). Governs republish cadence and update noise. This is where "don't spam unchanged packages" lives.
- **Axis 2 — dependency ranges** ("what I'm *compatible* with"). Governs resolution. This is where skew lives, **entirely**.

The two do not trade off. Truthful, independent, bump-on-change numbers (Axis 1) and zero skew (Axis 2) are simultaneously achievable. ARCH-0082 got Axis 1 right and left Axis 2 empty.

## Decision

### 1. Uniform independent versioning (two-tier retired)

**Every package versions independently** — there is no kernel lockstep. A package with no commits in its folder is **not** republished. This is the anti-spam guarantee.

This *supersedes* ARCH-0082's two-tier (kernel-lockstep) model. The reason two-tier existed was to keep the abstraction contract surface coherent and avoid a compatibility matrix among independently-versioned packages — but **§3's compatibility ranges now provide that guarantee directly and more precisely** (an incompatible pair fails to resolve regardless of version numbers). With ranges in place, lockstep added only cost: in nbgv it would require all 14 abstraction packages to carry identical `pathFilters` lists and be bumped together, a fragile, manual operation. So the kernel/periphery split no longer drives versioning. `build/kernel-manifest.txt` and `$(KoanPackageKind)` survive only as informational metadata (what is "contract surface"), not as a version lever.

Trade-off accepted: there is no longer a single "kernel version" number to cite; each abstraction package versions on its own cadence. Consumers reason about compatibility through the ranges, not through a coordinated kernel number.

### 2. Numbers: operator owns major.minor, tooling owns patch — via Nerdbank.GitVersioning

Adopt **Nerdbank.GitVersioning (nbgv)**. Each package declares its `major.minor` in a `version.json`; the **patch is the git commit height** of that package's path since its `major.minor` last changed. Consequences:

- **Major/minor are a human decision** (the only judgment a tool can't make: "is this breaking / a feature?"). Bumped by editing `version.json`.
- **Patch is automatic and deterministic** — same commit → same version, reproducible from a clean checkout. A folder with no commits keeps its height → same version → not republished (anti-spam, by construction).
- **Per-package independence** via one nested `version.json` per packable project with `pathFilters: ["."]`, so height counts only commits touching that package's folder. A repo-root `version.json` is the fallback for unpublished projects (tests, samples).
- nbgv also stabilizes `AssemblyVersion` at `major.minor.0.0` (reduces binding churn) while `FileVersion`/`PackageVersion` move freely, and stamps the git SHA into the informational version.
- **Non-release branches get prerelease versions** (`0.17.3-g1a2b3c`) by default; clean release versions are produced on `main`/release branches (nbgv `publicReleaseRefSpec`) or with `-p:PublicRelease=true`. §3's range target only bands clean release versions (it skips prerelease deps, which are never published).

This replaces the bespoke `versions.props` + `Update-Versions.ps1` number computation. **Stick to 3-part SemVer** (`Major.Minor.Patch`); no 4th "revision" part (it breaks SemVer 2.0 / prerelease semantics). A CI/build identifier, if wanted, goes in **build metadata** (`0.9.3+<sha>`), which NuGet ignores for resolution.

**Baseline (as built):** all ~104 packable packages start at **`0.17.0`** (`build/versions.props` retired; `version.json` files generated by `scripts/versioning/Initialize-NbgvBaseline.ps1`). To bump one package, edit the `version` field in its `version.json`.

### 3. Compatibility: intra-Koan dependencies emit a bounded range, not a floor (Axis 2 — the fix)

Every Koan→Koan package dependency is packed as a **compatibility band** keyed to the referenced package's own breaking tier:

- **Pre-1.0** (`major == 0`): minor is the breaking tier → `[X.Y.Z, X.(Y+1).0)` — e.g. `[0.8.2, 0.9.0)`.
- **1.0+**: major is the breaking tier → `[X.Y.Z, (X+1).0.0)` — e.g. `[1.2.3, 2.0.0)`.

Effect: an incompatible pair fails at **restore** (NU1107/NU1608 — loud, at build time) instead of `TypeLoadException` at runtime in a consumer's suite. **The whole game is moving the failure left.** Implemented as an MSBuild target (`build/compat-ranges.targets`) hooked `AfterTargets="_GetProjectReferenceVersions"` that rewrites each ProjectReference's emitted `ProjectVersion` to the band; gated by `$(KoanDisableCompatRanges)` for escape-hatch.

### 4. Coordinated closure republish on a breaking bump, enforced by CI

When any package makes a breaking (pre-1.0 minor) bump, **every package that depends on it must be rebuilt + republished** — its IL recompiles against the new surface and its band floor moves past the broken version. This is **not spam**: the dependent's *compatibility contract* changed even when its own code didn't. The closure is computed from the dependency graph (not a kernel manifest). A release-gate check fails the release if the published set does not include the full dependent closure of every breaking bump. This is the check that would have caught the missed Cache.

### 5. Change discipline: deprecate-then-remove in the kernel

The skew was *fatal* only because DATA-0096 **hard-deleted** a type. Pre-1.0, kernel type/member removals go through one minor cycle as `[Obsolete]` forwarders before deletion. With the type still present, a stale dependent **loads** (degraded, warned) instead of crashing. This is belt-and-suspenders with §3: ranges stop the bad pair resolving; deprecation makes it non-fatal if one slips through (e.g. a consumer with `KoanDisableCompatRanges`).

## Consequences

### Positive
- The skew bug class is structurally closed: incompatible pairs can't resolve, and if forced, don't crash.
- Versions become truthful and reproducible; major/minor stay a deliberate human signal.
- The release gate makes "missed a package in the breaking wave" a hard CI failure, not a consumer incident.

### Negative / tradeoffs
- **Upper bounds are viral.** They can cause restore conflicts when a consumer mixes packages built against different kernels — but that is exactly the case we *want* to fail loudly, and within a coherent release all bands align. The common "avoid upper bounds" guidance targets open-ecosystem libraries with unknown consumers; it does not apply to a cohesive family tracking one kernel with frequent pre-1.0 breaks.
- **A kernel break touches the whole closure.** Mitigated by keeping the kernel surface small, preferring additive kernel changes, and deprecate-then-remove. Frequency drops sharply post-1.0.
- **nbgv migration cost.** ~104 projects gain a `version.json`; the bespoke number tooling is retired; CI must do a non-shallow fetch (nbgv needs history for height). One-time.

## Migration / rollout

1. ✅ **§3 compat ranges** — `build/compat-ranges.targets`; verified emitting `[X.Y.Z, X.(Y+1).0)` bands.
2. ✅ **nbgv adoption, baselined at `0.17.0`** — `Nerdbank.GitVersioning` wired in `Directory.Build.props`; per-package `version.json` (+ root fallback) generated by `scripts/versioning/Initialize-NbgvBaseline.ps1`; the `versions.props` import + KoanPackageKind version PropertyGroups removed from `Directory.Build.targets`. Verified: `Koan.Cache` packs `0.17.0` with all Koan deps banded `[0.17.0, 0.18.0)`; generator (netstandard2.0), nested connector, and web projects build clean.
3. ⏳ **Release-pipeline rework (follow-up).** The GitHub workflows (`release-on-main.yml`, `nuget-release.yml`, `create-tag-on-main.yml`) and `scripts/versioning/{Update-Versions,Show-VersionStatus,New-Release}.ps1` still reference the retired `versions.props`/`bumped-packages.txt` flow. They must be reworked to: select pack/push set from nbgv version deltas, compute the §4 dependent-closure, and **fetch full git history** (nbgv height needs it; the repo's CI is currently a disabled no-op, so this is not blocking). `build/versions.props` is left in place with a DEPRECATED banner until then.
4. ⏳ **CI release gate** (§4) and the deprecate-then-remove checklist (§5) folded into the release workbook.

## Non-goals
- `kernel-manifest.txt` / `$(KoanPackageKind)` are retained only as informational "contract surface" metadata; they no longer drive versioning (see §1).
- No `Koan.Sdk` metapackage (a later option for consumers who want a single pinned version surface).
- No retroactive re-tagging of already-published 0.8.x packages.
