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

An explicit `vX.Y.Z` tag starts the release workflow. The workflow packs every inventoried project once,
then `scripts/verify-package-feed.ps1` proves the exact package IDs and train version, restores the
checked-in `tests/PackageConsumers/AppJson` application from the staged feed, and records SHA-256
hashes. Publication downloads and verifies that staged artifact instead of packing again.

Dependency-only bundles and templates follow the same inventory, version, staging, and verification
path as assembly packages. Content-only packages do not receive an assembly API baseline.

See [Versioning](versioning.md), [NuGet publishing](nuget-publishing.md), and
[ARCH-0124](../decisions/ARCH-0124-single-package-release-train.md).
