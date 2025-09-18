# Koan.Scheduling - Technical reference

Contract

- Inputs: ASP.NET Core host with DI; `IScheduledTask` implementations discovered via DI; options bound from `Koan:Scheduling`.
- Outputs: In-process background scheduler (HostedService) that triggers tasks on startup and on fixed delay; health facts per task.
- Errors: Task exceptions, timeouts, misconfigured options (invalid timespans), excessive concurrency, cancellation during shutdown.
- Success: Tasks run according to policy with bounded concurrency; health updated; no unhandled exceptions escape the orchestrator.

Scope and status

- Implemented triggers: OnStartup, FixedDelay.
- Reserved (Phase 2): Cron (`ICronScheduled`), Allowed windows (`IAllowedWindows`), Distributed lock (`IProvidesLock`). Interfaces exist; runtime gating will be added in a future revision.

Architecture

- Orchestrator: `SchedulingOrchestrator` is a `BackgroundService` that builds lightweight runners per discovered task.
- Discovery: Tasks are normal DI services implementing `IScheduledTask`; no bespoke reflection scans beyond reading `[Scheduled]` attributes for defaults.
- Merging: Effective job policy is merged from (highest to lowest): Options override → Attribute defaults (`[Scheduled]`) → Task interfaces (`IOnStartup`, `IFixedDelay`, `IHasTimeout`, `IHasMaxConcurrency`, `IIsCritical`).
- Health: Results are pushed to `IHealthAggregator` under keys `scheduling:task:{id}` with facts: id/state/critical/running/success/fail/lastError.

Options (`Koan:Scheduling`)

- Enabled (bool): default true in Development; default false in non-Dev unless the section exists. Controls orchestrator startup.
- ReadinessGate (bool): gate scheduling until app is "ready" (future use; placeholder wired in options).
- Jobs (map by Id): per-task overrides
  - Enabled (bool?) - disable a specific task
  - OnStartup (bool?) - run once at startup
  - FixedDelay (TimeSpan?) - periodic cadence (e.g., `"00:00:10"`)
  - Cron (string?) - reserved for Phase 2
  - Critical (bool?) - influences health reporting and ops runbook
  - Timeout (TimeSpan?) - per-invocation timeout
  - MaxConcurrency (int?) - concurrent runs allowed per task (default 1)
  - Runner/Tasks - reserved; allow pluggable runners like "bootstrap"

Triggers and time model

- OnStartup: fire-and-forget once after host starts. Combine with `FixedDelay` for periodic runs.
- Fixed delay: schedule next run as `now + delay` after each completion; 1-second polling loop keeps overhead low.
- Cron: interface exists; not yet active. Future behavior: calculate next occurrence in task-local or specified `TimeZone`.
- Allowed windows: interface exists; future gate to skip runs outside allowed time ranges.

Concurrency and timeouts

- Each task runner uses a `SemaphoreSlim` gate to bound concurrent executions (default 1).
- Timeout per run is enforced with a linked `CancellationTokenSource`; cancellation is treated as timeout with unhealthy health status.
- If a run is still active, the scheduler will not enqueue another run for the same task until a slot is available.

Error handling and health

- Exceptions are caught per run; runner records success/fail counters and the last error type/message.
- Health facts TTL: running (30s), terminal states (5m). Critical tasks should page when unhealthy per app policy.
- Avoid logging tokens/PII in task exceptions; prefer structured messages.

Operations

- Metrics: scheduled triggers, started runs, completions, failures/timeouts, concurrency saturation.
- Logs: include task id and run correlation; prefer structured logging.
- Runbook: timeouts → increase Timeout or reduce work; invalid config → fix options; saturation → raise `MaxConcurrency` if safe.
- Enable/disable: set `Koan:Scheduling:Enabled=false` or per-job `Enabled=false`.

Security

- Tasks run in-process as the app; ensure long-running or external calls are resilient, idempotent, and time-bounded.
- For future distributed locks (`IProvidesLock`), use leased locks with renewals and safe timeouts.

Examples

- Implementing a task with interfaces and attribute defaults:

```csharp
[Scheduled(FixedDelaySeconds = 10, OnStartup = true, TimeoutSeconds = 5, MaxConcurrency = 1, Critical = true)]
public sealed class CleanupTask : IScheduledTask, IHasTimeout, IFixedDelay, IIsCritical
{
    public string Id => "cleanup";
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public TimeSpan Delay => TimeSpan.FromSeconds(10);
    public Task RunAsync(CancellationToken ct) { /* work */ return Task.CompletedTask; }
}
```

- Minimal configuration (appsettings.json):

```json
{
  "Koan": {
    "Scheduling": {
      "Enabled": true,
      "Jobs": {
        "cleanup": {
          "OnStartup": true,
          "FixedDelay": "00:00:10",
          "Timeout": "00:00:05",
          "MaxConcurrency": 1,
          "Critical": true
        }
      }
    }
  }
}
```

References

- Engineering front door: `/docs/engineering/index.md`
- Architecture principles: `/docs/architecture/principles.md`
- Per-project docs pattern: `/docs/decisions/ARCH-0042-per-project-companion-docs.md`
