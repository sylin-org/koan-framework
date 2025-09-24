---
id: ARCH-0039
slug: ARCH-0039-Koanenv-static-runtime
domain: ARCH
status: Accepted
date: 2025-08-18
---

# ADR-00xy: Static runtime snapshot (KoanEnv) for environment flags

## Context
Environment checks (development/production, container, CI, feature flags) were implemented inconsistently across modules, mixing IHostEnvironment, raw env var reads, and ad-hoc config probes. This led to drift and brittle behavior.

## Decision
Adopt a single, immutable, static runtime snapshot in Koan.Core via `KoanEnv`.

- `KoanEnv.Initialize(IConfiguration?, IHostEnvironment?)` computes a snapshot once; thread-safe and idempotent.
- `KoanEnv.TryInitialize(IServiceProvider)` initializes opportunistically once DI is available.
- Static properties expose flags:
  - `EnvironmentName`, `IsDevelopment`, `IsProduction`, `IsStaging`
  - `InContainer`, `IsCi`, `AllowMagicInProduction`, `ProcessStart`
- Fallback: If accessed before initialization, a conservative snapshot is computed from environment variables; the first Initialize wins thereafter.
- Remove the DI type `IRuntimeInfo`; all modules use `KoanEnv` directly.

## Consequences
- Consistent, fast, allocation-free access to runtime flags.
- No runtime drift; a single source of truth.
- Slight startup-order requirement: `KoanEnv.TryInitialize(sp)` is called in `UseKoan()` and `StartKoan()` to ensure early initialization; manual `Initialize(cfg, env)` remains available.

## Guidance
- Do not query `IHostEnvironment` or env vars directly for prod/dev checks; use `KoanEnv`.
- Describe/reporting logic (boot report, Swagger gating) must reference `KoanEnv`.
- If tests need to simulate environments, prefer process env vars before initialization; an explicit test-only override can be added later if needed.
