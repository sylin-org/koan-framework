# ARCH-0110 — Git-driven independent package releases from `dev`

**Status**: Accepted
**Date**: 2026-07-14
**Deciders**: Framework maintainer
**Scope**: Package selection, verification, publication, recovery, and release evidence
**Related / supersedes**: completes the release-pipeline portion of **ARCH-0085** and supersedes
**BUILD-0072**, the legacy `main`/tag workflows, hand-authored package lists, and tokenized nuspec
bundles.

---

## Context

Koan packages version independently, but the release process did not. It packed the repository as a
batch, tolerated individual pack failures, reused one version for unrelated bundle dependencies,
disabled advisory enforcement, tagged before publication, and could finish successfully without a
publishing credential. That made Git an incomplete statement of intent and left consumers to discover
incoherent package sets.

The desired operator contract is smaller: advancing `dev` is the complete release decision. A direct
push and a merge are equivalent. Maintainers choose semantic major/minor intent in the affected
package's `version.json`; NBGV and repository history own the patch. There is no release form, package
checkbox list, version calculator, skip marker, or routine credential handling.

## Decision

### 1. Every advancement of `dev` is a release event

`.github/workflows/release-on-dev.yml` runs for every push to `dev`. Each event is retained; GitHub
concurrency cancellation is not used. Publication waits for earlier active release events so two
pushes cannot publish out of order.

### 2. A release plan is compiled, not curated

`tools/Koan.Packaging` evaluates packability, package IDs, metadata, and project references through
MSBuild. Every packable project must own a project-local `version.json`. For the event's `before` and
`after` commits, the compiler asks NBGV for each public package version and selects identities whose
version changed.

The online plan also reconciles a current identity missing from nuget.org. This makes a retry or an
initial migration converge without asking an operator to reconstruct the failed subset. The manifest
records commits, reason, dependency order, artifact names, and SHA-256 hashes.

### 3. Bundles are ordinary independently versioned projects

`Sylin.Koan` and `Sylin.Koan.App` are dependency-only SDK projects. Their ProjectReferences are
converted by the same bounded compatibility-range target as every other package. Their NBGV path
filters include their declared composition, so a member change advances the affected bundle without
forcing one package version onto unrelated dependencies. Hand-tokenized `.nuspec` files are retired.

### 4. Verification precedes publication

The workflow builds and tests the release commit, then packs the exact planned set fail-fast with
public versions and NuGet auditing enabled. High and critical advisories fail the release. Every nupkg
is inspected for identity, version, description, license, README, repository metadata, symbol policy,
internal dependency closure, and recorded hash.

A copied application outside the repository then restores only PackageReferences from the staged
feed, builds, starts, reports health, and performs SQLite-backed Entity create/read/delete through an
`EntityController<T>`. This is the minimum meaningful consumer proof; project references and
repository build props cannot make it pass accidentally.

### 5. Publication is exact, ordered, and resumable

Only artifacts uploaded by the verification job are publishable. Packages are pushed in project
dependency order. Existing identities are reconciled, transient pushes retry, registry visibility is
confirmed before dependents continue, and `release-state.json` records progress. Re-running the same
workflow converges instead of minting or selecting a different set.

nuget.org trusted publishing exchanges the workflow's GitHub OIDC identity for a short-lived
credential. A missing trusted-publishing owner is a hard failure. No long-lived API key is stored and
no release is reported successful when publication was skipped.

### 6. Evidence follows success

The workflow creates `release/dev/<commit>` and a GitHub release only after the entire release set is
available. The verified manifest and final state are attached. Tags are audit evidence; they never
drive package versions or publication.

## Consequences

### Positive

- Git contains all routine release intent; advancing `dev` needs no operator ceremony.
- Unchanged packages are neither rebuilt nor republished during steady-state operation.
- The artifact a consumer restores is the artifact that passed metadata, closure, advisory, and
  clean-room behavior checks.
- Agents and reviewers can inspect one deterministic manifest instead of reverse-engineering logs.
- Interrupted publication has an explicit, idempotent recovery path.

### Trade-offs and boundaries

- The first run may reconcile an accumulated set of unpublished current identities and will be much
  larger than steady-state releases.
- nuget.org cannot provide a multi-package transaction. Dependency ordering, visibility waits,
  immutable identities, and resumable convergence provide atomic *release behavior*, not registry
  rollback.
- A deliberate major/minor compatibility decision still belongs in `version.json`; automation does
  not infer semantic breaking intent.
- ARCH-0085's dependent-closure rule for a breaking compatibility-band change remains a separate
  version-graph invariant. This release compiler does not weaken bounded ranges or claim that a
  registry supports transactional rollback.

## Operational contract

The one-time setup is a nuget.org trusted-publishing policy for this repository/workflow and the
`NUGET_USER` repository variable. After that, the normal release instruction is simply:

```text
push or merge a package-affecting commit into dev
```

Failure is red and actionable. Fix the source or infrastructure problem and re-run the failed event;
do not hand-pack a replacement set or create a release tag.
