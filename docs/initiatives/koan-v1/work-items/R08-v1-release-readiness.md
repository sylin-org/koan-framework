---
type: SPEC
domain: framework
title: "R08 - Make Koan V1 Responsibly Releasable"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: R08-01 through R08-04 passed locally; R08-05 prepared; public observation and upgrade proof remain
---

# R08 — Make Koan V1 responsibly releasable

- Tranche: `T7B — V1 release readiness`
- Status: `pending R10 sample graduation and explicit remote authorization`
- Depends on: passed R09; R08-01 preserves its completed release-wave baseline
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

## Interruption and resume boundary

R08-01 is complete locally: Git-owned intent, historical inputs, exact release-wave escrow, resumable
cross-event promotion, and six least-privilege workflow boundaries are implemented. No real package,
tag, or Release was published.

The accepted [R09 Semantic Composition Kernel](R09-semantic-composition-kernel.md) was a newly discovered
prerequisite for the remaining product release. R09 is now passed: runtime decision owners, typed
contribution/election mechanics, truthful projections, contract boundaries, and one module lifecycle have
converged without redesigning the completed release mechanism. R08 resumes at **Safe public surface** below.

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
- PMC-016 and PMC-017 are closed locally. Lineage schema 3 retains prior/current input ownership; one
  exact hash-bound GitHub Release escrow supplies resumable package and symbol custody.
- The workflow now has six permission boundaries: proof is read-only, staging has contents write but no
  OIDC credential, and promotion consumes exact prepared evidence with narrow write plus OIDC authority.
- Connector-owned configuration, discovery, health-selection, and startup telemetry now crosses one
  credential-safe structured boundary; PMC-019 is resolved by R08-02. This does not yet promote any provider.
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

1. **R08-01 — one durable release wave — passed locally.** PMC-016/017, canonical source intent,
   exact escrow/recovery, and the six-job permission split are complete. Public observation is not.
2. **R09 prerequisite — passed.** Compile the Semantic Application Model and converge runtime decision
   owners without changing the completed release mechanism.
3. **R08-02 — safe connector telemetry — passed.** PMC-019 is resolved through one shared redaction sink,
   concern-owned logging chokepoints, runtime mutation proof, and a repository bypass gate.
4. **R08-03 — canonical product surface — passed.** One fail-closed compiler joins standard evaluated
   package facts to irreducible maturity/evidence claims and generates human/machine projections. The
   obsolete package-kind taxonomy and manual catalog are deleted; PMC-010 is resolved.
5. **R08-04 — package-first templates — passed locally.** Release-derived compatibility bands, suppressed
   impact edges, direct-pack refusal, 108 exact candidate packages, both generated template shapes, and
   package-only FirstUse/GoldenJourney are proved with no user version input.
6. **[R10 — golden sample portfolio](R10-golden-samples.md) — in progress.** Graduate every maintained
   sample as a current, executable Koan example; GardenCoop establishes the evidence template. R08-05 waits so
   the first public wave does not publish a framework whose own curriculum teaches legacy structure.
7. **[R08-05 — initial coherent public observation](r08/R08-05-initial-public-observation.md) — pending
   explicit authorization.** One readiness/observation contract covers trusted publishing, immutable evidence
   custody, exact package-only startup truth, NuGet-only installs, and fail-closed recovery. Preparation performs
   no remote mutation.
8. **Real upgrade/rollback proof.** Stage a later candidate, upgrade an application created from the coherent
   public baseline, restore its prior project/lock state, and publish the compatibility/support contract.
9. **Explicit V1 decision.** Ask the architect only after observed public and upgrade/rollback evidence agree.

Each active execution slice receives one bounded child card after focused discovery identifies its smallest
decision owners. Open the upgrade/rollback child only after R08-05 establishes a coherent public baseline.

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
