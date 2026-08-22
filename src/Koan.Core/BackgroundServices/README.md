# Koan Background Services

Long-running work that belongs to the host rather than to a request: a poller, a scheduler, a warm-up
step. Declare the class, and `AddKoan()` discovers, registers, and supervises it — including its health
contribution and its shutdown.

For retryable, schedulable *business* work with a durable ledger, use [Koan Jobs](../../Koan.Jobs/README.md)
instead. These services are infrastructure; a job is an Entity.

## Choose a base

| Base | For | You implement |
| --- | --- | --- |
| `KoanBackgroundServiceBase` | a continuous loop | `ExecuteCore` |
| `KoanPokablePeriodicServiceBase` | work on an interval that can also be triggered on demand | `Period`, `ExecutePeriodic` |
| `KoanStartupServiceBase` | one-time work during startup, ordered | `StartupOrder`, `ExecuteCore` |
| `KoanFluentServiceBase` | a service exposing named actions and events | `ExecuteCore`, plus `[ServiceAction]` methods |

Every base derives from `KoanBackgroundServiceBase`. `ExecuteCore(CancellationToken)` is the loop those
bases run; the periodic base implements it for you and calls `ExecutePeriodic` on each tick. `ExecuteAsync`
is sealed by the base and delegates inward, so overriding it steps outside this supervision.

## A continuous service

```csharp
[KoanBackgroundService]
public sealed class InventorySync : KoanBackgroundServiceBase
{
    public InventorySync(ILogger<InventorySync> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override async Task ExecuteCore(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            Logger.LogInformation("Reconciling inventory");
        }
    }
}
```

`Name`, `IsCritical`, `IsReady`, and `Check` are virtual. The base implements `IHealthContributor`, so
a service that overrides `Check` reports into `/health/ready` with no further registration; marking it
`IsCritical` makes an unhealthy result fail readiness.

## A periodic service

```csharp
[KoanPeriodicService(IntervalSeconds = 3600)]
public sealed class Cleanup : KoanPokablePeriodicServiceBase
{
    public Cleanup(ILogger<Cleanup> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override TimeSpan Period => TimeSpan.FromHours(1);
    public override bool RunOnStartup => true;

    protected override async Task ExecutePeriodic(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Running cleanup");
        await Task.CompletedTask;
    }
}
```

`Period`, `InitialDelay`, and `RunOnStartup` shape the schedule. Because this base is also pokable, the
same work can be demanded immediately through a `TriggerNowCommand` without waiting for the interval.

## Actions and events

`KoanFluentServiceBase` lets a service publish named actions and emit named events. Declare the events
on the class and mark the actions on their methods:

```csharp
[KoanBackgroundService]
[ServiceEvent("WorkCompleted", EventArgsType = typeof(WorkCompletedArgs))]
[ServiceEvent("WorkFailed", EventArgsType = typeof(WorkFailedArgs))]
public sealed class WorkerService : KoanFluentServiceBase
{
    public WorkerService(ILogger<WorkerService> logger, IConfiguration configuration)
        : base(logger, configuration) { }

    [ServiceAction("process-item")]
    public async Task ProcessItem(string itemId, CancellationToken cancellationToken)
    {
        try
        {
            await DoWork(itemId, cancellationToken);
            await EmitEvent("WorkCompleted", new WorkCompletedArgs(itemId));
        }
        catch (Exception ex)
        {
            await EmitEvent("WorkFailed", new WorkFailedArgs(itemId, ex.Message));
            throw;
        }
    }

    public override Task ExecuteCore(CancellationToken cancellationToken)
        => Task.Delay(Timeout.Infinite, cancellationToken);
}
```

An action runs through `ExecuteAction(actionName, parameters, cancellationToken)`. A subscriber takes a
handler and gets back an `IDisposable` that ends the subscription:

```csharp
using var subscription = worker.SubscribeToEvent(
    "WorkCompleted",
    args => { Logger.LogInformation("Done: {Args}", args); return Task.CompletedTask; },
    once: false);
```

`SubscribeToEvent` also accepts a `filter`, so a subscriber can decline events it does not care about
without waking its handler.

## Registration and configuration

Discovery is part of the ordinary bootstrap:

```csharp
builder.Services.AddKoan();
```

Configuration disables the whole set, bounds startup, or turns off one service by name:

```json
{
  "Koan": {
    "BackgroundServices": {
      "Enabled": true,
      "StartupTimeoutSeconds": 120,
      "Services": {
        "WorkerService": { "Enabled": false }
      }
    }
  }
}
```

Startup reporting names the services it discovered and started, so a service that never ran says so
there rather than in silence.
