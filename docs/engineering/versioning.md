---
type: DEV
domain: framework
title: "Package versioning"
audience: [maintainers, release-engineers]
status: current
last_updated: 2026-08-14
framework_version: v1.0.0
---

# Package versioning

All active Koan packages share one version. The repository-root `version.json` is the only NBGV
authority; every packable project inherits it and therefore produces one release train from one
commit. Shelved and explicitly non-packable projects are outside the train.

## Change the version

- For an ordinary compatible change, leave `version.json` alone. Git height supplies the patch.
- From 1.0 onward, advance the root major for a breaking change.

Do not add a project-local `version.json`, set `Version` in a project, hand-edit a patch, or maintain a
package-version map. A release tag confirms an already calculated version; it does not calculate one.

Preview the train version from a full-history checkout:

```powershell
dotnet nbgv get-version -p src/Koan.Core --public-release=true
```

`PublicRelease=true` removes the development commit suffix. The release workflow requires the
`vX.Y.Z` tag to equal that stable package version for the tagged commit.

## Compatibility

Internal Koan dependencies remain bounded to the next breaking line by
`build/compat-ranges.targets`. A 1.x package emits `[1.x.y, 2.0.0)`, so an incompatible major mix
fails at restore even though normal releases use one aligned version.

Assembly packages validate against the preceding public train. `KoanTrainBaselineVersion` is the
single SDK `PackageValidationBaselineVersion` source and is currently `1.0.0`; there are no
project-local baseline declarations. After publishing a complete train, advance this central value
to that released version before preparing the following train.

See [NuGet publishing](nuget-publishing.md) and
[ARCH-0124](../decisions/ARCH-0124-single-package-release-train.md).
