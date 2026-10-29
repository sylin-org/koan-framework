---
id: ARCH-0139
slug: registry-driven-services-register-after-discovery
domain: Architecture
status: Accepted
date: 2026-10-29
title: Registry-driven services register after discovery, whatever code ran first
related:
  - CORE-0072
  - ARCH-0116
  - ARCH-0136
  - ARCH-0138
---

# ARCH-0139: Registry-driven services register after discovery, whatever code ran first

## Context

`KoanRegistry` is process-wide. An assembly's descriptors reach it in one of two ways: its generated
`[ModuleInitializer]` runs the first time any of its code runs (CORE-0072), or module initialization's assembly scan
reaches it and the reflection loader reads them (ARCH-0138).

`AddKoan()` calls `AddKoanCore()` before `AppBootstrapper.InitializeModules`, and `AddKoanCore()` registered every
background service and service-discovery adapter it found in the registry at that moment. That registered whatever the
process had already run, not what the host references. A server runs `Main` inside the application assembly, so the
application's descriptors are always present and nothing showed. A test process that composes a host before touching
any of the application's types got a host without the application's background services, and hosted services that
depend on them failed: "No service for type '…' has been registered". A host composed after the first scan was fine, so
the failure came and went with the order of tests in a parallel run.

Measured by a consumer whose write loop, a discovered background service, was missing from test hosts.

## Decision

**A host's registry-driven services are registered once module initialization has scanned every assembly it reaches.**

1. `AddKoanCore()` registers the background-service substrate (options, the orchestrator, `IServiceRegistry`, the
   orchestrator's health contribution) and the service-discovery substrate, and no longer reads the registry for the
   services themselves.
2. `AppBootstrapper.InitializeModules` registers the discovered background services and service-discovery adapters
   right after its scan, before any module's `Register`, from the same registry snapshot it reports.
3. Registration keeps its rules: environment and `Enabled` filtering for background services, and each adapter
   registered as before. Which assemblies' services a host gets is not otherwise changed; ARCH-0138's "Not decided
   here" still holds for them.

## Compatibility

- A host composed before any application code ran now has the application's background services and discovery
  adapters, as a server always had.
- A collection prepared with `AddKoanCore()` alone and never composed has the orchestrator but no discovered services.
  Before, it had whatever the registry held when it was called. `AddKoan()`, `AddKoanDataCore()` and `StartKoan()` each
  run module initialization (ARCH-0136), so every composed host is unaffected.
- No public signature changes.

## Evidence

- `UntouchedApplicationServiceSpec` (Koan.Tests.Integration.Bootstrap): Koan.Core and an application assembly
  (`Koan.Tests.Integration.Bootstrap.UntouchedApplication`) are loaded into an isolated load context, so the registry
  is empty and the application's module initializer has not run; a host composed through that Koan.Core's `AddKoan()`
  registers the application's background service and the orchestrator starts it. Before this change the service was
  not registered. The application is built into its own folder under the test output, outside the loose `Koan.*.dll`
  files a default-context `AddKoan()` scans, so the result does not depend on the order the suite's specs run in.
- `HealthProbeSchedulerOwnershipSpec` and `BackgroundServiceConfigurationSpec` (Koan.Tests.Core.Unit) still find Core's
  own scheduler under one owner and still honour configured disablement.
