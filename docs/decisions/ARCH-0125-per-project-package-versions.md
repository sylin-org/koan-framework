---
id: ARCH-0125
slug: per-project-package-versions
domain: Architecture
status: Accepted
date: 2026-08-17
title: Each package owns its version on one shared compatibility train
related:
  - ARCH-0085
  - ARCH-0110
  - ARCH-0124
---

# ARCH-0125: Each package owns its version on one shared compatibility train

## Outcome

Every packable project owns a project-local `version.json`. A package's patch advances when that
package's own sources change, or when a shared build input that alters every package's output
changes. A package whose content did not change keeps the version it already published and is not
republished.

The compatibility train stays shared: the repository-root `version.json` owns `major.minor`, every
package version lies on that train, and `build/compat-ranges.targets` continues to bound internal
dependencies to the next breaking line. A release tag names the release event and must belong to the
train; it no longer names a package version.

This supersedes ARCH-0124's *One version authority* section only. ARCH-0124's release path — one
explicit tag on a `dev`-reachable commit, certify once, publish exactly the certified bytes, and no
publication from an ordinary branch push — remains in force.

## Decision

### Version ownership

Each packable project directory contains a `version.json` declaring the shared train with
`pathFilters` scoped to that project:

```json
{
  "version": "1.0",
  "versionHeightOffset": -7,
  "pathFilters": [
    ".",
    ":^./version.json",
    ":/Directory.Build.props",
    ":/Directory.Build.targets",
    ":/Directory.Packages.props",
    ":/build",
    ":/global.json"
  ]
}
```

`RepositoryInspector` requires this: a packable project that resolves versioning from an ancestor
`version.json` is rejected, because inheriting one would tie its patch number to commits that never
touched it. The rule is structural, so a new project cannot silently rejoin lockstep.

Shared build inputs are included by root-relative filters on purpose. A change to
`Directory.Build.props`, `Directory.Packages.props`, or `build/` alters every package's output, so it
must advance every package; otherwise a package's content would change while its version did not, and
the release would silently skip shipping the fix.

### Verified NBGV semantics

These were established empirically against nbgv 3.10.91 and are the reason the layout looks as it
does:

- `pathFilters` are evaluated **per commit against the `version.json` present at that commit**. They
  are not retroactive, so history recorded before a project owned a `version.json` counts in full.
- `.` is the owning file's directory. `:/path` is repository-root-relative — a bare `/path` is not.
  `:^` excludes; exclusions run after inclusions.
- Excluding `./version.json` keeps an edit to the version file itself from advancing the package.

Because filters are not retroactive, every project shared the same pre-existing height at adoption.
`versionHeightOffset` cancels exactly that measured height, so the whole inventory starts at `1.0.0`
— the version already published — and diverges only on subsequent change.

### The release is a set difference

`scripts/plan-release.ps1` asks NBGV for each project's version and nuget.org which of those versions
already exist. A version that is already published means that package did not change, so it is not
built, not packed, and not pushed. The resulting `release-plan.json` — train, per-package version,
publish flag, dependency-first order — is the only thing pack, verification, and publication consume.
Nothing downstream recomputes a version or re-decides what ships.

`main` is the published state. Merging `dev` into `main` releases whatever changed; a merge that
touched no packable source produces an empty plan and publishes nothing. Because versions come from
commit height, `main` must contain exactly `dev`'s commits: the workflow requires the pushed commit to
be reachable from `origin/dev`, which rejects a merge commit and enforces fast-forward.

### An unchanged package is not rebuilt

A package's bytes are **not** a function of its version. The build stamps the current commit into the
assembly — `AssemblyInformationalVersion` carries `+<sha>` and `FileVersion` varies with it — so the
same source rebuilt at a later commit produces a different artifact wearing an already published
version.

Any rule of the form "an existing version must contain exactly the bytes we just built" is therefore
unsatisfiable after the first release, and a design that compares them blocks every subsequent
release. The correct rule is to never produce the artifact at all: unchanged packages are excluded by
the plan, so there is nothing to compare and nothing to reconcile. Publication uses `--skip-duplicate`,
which makes re-running an interrupted push a no-op for whatever already landed.

The clean-consumer proof restores the staged feed **and** nuget.org, so it exercises the real mix a
developer gets — newly published packages resolving against previously published ones. An all-local
feed could not test that, and it is exactly where a wrong dependency range would surface.

## Consequences

- A release ships only what changed. Unchanged packages are never built, so a release's cost scales
  with the change, not with the size of the inventory.
- Publication is idempotent. Re-running a release that changed nothing is a green no-op.
- A consumer no longer reads one number across the closure. Bounded dependency ranges, not aligned
  version numbers, are what make a Koan mix coherent; an incompatible major mix still fails at
  restore.
- A shared build-input change advances the whole inventory. That is correct rather than incidental,
  and it is the one case that still behaves like a train.
- Documentation, samples, and support conversations name a package and its version, not a single
  framework version.
- `KoanTrainBaselineVersion` remains one central `PackageValidationBaselineVersion`. Validating every
  assembly package against the oldest published 1.x is the strongest form of the 1.x promise, so
  per-project baselines are not reintroduced.

## Supersession

Supersedes ARCH-0124 *One version authority*. Restores project-local version identity from ARCH-0085
§1 without restoring its reverse-dependent version cascades: bounded ranges already turn an
incompatible mix into a restore failure, so no cascade or synthetic commit is required.
