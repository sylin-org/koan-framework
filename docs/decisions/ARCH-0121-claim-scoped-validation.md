---
id: ARCH-0121
slug: claim-scoped-validation
domain: Architecture
status: Accepted
date: 2026-07-22
title: Claim-scoped validation and a cheap main boundary
related:
  - ARCH-0109
  - ARCH-0110
  - ARCH-0120
---

# ARCH-0121: Claim-scoped validation and a cheap main boundary

## Outcome

Koan validates the claim being made, at the owner that can prove it. An ordinary main pull request
proves repository coherence; affected projects prove changed behavior; the main publisher proves
package construction and delivery; the complete green ratchet proves the whole framework only when a
maintainer explicitly requests that portfolio-level claim.

Merge, package promotion, NBGV version calculation, and NuGet publication are not certification
events. They never imply a complete repository test wave.

## Context

The accepted main-boundary publisher is intentionally simple: derive independent package versions
from Git, pack, and push the supported set. The first R13 promotion nevertheless inherited the entire
107-project green ratchet, a permanent provider-specific PR workflow, a duplicate surface workflow,
and repeated package-consumer rehearsal. A successful exact run took about sixteen minutes; repairing
failures from its unrelated provider and evidence owners consumed days of framework time.

That topology conflated four different questions:

1. Does the changed behavior work?
2. Is the repository coherent enough to merge?
3. Can the selected packages be built and published?
4. Is the entire framework portfolio certified at this instant?

Only the fourth question needs complete certification. Paying for it on every merge did not make the
other three answers more precise.

## Decision checkpoint

**Application intent:** A maintainer can deliver an affected framework or package change promptly,
with evidence proportional to that change and without waiting for unrelated providers.

**Public expression:** Develop the change and run its owning tests. Open a pull request to `main`; one
cheap coherence job checks product/API agreement, a Release build, lockfiles, and structural
documentation/tooling projections. Merge; one main-push job selects, packs, and pushes supported
packages. Invoke the complete green ratchet only for an explicitly named whole-framework milestone.

**Guarantee/correction:** The PR rejects invalid support/version/dependency truth, API-baseline
policy, compilation, lockfile, or documentation/tooling projections. The publisher rejects invalid
selection, pack output, credentials, or registry delivery. Behavioral failure stays at the affected
test owner. No green PR or publication falsely claims that every provider was retested.

**Complete intent surface:** Existing project/family tests, `product-surface --check`, `api-baselines`,
`green-ratchet.ps1 -SkipTests`, the surface-ledger parser, standard `dotnet pack`, NBGV, and
`dotnet nuget push --skip-duplicate`. No validation manifest, changed-project planner, provider lane
registry, promotion coordinator, or permanent per-promotion workflow participates.

**Public concepts:** No application or framework API changes. Maintainers distinguish change evidence,
PR coherence, publication, and optional portfolio certification because each answers a different
operational question.

**Coalescence:** One PR workflow owns coherence and one main-push workflow owns publication. The tiny
surface parser is absorbed into the PR job. Provider-specific promotion evidence returns to its family
test owner. The complete ratchet remains a local/milestone tool. The permanent PR-native and standalone
surface workflows are deleted.

**Ergonomics:** The normal remote flow has two visible jobs: check, then publish. Contributors run the
test project they changed and record focused evidence in the PR. No package author learns a release
subsystem or waits for an unrelated database, vector store, model runtime, or service.

## Validation boundaries

### Change evidence

Run the smallest owning unit/integration project and the bounded capability matrix required by the
changed guarantee. A provider change includes its real container/runtime/protocol boundary. Review
requires the PR to name the commands and outcomes; CI does not infer an affected-project graph.

### Main PR coherence

The single PR job runs:

1. product-surface compile and generated-drift rejection;
2. supported API-baseline policy;
3. repository tool restore and one Release solution build;
4. composition-lockfile comparison;
5. docs, public-docs, changed code-example, skill, and blueprint lint; and
6. the surface-ledger parser.

It runs no test suite. The expected wall time is bounded by one solution build rather than provider
startup or package-consumer restore.

### Package promotion

A first public promotion still needs an honest user guarantee, affected family semantics, and a real
provider boundary where applicable. Packing is always required. A clean external consumer is required
only for first publication, changed package/dependency/activation shape, or when no prior artifact-use
proof exists. It is evidence gathered for that decision, not a permanent universal merge job.

### Main publication

The main-push job resolves the supported set, validates immutable API floors, packs, matches exactly
one artifact per selected identity, and pushes with `--skip-duplicate`. It runs no tests or ratchet.
Partial immutable publication is recovered by rerunning the same job.

### Portfolio certification

The complete green ratchet remains available when the maintainer explicitly asks whether the whole
framework is green. Its result is milestone evidence only. It is not required by ordinary development,
merge, promotion, version calculation, or publication.

## Evidence

On the accepting development host, the exact replacement flow—product-surface check, API-baseline
guard, `green-ratchet.ps1 -Configuration Release -Base origin/main -SkipTests`, and surface parser—
passed in 86.6 seconds. It included the full Release solution build and every declared coherence check.
The preceding connected complete gate took about 16 minutes 22 seconds. The reduction removes work
from the normal path rather than weakening or filtering the complete certification tool.

## Consequences

### Positive

- Normal integration returns to a predictable build-scale cost.
- Provider failures are investigated only when that provider's guarantee is in scope.
- Publication remains simple and independently versioned.
- Focused evidence becomes more meaningful because each command maps to a changed claim.
- Framework work no longer stops for portfolio certification by default.

### Tradeoffs

- CI does not automatically prove that a contributor chose every affected test; review must inspect
  the recorded focused evidence.
- Direct commits to `main` bypass PR coherence but still fail closed at pack/publication; direct main
  commits are therefore deliberate maintainer actions.
- Whole-framework regressions may be found later at an explicit milestone rather than on every merge.

## Supersession

This decision preserves ARCH-0110's main-boundary publisher and ARCH-0120's value-led, proportional
promotion model. It supersedes only language that treated publication as a reason to run the complete
ratchet or made a clean consumer an unconditional repeated requirement. Accepted ADR files remain
unchanged; this record owns the corrected validation boundary.
