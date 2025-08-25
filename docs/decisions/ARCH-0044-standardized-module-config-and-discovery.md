---
id: ARCH-0044
slug: standardized-module-config-and-discovery
domain: Architecture
status: Accepted
date: 2025-08-25
title: Standardized module configuration, dev defaults, and auto-discovery
---

## Context

Configuration and initialization patterns across Sora modules have diverged. We want a single, beginner-friendly approach that preserves Separation of Concerns (SoC), improves DX, and supports “just works” development defaults without surprising Production behavior.

## Decision

Golden path for all modules (library-first, opt-in):

1) Keys and aliases
- Key scheme: `Sora:<Area>:<ModuleName>:<Alias>:<Property>` (e.g., `Sora:Data:Postgres:Default:ConnectionString`).
- Default alias is `"Default"`; additional aliases allowed (e.g., `Reporting`).
- Env var mapping uses `SORA__...` prefix (e.g., `SORA__DATA__POSTGRES__DEFAULT__CONNECTIONSTRING`).

2) Options + validation (typed, strict in Production)
- Each module defines an `Options` class with a public `BindPath` constant, sensible dev defaults for non-critical values, and DataAnnotations.
- Register named options per alias; call `ValidateOnStart()`.
- Failure modes: Production → required keys missing cause startup failure; Development/CI → warn + allow safe defaults.

3) Single DI entrypoint per module
- `services.AddSora<Module>(IConfiguration config, string alias = "Default", Action<Options>? postConfigure = null)`
- The extension binds named options from `<BindPath>:<alias>`, registers validators, health checks, and keyed client/factory instances for the alias.
- Business code consumes `IOptionsMonitor<T>` or keyed clients; it never accesses `IConfiguration` directly.

4) SoraEnv as runtime accessor
- Modules use `SoraEnv` for environment/profile flags, container detection, and app directories (no direct `Environment.*`).

5) Development defaults and auto-discovery
- All modules should provide a Development “default that just works”.
- For adapters that require external services (e.g., RabbitMQ, Ollama, Mongo, Postgres, Redis, Weaviate): implement adapter-specific auto-discovery internally (SoC) with this order in Development:
  1) Explicit configuration (appsettings/env/secrets)
  2) Env overrides for host/port
  3) Localhost on the module’s default port
  4) Docker/compose hints: try canonical service DNS names on default ports when appropriate (host vs container contexts)
- Discovery is disabled in Production by default. It is enabled in Development and CI by default, with opt-outs available.

6) Discovery knobs and logging
- Global switches (env): `SORA__DISCOVERY__DISABLED`, `SORA__DISCOVERY__TOTALMS`, `SORA__DISCOVERY__PROBEMS`.
- Per-module/per-alias overrides allowed (e.g., `SORA__DATA__POSTGRES__DISCOVERY__TOTALMS`, `SORA__DATA__POSTGRES__REPORTING__DISCOVERY__PROBEMS`).
- Defaults (Development/CI): total budget 3000 ms; per-probe 300 ms. Production discovery remains disabled by default.
- Log a concise info message on success (host:port, source) and a single warn on failure (tried endpoints, total time). Redact secrets.

7) Health semantics
- If module enabled and discovery fails: Development/CI → `Degraded`; Production → `Unhealthy`.
- If module disabled, omit the health check entirely (preferred behavior).
- If discovery succeeds but auth fails, mark `Unhealthy` with an actionable message.

8) SQLite dev fallback
- When suitable, provide an opt-in Development fallback (e.g., SQLite) with predictable data path from `SoraEnv.DataDir`, and safe PRAGMAs (WAL, foreign_keys=ON, synchronous=NORMAL). No auto-migrate in Production.

## Scope
In-scope: Data, Messaging, Vector, AI, and Web modules. Out-of-scope now: multitenancy overlays (reserved for future ADR).

## Consequences
Positive: Consistency, quick start, safer Production posture, and cleaner SoC boundaries.
Trade-offs: Slight increase in module scaffolding (Options/Constants/Extension/HealthCheck), mitigated by templates.

## Implementation notes
- Provide a Roslyn analyzer to discourage raw `Environment.*` and magic config keys in modules; prefer `SoraEnv` and `Constants`.
- Add a “Tiny Module” template with copy-paste scaffolding.
- Update docs with a module author checklist and examples (Options, Constants, Extension, HealthCheck).

## Follow-ups
- Pilot refactors: Postgres, RabbitMQ, Weaviate, and Sqlite fallback.
- Add docs under Engineering: “Configuration patterns for module authors”.
