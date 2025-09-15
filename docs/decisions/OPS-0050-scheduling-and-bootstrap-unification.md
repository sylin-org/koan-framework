---
title: OPS-0050 — Scheduling core and Bootstrap unification (readiness-gated, auto-registered)
status: Accepted
date: 2025-08-21
---

## Context

We need a simple, toggleable way to run environment setup and recurring jobs ("bootstrap" tasks like ensure models/indexes/seed-if-empty, plus nightly checks) with:

- Sane defaults (Dev on, Prod off unless configured),
- Readiness gating for critical tasks,
- Clean health/observability, and
- Minimal DX (implement one interface, opt-in policies via small composable interfaces or attributes, config overrides code).

## Decision

1. Unify under a single scheduling core (module: `Koan.Scheduling`). Bootstrap is a first-class scheduled job preset (runner = `bootstrap`).
2. Auto-register via `IKoanAutoRegistrar`:
   - Add `SchedulingOrchestrator` (HostedService),
   - Bind `Koan:Scheduling` options (env-aware defaults),
   - Register health contributors and in-memory lock/store (dev-safe).
3. Contracts (simple-first, composable):
   - Required: `IScheduledTask { string Id; Task RunAsync(CancellationToken); }`
   - Optional policies: `IOnStartup`, `IFixedDelay`, `ICronScheduled`, `IHasTimeout`, `IIsCritical`, `IHasMaxConcurrency`, `IProvidesLock`, `IAllowedWindows`, `IHealthFacts`.
   - Optional attribute sugar: `[Scheduled(...)]` for defaults; config overrides attributes.
4. Readiness gating policy:
   - If `Koan:Scheduling:ReadinessGate=true`, any failing/timeout critical task keeps `/health/ready` Unhealthy (503) until success or timeout.
5. Health + capabilities surface:
   - Per-task: `scheduling:task:{id}` with compact facts (triggers, next/last run, counters, lastError, critical, timezone, lock).
   - Orchestrator: `scheduling:orchestrator` summary.
   - Capabilities flags (Startup, FixedDelay, Cron, Windows, Misfire, Jitter, MaxConcurrency, DistributedLock, RunOnce, ReadinessGate, AttributeDiscovery, ConfigOverrides).
6. Well-known endpoint integration (in `Koan.Web`):
   - Add `GET /.well-known/Koan/scheduling` (guarded by the same exposure policy as Observability) to expose enabled, readinessGate, defaults, capabilities, and a compact jobs snapshot.
7. Bootstrap as a schedule:
   - Provide `BootstrapJobRunner` composing `IBootstrapTask` items (ensure models, ensure vector indexes, schema, seed-if-empty). Config-first: `Koan:Scheduling:Jobs` with `Runner: "bootstrap"`.

## Consequences

Positive:

- One cohesive surface for startup and recurring jobs.
- Dead-simple DX: implement `IScheduledTask` (+ one trigger interface) and you’re scheduled.
- Strong defaults; Prod stays safe unless configured.
- Health/ready reflect scheduling and are queryable via well-known.

Negative / risks:

- Attribute defaults can be misleading across environments; mitigated by config precedence.
- Cron/timezones can be tricky; we’ll use Cronos and default to UTC.

## Alternatives considered

- Separate Bootstrap module: clearer naming, but duplicates scheduling mechanics; unifying reduces surface area.
- Heavyweight schedulers (Quartz): powerful but too much for Koan’s goals; a light, focused core fits better.

## Rollout

- Phase 1: Core orchestrator, `IScheduledTask` + `IOnStartup`/`IFixedDelay`, health wiring, well-known snapshot.
- Phase 2: Cron (Cronos), misfire policies, jitter, calendar windows, Redis lock provider.
- Phase 3: Bootstrap runner + built-in tasks from AI/Vector providers.

## Success criteria

- In Dev, a startup preflight runs once and readiness flips to healthy after success; status visible under well-known and health.
- In Prod, nothing runs unless configured; adding a nightly job takes one small config block or a tiny task class.
