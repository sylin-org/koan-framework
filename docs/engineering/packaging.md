---
type: ENGINEERING
domain: engineering
title: "NuGet packaging policy"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2026-07-19
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
  reverse-dependent closure for breaking roots, conservative mapped shared-input consumers, plus
  reconciliation of a current identity absent from nuget.org. The initial lineage is a one-time
  all-current-owner bootstrap rooted at the coherent source event; durable continuity begins there.
- **Output**: an exact, dependency-ordered manifest, hashed nupkg/snupkg artifacts, and one
  deterministic release-wave escrow.
- **Proof**: the canonical public-release green ratchet, advisory enforcement, package inspection, internal dependency
  closure, and package-only FirstUse and GoldenJourney execution outside the checkout.
- **Publication**: draft GitHub Release escrow first; the existing repository `NUGET_API_KEY` only in
  the promotion step after prepared proof; ordered nupkg push and exact symbol replay; registry
  visibility waits; one completion receipt; then the immutable Release at
  `release/dev/<full-VersionCommit>`.

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
- declare tracked source outside the owner with `KoanPackageInput` when that source produces bytes
  packed by the owner; analyzer ProjectReferences are mapped automatically for every consuming package;
- set `IsPackable=false` when it is an application, sample, fixture, or internal-only project.

Dependency-only bundles are SDK projects with `IncludeBuildOutput=false`, `IncludeSymbols=false`, and
ProjectReferences to their members. Do not author a parallel nuspec or substitute one bundle version
for its independently versioned dependencies.

## Package-affecting build inputs

Version ownership follows source, never generated output. Analyzer ProjectReferences automatically
map the analyzer project's Git-tracked source files to every packable consumer, so a generator change
mints each potentially affected package without an operator-maintained package list.

When a package directly packs generated output produced by a sibling project, declare that sibling's
tracked source as evaluated `KoanPackageInput` items on the package owner:

```xml
<KoanPackageInput Include="..\BuildTool\BuildTool.csproj;
                           ..\BuildTool\**\*.cs;
                           ..\BuildTool\version.json"
                  Exclude="..\BuildTool\bin\**;..\BuildTool\obj\**" />
```

The release compiler treats those paths as conservative shared inputs, preserves prior/current
ownership across add, delete, and rename, and writes a package-local lineage marker to mint the
owner's next patch identity. `bin/`, `obj/`, ignored artifacts, and untracked files are never version
intent. Packing generated output from another project without declared tracked source fails inventory
with a corrective error.

## Local verification

Inventory the package surface:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory --output artifacts/release/inventory.json
```

Compile the current package-product assessment without changing release state:

```powershell
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md
```

The generated status reports structural repairs and review signals; it does not infer product maturity
or replace the R11 keep/merge/split/rename/retire decision.

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
artifact, `HEAD` is not the exact version commit, either package-path application fails, or either
installed package template cannot create, restore, build, and reach its business result.

For a controlled proof of the escrow input, add:

```powershell
dotnet run --project tools/Koan.Packaging -- wave-bundle `
  --lineage artifacts/release/release-lineage.json `
  --manifest artifacts/release/release-set.json `
  --artifacts artifacts/release/packages `
  --evidence artifacts/release `
  --output artifacts/release
```

This writes `release-wave-<full-VersionCommit>.zip` plus `release-wave.json`; it performs no remote
mutation. `wave-stage` and `wave-promote` are protected-workflow operations, not normal local release
steps.

## Release-wave contract

A non-empty manifest becomes one draft GitHub Release. The exact ZIP is uploaded first and the marker
last. The marker binds the full version commit, canonical full-commit tag, inner lineage/manifest
hashes, package count, and bundle hash. Once that marker is uploaded, its escrow is authority and is
never replaced.

The workflow derives `missing`, `staging`, `prepared`, and `published` from the Release itself. Only a
draft without an uploaded marker is resettable, and only when none of its selected nupkgs is public.
Prepared promotion downloads the original bytes, pushes missing nupkgs in manifest dependency order,
always replays required exact snupkgs with duplicate-safe semantics, waits for every nupkg, uploads one
deterministic completion receipt, creates or verifies `release/dev/<full-VersionCommit>` without
force, and publishes the same draft. Published is terminal only when GitHub reports that Release as
immutable and the tag resolves to the exact commit.

Every event reconciles the prior version wave before compiling the current one. Package visibility
without exact prepared escrow is a hard block, not permission to rebuild. An empty manifest creates no
bundle, draft, tag, or completion receipt.

The six workflow authority boundaries are `prepare_prior` (read), `stage_prior` (contents write),
`promote_prior` (contents write plus step-scoped API key), `prove_current` (read), `stage_current`
(contents write), and `promote_current` (contents write plus step-scoped API key). Proof jobs never
receive publish permissions or the key; staging jobs never receive credentials; promotion jobs never
rebuild source.

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
- Do not use a generated DLL, `bin/`, `obj/`, ignored artifact, or an owner-local file as
  `KoanPackageInput`; declare the external tracked source that produces the payload.
- Do not tolerate a failed pack and continue to publication.
- Do not disable NuGet audit to make a release green.
- Do not hand-create a release tag; automation creates or verifies it only at the final convergence
  boundary, immediately before publishing the prepared draft.
- Do not rebuild a manifest during promotion; `wave-promote` consumes downloaded, revalidated prepared
  escrow.
- Do not introduce a per-package recovery checklist; the immutable Release and one completion receipt
  are the recovery authority.
- Do not replace an uploaded release-wave marker, bundle, or completion asset.
- Do not merge `dev` into the version-lineage branch by hand; the compiler applies one source-tree
  delta so unrelated package heights remain stable.
- Do not edit generated lineage state or marker files on `dev`; their paths are reserved and rejected.
- Do not use the old nuspec, `apply-version`, `pack-meta`, or pack-everything scripts.

## Current observation boundary

The mechanism and failure simulations are implemented and focused tests are green. This cycle has not
observed a real NuGet publication or immutable GitHub Release. Before the separately authorized first
public wave, verify immutable Releases and the existing publish-scoped `NUGET_API_KEY` repository
secret as described in [NuGet publishing](nuget-publishing.md).

## Related

- [Versioning](versioning.md)
- [NuGet publishing](nuget-publishing.md)
- [ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)
- [ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md)
