---
type: SPEC
domain: framework
title: "R08 - Make Koan V1 Responsibly Releasable"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: in-progress
  scope: T7 entry assessment and dependency-ordered release-readiness plan
---

# R08 — Make Koan V1 responsibly releasable

- Tranche: `T7 — V1 release readiness`
- Status: `in-progress`
- Depends on: R07
- Unlocks: one trustworthy public package generation, coherent package-first first use, and the
  explicit V1 decision
- Owner: release-wave truth, public product boundary, upgrade contract, and release evidence

## Meaningful outcome

A developer starts from NuGet or `dotnet new`, writes business code, and reaches the same meaningful
result already proved from source. Advancing `dev` automatically mints only the affected package
identities, proves their complete closure, reconciles interrupted publication, and exposes enough
evidence for an agent or reviewer to explain exactly what shipped. Routine release work requires no
version calculation, package checklist, recovery reconstruction, or operator-selected release set.

## Why now

R07 closes the semantic capability ring. The remaining mismatch is promotion: local packages and
clean-room applications are substantially stronger than the public installation surface. Publishing
before that boundary is trustworthy would turn good framework code into a poor first-use experience
and make package availability look like a support promise.

## T7 entry assessment

The assessment was performed read-only on 2026-07-16. No package, branch, tag, release, or remote
configuration changed.

### Strong base

- 112 packable projects have independent local version ownership and evaluated dependency order.
- The release compiler derives exact Git identities, breaking reverse closures, bounded dependency
  ranges, symbols, hashes, and package-only FirstUse/GoldenJourney evidence.
- The full public-release ratchet has passed once from a clean exact version commit.
- The capability ledger correctly promotes no surface to `supported-*` while public installation and
  compatibility remain unproved.

### Red before the first trusted publication

- Public `Sylin.Koan` and `Sylin.Koan.App` stop at `0.5.2` and their dependency ranges cannot resolve
  against the current public leaf packages. Public Core reaches `0.17.0`; Communication and Templates
  have no public version. NuGet is not a coherent first-use path.
- The new `release-on-dev` workflow exists only in the local 56-commit advancement and has never been
  observed. The required non-secret `NUGET_USER` repository variable is absent, so the first run would
  currently fail.
- Canonical source intent is now enforced across inventory and historical lineage reads, and
  `src/Koan.AI/version.json` is corrected to `0.18`. Git remains the sole patch-identity owner.
- The console template pins `Sylin.Koan` to `0.17.*` although the current bundle intent is `0.18`.
  Neither template is installed, instantiated, restored, built, and run by the clean-room gate.
- Cross-event artifact/symbol recovery remains open as PMC-016. PMC-017 is closed by lineage schema
  3, which retains normalized per-owner input maps and compares prior plus current ownership.
- Build, test, and repository-controlled scripts currently run in the same job granted publication
  permissions. Verification and promotion do not yet have a least-privilege boundary.
- Connector-wide secret redaction remains unproved (PMC-019), so the first all-owner bootstrap is not
  safe to interpret as a production-provider promotion.
- There is no current public upgrade rehearsal, support window, rollback contract, or single
  evidence-derived provider/package maturity matrix. Stale public guides can still outrank the
  conservative capability ledger in navigation.

## Decisions

### DECIDED

- A `dev` advancement remains the complete routine release intent. Commit-message vocabulary,
  package selection, patch numbers, and operator-authored manifests do not participate.
- One durable release-wave owner must cover source input history, exact package/symbol artifacts,
  publication stages, and recovery. Parallel lineage, artifact, and recovery ledgers are rejected.
- A later event cannot hide, skip, or supersede an incomplete earlier wave. Earlier exact identities
  reconcile before later publication advances.
- Registry visibility proves only the package state it can actually observe. It does not prove symbol
  publication or authorize rebuilding different bits beneath an existing identity.
- Verification runs without publish-grade credentials. A narrow promotion boundary consumes already
  verified immutable evidence.
- Package availability and product support are distinct. Foundation, supported extensions,
  experimental packages, and retired surfaces must be mechanically or canonically distinguishable.
- The first trusted public wave is an explicit operator-authorized observation. This initiative may
  prepare it but may not push, publish, tag, or release implicitly.

### DEFAULT

- Prefer extending the existing lineage state and release manifest over adding a new service or
  database.
- Prefer generated exact template dependency identities over hand-maintained shared minor pins or
  floating-to-any-version restores.
- Treat one-time trusted-publishing setup as deployment configuration; after setup, normal release
  events require no operator input.

### OPEN

- The final V1 compatibility/support window and which pre-1.0 removal rule supersedes conflicting
  historical guidance.

### PROVED

- Repacking the same project twice at the same exact commit produced different nupkg and snupkg
  SHA-256 hashes. Recovery therefore retains and replays the originally verified binaries; a later
  build is never accepted as a substitute beneath the same package identity.

## Dependency-ordered execution

1. **R08-01 — one durable release wave.** Close PMC-016/017 under the existing compiler owner,
   normalize current source intent, and separate unprivileged proof from narrow promotion.
2. **Safe public surface.** Close or fail-closed gate PMC-019 and publish one evidence-backed package,
   provider, platform, and maturity boundary instead of competing catalogs.
3. **Package-first and upgrade proof.** Generate correct template identities; install, instantiate,
   restore, build, and run both templates; rehearse a public-to-candidate upgrade and rollback; publish
   the compatibility/support contract.
4. **Observed release and decision.** With separate authorization, configure trusted publishing,
   observe one automatic public wave and NuGet-only clean installs, then ask the architect for the
   explicit V1 decision.

Only the current slice receives a child card. Later steps remain outcomes until their prerequisite
evidence is green.

## Verification

- Focused compiler, Git, workflow-contract, package, template, and upgrade proofs per child.
- The complete public-release ratchet only at an explicit release-certification boundary, not after
  every implementation slice.
- Official NuGet package-content/registration evidence for public state and isolated NuGet-only
  consumer applications for candidate state.
- Documentation lint, changed executable examples, diff, and privacy gates for every child.

## Stop conditions

- Stop if a repair requires routine operator selection or reconstructing package intent outside Git.
- Stop if recovery can reuse an identity for different bits or infer symbol success from nupkg
  visibility.
- Stop if a second release-history authority starts competing with the existing lineage compiler.
- Stop before any real publication, push, tag, release, or remote configuration mutation without a
  separate explicit operation.
