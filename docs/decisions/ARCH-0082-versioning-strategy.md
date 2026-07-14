# ARCH-0082 — Two-tier versioning: kernel lockstep, periphery independent

**Status**: Superseded by ARCH-0085
**Date**: 2026-05-16
**Deciders**: Enterprise Architect
**Scope**: NuGet package versioning across the Koan monorepo
**Related**: ARCH-0079 (integration tests as canon), ARCH-0080 (shared transport ownership), ARCH-0081 (typed registration helpers)

---

## Context

Koan ships ~95 NuGet packages from a single monorepo. Until now every package shared one hand-edited `<Version>` value: when ANY package needed a bump, every package bumped. This had three concrete costs:

1. **Version inflation.** A patch fix in `Koan.Cache.Adapter.Redis` forced a patch bump on `Koan.Storage`, `Koan.AI.Eval`, etc. — packages whose code didn't change. SemVer became dishonest: a patch bump implies "fix in this package," not "fix in some package."
2. **Consumer noise.** Reference = Intent means apps reference a handful of packages directly (`Koan.Web`, `Koan.Cache.Adapter.Sqlite`, …). Every Koan release dumped NuGet update prompts for every Koan package they had referenced, even when the changes were unrelated to their adapters.
3. **Manual coordination.** Bumping ~95 csprojs in lockstep by hand is error-prone (the codebase already had drift — some csprojs had `<Version>0.6.4</Version>`, some `0.7.0`, some no version at all defaulting to `1.0.0`).

A pure independent-versioning approach was considered and rejected for now: the cross-package coupling at the **abstractions** layer is real (interfaces in `Koan.Cache.Abstractions` are consumed by every cache adapter and by the cache pillar core). Letting those drift independently would force a compatibility-matrix story that's premature for a pre-1.0 framework.

## Decision

Adopt **two-tier versioning**:

### Tier 1 — Kernel (lockstep)
A small, fixed set of packages share a single version (`KoanKernelVersion`). When any kernel package's API changes, every kernel package bumps together. Kernel = the contract surface that periphery packages compile against.

**Kernel manifest** (authoritative list: [build/kernel-manifest.txt](../../build/kernel-manifest.txt)):
- `Koan.Core` — framework primitives
- `Koan.Core.Adapters` — adapter contract (`IAdapterOptions`, etc.)
- `Koan.Core.Registry.Generators` — source generator paired with `Koan.Core`
- `Koan.Cache.Abstractions`
- `Koan.Data.Abstractions`
- `Koan.Data.Vector.Abstractions`
- `Koan.Media.Abstractions`
- `Koan.Orchestration.Abstractions`
- `Koan.Rag.Abstractions`
- `Koan.Recipe.Abstractions`
- `Koan.Secrets.Abstractions`
- `Koan.ServiceMesh.Abstractions`
- `Koan.AI.Contracts` (de-facto abstractions package, named differently for historical reasons)
- `Koan.AI.Contracts.Shared`

### Tier 2 — Periphery (independent)
Everything else versioned independently. Each periphery package's version bumps only when its own directory has changes since the last release. Bump magnitude follows conventional commits:
- `feat!:` or `BREAKING CHANGE:` → major
- `feat:` → minor
- `fix:`, `refactor:`, `perf:`, `chore:`, `docs:`, `test:`, `ci:` → patch
- no commits in folder → no bump

### Storage
A single generated file [build/versions.props](../../build/versions.props) holds:
- `KoanKernelVersion` (one value)
- `KoanPeripheryVersion_<package-id-with-underscores>` (one per periphery package)

`Directory.Build.props` imports this file and resolves each csproj's `<Version>` via a `KoanPackageKind` property on the csproj (`Kernel` or `Periphery`).

### Release tag scheme
Releases are tagged `release/v<kernel-version>` (e.g., `release/v0.7.1`). Periphery versions at that commit are recorded in `versions.props` — git history is the source of truth for "what version of `Koan.Cache.Adapter.Redis` shipped with kernel 0.7.1."

### Tooling
Three PowerShell scripts under [scripts/versioning/](../../scripts/versioning/):
- `Show-VersionStatus.ps1` — read-only diagnostic: lists packages, current versions, and what would bump if released now
- `Update-Versions.ps1` — writes a new `versions.props` based on git history + conventional commits
- `New-Release.ps1` — finalizes the release: tags the repo and (optionally) pushes NuGet packages

Full operational guide: [docs/guides/versioning-workbook.md](../guides/versioning-workbook.md).

## Consequences

### Positive

- **Truthful versions.** A patch bump on `Koan.Storage` means storage actually changed. Consumers can read changelogs by package and trust them.
- **Reduced consumer noise.** An app that references `Koan.Cache.Adapter.Sqlite` only sees a NuGet update prompt when SQLite-cache-adjacent code (the adapter, `Koan.Cache.Abstractions`, or `Koan.Core`) actually changes.
- **Clear coordination surface.** The kernel manifest is one short file; reviewers checking "did this PR break the abstraction contract?" know exactly where to look.
- **Removes hand-editing drift.** Versions are computed; the build is reproducible from a clean checkout.
- **Backward-compatible.** Consumers' existing references still resolve. The change is bump-cadence, not API.

### Negative / acceptable trade-offs

- **One new file to commit per release.** `versions.props` becomes part of every release commit. This is also a feature — the diff *is* the release manifest.
- **Compatibility matrix is implicit.** A given periphery version assumes the kernel version recorded in `versions.props` at the same commit. We rely on git as the source of truth; future tooling could materialize this as a CSV or NuGet metapackage if needed.
- **Kernel bumps remain manual.** Periphery bumps are mechanical from git history. Kernel bumps require a deliberate decision (an ADR or explicit `-Reason` flag). This is intentional — the kernel is the contract.
- **Pre-1.0 kernel still moves.** ARCH-0078/0080/0081 all touched kernel surfaces. Kernel bumps will be common until 1.0; that's accurate and not a problem.

### Edge cases handled

- **Source generators (`Koan.Core.Registry.Generators`).** Generates code into Koan.Core consumers; treated as kernel because its output is part of the framework contract.
- **Analyzers (`Koan.Cache.Analyzers`).** Pure enforcement, no API consumers depend on. **Periphery.**
- **Renamed packages.** `Koan.AI.Contracts` is de-facto abstractions despite the name; included in kernel. No rename today; flagged for future cleanup.

## Non-goals

- **No NuGet-side compatibility constraints in this ADR.** Periphery `<PackageReference Include="Koan.Cache.Abstractions" Version="…">` declarations continue to use exact versions managed by the tooling. A future ADR may introduce floating version ranges (`[0.7.0,0.8.0)`) or a `Koan.Sdk` metapackage; not in scope here.
- **No CI/CD automation in this ADR.** Tooling is manual-first; a developer runs scripts locally. Wiring into GitHub Actions is a follow-up.
- **No retroactive versioning of existing 0.6.x/0.7.0 packages.** Initial `versions.props` captures the current state as the starting point; bumps proceed from there.

## Adoption checklist (also see [the workbook](../guides/versioning-workbook.md))

- [x] Kernel manifest committed at `build/kernel-manifest.txt`
- [x] `Directory.Build.props` imports `build/versions.props` and resolves `<Version>` via `KoanPackageKind`
- [x] All packaged csprojs declare `<KoanPackageKind>` (Kernel or Periphery)
- [x] Initial `versions.props` generated from current state
- [x] Tooling scripts in `scripts/versioning/`
- [x] Workbook documentation in `docs/guides/versioning-workbook.md`
- [x] Full solution still builds with 0 errors after migration
- [ ] First post-migration release (deferred to operator)

## Notes for reviewers

- The line between kernel and periphery is the **abstraction contract**. If a package only exposes interfaces and value types that other packages compile against, it's kernel. If a package is an implementation (adapter, connector, service, sample, web extension), it's periphery.
- `Koan.Cache.Analyzers` is periphery despite being framework infrastructure — its diagnostic IDs are a forward-compat surface but its public types aren't consumed.
- The tooling deliberately writes versions to a committed file rather than computing at build time. This keeps the build reproducible, the diff reviewable, and the version history readable via `git log build/versions.props`.
