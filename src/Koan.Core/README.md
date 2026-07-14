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
- Application identity: `AppHost.Identity` follows that same current provider or flow scope. With no
  active host, it falls back to the frozen process identity in `KoanEnv`.

## Links
- Repo: https://github.com/sylin-org/Koan-framework
- Docs: https://github.com/sylin-org/Koan-framework/tree/dev/docs
