# ADR-00xy: Static runtime snapshot (SoraEnv) for environment flags

Date: 2025-08-18

## Status
Accepted

## Context
Environment checks (development/production, container, CI, feature flags) were implemented inconsistently across modules, mixing IHostEnvironment, raw env var reads, and ad-hoc config probes. This led to drift and brittle behavior.

## Decision
Adopt a single, immutable, static runtime snapshot in Sora.Core via `SoraEnv`.

- `SoraEnv.Initialize(IConfiguration?, IHostEnvironment?)` computes a snapshot once; thread-safe and idempotent.
- `SoraEnv.TryInitialize(IServiceProvider)` initializes opportunistically once DI is available.
- Static properties expose flags:
  - `EnvironmentName`, `IsDevelopment`, `IsProduction`, `IsStaging`
  - `InContainer`, `IsCi`, `AllowMagicInProduction`, `ProcessStart`
- Fallback: If accessed before initialization, a conservative snapshot is computed from environment variables; the first Initialize wins thereafter.
- Remove the DI type `IRuntimeInfo`; all modules use `SoraEnv` directly.

## Consequences
- Consistent, fast, allocation-free access to runtime flags.
- No runtime drift; a single source of truth.
- Slight startup-order requirement: `SoraEnv.TryInitialize(sp)` is called in `UseSora()` and `StartSora()` to ensure early initialization; manual `Initialize(cfg, env)` remains available.

## Guidance
- Do not query `IHostEnvironment` or env vars directly for prod/dev checks; use `SoraEnv`.
- Describe/reporting logic (boot report, Swagger gating) must reference `SoraEnv`.
- If tests need to simulate environments, prefer process env vars before initialization; an explicit test-only override can be added later if needed.
