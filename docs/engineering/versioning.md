---
type: DEV
domain: framework
title: "Package versioning"
audience: [maintainers, release-engineers]
status: current
last_updated: 2026-08-17
framework_version: v1.0.0
---

# Package versioning

Each packable Koan project owns its version. A project-local `version.json` supplies the patch from
the commits that touched that project, so a package advances when its own sources change and holds
still when they do not. Shelved and explicitly non-packable projects are outside the inventory.

The repository-root `version.json` owns the shared compatibility train — the `major.minor` every
package sits on.

## Change a version

- For an ordinary compatible change, edit the project and commit. Git height supplies the patch.
- To move the whole compatibility line, advance `major.minor` in the root `version.json` and in each
  project's `version.json`.
- Do not set `Version` in a project, hand-edit a patch, or maintain a version map by hand. The
  release path calculates versions; a tag does not.

Preview any package's version from a full-history checkout:

```powershell
dotnet nbgv get-version -p src/Koan.AI --public-release=true
```

`PublicRelease=true` removes the development commit suffix.

## What advances a package

| Change | Effect |
|---|---|
| A file inside the project directory | that package advances |
| That project's `version.json` | nothing advances |
| `Directory.Build.props` / `.targets`, `Directory.Packages.props`, `build/`, `global.json` | every package advances |
| Docs, samples, tests, skills, evals | nothing advances |

Shared build inputs advance everything on purpose: they change every package's output, so every
package needs a new version to carry it.

## Adding a project

A new packable project needs its own `version.json` before it can join the inventory. Copy one from a
sibling and set `versionHeightOffset` to `0` so the package starts at the train's `.0`. The packaging
tool rejects a packable project that inherits an ancestor `version.json`, so this cannot be forgotten
silently.

## Releasing

`main` is the published state. Fast-forward `main` from `dev` and the release runs:

```powershell
git checkout main
git merge --ff-only dev
git push origin main
```

The workflow computes a release plan — each project's version from NBGV, minus the versions already
on nuget.org — then builds, packs, proves, and publishes only that difference. A merge that changed no
packable source yields an empty plan and publishes nothing.

`main` must **fast-forward**. Versions come from commit height, so a merge commit would give `main` a
different history than `dev` and compute different patch numbers for the same sources. The workflow
requires the pushed commit to be reachable from `origin/dev` and fails otherwise.

An unchanged package is never rebuilt. The build stamps the commit into the assembly, so rebuilding
unchanged sources at a later commit would produce different bytes under an already published version;
excluding it from the plan is what keeps "no change, no new package" true.

Inspect a release without publishing:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory --output artifacts/release/inventory.json
./scripts/plan-release.ps1 -InventoryPath artifacts/release/inventory.json -OutputPath artifacts/release/release-plan.json
```

## Compatibility

Internal Koan dependencies are bounded to the next breaking line by `build/compat-ranges.targets`. A
1.x package emits `[1.x.y, 2.0.0)`, so an incompatible major mix fails at restore. Bounded ranges,
not matching version numbers, are what keep a Koan package mix coherent.

Assembly packages validate against the oldest published train version. `KoanTrainBaselineVersion` is
the single SDK `PackageValidationBaselineVersion` source and is currently `1.0.0`; there are no
project-local baseline declarations.

See [NuGet publishing](nuget-publishing.md) and
[ARCH-0125](../decisions/ARCH-0125-per-project-package-versions.md).
