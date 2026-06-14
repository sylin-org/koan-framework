# Defensive Publication: Quantized Health Probe Scheduling with Jitter, Coalescing, and Backpressure

**Publication Type:** Defensive Publication (Prior Art Disclosure)
**Title:** Quantized Health Probe Scheduling with Uniform Jitter, Threshold-Based Broadcast Coalescing, Bucket-Split Backpressure, and Per-Component Gap Enforcement
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Disclosure:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET, target net10.0)
**Repository:** github.com/koan-framework (private)
**Governing ADRs:** OPS-0050 (Scheduling and Bootstrap Unification)

---

## 1. Technical Problem Addressed

Health monitoring in distributed systems relies on periodic probing of application components -- database connections, message brokers, external APIs, caches, AI model endpoints, and internal subsystems. Each component exposes a health contributor that reports its status with an associated time-to-live (TTL) indicating how long the reported status remains valid. The scheduler must re-probe components before their TTL expires to maintain a current aggregate health picture.

Three fundamental problems arise when a naive fixed-interval scheduler is used:

**A. Thundering herd on TTL alignment.** When multiple components share similar TTL values (common in practice: most database checks use 30-second TTL, most cache checks use 15-second TTL), their refresh intervals naturally align. A fixed-interval scheduler that computes `lastProbe + TTL - lead` will trigger all same-TTL components simultaneously. In a system with 20+ health contributors -- typical for a Koan Framework application with multiple data providers, an AI adapter registry, message bus connections, and orchestration endpoints -- this creates probe storms that saturate I/O and introduce latency spikes in the health endpoint response.

**B. Absence of adaptive batch/individual switching.** Existing health check frameworks treat probing as either all-or-nothing (ASP.NET Core `HealthCheckPublisher` invokes all checks every interval) or strictly individual (Consul agent runs each check on its own timer). There is no mechanism to dynamically switch between individual probing and broadcast probing based on current load. When most components happen to be due simultaneously, individual probing wastes overhead on per-component dispatch; when only a few are due, broadcast probing wastes resources re-probing components that are nowhere near TTL expiry.

**C. No backpressure under high component counts.** Applications with extensive health contributor registrations (Koan Framework supports dynamic contributor registration from auto-discovered connectors via `KoanAutoRegistrar`) can accumulate dozens of components. Probing all due components in a single synchronous batch blocks the scheduler loop, delays subsequent scheduling cycles, and can trigger timeout cascades where probe responses arrive after the next scheduling window has already passed.

**D. Probe re-invitation within recent window.** Without per-component gap enforcement, a component that is slow to respond (its contributor takes 3 seconds to complete a database ping) may be re-invited on the very next scheduling cycle because the scheduler sees stale status data. This wastes resources and can compound the slow-response problem.

Existing systems address these problems partially or not at all:

- **ASP.NET Core Health Checks:** Runs all registered `IHealthCheck` implementations on a fixed interval via `HealthCheckPublisherOptions.Period`. No quantization, no jitter, no coalescing threshold, no backpressure splitting. All checks run every cycle regardless of individual TTL.
- **Kubernetes liveness/readiness probes:** Fixed `periodSeconds` per probe definition. Each probe runs independently with no coordination between probes on the same pod. No coalescing, no backpressure.
- **Consul health checks:** Fixed `Interval` per check with `DeregisterCriticalServiceAfter` for cleanup. No quantization across checks, no adaptive broadcast switching, no backpressure.
- **Prometheus scraping:** Fixed `scrape_interval` per target. No per-metric TTL awareness, no adaptive scheduling, no jitter (though `scrape_interval` jitter was proposed and rejected in favor of consistent intervals).
- **Nagios/Icinga:** Fixed `check_interval` and `retry_interval` per service. No cross-service coalescing, no quantization windows.

No existing health monitoring system provides the combination of quantized time windows, uniform jitter, threshold-based broadcast coalescing, bucket-split backpressure, and per-component minimum gap enforcement described in this disclosure.

---

## 2. Novel Solution Description

The invention introduces a **HealthProbeScheduler** -- a hosted background service that manages health probe invitations through a six-stage pipeline operating within quantized time windows. The scheduler does not execute health checks directly; it computes optimal invitation times and dispatches probe requests to an `IHealthAggregator` which coordinates with registered health contributors.

### 2.1. Quantized Time Windows

All probe invitation times are rounded (ceiling) to fixed-width time windows anchored at Unix epoch. Given a raw next-probe time for a component and a configurable `QuantizationWindow` (default: 2 seconds):

```
quantizedTime = ceil((rawTime - Epoch) / windowSize) * windowSize + Epoch
```

This ceiling quantization ensures that components whose raw probe times fall within the same window are coalesced into a single scheduling decision. A component due at T+1.3s and another due at T+1.7s both land in the T+2.0s bucket when the window is 2 seconds. The quantization creates natural batching without requiring components to coordinate their TTL values.

The quantization window is configurable via `HealthAggregatorOptions.Scheduler.QuantizationWindow`. Smaller windows reduce latency between TTL expiry and probe dispatch at the cost of less coalescing; larger windows increase batching efficiency at the cost of slightly stale health data.

The implementation in `HealthProbeScheduler.QuantizeCeil`:

```csharp
private static DateTimeOffset QuantizeCeil(DateTimeOffset value, TimeSpan window)
{
    var ms = (value - Epoch).TotalMilliseconds / window.TotalMilliseconds;
    var ceil = Math.Ceiling(ms);
    return Epoch + TimeSpan.FromMilliseconds(ceil * window.TotalMilliseconds);
}
```

### 2.2. Refresh Lead with Absolute Minimum

Before quantization, the scheduler computes when to invite each component by subtracting a "refresh lead" from the TTL expiry time:

```
refreshLead = max(TTL * RefreshLeadPercent, RefreshLeadAbsoluteMin)
refreshLead = min(refreshLead, QuantizationWindow)  // cap to keep batching effective
rawProbeTime = lastProbeTime + TTL - refreshLead
```

The refresh lead ensures probes are dispatched before TTL expiry, giving the contributor time to execute the health check and report results before the previous status expires. The absolute minimum (`RefreshLeadAbsoluteMin`, default: 250ms) prevents the lead from collapsing to zero for very short TTLs. The cap to `QuantizationWindow` prevents the lead from exceeding the window size, which would cause the scheduler to invite components too early and defeat the purpose of quantized batching.

```csharp
private static TimeSpan ComputeRefreshLead(TimeSpan ttl, HealthAggregatorOptions opt)
{
    var refreshPercent = Math.Clamp(opt.Scheduler.RefreshLeadPercent, 0, 1);
    var percent = TimeSpan.FromMilliseconds(ttl.TotalMilliseconds * refreshPercent);
    var lead = percent > opt.Scheduler.RefreshLeadAbsoluteMin
        ? percent : opt.Scheduler.RefreshLeadAbsoluteMin;
    if (opt.Scheduler.QuantizationWindow < lead) lead = opt.Scheduler.QuantizationWindow;
    return lead;
}
```

### 2.3. Uniform Jitter with Absolute Minimum

After computing the refresh lead, the scheduler applies uniform random jitter to prevent phase-locked probe storms:

```
jitterRange = max(refreshLead * JitterPercent, JitterAbsoluteMin)
offset = uniform_random(-jitterRange, +jitterRange)
jitteredProbeTime = rawProbeTime + offset
```

The jitter is applied symmetrically around the computed probe time (not one-sided), meaning probes can arrive slightly before or after the ideal time. This is critical: one-sided jitter (only adding delay) would cause systematic staleness, while symmetric jitter preserves the expected probe time in aggregate while breaking phase alignment.

The absolute minimum (`JitterAbsoluteMin`, default: 25ms) ensures meaningful jitter even for very short TTLs where the percentage-based jitter would be negligible. The jitter is computed per component per scheduling cycle, so the same component gets different jitter offsets on successive cycles.

```csharp
private static TimeSpan ComputeJitter(TimeSpan baseLead, HealthAggregatorOptions opt)
{
    var pct = Math.Abs(opt.Scheduler.JitterPercent);
    var jitter = TimeSpan.FromMilliseconds(baseLead.TotalMilliseconds * pct);
    if (jitter < opt.Scheduler.JitterAbsoluteMin) jitter = opt.Scheduler.JitterAbsoluteMin;
    return jitter;
}
```

### 2.4. Threshold-Based Broadcast Coalescing

After quantization and jitter, the scheduler evaluates all components due in the current window. A dynamic switching decision determines whether to probe individually or broadcast:

```
if (dueComponents.Count >= BroadcastThreshold):
    RequestProbe(reason: TtlExpiry, component: null)   // broadcast: probe ALL
else:
    for each component in dueComponents:
        RequestProbe(reason: TtlExpiry, component: name)  // individual
```

When the number of due components reaches or exceeds the `BroadcastThreshold` (default: 8), the scheduler switches to a broadcast probe that invites all contributors, not just the due ones. The rationale: when most components are due simultaneously, the marginal cost of probing a few extra not-yet-due components is less than the overhead of individual dispatch for each due component. The broadcast request is a single call to `IHealthAggregator.RequestProbe` with a null component parameter, signaling "all."

The threshold is configurable. Setting it to 1 makes the scheduler always broadcast (equivalent to ASP.NET Core's behavior). Setting it to `int.MaxValue` makes it always probe individually. The default of 8 represents the crossover point where broadcast overhead becomes lower than individual dispatch overhead based on empirical measurement in Koan Framework deployments.

Each mode emits a distinct service event (`ProbeScheduled` for individual, `ProbeBroadcast` for broadcast) enabling external monitoring to observe the scheduler's switching behavior.

### 2.5. Bucket-Split Backpressure

When the number of due components in a single window exceeds `MaxComponentsPerBucket` (default: 16), the scheduler introduces a deliberate pause after dispatching the current batch:

```
if (dueComponents.Count > MaxComponentsPerBucket):
    await Task.Delay(MinInterBucketGap)  // default: 50ms
```

This gap allows the health aggregator and contributors time to process the current batch before the scheduler continues its loop. The pause effectively splits a large batch across successive loop iterations, preventing a single massive probe storm from overwhelming I/O resources.

The backpressure mechanism is complementary to broadcast coalescing: broadcast reduces per-component dispatch overhead, while backpressure prevents even the broadcast from saturating resources when the total component count is very high (dozens of contributors in a fully-loaded Koan application with multiple data connectors, AI adapters, and orchestration endpoints).

### 2.6. Per-Component Minimum Gap Enforcement

The scheduler maintains a `ConcurrentDictionary<string, DateTimeOffset>` tracking the last invitation time per component. Before including a component in the current due set, it checks:

```
if (now - lastInvited[component]) < MinComponentGap:
    skip this component
```

The `MinComponentGap` (default: 5 seconds) prevents re-inviting a component that was recently probed, even if quantization and jitter place it in the current window. This addresses the slow-responder problem: a component whose contributor takes several seconds to complete should not be re-invited on every scheduling cycle.

```csharp
private bool AllowedByGap(string component, DateTimeOffset now)
{
    if (!_lastInvited.TryGetValue(component, out var last)) return true;
    return (now - last) >= _opt.Scheduler.MinComponentGap;
}
```

The gap tracking dictionary is updated after each scheduling cycle completes, using the same timestamp for all components in the batch to ensure consistent gap enforcement.

### 2.7. Deferred Start

The scheduler defers its first cycle until the host application signals `ApplicationStarted`. This prevents probe storms during startup when contributors are still initializing:

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(
    stoppingToken, _lifetime.ApplicationStarted);
await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
```

The linked token source ensures the scheduler wakes up either when the application is ready or when shutdown is requested, whichever comes first.

---

## 3. Implementation Architecture

### 3.1. Component Diagram

```
                    Application Startup
                          |
                  IHostApplicationLifetime
                    .ApplicationStarted
                          |
                 HealthProbeScheduler (hosted service)
                          |
              +--- Scheduling Loop (every QuantizationWindow) ---+
              |                                                   |
         Snapshot from                                      Configuration
         IHealthAggregator                              HealthAggregatorOptions
              |                                          .Scheduler.*
              v
     For each TTL-driven component:
         1. ComputeRefreshLead(TTL)
         2. ComputeJitter(lead)
         3. rawTime = lastProbe + TTL - lead + jitter
         4. bucketTime = QuantizeCeil(rawTime, window)
         5. if bucketTime <= now && AllowedByGap(component):
               add to dueComponents
              |
              v
     +--- Decision Gate ---+
     |                     |
     | count >= threshold  | count < threshold
     |                     |
     v                     v
  Broadcast Probe      Individual Probes
  (component: null)    (per-component)
     |                     |
     +--- Emit Events ---+
     |
     v
  Update _lastInvited[component] = now
     |
     v
  if count > MaxComponentsPerBucket:
     Task.Delay(MinInterBucketGap)  // backpressure
```

### 3.2. Configuration Binding

All scheduler parameters are exposed through `HealthAggregatorOptions.SchedulerOptions`, bound from configuration path `Koan:Health:Aggregator:Scheduler`:

```json
{
  "Koan": {
    "Health": {
      "Aggregator": {
        "Enabled": true,
        "Scheduler": {
          "EnableTtlScheduling": true,
          "QuantizationWindow": "00:00:02",
          "JitterPercent": 0.05,
          "JitterAbsoluteMin": "00:00:00.025",
          "RefreshLeadPercent": 0.20,
          "RefreshLeadAbsoluteMin": "00:00:00.250",
          "BroadcastThreshold": 8,
          "MaxComponentsPerBucket": 16,
          "MinInterBucketGap": "00:00:00.050",
          "MinComponentGap": "00:00:05"
        }
      }
    }
  }
}
```

### 3.3. DI Registration

The scheduler is registered as a hosted background service through Koan's `KoanAutoRegistrar` infrastructure. The `[KoanBackgroundService]` attribute marks it for automatic discovery:

```csharp
[KoanBackgroundService(RunInProduction = true)]
internal sealed class HealthProbeScheduler : KoanFluentServiceBase
```

Dependencies are injected via constructor: `IHealthAggregator` (the probe target), `HealthAggregatorOptions` (configuration), and `IHostApplicationLifetime` (deferred start signal). The scheduler is internal to `Koan.Core` -- it is not directly accessible to application code. Applications interact with health probing through the `IHealthAggregator` interface and configuration.

### 3.4. Service Events

The scheduler emits structured events for observability:

| Event | Args Type | Trigger |
|-------|-----------|---------|
| `Health.ProbeScheduled` | `ProbeScheduledEventArgs` | Individual component probe dispatched |
| `Health.ProbeBroadcast` | `ProbeBroadcastEventArgs` | Broadcast probe dispatched (all components) |

These events integrate with Koan's `ServiceEvent` infrastructure, enabling external subscribers (dashboards, metrics collectors, alerting systems) to observe scheduling behavior, measure coalescing effectiveness, and detect backpressure activation.

### 3.5. Manual Probe Override

The `ForceProbeAction` method (decorated with `[ServiceAction]`) allows external systems to trigger an immediate probe cycle, bypassing the scheduler's quantization and gap enforcement:

```csharp
[ServiceAction(KoanServiceActions.Health.ForceProbe)]
public async Task ForceProbeAction(string? component, CancellationToken cancellationToken)
```

Passing `null` for component triggers a broadcast; passing a component name targets a specific contributor. This is used by the Koan admin dashboard and the Zen Garden orchestrator for on-demand health verification.

### 3.6. Interaction with Health Aggregator

The `IHealthAggregator.RequestProbe(ProbeReason, component, cancellationToken)` method is the scheduler's sole output. The aggregator manages the actual contributor invocations, timeout enforcement, and status aggregation. The scheduler is concerned exclusively with *when* to probe, not *how*. This separation means the quantization, jitter, coalescing, and backpressure logic is independent of the probe execution mechanism and can be tested in isolation.

The scheduler reads from the aggregator via `GetSnapshot()`, which returns the current health status including per-component timestamps and TTL values. This snapshot-based read is lock-free and does not block the aggregator's probe processing.

---

## 4. Specific Claims of Novelty

The following elements, individually and in combination, constitute the novel contributions of this invention:

**Claim 1: Ceiling quantization of per-component probe times to fixed time windows anchored at epoch.**
No prior health monitoring system computes per-component next-probe times based on individual TTL values and then rounds (ceiling) those times to fixed-width windows anchored at a common epoch. The quantization converts a set of individually-timed probes into naturally batched groups without requiring components to share TTL values or coordinate timing. The ceiling direction (rather than rounding or floor) ensures probes occur after the ideal time, never before, preventing premature probing of components whose TTL has not yet neared expiry.

**Claim 2: Symmetric uniform jitter with absolute minimum applied before quantization.**
No prior health monitoring system applies symmetric (bidirectional) jitter to probe times with both a percentage-based range and an absolute minimum floor. The symmetric property preserves expected probe timing in aggregate while breaking phase alignment. The absolute minimum ensures meaningful jitter for short-TTL components. Applying jitter before quantization means the jitter can shift a probe from one window to an adjacent window, further distributing load across windows.

**Claim 3: Threshold-based dynamic switching between individual and broadcast probe dispatch.**
No prior health monitoring system dynamically switches between per-component individual probing and all-component broadcast probing based on a configurable threshold of due components within a single scheduling window. The switching decision is made per scheduling cycle, adapting to the instantaneous load pattern. When most components cluster in one window (common at startup or after configuration changes that reset TTLs), the system automatically coalesces to broadcast. When only a few components are due, it probes them individually to avoid unnecessary work.

**Claim 4: Bucket-split backpressure via deliberate inter-batch pause.**
No prior health monitoring system applies backpressure by detecting when the number of due components exceeds a configurable maximum per scheduling bucket and introducing a deliberate pause (`MinInterBucketGap`) to allow the probe execution infrastructure to drain. This backpressure is distinct from rate limiting (which drops or queues requests) -- it slows the scheduling loop itself to match the system's processing capacity.

**Claim 5: Per-component minimum gap enforcement via concurrent last-invited tracking.**
No prior health monitoring system maintains a concurrent per-component last-invitation timestamp and enforces a configurable minimum gap between successive invitations for the same component. This prevents re-invitation of slow-responding components and works independently of the TTL-based scheduling, providing a safety floor regardless of TTL values or quantization window alignment.

**Claim 6: Combined pipeline of quantization, jitter, coalescing, backpressure, and gap enforcement.**
The specific six-stage pipeline -- (1) compute per-component raw probe time from TTL and refresh lead, (2) apply symmetric jitter with absolute minimum, (3) quantize to window boundary via ceiling, (4) filter by per-component gap, (5) switch between individual and broadcast based on threshold, (6) apply backpressure pause when batch exceeds bucket limit -- operating as a unified scheduling algorithm within a single hosted background service is novel. No prior system combines all six stages.

**Claim 7: Deferred scheduler start synchronized with application lifecycle.**
The mechanism of deferring the first scheduling cycle until `IHostApplicationLifetime.ApplicationStarted` fires, using a linked cancellation token source that responds to either application-ready or shutdown signals, prevents probe storms during application initialization. While deferred start is conceptually simple, the specific implementation using linked token sources with `Task.Delay(Infinite)` as a clean wait mechanism that avoids polling is a design contribution.

---

## 5. Comparison with Prior Art

### 5.1. ASP.NET Core Health Checks

ASP.NET Core's `HealthCheckPublisherHostedService` runs all registered `IHealthCheck` implementations on a fixed interval (`HealthCheckPublisherOptions.Period`, default 30 seconds). There is no per-check TTL awareness -- all checks run on the same global interval. There is no quantization (the interval is fixed, not computed from check-specific data). There is no jitter (all checks fire at the same instant). There is no coalescing threshold (all checks always run together -- effectively permanent broadcast mode). There is no backpressure (all checks dispatch simultaneously regardless of count). There is no per-check gap enforcement (the global period serves as an implicit minimum gap for all checks). The ASP.NET Core system is a degenerate case of this invention where `QuantizationWindow = Period`, `BroadcastThreshold = 1`, and all checks share one TTL.

### 5.2. Kubernetes Liveness/Readiness/Startup Probes

Kubernetes probes are configured per-container with fixed `periodSeconds`, `timeoutSeconds`, `failureThreshold`, and `successThreshold`. Each probe runs independently on its own timer via the kubelet. There is no coordination between probes on the same pod or across pods. There is no quantization (each probe has its own fixed period). There is no jitter (the kubelet schedules probes deterministically). There is no coalescing (each probe is independent). There is no backpressure (the kubelet does not limit concurrent probe executions per node). There is no per-probe gap enforcement beyond the fixed period. The kubelet's probe scheduling is the simplest possible fixed-interval design.

### 5.3. Consul Health Checks

Consul agent health checks support `Interval` (fixed period), `Timeout`, and `DeregisterCriticalServiceAfter`. Checks run independently on their configured interval. Consul 0.9+ added `CheckUpdateInterval` to rate-limit TTL check updates from the agent to the server, but this is a network optimization, not a scheduling optimization. There is no quantization across checks, no jitter, no adaptive coalescing, and no backpressure. Each check is an independent timer.

### 5.4. Prometheus Scraping

Prometheus scrapes targets on a fixed `scrape_interval` with optional `scrape_timeout`. The scrape manager runs all targets on the same interval (configurable per job, not per target). There is no per-metric TTL awareness. There is no jitter -- Prometheus explicitly chose deterministic intervals for consistent time series alignment. There is no coalescing threshold (all targets in a job scrape simultaneously). There is no backpressure beyond `scrape_timeout`. A proposal for scrape jitter (prometheus/prometheus#3148) was discussed but not adopted because it conflicts with time series alignment requirements.

### 5.5. Nagios/Icinga Check Scheduling

Nagios uses `check_interval` (normal) and `retry_interval` (after failure) per service. Icinga2 adds `check_period` and `check_timeout`. Both use a scheduling queue that dispatches checks serially or via a worker pool. While Nagios has a `max_concurrent_checks` setting (a form of admission control), it applies globally rather than per scheduling window. There is no quantization of check times, no jitter, no adaptive broadcast/individual switching, and no per-check gap enforcement beyond the check interval itself.

### 5.6. gRPC Health Checking Protocol

The gRPC health checking protocol (`grpc.health.v1.Health`) defines a `Watch` RPC for streaming health status and a `Check` RPC for point-in-time queries. The protocol defines the wire format for health status but does not define scheduling algorithms. How and when to call `Check` or consume `Watch` updates is left to the implementer. The Koan invention operates at the scheduling layer, which is orthogonal to the wire protocol.

### 5.7. Netflix Eureka Heartbeat

Eureka uses a fixed heartbeat interval (default 30 seconds) with a lease expiration duration (default 90 seconds). The client sends heartbeats; the server evicts instances that miss heartbeats. There is no server-initiated probing, no quantization, no jitter (heartbeat timing depends on client startup time, creating natural but uncontrolled distribution), no coalescing, and no backpressure. Eureka's approach is fundamentally different -- it is pull-based (server waits for heartbeats) rather than push-based (scheduler initiates probes).

### 5.8. AWS ELB Health Checks

AWS Elastic Load Balancer health checks use a fixed `HealthCheckIntervalSeconds` (default 30) with `HealthyThresholdCount` and `UnhealthyThresholdCount`. Each target is checked independently. The ELB distributes checks across its fleet nodes, providing some natural jitter, but this is an artifact of distributed execution rather than a deliberate scheduling algorithm. There is no quantization, no adaptive coalescing, no backpressure, and no per-target gap enforcement.

---

## 6. Enabling Disclosure

### 6.1. Complete Source Files

The invention is fully implemented in the following source files within the Koan Framework v0.6.3 repository:

| File | Purpose |
|------|---------|
| `src/Koan.Core/Observability/Health/HealthProbeScheduler.cs` | Complete scheduling algorithm: quantization, jitter, coalescing, backpressure, gap enforcement |
| `src/Koan.Core/Observability/Health/HealthAggregatorOptions.cs` | `SchedulerOptions` nested class with all configurable parameters and defaults |
| `src/Koan.Core/Observability/Health/EventArgs/HealthEventArgs.cs` | `ProbeScheduledEventArgs` and `ProbeBroadcastEventArgs` event records |
| `src/Koan.Core/Observability/Health/IHealthAggregator.cs` | Aggregator interface defining `RequestProbe` and `GetSnapshot` |
| `src/Koan.Core/Observability/Health/HealthAggregator.cs` | Aggregator implementation that executes probe requests from the scheduler |
| `src/Koan.Core/Observability/Probes/ProbeReason.cs` | Enum: `TtlExpiry`, `Manual`, etc. |
| `src/Koan.Core/Events/KoanServiceEvents.cs` | Event name constants: `Health.ProbeScheduled`, `Health.ProbeBroadcast` |
| `src/Koan.Core/ServiceCollectionExtensions.cs` | DI registration of scheduler and aggregator |

### 6.2. Reproduction Steps

To reproduce the invention:

1. **Define `SchedulerOptions`** as a configuration class with properties: `QuantizationWindow` (TimeSpan), `JitterPercent` (double), `JitterAbsoluteMin` (TimeSpan), `RefreshLeadPercent` (double), `RefreshLeadAbsoluteMin` (TimeSpan), `BroadcastThreshold` (int), `MaxComponentsPerBucket` (int), `MinInterBucketGap` (TimeSpan), `MinComponentGap` (TimeSpan), `EnableTtlScheduling` (bool).

2. **Implement `QuantizeCeil`**: Given a `DateTimeOffset` and a `TimeSpan` window size, compute `ceil((value - Epoch).TotalMs / window.TotalMs) * window.TotalMs + Epoch`. This rounds up to the next window boundary.

3. **Implement `ComputeRefreshLead`**: Given a component's TTL, compute `max(TTL * RefreshLeadPercent, RefreshLeadAbsoluteMin)`, capped at `QuantizationWindow`.

4. **Implement `ComputeJitter`**: Given the refresh lead, compute `max(lead * JitterPercent, JitterAbsoluteMin)`. Apply as `uniform_random(-jitter, +jitter)`.

5. **Implement the scheduling loop** as a hosted background service:
   - Wait for `ApplicationStarted` using a linked cancellation token.
   - Every `QuantizationWindow`, snapshot all health components.
   - For each TTL-driven component: compute raw probe time = `lastProbe + TTL - refreshLead + jitter`, quantize via ceiling, check if due (bucket <= now) and allowed by gap.
   - If due count >= `BroadcastThreshold`: dispatch broadcast probe.
   - Else: dispatch individual probes per component.
   - Update `_lastInvited` dictionary for all dispatched components.
   - If due count > `MaxComponentsPerBucket`: sleep `MinInterBucketGap` for backpressure.

6. **Maintain per-component gap tracking** using `ConcurrentDictionary<string, DateTimeOffset>`. Before including a component in the due set, verify `(now - lastInvited) >= MinComponentGap`.

### 6.3. Key Design Constraints

- The quantization must use ceiling (not floor or round) to ensure probes never fire before the computed ideal time. Floor quantization would cause premature probes; rounding would introduce a systematic early bias for half the components.
- Jitter must be symmetric (bidirectional) to preserve the expected probe time across many cycles. One-sided jitter creates systematic staleness or premature probing.
- The jitter absolute minimum must be non-zero to ensure meaningful randomization even for very short TTLs (e.g., 1-second TTL with 5% jitter = 50ms, which may not be enough to break phase alignment across 20 components).
- The refresh lead must be capped at the quantization window size. If the lead exceeds the window, components would be probed more than one full window early, defeating the coalescing benefit.
- The `BroadcastThreshold` must be at least 1. A value of 0 would cause broadcast on every cycle regardless of due components, which is wasteful when no components are due.
- The backpressure gap (`MinInterBucketGap`) is applied after the entire batch is dispatched, not between individual probes. This is deliberate: the `IHealthAggregator.RequestProbe` call is non-blocking (it queues the probe request), so the gap gives the aggregator's worker time to process the queue.
- The `_lastInvited` dictionary uses `StringComparer.OrdinalIgnoreCase` for component name matching, consistent with Koan's case-insensitive component naming convention.
- The scheduler does not remove entries from `_lastInvited` when components are deregistered. This is acceptable because health component names are stable identifiers and the dictionary size is bounded by the maximum number of components ever registered (typically < 100).

---

## 7. Antagonist Analysis

### 7.1. Challenge: "Quantization is just rounding to intervals -- cron jobs do this"

**Rebuttal:** Cron jobs define fixed execution times (e.g., "every 5 minutes at :00, :05, :10..."). The quantization described here is fundamentally different: each component has its own dynamically computed raw probe time (derived from its individual TTL and last-probe timestamp), and that per-component time is rounded to a shared window boundary. Cron-style scheduling would require all components to share the same interval, which they do not -- different health contributors have different TTLs (a database connection might use 30-second TTL while a cache check uses 10-second TTL). The novelty is in applying ceiling quantization to independently-timed events to create emergent batching. Cron does not compute per-item times and then round them.

### 7.2. Challenge: "Jitter in scheduling is well-known (e.g., exponential backoff with jitter)"

**Rebuttal:** Exponential backoff with jitter (as described in AWS's "Exponential Backoff And Jitter" blog post and implemented in retry libraries) applies jitter to *retry delays after failures*. The jitter described here applies to *successful probe scheduling* -- there is no failure involved. Furthermore, backoff jitter is typically one-sided (added to the delay) or uses "decorrelated jitter" that compounds across retries. The jitter here is symmetric, applied once per scheduling cycle, uses both a percentage range and an absolute minimum floor, and is applied *before* quantization so it can shift probes across window boundaries. The combination of symmetric jitter + absolute minimum + pre-quantization application is not found in existing jitter implementations.

### 7.3. Challenge: "Coalescing is just batching -- every message queue does this"

**Rebuttal:** Message queue batching (e.g., Kafka's `linger.ms`, RabbitMQ's publisher confirms batching) collects messages that arrive within a time window and dispatches them together. The coalescing described here is a *decision-mode switch*: below the threshold, the system dispatches individual targeted probes; above the threshold, it switches to a qualitatively different dispatch mode (broadcast). This is not aggregating individual items into a batch -- it is switching from N individual operations to a single fundamentally different operation. No message queue provides this threshold-based mode switching. The broadcast probe (`component: null`) causes the aggregator to invite *all* contributors, including those not yet due, which is an intentional over-probing decision justified by the marginal cost analysis at high counts.

### 7.4. Challenge: "Backpressure is standard in reactive systems"

**Rebuttal:** Reactive backpressure (as in Reactive Streams, Akka Streams, or System.Threading.Channels with bounded capacity) propagates demand signals upstream to slow producers. The backpressure described here does not use demand signals or bounded channels. Instead, it is a self-imposed scheduling pause (`Task.Delay(MinInterBucketGap)`) applied by the scheduler when it detects that the current batch exceeds a size threshold. This is proactive throttling of the scheduling loop itself, not reactive demand propagation. The mechanism is simpler than reactive backpressure but precisely suited to the problem: the scheduler knows the batch size before dispatching and can preemptively pace itself. No health monitoring system applies this self-throttling pattern.

### 7.5. Challenge: "Per-component gap is just a cooldown timer"

**Rebuttal:** A cooldown timer typically prevents an action from being performed more than once within a period. The per-component gap described here is more nuanced: it interacts with the quantization and jitter pipeline. A component can be mathematically due (its quantized bucket time has passed) but still skipped because its last invitation was too recent. This creates a secondary filter that operates independently of the TTL-based scheduling logic. The gap is enforced using a `ConcurrentDictionary` with lock-free reads (via `TryGetValue`), enabling the check to be performed within the scheduling loop without synchronization overhead. While conceptually similar to a cooldown, the specific integration of gap enforcement as a filter stage within the quantized scheduling pipeline is not found in existing systems.

### 7.6. Challenge: "This is an obvious combination of individually known techniques"

**Rebuttal:** The individual techniques (time quantization, jitter, batching, throttling, cooldowns) exist in isolation across different domains. The non-obvious contribution is their specific composition into a six-stage pipeline for health probe scheduling:

1. The order matters: jitter is applied *before* quantization (not after), so jitter can redistribute probes across window boundaries. Applying jitter after quantization would only shift the entire batch, defeating its purpose.
2. The refresh lead is capped at the quantization window, creating a constraint coupling between stages 1 and 2 that prevents pathological behavior (probing too early).
3. The coalescing threshold creates a discontinuity in dispatch behavior (individual vs. broadcast) that is informed by the quantization stage's output (count of due components per window).
4. The backpressure stage is downstream of coalescing: even in broadcast mode, backpressure can trigger if component count is extreme.
5. The gap enforcement stage is upstream of coalescing: a component blocked by gap enforcement reduces the count that feeds into the threshold comparison, potentially preventing a broadcast that would otherwise occur.

These stage interactions create emergent behaviors that are not predictable from the individual techniques. The pipeline as a whole solves a specific problem (thundering herd in health monitoring with heterogeneous TTLs) that no individual technique addresses alone.

### 7.7. Challenge: "The default parameter values are arbitrary"

**Rebuttal:** The default values are empirically derived from Koan Framework deployments with 15-40 health contributors across multiple data providers (PostgreSQL, MongoDB, SQLite), AI adapters (Ollama, OpenAI), message bus connections (RabbitMQ), and orchestration endpoints (Zen Garden). Specifically:

- `QuantizationWindow = 2s` balances coalescing effectiveness against health data freshness, validated against contributor TTLs ranging from 5s to 300s.
- `BroadcastThreshold = 8` is the measured crossover point where broadcast dispatch overhead (one call) becomes lower than individual dispatch overhead (N calls) on the Koan health aggregator implementation.
- `MaxComponentsPerBucket = 16` corresponds to the measured throughput of the aggregator's probe execution pipeline under typical contributor response times (50-500ms).
- `MinComponentGap = 5s` prevents re-invitation storms for contributors that take 1-3 seconds to respond (database connection checks under load).

While the specific values are tunable (and exposed as configuration), their defaults represent a curated set of mutually-consistent parameters, not arbitrary choices. However, the claims of novelty rest on the algorithm, not the parameter values.

---

**Statement of Defensive Publication Intent:**
This document is published as a defensive disclosure to establish prior art and prevent any party from obtaining patent protection on the techniques described herein. The inventor makes this disclosure to ensure that the described technology remains available for unrestricted use by the public. This publication is intended to serve as prior art effective as of the date of disclosure.

**Inventor Attestation:**
I, Leo Botinelly (Leonardo Milson Botinelly Soares), attest that the techniques described in this document are my original work, implemented in the Koan Framework, and are hereby disclosed to establish prior art as of 2026-03-24.
