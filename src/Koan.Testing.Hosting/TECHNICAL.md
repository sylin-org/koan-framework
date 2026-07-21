---
uid: reference.modules.koan.testing.hosting
title: Koan.Testing.Hosting - Technical Reference
description: Generic-host construction, startup ownership, disposal, and concurrency boundaries.
packages: [Sylin.Koan.Testing.Hosting]
source: src/Koan.Testing.Hosting/
---

## Contract

`Koan.Testing.Hosting` is the xUnit-agnostic integration-host seam. It constructs a real
`Microsoft.Extensions.Hosting.IHost`, exposes its service provider, and preserves the caller's choice
of bootstrap shape. The package has no knowledge of test framework lifecycle or infrastructure
fixtures.

## Lifecycle and ownership

| Operation | Result | Owner |
|---|---|---|
| `Configure()` | Mutable builder with environment `Test` | caller |
| `Build()` | Unstarted `IntegrationHost` | caller |
| `StartAsync()` succeeds | Started `IntegrationHost` | caller after return |
| `StartAsync()` fails | Original exception after owned host cleanup | builder until cleanup completes |
| `DisposeAsync()` | Best-effort stop followed by host disposal | wrapper |

`Builder.StartAsync` creates exactly one `IntegrationHost` wrapper and uses it as the ownership
boundary. Ownership transfers only after hosted-service startup succeeds. Before that point, failure
cleanup awaits the wrapper's disposal and then rethrows; it does not return a partially started host.

`IntegrationHost.DisposeAsync` is idempotent. It first requests `StopAsync` with no new cancellation
deadline because test teardown is already the owning boundary. Stop errors do not prevent resource
disposal. If the underlying host implements `IAsyncDisposable`, its asynchronous path is awaited;
otherwise the wrapper falls back to `Dispose()`.

The cleanup contract preserves the normal startup exception when disposal succeeds. It is not a
general multi-error envelope: a disposal exception can still replace the startup exception. Unified
failure facts and redaction belong to the later framework explanation/error work.

## Composition

The builder applies configuration in this order:

1. host environment, defaulting to `Test`;
2. settings supplied by `WithSetting` or `WithSettings` as in-memory configuration;
3. sources supplied by `ConfigureAppConfiguration`, where later sources may override earlier values;
4. accumulated `ConfigureServices` callbacks.

The host does not call `AddKoan()`. This is deliberate: the test assembly's references express module
intent, while the caller decides whether to invoke full discovery, Core-only composition, or custom
service registration.

## Concurrency boundary

Koan's generic-host binder owns process-default host selection. Its compare-and-release lease makes
overlapping teardown owner-safe, but it does not give simultaneous static Entity operations separate
process defaults. Test projects that boot real Koan hosts therefore disable parallelization unless
their operations are explicitly flow-scoped.

## Evidence

`KoanIntegrationHostFailedStartSpec` in the bounded Fast bootstrap lane registers an async-owned
service and a hosted service that fails startup. It proves the startup error remains visible and the
async-owned resource is disposed before the exception escapes. ARCH-0109 defines the bounded lane
and its nonzero-test requirement.
