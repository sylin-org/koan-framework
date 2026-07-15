---
uid: reference.modules.Koan.core
title: Koan.Core - Technical Reference
description: Core utilities, primitives, and conventions used across Koan modules.
since: 0.2.x
packages: [Sylin.Koan.Core]
source: src/Koan.Core/
---

## Contract

- Inputs/Outputs: foundational types, result helpers, guards, and common abstractions.
- Options: follow ADR ARCH-0040 for constants/options.
- Error modes: required terse host-backed surfaces use `KoanHostContextException`; avoid magic values.
- Runtime ownership: the generic-host binder owns the process-default `AppHost` provider from host
  start through stop. Explicit flow scopes override that default without mutating its lease.

## Key types

- Core primitives surfaced by other modules (data, web, messaging, ai).
- `AppHost`: resolves the current flow-scoped provider, then the running host's leased default.
- `AppHost.PushScope(IServiceProvider)`: selects a provider for one async flow and restores the prior
  flow value when disposed.
- `AppHost.Attach(IServiceProvider)`: low-level hosting integration lease. Disposing it clears the
  process default only if it still owns that binding; it never revives a predecessor.
- `AppHost.GetRequiredService<T>(operation)`: resolves a required service from the selected host and
  distinguishes missing host, disposed host, and missing service through `KoanHostContextException`.
- `AppHost.Identity`: resolves the immutable identity snapshot registered by that same provider;
  hostless callers receive the frozen `KoanEnv` application identity.
- `KoanLog.For<T>()`: creates a category-only reusable scope. Each emission resolves
  `ILoggerFactory` from the current `AppHost` provider, so host leases and flow scopes also govern
  logging without a second ambient owner.
- `IKoanRuntimeFacts`: read-only access to the current host's schema-versioned runtime fact envelope.
- `KoanFactJson`: the canonical deterministic JSON projection used by Web and MCP.
- `buildTransitive/Sylin.Koan.Core.targets`: emits static composition, the embedded intent manifest,
  and trimming roots for applicable executable builds even when Core arrives through a bundle.

## Usage guidance

- Prefer these utilities over bespoke helpers; keep concerns separated.
- Let `AddKoan()` and the generic host manage the default provider. Use `PushScope` for concurrent
  integration hosts, jobs, or other explicit execution contexts.
- Custom hosting integrations that call `Attach` must keep its lease for exactly the provider's active
  lifetime and dispose the lease no later than the provider. Prefer `StartKoan()` for synchronous,
  non-hosted startup.
- Resolve host-specific application identity through `AppHost.Identity` or an explicitly supplied
  provider. Do not retain configuration-derived identity in another process static.
- Do not cache services obtained from `AppHost.Current` in process-static fields. Immutable reflection
  metadata may be process-static; services and configuration remain host-owned.
- Reserve `GetRequiredService<T>(operation)` for terse framework APIs and advanced hosting seams.
  Application business code should use constructor injection. Optional probes should retain explicit
  `Try*`, nullable, or availability behavior instead of throwing this exception.
- Static `KoanLogScope` fields are safe because they retain only category text. A hostless flow or a
  selected provider without `ILoggerFactory` emits nothing and never falls back to another host.
- Read fact `Code`/`ReasonCode`/`State` for automation. Do not parse startup prose or treat
  `Complete=false` as healthy.
- Keep the checked-in `koan.lock.json` under review. It contains static app/module identity only;
  negotiated elections and runtime facts belong to `obj/koan.lock.resolved.json`.

## Observability & Security

- Runtime facts exclude arbitrary payloads, raw exception messages, stack traces, and configuration
  values. They still expose topology identifiers and should use an operational access boundary.

## References

- ARCH-0040 config and constants: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- Engineering guardrails: `/docs/engineering/index.md`
- Runtime facts: `/docs/engineering/runtime-facts.md`
- ARCH-0111: `/docs/decisions/ARCH-0111-unified-runtime-facts.md`
