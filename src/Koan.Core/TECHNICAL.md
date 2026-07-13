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
- Error modes: standard .NET exceptions; avoid magic values.
- Runtime ownership: the generic-host binder owns the process-default `AppHost` provider from host
  start through stop. Explicit flow scopes override that default without mutating its lease.

## Key types

- Core primitives surfaced by other modules (data, web, messaging, ai).
- `AppHost`: resolves the current flow-scoped provider, then the running host's leased default.
- `AppHost.PushScope(IServiceProvider)`: selects a provider for one async flow and restores the prior
  flow value when disposed.

## Usage guidance

- Prefer these utilities over bespoke helpers; keep concerns separated.
- Let `AddKoan()` and the generic host manage the default provider. Use `PushScope` for concurrent
  integration hosts, jobs, or other explicit execution contexts.
- Do not cache services obtained from `AppHost.Current` in process-static fields. Immutable reflection
  metadata may be process-static; services and configuration remain host-owned.

## Observability & Security

- Integrates with logging/tracing where applicable; no direct security surface.

## References

- ARCH-0040 config and constants: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`
- Engineering guardrails: `/docs/engineering/index.md`
