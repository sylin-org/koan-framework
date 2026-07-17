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
Lineage schema 3 persists the normalized map for every owner. Impact is derived from the prior and
current maps together, so add, change, deletion, and rename remain package-local even when a path has
disappeared from current evaluation. Missing or noncanonical maps fail closed rather than being
inferred from a newer toolchain.

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

### 5. Exact binary escrow precedes publication

For a non-empty manifest, verification produces one
`release-wave-<full-VersionCommit>.zip`. Its exact entry set is lineage, manifest, all selected
nupkg/snupkg artifacts, and the separate FirstUse and GoldenJourney evidence. For one fixed set of
exact inputs, canonical entry names, order, timestamps, lengths, and hashes give the ZIP deterministic
encoding. A standalone `release-wave.json` binds the ZIP, its inner lineage/manifest hashes, package
count, source/version commits, and canonical tag. Recovery preserves that escrow; it does not assume a
later build from the same commit will recreate identical evidence metadata.

The workflow creates one draft GitHub Release, uploads the ZIP first, and uploads the marker last. The
uploaded marker is prepared authority: it is never replaced or rebuilt. A missing or markerless draft
may be reset only while no selected nupkg is public. Once any selected identity is public, missing
prepared escrow is a hard block.

State is derived from that one Release:

- `missing`: no Release uses the exact tag;
- `staging`: a draft has no uploaded marker;
- `prepared`: the draft's exact marker and bundle validate;
- `published`: the same Release has the exact completion receipt, full-commit tag, and immutable bit.

Recovery needs no secondary database or operator-maintained archive. The only in-place repair is an
exact completion asset left by GitHub in `starter` state; uploaded assets are never clobbered.

### 6. Publication is exact, ordered, and recoverable across events

Promotion downloads and revalidates the original escrow. Packages are processed in manifest
dependency order. A missing nupkg is pushed; every required exact snupkg is replayed using
duplicate-safe semantics on each nonterminal attempt; every nupkg must become visible. One
deterministic `release-completion.json` then binds the marker, bundle, lineage, manifest, and
package/symbol hashes.

Promotion re-reads the complete draft immediately before the final boundary, creates or verifies
`release/dev/<full-VersionCommit>` at the exact `VersionCommit` without force, and publishes that same
draft. Terminal success requires GitHub to report the Release immutable. The completion receipt is
historical custody evidence; later registry outage or unlisting does not reopen it.

Before compiling the current source wave, automation inspects and converges the prior durable
`VersionCommit`. A package push followed by symbol, receipt, visibility, or response failure is
therefore completed from the original prior escrow even after a later `dev` event arrives. It is never
rebuilt under an already-public identity.

nuget.org trusted publishing exchanges GitHub OIDC identity for a short-lived credential only after
prepared escrow is rechecked. A missing trusted-publishing owner is fatal. No long-lived API key is
stored. An empty manifest advances lineage as required but creates no bundle, draft Release, tag, or
receipt.

### 7. Proof, staging, and promotion have separate authority

The workflow has six permission boundaries: read-only `prepare_prior`, write-only `stage_prior`,
write-plus-OIDC `promote_prior`, read-only `prove_current`, write-only `stage_current`, and
write-plus-OIDC `promote_current`.

Build, test, pack, and clean-room proof never receive contents-write or OIDC permission. Staging never
receives a NuGet credential. Promotion consumes the already-built coordinator and exact handoff; it
does not restore, compile, test, or rebuild source.

## Consequences

### Positive

- Git contains all routine release intent; advancing `dev` needs no operator ceremony.
- Unchanged packages are neither rebuilt nor republished during steady-state operation; a generated
  marker means the dependency compatibility contract changed even though application source did not.
- An artifact newly pushed by the verified run is the artifact that passed metadata, closure,
  advisory, and clean-room behavior checks. Original verified bytes survive in draft/immutable Release
  escrow until nupkg, symbols, receipt, and tag converge.
- Agents and reviewers can inspect one canonical manifest, bundle marker, and completion receipt
  instead of reverse-engineering logs or mutable recovery checklists.
- Interrupted publication is recovered automatically before the next source wave is compiled; an
  operator never selects the failed package, old commit, or artifact path.
- Expensive proof and credentialed mutation no longer share one job authority.

### Trade-offs and boundaries

- The first run may reconcile an accumulated set of unpublished current identities and will be much
  larger than steady-state releases.
- nuget.org cannot provide a multi-package transaction. Dependency ordering, visibility waits,
  immutable identities, exact escrow, and resumable convergence provide atomic *release behavior*,
  not registry rollback.
- A deliberate major/minor compatibility decision still belongs in `version.json`; automation does
  not infer semantic breaking intent.
- Package-owner rename, reserved lineage paths on `dev`, non-forward source history, and manual
  lineage divergence are unsupported and fail before packing. Package deletion is explicit retirement;
  mapped external input add/change/delete/rename remains owner-local.
- The dedicated version branch is an automation projection, not an application-development branch.
  Source ancestry is recorded explicitly rather than merged into its topology.
- GitHub's workflow token cannot read the administration-level immutable-Releases setting. Enabling
  it is a one-time repository prerequisite verified before the separately authorized first public
  wave; terminal publication still fails closed unless the resulting Release reports immutable.
- Completion schema 1 is durable history. Any future receipt evolution must add schema-dispatched
  readers and preserve a frozen schema-1 fixture before changing canonical bytes.
- The implementation and adversarial simulations are complete locally, but no real NuGet publication
  or immutable GitHub Release was observed in this implementation cycle.

## Operational contract

The one-time setup is a nuget.org trusted-publishing policy for this repository/workflow, the
`NUGET_USER` repository variable, immutable GitHub Releases, and the repository's protected release
trust boundary. After the separately authorized first public observation, the normal release
instruction is simply:

```text
push or merge a package-affecting commit into dev
```

Failure is red and actionable. Fix the source or infrastructure problem and re-run the failed event;
the next event also reconciles any incomplete prior wave before compiling current work. Do not
hand-pack a replacement set, edit escrow assets, or create/move the release tag.
