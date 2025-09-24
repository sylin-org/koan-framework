---
id: ARCH-0013
slug: ARCH-0013-health-announcements-and-readiness
domain: ARCH
status: Accepted
date: 2025-08-16
---

# 0013 - Health announcements (push) with static one-liners; merged into readiness

Context

- We already support pull checks via `IHealthContributor` and aggregate them in `IHealthService` for readiness.
- Some failures are transient or best reported by the component experiencing them (e.g., downstream API flapping). We want a minimal DX to report these without scaffolding.

Decision

- Introduce a push channel: `IHealthAnnouncer` and a static facade `HealthReporter` with one-liners:
  - `HealthReporter.Degraded(name, description?, data?, ttl?)`
  - `HealthReporter.Unhealthy(name, description?, data?, ttl?)`
  - `HealthReporter.Healthy(name)` clears messages for that name.
- Announcements are stored with a TTL (default 2 minutes). A `lastNonHealthyAt` timestamp is tracked and surfaced in `data`.
- `IHealthService` merges announcement snapshots with contributor results and computes overall readiness per existing policy (critical → Unhealthy, else Degraded).

Consequences

- Minimal usage for developers: a single static call suffices; no scaffolding.
- Readiness reflects both periodic checks and real-time announcements.
- Sensitive data should be avoided in announcements by default. In dev, details can be included in responses; in prod, keep descriptions generic.
