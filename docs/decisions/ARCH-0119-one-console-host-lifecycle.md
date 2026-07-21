---
id: ARCH-0119
slug: one-console-host-lifecycle
domain: Architecture
status: Accepted
date: 2026-07-17
title: Console bootstrap uses the standard .NET host lifecycle
supersedes:
  - OPS-0015
---

# ARCH-0119: Console bootstrap uses the standard .NET host lifecycle

## Context

`IServiceCollection.StartKoan()` is the one-line console bootstrap used by the public template. It
previously built a raw service provider, supplied configuration itself, invoked runtime discovery,
and attached a special ambient-host lease without starting `IHostedService`.

That narrower path looked cheap but was not a complete Koan host. Standard services such as
`IHostEnvironment` and `IHostApplicationLifetime` were absent. Communication composition and health
inspection therefore emitted collection failures in a successful package-first application, and
local Events, Transport, health, and other hosted capabilities did not receive their normal
start/stop lifecycle.

The .NET Generic Host already owns configuration, logging, environment, application lifetime, DI,
hosted-service startup, and graceful shutdown. A second partial lifecycle duplicates those concerns
and makes the one-line path semantically weaker than `AddKoan()`.

## Decision

Keep the public one-line expression and replace its internal raw-provider path:

```csharp
using var app = new ServiceCollection().StartKoan();
```

- `StartKoan()` creates and synchronously starts a standard `HostApplicationBuilder`/`IHost`.
- Caller service descriptors are preserved. An explicitly registered `IConfiguration` remains the
  selected DI configuration; otherwise the standard host loads appsettings and environment sources.
- `AddKoan()` remains the single Koan composition entry point.
- The standard host supplies `IHostEnvironment`, `IHostApplicationLifetime`, logging, configuration,
  validation, and every `IHostedService` lifecycle.
- The method returns the standard `IHost` it owns, not a Koan-specific lifecycle or a provider-shaped facade.
  `using var` is therefore ordinary .NET ownership, and explicit service access uses `app.Services`.
- The private host wrapper also serves as the ambient provider. Synchronous disposal releases the ambient lease,
  stops the host, and safely disposes async-only services; focused infrastructure may use its async disposal path.
- Custom `IAppRuntime` registrations retain the existing direct discovery/start callback; the built-in
  runtime is idempotent and is normally invoked by its hosted bridge.

No fallback host-environment type, no fake application lifetime, and no second console-only service
runner are introduced.

## Consequences

- Console applications receive the same local capability ring and correction behavior as other Koan
  hosts while keeping one line of application code.
- Development-mode DI validation can reject incomplete graphs earlier instead of allowing a latent
  resolution failure.
- `StartKoan()` starts background services and therefore must be disposed. The public template and S0 sample use
  `using var app = ...`; process exit is no longer taught as lifecycle ownership.
- The Generic Host package becomes an intentional Data.Core dependency because Data.Core exposes the
  Entity-oriented console convenience.
- OPS-0015's custom configuration fallback and ARCH-0107's statement that `StartKoan()` does not use a
  hosted lifecycle are superseded by this standard-host implementation. ARCH-0107's ambient logging
  ownership law remains unchanged.

## Verification

- The StartKoan ownership suite proves environment/lifetime availability, hosted start/stop, overlapping
  ambient ownership, async disposal, and cleanup after startup failure.
- Communication's lifecycle suite proves the default console host projects composition without a
  collection failure.
- A source-equivalent console template persists and queries an Entity while the standard host starts
  Communication and health, with neither previously observed collection failure.
- S0's process-level golden contract observes JSON persistence, a matching composition lock, meaningful business
  output, and the standard shutdown message after ordinary `using var` ownership.
- A focused Data.Core nupkg contains the explicit Generic Host dependency. The next exact package
  candidate remains responsible for repeating the package-only console, FirstUse, and GoldenJourney
  assertions before public promotion.

## References

- [.NET Generic Host](https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host)
- [`IHost.StartAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.ihost.startasync)
- [ARCH-0107 — host-scoped KoanLog](ARCH-0107-host-scoped-koanlog.md)
- [R08-04 — package-first templates](../initiatives/koan-v1/work-items/r08/R08-04-package-first-templates.md)
