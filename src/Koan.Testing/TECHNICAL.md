---
uid: reference.modules.koan.testing
title: Koan.Testing - Technical Reference
description: Entity conformance batteries, host ownership, capability gating, and failure behavior.
since: 0.17.0
packages: [Sylin.Koan.Testing]
source: src/Koan.Testing/
---

## Contract

`Koan.Testing` is the application-facing Entity conformance kit. A consumer subclasses
`EntityConformanceSpecs<TEntity>`, supplies `NewValid()`, and receives capability-aware integration
tests without recreating host, storage, partition, or reference-oracle wiring.

The package depends on the xUnit-free `Koan.Testing.Hosting` host, Data.Core's Entity grammar, and
Data.Abstractions' filter oracle. Container fixtures remain in `Koan.Testing.Containers` so application
tests do not acquire Testcontainers dependencies merely by referencing the conformance kit.

## Lifecycle

xUnit creates one conformance instance for each inherited battery. `InitializeAsync`:

1. creates a unique temporary root and Entity partition;
2. seeds Test-environment JSON and SQLite settings;
3. applies the consumer's `Configure` overrides;
4. starts a real `KoanIntegrationHost` with `AddKoan()`;
5. probes the selected Entity adapter for reachability.

`EntityConformanceSpecs` does not attach or clear `AppHost.Current` directly. The generic host's
`AppHostBinderHostedService` owns the process-default provider with a compare-and-release lease.
`DisposeAsync` delegates to integration-host disposal, then removes the temporary root best-effort.
Stopping an older overlapping host cannot clear a newer owner.

Conformance projects still disable test parallelization. Owner-safe teardown prevents cross-host
clearing; it does not give simultaneous static Entity operations distinct process-default providers.

## Battery behavior

### Always applicable

- `RoundTrip_persists_and_reads_back_by_id` verifies id assignment and read-after-write.
- `Paging_returns_every_row_exactly_once` inserts 23 valid instances and reads pages of 10 until the
  short final page.
- `Partition_isolates_writes` verifies visibility in the owning partition and absence in another.

### Capability or trait gated

- `QueryPushdown_agrees_with_reference_evaluator` runs only when the adapter declares
  `query.filter`. It compares `Id` equality, inclusion, inequality, and empty-filter results with
  `InMemoryFilterEvaluator`.
- `Cacheable_invalidates_on_delete` runs only when the Entity has the cache attribute, detected by
  full type name to avoid a hard Cache package dependency.
- `Embedding_does_not_break_the_save_path` runs only when the Entity has the embedding attribute,
  also detected without a hard AI package dependency.

## Consumer extension points

- `NewValid()` is required and must return a fresh business-valid Entity without relying on a
  parameterless-constructor constraint.
- `Mutate(TEntity)` defaults to no change and is reserved for batteries that need an updated value.
- `Configure(IDictionary<string, string?>)` may replace defaults or add adapter settings before host
  construction.

There is intentionally no repository factory, service-registration callback, fixture locator, or
scaffolding generator on the public surface. Tests needing custom DI composition should use
`KoanIntegrationHost` directly.

## Failure and skip behavior

- An exception during the initial adapter reachability probe records one redacted first-line reason;
  inherited batteries then skip through native xUnit behavior.
- After reachability succeeds, assertion and operation failures propagate normally.
- Trait and capability absence produce explicit skips naming the missing declaration.
- Temporary-root deletion is best-effort and cannot replace a test result with a cleanup failure.

The reachability catch currently treats every setup/probe exception as backing-store unavailability.
It is not a general failure-classification model; finer composition-versus-infrastructure facts belong
to the framework's later unified error/explanation work.

## Evidence boundary

The meta-suite proves positive batteries, trait gating, a deliberately failing paging oracle, and
host-owner preservation through generated module composition. S1 and S5 provide application-level
consumer proofs using the unchanged one-method inheritance grammar.
