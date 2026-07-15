---
type: GUIDE
domain: framework
title: "R06-01 - Make Conformance Host Isolation Framework-Owned"
audience: [maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: consumer conformance host ownership and parallel execution
---

# R06-01 — Make conformance host isolation framework-owned

- Status: `in-progress`
- Depends on: R05
- Owner: Koan.Testing plus the existing Core host-context seam

## User-visible failure

`EntityConformanceSpecs<T>` promises that an application writes one Entity-specific subclass and
inherits meaningful tests, but its public remarks require:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

That restriction serializes unrelated application tests and asks the developer to understand Koan's
process-default host. It is infrastructure ceremony on the shortest testing path and allows a missed
attribute to become nondeterministic cross-host behavior.

## Smallest meaningful fix

Use Koan's existing host/context ownership primitives to bind each conformance specification's Entity
operations to the host it created. Keep the public Entity language and one-subclass test model. Remove
the assembly-wide serialization instruction only after a repository-owned concurrency probe proves
two independent specifications cannot see or dispose each other's state.

## In scope

- Map the current `IntegrationHost`, `AppHost`, and Entity ambient-context lifecycle.
- Add the smallest reusable testing-owned host scope at the correct existing layer.
- Keep the scope active for initialization, every inherited battery, and disposal without retaining a
  disposed provider.
- Run two real conformance specifications concurrently with distinct hosts, settings, partitions, and
  temporary roots.
- Remove the serialization requirement from the public XML documentation and conformance meta-test.
- Preserve explicit capability/store-unavailable skip reasons.

## Out of scope

- Removing every `DisableTestParallelization` attribute in the repository.
- Redesigning `Entity<T>`, `AppHost`, xUnit, providers, or all integration fixtures.
- Claiming parallel safety for external containers or tests with deliberately shared infrastructure.
- Changing public package maturity or publishing packages.

## Failure behavior

- An Entity operation without an owned live host fails with the existing corrective host-context
  error; it does not fall back to another concurrently running spec.
- Disposing one specification cannot clear or replace another specification's active context.
- A backing store known to be unavailable remains an explicit skip; host-ownership defects fail.

## Acceptance evidence

- A red/green concurrency test proves distinct Entity types and preferably the same closed Entity type
  can execute under two simultaneous conformance hosts without state leakage.
- The existing conformance meta-suite passes without assembly-wide parallelization disabled.
- Core host-lifecycle and Data.Core suites pass in proportion to the seam touched.
- `EntityConformanceSpecs<T>` XML documentation states the automatic ownership contract and remaining
  shared-infrastructure limits.
- Warning-as-error affected builds, strict docs, and `git diff --check` pass.

## Compatibility and rollback

This removes a consumer restriction without changing Entity source syntax. If the existing ambient
seam cannot safely express per-spec ownership, stop and record the architectural gap; do not add a
second service-locator path inside Koan.Testing.
