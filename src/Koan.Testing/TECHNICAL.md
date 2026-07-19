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
4. starts a real `KoanIntegrationHost` with `AddKoan()`.

The generic host's `AppHostBinderHostedService` owns the process-default provider with a
compare-and-release lease. Every inherited battery additionally enters `AppHost.PushScope(host.Services)`,
so static Entity operations resolve the correct provider in
their own async flow even while another conformance host is active. `DisposeAsync` delegates to
integration-host disposal, then removes the temporary root best-effort. Stopping an older overlapping
host cannot clear a newer owner.

The scope is deliberately battery-owned instead of being pushed once from xUnit `InitializeAsync`:
async-local changes made inside a lifecycle callback are not a public scheduling contract for the
later test-method invocation. Consumers therefore need no assembly-wide parallelization switch.

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
- `Configure(IDictionary<string, string?>)` may replace defaults or add adapter settings before host
  construction.

There is intentionally no repository factory, service-registration callback, fixture locator, or
scaffolding generator on the public surface. Tests needing custom DI composition should use
`KoanIntegrationHost` directly.

## Failure and skip behavior

- Host startup, composition, provider access, assertion, and Entity-operation failures propagate with
  their original exception.
- Trait and capability absence produce explicit skips naming the missing declaration.
- Temporary-root deletion is best-effort and cannot replace a test result with a cleanup failure.

## Evidence boundary

The meta-suite proves positive batteries, trait gating, a deliberately failing paging oracle, fail-loud
provider selection, host-owner preservation, and concurrent same-Entity specifications resolving
distinct hosts through generated module composition. TaskGraph provides an application-level consumer
proof using the one-method inheritance grammar.
