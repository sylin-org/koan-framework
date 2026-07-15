---
type: ENGINEERING
domain: engineering
title: "NuGet packaging policy"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  status: tested
  scope: tools/Koan.Packaging and PackageCleanRoom
---

# NuGet packaging policy

## Contract

- **Unit of ownership**: one packable MSBuild project, one package ID, one local `version.json`.
- **Release intent**: a push or merge that advances `dev`.
- **Version lineage**: one serialized linear projection per `dev` event, preserved on
  `automation/package-lineage-dev`.
- **Selection**: exact identities stored by the prior/current lineage commits, automatic
  reverse-dependent closure for breaking roots, evaluated shared-input consumers, plus
  reconciliation of a current identity absent from nuget.org. The initial lineage is a one-time
  all-owner bootstrap.
- **Output**: an exact, dependency-ordered manifest and its hashed nupkg/snupkg artifacts.
- **Proof**: the canonical public-release green ratchet, advisory enforcement, package inspection, internal dependency
  closure, and package-only FirstUse and GoldenJourney execution outside the checkout.
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

The normal release instruction is only:

```text
push or merge the package-affecting change into dev
```

The protected workflow serializes before it calculates versions. It writes `release-lineage.json`,
then `release-set.json`. To replay the package and external-consumer gate from those exact artifacts:

```powershell
dotnet run --project tools/Koan.Packaging -- pack `
  --manifest artifacts/release/release-set.json `
  --output artifacts/release/packages `
  --clean-room
```

`pack` is fail-fast. A package is not releasable when its expected identity was not produced, required
symbols are absent, required metadata is missing, a high/critical advisory exists, an internal
dependency floor is neither in the release set nor public, committed lineage differs from its
artifact, `HEAD` is not the exact version commit, or either clean-room application fails.

## Adding a package

1. Create the SDK project and its consumer-facing README.
2. Add a project-local `version.json` with the intended major/minor and `pathFilters: ["."]`.
3. Express internal package dependencies as ProjectReferences.
4. Add the project to `Koan.sln`.
5. Run `inventory`; it must report one owner for the new package ID.
6. Let the protected `dev` event compile the exact lineage/manifest; for a controlled rehearsal, use
   a disposable checkout and the sequence in the
   [packaging tool README](../../tools/Koan.Packaging/README.md).

NBGV owns patch versions. Do not add `<Version>`, `<AssemblyVersion>`, or `<FileVersion>` to the
project and do not run a stamping script.

## Anti-patterns

- Do not enumerate packages by parsing XML or directory names; evaluated MSBuild state is canonical.
- Do not tolerate a failed pack and continue to publication.
- Do not disable NuGet audit to make a release green.
- Do not hand-create a release tag; tags record completed publication and do not trigger it.
- Do not rebuild a manifest during publish; publish consumes the verified manifest and hashes.
- Do not merge `dev` into the version-lineage branch by hand; the compiler applies one source-tree
  delta so unrelated package heights remain stable.
- Do not edit generated lineage state or marker files on `dev`; their paths are reserved and rejected.
- Do not use the old nuspec, `apply-version`, `pack-meta`, or pack-everything scripts.

## Related

- [Versioning](versioning.md)
- [NuGet publishing](nuget-publishing.md)
- [ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)
- [ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md)
