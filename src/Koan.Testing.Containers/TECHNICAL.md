---
uid: reference.modules.koan.testing.containers
title: Koan.Testing.Containers - Technical Reference
description: xUnit v3 fixtures and host ownership for engine-backed Koan integration specs.
packages: [Sylin.Koan.Testing.Containers]
source: src/Koan.Testing.Containers/
---

## Contract

`Koan.Testing.Containers` provides the reusable xUnit v3 boundary between adapter specs, shared
backing-store fixtures, and the real ARCH-0079 `AddKoan()` integration host.

- `KoanContainerFixture` owns backing-store setup, explicit connection overrides, availability
  diagnostics, and teardown.
- Concrete fixtures translate one live backing store into canonical Koan settings.
- `KoanDataSpec<TFixture>` supplies fail-closed readiness, host boot, per-test settings, and partition helpers.
- `BoundHost` owns only the returned integration-host handle. Koan's generic-host binder owns the
  process-default `AppHost` lease and releases it when the host stops.

## Host lifecycle and ownership

Both `BootAsync` overloads perform the same lifecycle:

1. obtain settings from `TFixture.SettingsForBoot()`;
2. optionally merge per-test settings over those defaults;
3. build a `KoanIntegrationHost` in the `Test` environment;
4. register `AddKoan()` before test-supplied services;
5. start the generic host, including its hosted services;
6. return `BoundHost` without writing or clearing `AppHost.Current` directly.

`AppHostBinderHostedService` is the sole owner of the host-lifetime process-default attachment. Its
lease uses compare-and-release semantics: stopping an older host cannot clear a newer owner, and a
hosted service that deliberately attaches a newer owner during startup is not overwritten after
`BootAsync` returns.

The provider injected into a hosted service and `IntegrationHost.Services` may be different wrapper
objects over the same root container. Ownership assertions should therefore resolve a host-specific
marker rather than compare those wrapper references.

## Concurrency boundary

`KoanDataSpec` intentionally retains the established assembly-serialized contract. Starting a second
host selects that host as the process default, so concurrent test methods cannot safely use static
Entity operations merely because their data partitions differ.

Use `[assembly: CollectionBehavior(DisableTestParallelization = true)]` for suites based on this
helper. `AppHost.PushScope` remains available when a test explicitly owns an entire parallel async
flow, but `BootAsync` does not manufacture a caller flow scope from inside its asynchronous method.

## Data isolation

Container fixtures are normally shared for the assembly. `NewPartition(label)` creates an
engine-prefixed GUID v7 name, and `Lease(partition)` pushes it through `EntityContext.Partition`.
This separates records between specs and between test processes without recreating schemas or
containers.

File-backed fixtures may override `SettingsForBoot()` to create a fresh location for each host when a
test must inspect the complete physical store rather than one logical partition.

## Availability and failure behavior

- An explicit `Koan_<ENGINE>__CONNECTION_STRING` bypasses container startup for CI-managed services.
- A container startup exception sets `IsAvailable = false`, records a diagnostic `Reason`, attempts
  owned partial-start cleanup, and rethrows the original error. Dual startup/cleanup failure aggregates.
- `RequireBackingStore()` rejects an uninitialized/unavailable fixture; it never converts missing
  required infrastructure into a green result.
- Host startup failures propagate; `KoanDataSpec` does not convert composition failures into skips.
- Fixture teardown failures propagate. The bounded admission runner still owns the outer process-tree
  deadline and cleanup when a native tool or runtime hangs.

## Extension points

- `BootAsync(Action<IServiceCollection>)` registers test services after `AddKoan()`.
- `BootAsync(IEnumerable<KeyValuePair<string, string?>>, Action<IServiceCollection>?)` overlays
  settings and optionally adds services.
- Subclasses of `KoanContainerFixture` provide `Engine`, `Adapter`, container start/stop, and optional
  adapter-specific settings. They do not reimplement host composition.

## Verification

The deterministic container lifecycle meta-suite proves setup/cleanup propagation and dual-failure
aggregation. The Docker-free InMemory connector suite remains the focused host oracle. It covers both boot
overloads, later-owner preservation, overlapping host disposal, Entity operations, partitions,
instructions, filtering, and adapter conformance through the shipped helper.
