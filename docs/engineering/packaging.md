---
type: ENGINEERING
domain: engineering
title: "NuGet packaging policy"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2026-08-14
framework_version: v1.0.0
---

# NuGet packaging policy

## Package contract

One packable SDK project owns one package ID. Standard MSBuild and NuGet metadata describe the
artifact; the root NBGV version supplies its identity.

Every packable project under `src/`, `packaging/`, or the top level of `templates/` must:

- evaluate one unambiguous `PackageId`;
- provide useful package metadata and its README;
- express internal package dependencies as `ProjectReference` items;
- use `IncludeBuildOutput=false` when it is dependency- or content-only; and
- set `IsPackable=false` when it is an application, sample, fixture, or internal tool.

`build/compat-ranges.targets` converts internal dependencies to the bounded next-breaking-line range.
Do not add a nuspec, local version file, or dependency-version map when an SDK project can express the
same package.

## Release inventory

The packaging tool evaluates every active project and includes every unambiguous `IsPackable=true`
project in the train. That evaluated inventory currently contains 94 packages. Code under
`shelved/` and projects with `IsPackable=false` are excluded.

`product/claims.json` documents capability maturity, guarantees, and evidence. It does not select
which packages ship. The generated product surface is a reviewable projection of the evaluated
inventory, not another release manifest.

Inspect the package graph and generated projection locally:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
dotnet run --project tools/Koan.Packaging -- quality
dotnet run --project tools/Koan.Packaging -- product-surface --check
```

## Release proof

A fast-forward push of `main` starts the release workflow. `scripts/plan-release.ps1` resolves every
project's version and subtracts what nuget.org already has; `scripts/pack-release.ps1` builds and packs
only that difference; `scripts/verify-release.ps1` restores and runs the checked-in
`tests/PackageConsumers/AppJson` application against the staged feed plus nuget.org, proving the real
package mix; `scripts/publish-release.ps1` pushes the planned set from a job that never built anything.

Dependency-only bundles and templates follow the same inventory, planning, and verification path as
assembly packages. Content-only packages do not receive an assembly API baseline.

See [Versioning](versioning.md), [NuGet publishing](nuget-publishing.md), and
[ARCH-0125](../decisions/ARCH-0125-per-project-package-versions.md).
