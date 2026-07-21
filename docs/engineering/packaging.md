---
type: ENGINEERING
domain: engineering
title: "NuGet packaging policy"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
---

# NuGet packaging policy

## Contract

One packable MSBuild project owns one package ID and one local `version.json`. Standard MSBuild and
NuGet metadata describe the package; NBGV owns its assembly/package version.

Every packable project under `src/`, `packaging/`, or the top level of `templates/` must:

- evaluate an unambiguous `PackageId`;
- own a project-local `version.json`;
- provide a useful description, tags, README, license, repository metadata, icon, and symbols;
- express internal package dependencies as ProjectReferences;
- include every source path that produces its package bits in NBGV `pathFilters`; and
- set `IsPackable=false` when it is an application, sample, fixture, or internal tool.

Internal dependencies are converted to bounded compatibility ranges by
`build/compat-ranges.targets`. Dependency-only bundles remain ordinary SDK projects with
`IncludeBuildOutput=false`; do not create parallel nuspecs.

## Inspect locally

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
dotnet run --project tools/Koan.Packaging -- quality
dotnet nbgv get-version -p src/Koan.Core --public-release=true
```

These commands inspect product/package shape. They do not stage or publish anything.

## Publish

Merge the intended source into `main`. The resulting `main` commit runs **Release packages**, which
packs the solution and the packable template project with `PublicRelease=true`, then pushes the
produced nupkgs using the repository's `NUGET_API_KEY`. Existing immutable identities are skipped;
any other failure stops the job. Development commits and open pull requests cannot publish.

Do not maintain package checklists, hand-authored manifests, release branches, escrow formats, or
recovery ledgers. NuGet owns immutable publication; Git and each local `version.json` own identity.

See [Versioning](versioning.md), [NuGet publishing](nuget-publishing.md), and
[ARCH-0110](../decisions/ARCH-0110-main-release-boundary.md).
