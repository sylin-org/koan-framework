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

A `vX.Y.Z` tag on a `dev`-reachable commit starts a release. The tag names the release event and must
belong to the root train; it does not name a package version. The workflow resolves each project's
version into `package-versions.json`, packs once, verifies the feed and a package-only consumer
against that manifest, then publishes.

A package whose certified bytes already exist at its version is verified and skipped rather than
republished. An existing version whose remote content differs from the certified bytes fails the
release before anything is pushed.

## Compatibility

Internal Koan dependencies are bounded to the next breaking line by `build/compat-ranges.targets`. A
1.x package emits `[1.x.y, 2.0.0)`, so an incompatible major mix fails at restore. Bounded ranges,
not matching version numbers, are what keep a Koan package mix coherent.

Assembly packages validate against the oldest published train version. `KoanTrainBaselineVersion` is
the single SDK `PackageValidationBaselineVersion` source and is currently `1.0.0`; there are no
project-local baseline declarations.

See [NuGet publishing](nuget-publishing.md) and
[ARCH-0125](../decisions/ARCH-0125-per-project-package-versions.md).
