# ARCH-0110 — Explicit independently versioned package releases

**Status**: Accepted
**Date**: 2026-07-14
**Replaced**: 2026-07-20 — the automatic release compiler and release-wave protocol were removed
**Deciders**: Framework maintainer
**Scope**: Package versioning and NuGet publication

## Context

Koan packages need independent, correct versions and a reliable way to publish them. The first
implementation expanded that need into automatic publication on every `dev` push, a second linear Git
history, manifests, release-wave escrow, GitHub Release state, recovery coordination, six workflow
jobs, and full-repository certification.

Repeated pre-release attempts showed that this machinery consumed substantial development time while
failing in its own orchestration layers. No 0.20 package was published through it. The framework does
not need to implement a package registry transaction protocol.

## Decision

### Independent version ownership remains

Every packable project owns a local NBGV `version.json`. The file declares major/minor compatibility
intent; package-affecting Git history supplies the patch. `PublicRelease=true` produces stable public
package identities.

Package paths that embed output from another project include that source directly in their NBGV
`pathFilters`. There is no parallel package-input map or synthetic version branch.

### Publication is explicit

`.github/workflows/release-packages.yml` is manually dispatched from `dev`. It contains one read-only
repository job and uses only the established `NUGET_API_KEY` for nuget.org publication.

The job:

1. checks out full Git history;
2. verifies evaluated packable projects have local version owners;
3. runs standard `dotnet pack` with `PublicRelease=true` for the solution and the one packable
   template project intentionally outside it; and
4. runs standard `dotnet nuget push --skip-duplicate` for the resulting packages.

Development pushes do not publish. The release job does not run the repository test ratchet, create
Git commits or branches, create tags or GitHub Releases, stage escrow, or maintain recovery state.

### Failure and rerun

NuGet identities are immutable. A rerun skips identities already published and pushes missing ones.
A conflicting identity, pack error, invalid version owner, rejected credential, or registry failure
stops the job and is corrected at its ordinary owner before rerunning.

## Consequences

- The complete operator action is one manual workflow dispatch from `dev`.
- Version ownership remains local, explicit, and independently inspectable.
- Release implementation uses standard .NET/NuGet concepts and one credential.
- Multi-package publication is not atomic; short-lived partial availability is accepted for the
  pre-release rather than simulated through a Koan-owned transaction system.
- Full certification remains an explicit milestone activity, not a prerequisite repeated on every
  release correction.

## Removed paths

- automatic release on every `dev` push;
- `automation/package-lineage-dev` and synthetic lineage commits;
- release manifests, closure markers, and shared-input release maps;
- release-wave ZIPs, draft/immutable GitHub Release custody, tags, and completion receipts;
- prior-wave reconciliation and six-job permission choreography;
- package-only FirstUse/GoldenJourney/template proof inside publication; and
- release workflow contract tests that restated YAML implementation details.

Historical detail remains available in Git history. Current guidance teaches only the explicit path
in [NuGet publishing](../engineering/nuget-publishing.md).
