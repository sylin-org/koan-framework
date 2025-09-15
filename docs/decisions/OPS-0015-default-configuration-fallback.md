---
id: OPS-0015
slug: OPS-0015-default-configuration-fallback
domain: OPS
status: Accepted
date: 2025-08-16
title: Default IConfiguration fallback in Koan
---
 
# 0015: Default IConfiguration fallback in Koan

## Context
Adapters and modules bind options from `IConfiguration`. In host-based apps (ASP.NET Core), the host builds and registers `IConfiguration`. In console or non-host scenarios, `IConfiguration` might be absent, causing option configurators to fail.

We want:
- `IConfiguration` available by default in Koan.
- Never override an app/host-provided configuration.
- Keep console apps simple, while preserving host composition in web apps.

## Decision
- Koan does not register `IConfiguration` in Core. Instead, `services.StartKoan()` (the one-liner for non-hosted apps) provides a fallback `IConfiguration` only if none is registered.
- The fallback configuration is built from:
  - `appsettings.json` (optional)
  - Environment variables
- If an `IConfiguration` already exists in the service collection, Koan uses it as-is.

## Consequences
- Console apps can use `services.StartKoan()` and get a working `IConfiguration` for option binding without manual wiring.
- Host apps (ASP.NET Core) retain full control of configuration composition; Koan will not replace or layer on top of the host configuration.
- Adapter configurators can safely depend on `IConfiguration` across app types.

## Alternatives considered
- Register `IConfiguration` in Core at all times: rejected, because it can override or conflict with host configuration.
- Require samples to wire `IConfiguration` manually: rejected, hurts developer experience and violates “sensible defaults.”

## Notes
- Tests and samples continue to override options via `services.PostConfigure<TOptions>(...)` where needed.
