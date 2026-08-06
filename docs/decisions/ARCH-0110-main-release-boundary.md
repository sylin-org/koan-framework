# ARCH-0110 — Main-boundary independently versioned package releases

**Status**: Accepted
**Date**: 2026-07-14
**Amended**: 2026-08-06 — dev is the sole development coalescence point; main remains the publication boundary
**Deciders**: Framework maintainer
**Scope**: Package versioning and NuGet publication

## Context

Koan packages need independent, correct versions and a reliable way to publish them. An automatic
release compiler expanded that need into publication from `dev`, a second linear Git history,
manifests, release-wave escrow, GitHub Release state, recovery coordination, six workflow jobs, and
full-repository certification. Replacing it with a manual one-job workflow removed that machinery but
mistakenly kept `dev` as the publication source.

Development work is not a release event. Focused pull requests coalesce through `dev`, where the
repository-coherence gate validates the integrated change. Promotion of that validated tree to `main`
is the publication boundary. Standard GitHub Actions, MSBuild, NBGV, and NuGet already express the
complete lifecycle.

## Decision checkpoint

**Application intent:** A maintainer merges focused development pull requests into `dev`, then promotes
the validated integrated tree to `main`; the resulting `main` commit publishes the repository's
independently versioned packages.

**Public expression:** GitHub Actions validates `pull_request` events targeting `dev` and runs the
single package release job on `push` to `main`. A `dev` commit cannot publish, and a `main` push does
not re-run the development gate.

**Guarantee/correction:** Only source present on `main` can receive the NuGet credential and reach
publication. Product-surface compilation selects exactly the supported package closure and each owner's declared
compatibility line; lower
maturity packages cannot be pushed by the job. A rejected credential, invalid version owner,
release-scope/artifact mismatch, pack error, or registry failure stops the job; after correcting the
cause, rerun the same `main` workflow run.

**Complete intent surface:** Open and merge focused pull requests to `dev`; when the aggregate is ready,
promote that exact tree to `main`. No manual release dispatch, branch selector, release branch, tag,
GitHub Release, package manifest, or Koan-specific coordinator participates.

**Public concepts:** GitHub's ordinary pull-request and push events, standard .NET pack/NuGet push,
and project-local NBGV `version.json` files. Each exists because it owns validation, publication, or
package identity respectively; no additional public release concept is required.

**Coalescence and ergonomics:** `dev` is the single development integration chokepoint. Its PR gate
validates each focused change before merge; promotion carries the validated aggregate to `main`, whose
push publishes afterward. The parallel `release-on-dev` path is deleted. The workflow names, branches,
logs, and rerun mechanics remain ordinary GitHub UI concepts.

## Independent version ownership

Every packable project owns a local NBGV `version.json`. The file declares major/minor compatibility
intent; package-affecting Git history supplies the patch. `PublicRelease=true` produces stable public
package identities.

Package paths that embed output from another project include that source directly in their NBGV
`pathFilters`. There is no parallel package-input map or synthetic version branch.

## Publication

`.github/workflows/release-on-main.yml` runs on every push to `main`. It contains one read-only
repository job and uses only the established `NUGET_API_KEY` for nuget.org publication.

The job:

1. checks out full Git history;
2. compiles the evaluated product surface, proving that supported claims and supported version owners are
   the same package set;
3. runs standard `dotnet pack` with `PublicRelease=true` for the solution and the one packable
   template project intentionally outside it; and
4. runs standard `dotnet nuget push --skip-duplicate` only for that guaranteed set, matching each
   artifact to its owner's declared compatibility line.

The release job does not run the repository test ratchet, create Git commits or branches, create tags
or GitHub Releases, stage escrow, or maintain recovery state. Validation belongs to the existing PR
gate before the change reaches `main`.

## Failure and rerun

NuGet identities are immutable. A rerun skips identities already published and pushes missing ones.
A conflicting identity, pack error, invalid version owner, rejected credential, or registry failure
stops the job and is corrected at its ordinary owner before rerunning.

## Consequences

- A pull request targeting `dev` validates but cannot publish.
- A `dev` commit cannot receive the NuGet credential.
- Promotion of the validated `dev` tree to `main` automatically invokes the one publication job.
- Packing may produce lower-maturity artifacts for build completeness, but publication selects only
  the product-surface package IDs validated at 0.20.
- Version ownership remains local, explicit, independently inspectable, and free to advance one
  breaking tier without teaching the publisher another hard-coded release line.
- Multi-package publication is not atomic; short-lived partial availability is accepted for the
  pre-release rather than simulated through a Koan-owned transaction system.
- Full certification remains an explicit milestone activity, not a publication prerequisite.

## Removed paths

- publication triggered from `dev` or development integration performed directly on `main`;
- manual branch-selected publication;
- `automation/package-lineage-dev` and synthetic lineage commits;
- release manifests, closure markers, and shared-input release maps;
- release-wave ZIPs, draft/immutable GitHub Release custody, tags, and completion receipts;
- prior-wave reconciliation and six-job permission choreography; and
- release workflow contract tests that restated YAML implementation details.

Historical detail remains available in Git history. Current guidance teaches the `dev` integration
path and `main` publication boundary in [NuGet publishing](../engineering/nuget-publishing.md).
