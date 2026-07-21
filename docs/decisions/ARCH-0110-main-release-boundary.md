# ARCH-0110 — Main-boundary independently versioned package releases

**Status**: Accepted
**Date**: 2026-07-14
**Amended**: 2026-07-20 — restored `main` as the validation/publication boundary
**Deciders**: Framework maintainer
**Scope**: Package versioning and NuGet publication

## Context

Koan packages need independent, correct versions and a reliable way to publish them. An automatic
release compiler expanded that need into publication from `dev`, a second linear Git history,
manifests, release-wave escrow, GitHub Release state, recovery coordination, six workflow jobs, and
full-repository certification. Replacing it with a manual one-job workflow removed that machinery but
mistakenly kept `dev` as the publication source.

Development work is not a release event. A pull request to `main` is the review and validation
boundary; its resulting `main` commit is the publication boundary. Standard GitHub Actions, MSBuild,
NBGV, and NuGet already express the complete lifecycle.

## Decision checkpoint

**Application intent:** A maintainer merges a pull request into `main` or commits directly to `main`;
the resulting `main` commit publishes the repository's independently versioned packages.

**Public expression:** GitHub Actions validates `pull_request` events targeting `main` and runs the
single package release job on `push` to `main`. Commits and pull requests targeting `dev` trigger
neither path.

**Guarantee/correction:** Only source present on `main` can receive the NuGet credential and reach
publication. A rejected credential, invalid version owner, pack error, or registry failure stops the
job; after correcting the cause, rerun the same `main` workflow run.

**Complete intent surface:** Open and merge a pull request to `main`, or deliberately commit to
`main`. No manual release dispatch, branch selector, release branch, tag, GitHub Release, package
manifest, or Koan-specific coordinator participates.

**Public concepts:** GitHub's ordinary pull-request and push events, standard .NET pack/NuGet push,
and project-local NBGV `version.json` files. Each exists because it owns validation, publication, or
package identity respectively; no additional public release concept is required.

**Coalescence and ergonomics:** GitHub Actions owns the single integration chokepoint. The PR gate
validates before merge and the `main` push publishes afterward. The earlier `release-on-dev` path is
deleted. The human expression is one familiar action—merge to `main`—and the workflow name, branch,
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
2. verifies evaluated packable projects have local version owners;
3. runs standard `dotnet pack` with `PublicRelease=true` for the solution and the one packable
   template project intentionally outside it; and
4. runs standard `dotnet nuget push --skip-duplicate` for the resulting packages.

The release job does not run the repository test ratchet, create Git commits or branches, create tags
or GitHub Releases, stage escrow, or maintain recovery state. Validation belongs to the existing PR
gate before the change reaches `main`.

## Failure and rerun

NuGet identities are immutable. A rerun skips identities already published and pushes missing ones.
A conflicting identity, pack error, invalid version owner, rejected credential, or registry failure
stops the job and is corrected at its ordinary owner before rerunning.

## Consequences

- A `dev` commit causes no validation or publication workflow activity.
- A pull request targeting `main` validates but cannot publish.
- The resulting `main` commit automatically invokes the one publication job.
- Version ownership remains local, explicit, and independently inspectable.
- Multi-package publication is not atomic; short-lived partial availability is accepted for the
  pre-release rather than simulated through a Koan-owned transaction system.
- Full certification remains an explicit milestone activity, not a publication prerequisite.

## Removed paths

- publication or validation triggered from `dev`;
- manual branch-selected publication;
- `automation/package-lineage-dev` and synthetic lineage commits;
- release manifests, closure markers, and shared-input release maps;
- release-wave ZIPs, draft/immutable GitHub Release custody, tags, and completion receipts;
- prior-wave reconciliation and six-job permission choreography; and
- release workflow contract tests that restated YAML implementation details.

Historical detail remains available in Git history. Current guidance teaches only the `main`
integration path in [NuGet publishing](../engineering/nuget-publishing.md).
