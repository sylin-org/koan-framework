# Defensive Publication: Push-Based Health Announcements with TTL and Real-Time Merging with Pull-Based Contributors

## Header Block

- **Title:** Hybrid Push-Pull Health Monitoring System with TTL-Based Announcement Expiry, Static Reporter Facade, and Merged Readiness Computation
- **Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
- **Disclosure Date:** 2026-03-24
- **Field of Invention:** Application health monitoring infrastructure, specifically methods for combining real-time push-based health announcements with periodic pull-based health checks into a unified readiness computation with automatic expiry.
- **Keywords:** health check, health announcement, TTL, push-pull hybrid, readiness, degraded, static facade, announcement expiry, merged health, component health, probe scheduling

---

## 1. Problem Statement

Application health monitoring traditionally uses pull-based mechanisms — a scheduler periodically invokes health check methods that return current status. ASP.NET Core Health Checks, Kubernetes probes, and Consul checks all follow this model. Pull-based checks have inherent latency: a component may become unhealthy immediately after a check completes, remaining undetected until the next scheduled check.

Push-based systems (event-driven health updates) provide immediate notification but require explicit lifecycle management: if a component stops pushing updates (because it crashed or hung), the health system must detect the absence of updates and mark the component as unknown/stale. Without TTL-based expiry, a push-only system would retain the last-reported status indefinitely, even after the reporter has failed.

Additionally, existing frameworks require dependency injection to report health — components must receive an `IHealthCheck` or similar service via constructor injection. This makes it difficult for static utilities, background workers, or deeply nested code to report health without threading DI parameters through the call stack.

---

## 2. Prior Art Summary

**ASP.NET Core Health Checks:** Pull-only. `IHealthCheck.CheckHealthAsync()` invoked by `HealthCheckPublisher` on a fixed interval. No push mechanism. No TTL. No static reporting facade.

**Kubernetes Probes (liveness/readiness):** Pull-only via HTTP endpoint. Fixed interval. No push. No merged computation from multiple sources.

**Consul Agent Checks:** Pull-based with `deregister_critical_service_after` TTL for automatic deregistration. No push channel for real-time updates. No merged readiness from push + pull.

**Spring Boot Actuator:** Pull-only `/health` endpoint. No push announcements. No TTL-based expiry.

**Specific gaps:**
1. No framework provides a hybrid push+pull health system with merged readiness computation.
2. No framework provides TTL-based automatic expiry for push-based health announcements.
3. No framework provides a static one-liner facade for health reporting without DI injection.
4. No framework tracks `lastNonHealthyAt` timestamps for degraded/unhealthy components.

---

## 3. Detailed Description of the Invention

### 3.1 Push Channel: IHealthAnnouncer

```
IHealthAnnouncer:
  Healthy(name)
  Degraded(name, description?, data?, ttl?)
  Unhealthy(name, description?, data?, ttl?)

// Each call stores an announcement with:
//   - Component name
//   - Status (Healthy, Degraded, Unhealthy)
//   - Optional description and structured data
//   - TTL (default 2 minutes, configurable per-announcement)
//   - Timestamp of announcement
//   - lastNonHealthyAt (tracked per component, updated on Degraded/Unhealthy)
```

### 3.2 Static Facade: HealthReporter

```
HealthReporter.Healthy("cache-warmer")
HealthReporter.Degraded("external-api", "Latency above threshold")
HealthReporter.Unhealthy("database", "Connection refused", ttl: TimeSpan.FromMinutes(5))

// Resolves IHealthAnnouncer from AppHost.Current (ambient service provider)
// If AppHost not initialized, calls are silently no-ops
// Enables health reporting from static methods, background workers, utilities
```

### 3.3 TTL Mechanics

```
On announcement storage:
  expiresAt = DateTimeOffset.UtcNow + ttl

On readiness computation:
  if (DateTimeOffset.UtcNow > expiresAt):
    treat component as "unknown/stale" (not Healthy, not Unhealthy)
    stale announcements do NOT contribute to overall readiness
```

If a component announces `Healthy` and then crashes without announcing `Unhealthy`, the announcement expires after TTL, and the component is no longer reported as healthy.

### 3.4 Pull Channel: IHealthContributor

Traditional health checks are registered as `IHealthContributor` implementations, probed by the health probe scheduler (described in a related disclosure). Each contributor returns status on demand.

### 3.5 Merged Readiness Computation

```
OverallReadiness = merge(announcementSnapshots, contributorProbeResults)

Algorithm:
  1. Collect non-expired announcements → set A
  2. Collect latest contributor results → set B
  3. Combined = A ∪ B (deduplicated by component name; announcement takes precedence if fresher)
  4. OverallStatus = worst-status-wins:
     - Any Unhealthy → Overall Unhealthy
     - Any Degraded → Overall Degraded
     - All Healthy → Overall Healthy
  5. Facts clamped: data payload per component limited to configurable max size
  6. Message length limits enforced per component
```

### 3.6 Per-Component Scoped Handlers

The system supports targeted health probing:
```
// Targeted: probe specific component by name
healthService.Probe("database")

// Broadcast: probe all contributors not handling specific components
healthService.ProbeAll()
```

Contributors can register for specific component names, receiving probe requests only for their components.

### 3.7 Critical Flag

Announcements can be marked as critical:
```
healthAnnouncer.Unhealthy("payment-gateway", critical: true)
```

Critical announcements bypass normal readiness aggregation and immediately set overall status to Unhealthy, regardless of other component statuses.

---

## 4. Claims-Style Disclosure

1. A hybrid health monitoring system combining push-based announcements (with TTL-based automatic expiry) and pull-based contributor checks into a unified readiness computation, distinct from pull-only systems (ASP.NET Core, Kubernetes) in that components can report status changes immediately without waiting for the next scheduled probe.

2. A TTL-based announcement expiry mechanism wherein health announcements automatically become stale after a configurable duration, causing the component to be excluded from readiness computation, distinct from permanent status records in that component failure (crash/hang) is detected by absence of renewal.

3. A static facade (`HealthReporter`) for health announcements that resolves the health service from an ambient service provider, enabling health reporting from static methods, background workers, and deeply nested code without constructor-injected dependencies, distinct from DI-only health reporting in that no `IHealthCheck` interface implementation or service injection is required.

4. A `lastNonHealthyAt` tracking mechanism that records the most recent timestamp when a component was not healthy, persisted across announcement cycles, providing operational insight into component stability patterns.

5. A critical flag mechanism for health announcements that immediately sets overall readiness to Unhealthy regardless of other component statuses, enabling priority-based health aggregation for business-critical components.

6. A merged readiness algorithm that deduplicates components across push and pull channels (with push announcements taking precedence when fresher), applies worst-status-wins aggregation, and enforces data payload size limits per component.

---

## 5. Implementation Evidence

- **Interface:** `IHealthAnnouncer` in `src/Koan.Core/IHealthAnnouncer.cs`
- **Facade:** `HealthReporter` in `src/Koan.Core/HealthReporter.cs`
- **Implementation:** `HealthAnnouncements` in `src/Koan.Core/HealthAnnouncements.cs`
- **Aggregator:** `HealthAggregator` in `src/Koan.Core/HealthAggregator.cs`
- **Options:** `HealthAggregatorOptions` in `src/Koan.Core/HealthAggregatorOptions.cs`
- **Store:** `IHealthAnnouncementsStore` in `src/Koan.Core/IHealthAnnouncementsStore.cs`
- **Bridge:** `HealthContributorsBridge` in `src/Koan.Core/HealthContributorsBridge.cs`
- **ADR:** `docs/decisions/ARCH-0013-health-announcements-and-readiness.md`
- **Framework Version:** Koan Framework v0.6.3

---

## 6. Publication Notice

This document is published as a defensive disclosure to establish prior art. The inventor(s) dedicate this disclosure to the public domain and assert no patent rights over the described inventions. All rights to use, implement, and build upon these inventions are hereby granted to the public.

---

## Antagonist Review Log

### Pass 1
**Antagonist:** Push-based health with TTL is equivalent to a heartbeat system. If a service misses its heartbeat, it's marked stale. This is standard distributed systems practice (e.g., Consul's TTL checks).

**Author revision:** Consul's TTL checks are external (agent-based) and serve a different purpose (service deregistration). The disclosure describes an in-process announcement system that coexists with pull-based checks and merges both into unified readiness. The specific combination of push TTL + pull probing + merged readiness + static facade + per-component targeting is not present in Consul or any health check framework. Strengthened the prior art comparison.

### Pass 2
**Antagonist:** No further objections. The hybrid push-pull architecture with merged readiness is sufficiently distinct from pure-pull or pure-TTL systems.

### Final Status
✅ CLEARED — Antagonist found no further weaknesses. Safe to publish.
