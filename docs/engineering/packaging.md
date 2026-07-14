---
type: ENGINEERING
domain: engineering
title: "NuGet packaging policy"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  status: tested
  scope: tools/Koan.Packaging and PackageCleanRoom
---

# NuGet packaging policy

## Contract

- **Unit of ownership**: one packable MSBuild project, one package ID, one local `version.json`.
- **Release intent**: a push or merge that advances `dev`.
- **Selection**: NBGV version difference between the event's two Git commits, plus reconciliation of
  a current identity absent from nuget.org.
- **Output**: an exact, dependency-ordered manifest and its hashed nupkg/snupkg artifacts.
- **Proof**: release build/tests, advisory enforcement, package inspection, internal dependency
  closure, and a package-only external application that runs health and SQLite Entity CRUD.
- **Publication**: trusted GitHub OIDC identity, ordered push, registry visibility waits, resumable
  state, then a release tag.

The implementation is [Koan.Packaging](../../tools/Koan.Packaging/README.md); the governing decision
is [ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md).

## Package project requirements

Every packable project under `src/`, `packaging/`, or the top level of `templates/` must:

- evaluate an unambiguous `PackageId`;
- own a `version.json` in the same directory;
- provide a useful `Description` and package README;
- inherit the repository license, repository, SourceLink, and symbol defaults unless its artifact
  kind requires an explicit exception;
- represent internal dependencies as ProjectReferences so MSBuild can produce bounded compatibility
  ranges and the release compiler can order the graph;
- set `IsPackable=false` when it is an application, sample, fixture, or internal-only project.

Dependency-only bundles are SDK projects with `IncludeBuildOutput=false`, `IncludeSymbols=false`, and
ProjectReferences to their members. Do not author a parallel nuspec or substitute one bundle version
for its independently versioned dependencies.

## Local verification

Inventory the package surface:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory --output artifacts/release/inventory.json
```

Preview a release without nuget.org reconciliation:

```powershell
dotnet run --project tools/Koan.Packaging -- plan `
  --before HEAD~1 --after HEAD --offline `
  --output artifacts/release/release-set.json
```

The protected workflow runs the online plan. To rehearse the same package and external-consumer gate:

```powershell
dotnet run --project tools/Koan.Packaging -- pack `
  --manifest artifacts/release/release-set.json `
  --output artifacts/release/packages `
  --clean-room
```

`pack` is fail-fast. A package is not releasable when its expected identity was not produced, required
symbols are absent, required metadata is missing, a high/critical advisory exists, an internal
dependency floor is neither in the release set nor public, or the clean-room application fails.

## Adding a package

1. Create the SDK project and its consumer-facing README.
2. Add a project-local `version.json` with the intended major/minor and `pathFilters: ["."]`.
3. Express internal package dependencies as ProjectReferences.
4. Add the project to `Koan.sln`.
5. Run `inventory`; it must report one owner for the new package ID.
6. Run an offline plan across the introducing commit and inspect the manifest.

NBGV owns patch versions. Do not add `<Version>`, `<AssemblyVersion>`, or `<FileVersion>` to the
project and do not run a stamping script.

## Anti-patterns

- Do not enumerate packages by parsing XML or directory names; evaluated MSBuild state is canonical.
- Do not tolerate a failed pack and continue to publication.
- Do not disable NuGet audit to make a release green.
- Do not hand-create a release tag; tags record completed publication and do not trigger it.
- Do not rebuild a manifest during publish; publish consumes the verified manifest and hashes.
- Do not use the old nuspec, `apply-version`, `pack-meta`, or pack-everything scripts.

## Related

- [Versioning](versioning.md)
- [NuGet publishing](nuget-publishing.md)
- [ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)
- [ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md)
