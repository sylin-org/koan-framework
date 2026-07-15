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
concurrency cancellation is not used. The complete version-lineage/build/plan/pack/publish operation
waits for every earlier `requested`, queued, waiting, pending, or running release event, so two pushes
cannot mint or publish out of order.

### 2. A release plan is compiled, not curated

`tools/Koan.Packaging` evaluates packability, package IDs, metadata, and project references through
MSBuild. Every packable project must own a project-local `version.json`.

`SourceCommit` records the `dev` event. The compiler applies the exact source-tree delta onto the
previous `automation/package-lineage-dev` tip and commits one linear `VersionCommit`. Linear
projection is load-bearing: importing source merge topology would advance unrelated NBGV heights.
The first lineage is an explicit all-owner bootstrap and requires its predecessor's package inventory
to remain evaluable by the pinned toolchain. Each lineage commit then stores every package owner's
exact minted identity. Later events compare that durable inventory with the checked-out
`VersionCommit`; they do not recalculate an old release with a newer SDK or NBGV tool.

A deliberate pre-1.0 minor or post-1.0 major advance is a breaking root. The evaluated package graph
derives its complete transitive reverse-dependent closure. After a provisional commit exposes actual
NBGV identities, only closure members still equal to their previous identity receive a deterministic
marker; the final commit is rechecked member by member.

Known repository/ancestor build policy and evaluated packed files outside an owner directory form a
conservative per-package input map. A change fans out to mapped package owners and uses the same
marker/identity proof; it cannot silently alter package bits beneath an unchanged identity.
Known paths remain mapped as tombstones, so their deletion is visible. The lineage does not yet retain
arbitrary external pack inputs discovered only by a prior evaluation; deleting or renaming one of
those paths is not certified until [PMC-017](../initiatives/koan-v1/POST-CYCLE-TODO.md#current-register)
is resolved or the path is promoted into the known map.

The online plan also reconciles a current identity missing from nuget.org. This makes a retry or an
initial migration converge without asking an operator to reconstruct the failed subset. The manifest
records source/version commits, breaking causes, dependency order, artifact names, and SHA-256 hashes.
Planning rejects any external lineage artifact that differs from state committed at the version SHA.

### 3. Bundles are ordinary independently versioned projects

`Sylin.Koan` and `Sylin.Koan.App` are dependency-only SDK projects. Their ProjectReferences are
converted by the same bounded compatibility-range target as every other package. Their NBGV path
filters include their declared composition, so a member change advances the affected bundle without
forcing one package version onto unrelated dependencies. Hand-tokenized `.nuspec` files are retired.

### 4. Verification precedes publication

The workflow runs the repository's complete green ratchet against the exact public-release commit,
proves the tracked tree stayed clean, then packs the planned set fail-fast with NuGet auditing enabled.
High and critical advisories fail the release. Every nupkg
is inspected for identity, version, description, license, README, repository metadata, symbol policy,
internal dependency closure, and recorded hash.

FirstUse and GoldenJourney are copied outside the repository and restore only PackageReferences from
the staged feed. They prove the shortest meaningful result and the cumulative persistence/jobs/agent/
operator journey without project references or repository build props making it pass accidentally.

### 5. Publication is exact, ordered, and resumable

Only exact artifacts that passed verification are publishable. Packages are pushed in project
dependency order. Existing nupkgs are reconciled while their symbol artifact is replayed, transient
pushes retry, registry visibility is confirmed before dependents continue, and version-keyed
`release-state.json` records progress. Re-running the same source resolves the same durable version
commit and converges instead of minting a replacement set.

nuget.org trusted publishing exchanges the workflow's GitHub OIDC identity for a short-lived
credential. A missing trusted-publishing owner is a hard failure. No long-lived API key is stored and
no release is reported successful when publication was skipped.

### 6. Evidence follows success

For a non-empty release set, the workflow creates `release/dev/<source-commit>` and a GitHub release
only after the entire release set is available. It creates or verifies the tag ref itself at
`VersionCommit` without force, then uses that existing exact tag; lineage, manifest, and final state
are attached. An empty set exits successfully without a tag or release.
Tags are audit evidence; they never drive package versions or publication.

## Consequences

### Positive

- Git contains all routine release intent; advancing `dev` needs no operator ceremony.
- Unchanged packages are neither rebuilt nor republished during steady-state operation; a generated
  marker means the dependency compatibility contract changed even though application source did not.
- An artifact newly pushed by the verified run is the artifact that passed metadata, closure,
  advisory, and clean-room behavior checks. An immutable identity already present in the registry is
  existence-reconciled; cross-run byte-hash retention is not claimed.
- Agents and reviewers can inspect one deterministic manifest instead of reverse-engineering logs.
- Interrupted same-source publication has an explicit, idempotent identity-reconciliation path.
  Cross-event artifact recovery after a later lineage advancement remains uncertified and is tracked
  as [PMC-016](../initiatives/koan-v1/POST-CYCLE-TODO.md#current-register).

### Trade-offs and boundaries

- The first run may reconcile an accumulated set of unpublished current identities and will be much
  larger than steady-state releases.
- nuget.org cannot provide a multi-package transaction. Dependency ordering, visibility waits,
  immutable identities, and resumable convergence provide atomic *release behavior*, not registry
  rollback.
- A deliberate major/minor compatibility decision still belongs in `version.json`; automation does
  not infer semantic breaking intent.
- Package deletion/rename, reserved lineage paths on `dev`, non-forward source history, and manual
  lineage divergence are initially unsupported and fail before packing.
- The dedicated version branch is an automation projection, not an application-development branch.
  Source ancestry is recorded explicitly rather than merged into its topology.
- Same-source rerun replays selected symbols/state from the same version commit. If a later lineage
  event has already advanced after a partial symbol publication, automatic cross-event artifact
  recovery is not yet certified and the missing symbol may remain undetected by that later event. The
  first trusted publication remains gated by
  [PMC-016](../initiatives/koan-v1/POST-CYCLE-TODO.md#current-register).

## Operational contract

The one-time setup is a nuget.org trusted-publishing policy for this repository/workflow and the
`NUGET_USER` repository variable. After that, the normal release instruction is simply:

```text
push or merge a package-affecting commit into dev
```

Failure is red and actionable. Fix the source or infrastructure problem and re-run the failed event;
do not hand-pack a replacement set or create a release tag.
