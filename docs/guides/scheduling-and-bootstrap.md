# Scheduling and Bootstrap

This guide shows how to schedule simple jobs and how the Bootstrap preset works.

## Quick start

1. Add the module package (placeholder): `Koan.Scheduling`.
2. Enable scheduling (Dev default on, Prod default off):

appsettings.Development.json
{
"Koan": {
"Scheduling": {
"Enabled": true,
"ReadinessGate": true
}
}
}

3. Implement a task:

public sealed class WarmupEmbeddings : IScheduledTask, IOnStartup, IHasTimeout
{
public string Id => "warmup-embeddings";

    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken ct)
    {
        // call into Vector<TEntity> or Ai facade, small warmup
    }

}

4. That’s it. The orchestrator runs on startup, gates readiness until success or timeout, and reports health.

## Triggers

- IOnStartup — run once on app start.
- IFixedDelay — run forever with a delay between runs.
  - Properties: Delay (TimeSpan), Jitter? (optional)
- ICronScheduled — CRON expression using cronos, default timezone UTC.

Example: nightly at 02:00 UTC

public sealed class NightlyReindex : IScheduledTask, ICronScheduled
{
public string Id => "nightly-reindex";
public string Cron => "0 2 \* \* \*"; // minute hour day month dayOfWeek
public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
public Task RunAsync(CancellationToken ct) => Vector<MyDoc>.Rebuild(ct);
}

## Policies (optional)

- IHasTimeout — cancel a run after a budget.
- IIsCritical — if true and ReadinessGate is on, keeps readiness unhealthy until first success.
- IHasMaxConcurrency — per-task max concurrent runs (default 1).
- IProvidesLock — provide name/provider for distributed lock (e.g., Redis) to prevent multi-instance overlap.
- IAllowedWindows — calendar windows when the task may run.
- IHealthFacts — add custom facts to health snapshot.

## Attributes and config

You can use [Scheduled] to set defaults on the class; config always overrides attributes.

[Scheduled(FixedDelaySeconds = 300, Critical = true, TimeoutSeconds = 20)]
public sealed class SyncExternal : IScheduledTask, IFixedDelay, IHasTimeout, IIsCritical
{
public string Id => "sync-external";
public TimeSpan Delay => TimeSpan.FromMinutes(5);
public TimeSpan Timeout => TimeSpan.FromSeconds(20);
public Task RunAsync(CancellationToken ct) => DoWork(ct);
}

Override via config:

appsettings.json
{
"Koan": {
"Scheduling": {
"Jobs": {
"sync-external": {
"Enabled": true,
"FixedDelay": "00:10:00",
"Critical": false
}
}
}
}
}

## Bootstrap preset

Use the preset to run environment bring-up tasks.

appsettings.Development.json
{
"Koan": {
"Scheduling": {
"Enabled": true,
"Jobs": {
"bootstrap": {
"Runner": "bootstrap",
"OnStartup": true,
"Critical": true,
"Tasks": [
"ai:ensure-model:llama3:8b",
"vector:ensure-schema",
"db:seed-if-empty"
]
}
}
}
}
}

The Bootstrap runner maps task keys to provider-specific operations when those packages are referenced (e.g., Ollama ensure-model, Weaviate ensure-schema).

## Health and well-known

- Health contributors: one per task and one for the orchestrator.
- /.well-known/Koan/scheduling (when observability is exposed) returns enabled, readinessGate, capabilities, and a compact tasks snapshot.

## Error handling & best practices

- Keep tasks idempotent.
- Default to UTC for CRON/time windows.
- Use IHasTimeout to avoid stuck runs.
- Mark truly critical jobs as critical; gate readiness only when it’s required for the app to function.
