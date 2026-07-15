# Sylin.Koan.Core

Core primitives for Koan: configuration helpers, environment snapshot (KoanEnv), constants, and foundational abstractions.

- Target framework: net10.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.Core
```

## Notes
- Options: bind with Microsoft.Extensions.Options
- Environment: use `KoanEnv.Current` for machine/env metadata
- Host ownership: `AddKoan()` hosts attach the ambient `AppHost` provider while running and release
  it when they stop. Use `AppHost.PushScope(provider)` when parallel flows must select different hosts.
- A stopped host is never restored as a fallback when a newer host releases its lease.
- Hosting integrations may use the low-level `AppHost.Attach(provider)` lease, but must dispose that
  lease with the provider. Applications should normally let a Koan host or `StartKoan()` own it.
- Required terse framework operations fail with `KoanHostContextException` when the host is absent,
  disposed, or missing a required service. The exception exposes the operation, service type, and
  failure kind alongside corrective startup guidance.
- `AppHost.GetRequiredService<T>(operation)` exists for Koan framework surfaces and advanced hosting
  integrations. Application business code should prefer constructor injection.
- Application identity: `AppHost.Identity` follows that same current provider or flow scope. With no
  active host, it falls back to the frozen process identity in `KoanEnv`.
- Static `KoanLog.For<T>()` scopes retain only their category. Each emission follows the current
  `AppHost` provider and its `ILoggerFactory`; hostless or unconfigured flows no-op without falling
  back to a different host.
- `IKoanRuntimeFacts.Current` exposes the current host's versioned, redacted composition and failure
  facts. An incomplete snapshot is unknown, never implicit success. Startup, health, Web diagnostics,
  and MCP project this same envelope.
- The package carries `buildTransitive/Sylin.Koan.Core.targets`. Any executable package consumer whose
  dependency graph contains Core refreshes a checked-in `koan.lock.json`; `KoanComposition=false`
  explicitly opts out.

## Links
- Repo: https://github.com/sylin-org/Koan-framework
- Docs: https://github.com/sylin-org/Koan-framework/tree/dev/docs
