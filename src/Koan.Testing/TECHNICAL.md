---
uid: reference.modules.koan.testing
title: Koan.Testing - Technical Reference
description: Entity conformance batteries, host ownership, capability gating, and failure behavior.
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
integration-host disposal, then removes the temporary root. Both phases are attempted; either failure
fails teardown and dual failures are reported together. Stopping an older overlapping host cannot clear
a newer owner.

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
- Temporary-root deletion is part of the owned lifecycle. A cleanup failure is a failed conformance
  result, and a simultaneous host-disposal failure is retained in the same aggregate exception.

## Evidence boundary

The meta-suite proves positive batteries, trait gating, a deliberately failing paging oracle, fail-loud
provider selection, host-owner preservation, and concurrent same-Entity specifications resolving
distinct hosts through generated module composition. TaskGraph provides an application-level consumer
proof using the one-method inheritance grammar.

## Data adapter conformance protocol

The Data Adapter Development Primer is the semantic authority. `scripts/forge-verify.ps1 -CatalogOnly` parses its 105
stable IDs, evidence conjunctions, and 27 applicability profiles into the embedded
`Conformance/data-conformance-catalog.json`; the generated resource is a projection, not a manually maintained second
catalog. Startup validation rejects a missing cell, duplicate ID, unknown evidence kind, unknown profile reference, or
stale protocol identity.

`DataConformanceManifest` records Observed, Target, and Declined profile claims. `Source Core` is automatic; runtime
`DataCaps` map through a registry whose completeness is reflection-tested; every omitted optional profile becomes an
explicit unproved decline. `DataConformancePacket.Compile` expands positive claims into exact
`<Acceptance ID>/<Case>/<Owner>` rows, computes verdicts from their required evidence, sorts every collection, embeds
the primer/catalog fingerprints, and records owner/source/tool/profile/fixture dependencies. Artifact references must
be repository-relative and traversal-free.

`DataConformancePacket.Validate` independently recomputes row verdicts and distinguishes:

- `Pass` / exit 0: all selected evidence and corrective decline proofs pass;
- `Red` / exit 1: behavioral failure, unsupported path without corrective proof, or false advertised claim;
- `Deferred` / exit 2: an explicit blocker or deferred evidence;
- `Error` / exit 3: malformed, incomplete, duplicated, unresolved, or stale protocol data;
- `Infrastructure` / exit 4: required live evidence could not run against the provider.

`DataAdapterConformanceSpecs` is the reusable 105-case xUnit projection. Strict Forge invokes the same C# validator at
the process boundary and only classifies its stable status marker; packet rules are not reimplemented in PowerShell.

`DataScenarioCatalog` is the shared fault/lifecycle module inventory. Each definition names its stable acceptance
cells and whether it needs a live provider, second host, restart, or minimum operation count. Provider fixtures supply
mechanics and receipts without redefining applicability. `DataBenchmarkRunner` captures the four P-05 measurements
for a pinned `DataBenchmarkFixture`; it emits observations and never owns cross-provider thresholds.
