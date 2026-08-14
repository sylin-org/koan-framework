---
id: ARCH-0124
slug: single-package-release-train
domain: Architecture
status: Accepted
date: 2026-08-13
title: One stable release train for all active packages
related:
  - ARCH-0085
  - ARCH-0110
  - ARCH-0120
  - ARCH-0121
---

# ARCH-0124: One stable release train for all active packages

## Outcome

Koan publishes every active packable project as one stable release train. One root `version.json`
owns the version, the evaluated MSBuild inventory defines the package set, and one explicit
`vX.Y.Z` tag on a commit reachable from `dev` starts release. The initial train is `1.0.0`. The
workflow independently certifies the tagged source, stages one package set, and publishes those
exact bytes; it never repacks between proof and publication.

This replaces package-local versions, reverse-dependent version cascades, and the `dev`-to-`main`
promotion ceremony. It retains bounded next-breaking-line dependency ranges because they turn an
incompatible Koan package mix into a restore failure instead of a runtime type-load failure.

## Decision

### One version authority

The repository-root `version.json` is the only NBGV version authority. Every packable project
inherits it, so packages produced from one commit have one version. Git height supplies the patch;
maintainers change the root major/minor only for a deliberate compatibility-line change.

NBGV's repository-wide default change scope is deliberate. There are no project-local
`version.json` files, package version manifests, or synthetic commits.

### One release inventory

Every active project for which evaluated MSBuild sets `IsPackable` to true joins the train. Projects
with `IsPackable=false` and code under `shelved/` do not. The packaging tool derives this inventory
from the project graph; the checked-in product surface is a reviewable projection, not a second
release manifest.

`product/claims.json` continues to describe capability maturity, guarantees, documentation, and
evidence. Claim maturity neither includes nor excludes an active package from publication. Adding
an active packable project is therefore an explicit commitment to ship and support it on the next
train.

### Compatibility remains bounded

`build/compat-ranges.targets` continues to emit closed-open Koan dependency ranges:

- before 1.0: `[X.Y.Z, X.(Y+1).0)`;
- from 1.0: `[X.Y.Z, (X+1).0.0)`.

All packages in a train align naturally, while consumers still fail at restore if they combine
packages from incompatible lines.

### One explicit release event

`dev` is the integration branch. Pull requests targeting it ordinarily pass the repository
coherence gate, but branch ancestry and branch protection are not release evidence. When an
integrated commit is ready, a maintainer creates `vX.Y.Z` for that exact commit. Reachability makes
the tag eligible; the release workflow validates and builds the tagged SHA and rejects a tag that:

- is not reachable from `dev`;
- is not a stable semantic version; or
- does not equal the root NBGV package version for the tagged commit.

Ordinary branch pushes cannot publish. `main`, release branches, GitHub Releases, and mutable release
manifests are not part of the package path.

### Certify once, publish the certified bytes

The unprivileged release job:

1. builds the Release solution and template on the tagged SHA;
2. evaluates the package inventory and packs every inventoried project once;
3. proves that the feed contains exactly one expected version of every inventory package and no
   extras;
4. restores, builds, and runs the checked-in package-only consumer through
   `Sylin.Koan.App`, with no repository project references; and
5. records SHA-256 hashes and uploads the feed as the release artifact.

A separate publish job receives the NuGet credential, downloads the artifact, verifies its hashes,
derives dependency-first order from the certified inventory, and pushes those same packages. Initial
publication requires every primary identity to be absent before the first push. During
partial-failure recovery, the publisher accepts a remote primary only when its original ZIP entry
names and uncompressed byte hashes match the certified local `.nupkg`; nuget.org's repository-added
`.signature.p7s` is excluded. It then retries the certified symbol sidecars. It cannot rebuild or
change the selected set.

### API-baseline transition

The first shared train had no shared public predecessor, so its `1.0.0` release retained each
assembly package's existing historical baseline while packing the complete active inventory. Once
that train was public, `KoanTrainBaselineVersion=1.0.0` became the single shared
`PackageValidationBaselineVersion` source and the historical project-local declarations were
removed.

Each later release validates every assembly package against the preceding complete train. After a
train is public, maintainers advance the one central baseline before preparing the following train.
Content-only packages remain covered by dependency-shape and package-consumer proof. The policy is
evaluated from source; it does not discover or choose baselines from live registry state.

## Consequences

- A consumer can pin one Koan version and receive a coherent closure.
- A package-affecting change advances the train, including packages whose bits did not otherwise
  change. That publication cost is accepted in exchange for a simpler contract and release path.
- Every new active packable project joins the next train; removing it from publication requires
  making it non-packable or shelving it explicitly.
- Claim maturity remains useful product evidence but does not weaken the stable 1.0 compatibility
  commitment of a published package.
- A source correction after tagging requires a new commit and version; a published tag is not moved.
- Focused provider behavior remains owned by its normal tests. Release certification proves the
  actual NuGet closure and the representative package-first application path.

## Supersession

This decision supersedes ARCH-0085 sections 1, 2, and 4: package identity is shared rather than
independent, and no reverse-dependent identity cascade is needed. ARCH-0085's bounded dependency
ranges and deprecate-before-remove discipline remain in force.

It supersedes ARCH-0110's project-local identity and `main`-push publication model. It also
supersedes ARCH-0120 where claim maturity selected publication or required project-local supported
version lines; claims remain the authority for capability guarantees and evidence. ARCH-0121's
claim-scoped development evidence remains in force, while this decision owns the complete release
inventory and certification boundary.
