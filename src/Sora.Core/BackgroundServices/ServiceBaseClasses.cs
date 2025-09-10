using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Core.Observability.Health;

namespace Sora.Core.BackgroundServices;

/// <summary>
/// Base class for Sora background services with logging and health checks
/// </summary>
public abstract class SoraBackgroundServiceBase : BackgroundService, ISoraBackgroundService, IHealthContributor
{
    protected readonly ILogger Logger;
    protected readonly IConfiguration Configuration;
    protected DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    protected SoraBackgroundServiceBase(ILogger logger, IConfiguration configuration)
    {
        Logger = logger;
        Configuration = configuration;
    }

    public virtual string Name => GetType().Name;
    public virtual bool IsCritical => false;

    public abstract Task ExecuteCoreAsync(CancellationToken cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => ExecuteCoreAsync(stoppingToken);

    Task ISoraBackgroundService.ExecuteAsync(CancellationToken cancellationToken) => ExecuteCoreAsync(cancellationToken);

    public virtual Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public virtual Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var uptime = DateTimeOffset.UtcNow - StartedAt;
        return Task.FromResult(HealthReport.Healthy($"{Name} is running (uptime: {uptime:hh\\:mm\\:ss})"));
    }

    protected virtual void Dispose(bool disposing) { }
    public override void Dispose() { base.Dispose(); Dispose(true); }
}

/// <summary>
/// Base class for services that support the fluent API and events
/// </summary>
public abstract class SoraFluentServiceBase : SoraBackgroundServiceBase, ISoraPokableService
{
    private readonly Dictionary<string, Func<object?, CancellationToken, Task>> _actionHandlers = new();
    private readonly Dictionary<string, List<ServiceEventSubscription>> _eventSubscriptions = new();

    protected SoraFluentServiceBase(ILogger logger, IConfiguration configuration)
        : base(logger, configuration)
    {
        RegisterActionsAndEvents();
        RegisterDefaultHandlers();
    }

    public virtual IReadOnlyCollection<Type> SupportedCommands { get; } = new[]
    {
        typeof(TriggerNowCommand),
        typeof(HealthCheckCommand),
        typeof(StatusCommand)
    };

    public virtual async Task HandleCommandAsync(ServiceCommand command, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Handling command {CommandType} with correlation ID {CorrelationId}",
            command.GetType().Name, command.CorrelationId);

        try
        {
            switch (command)
            {
                case TriggerNowCommand trigger:
                    await HandleTriggerNow(trigger, cancellationToken);
                    break;
                case HealthCheckCommand health:
                    await HandleHealthCheck(health, cancellationToken);
                    break;
                case StatusCommand status:
                    await HandleStatus(status, cancellationToken);
                    break;
                default:
                    break;
            }

            Logger.LogDebug("Command {CommandType} handled successfully", command.GetType().Name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to handle command {CommandType}", command.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// Execute a named action with parameters
    /// </summary>
    public async Task ExecuteActionAsync(string actionName, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (!_actionHandlers.TryGetValue(actionName, out var handler))
        {
            throw new InvalidOperationException($"Action '{actionName}' not found on service '{Name}'");
        }

        Logger.LogInformation("Executing action '{ActionName}' on service '{ServiceName}'", actionName, Name);
        await handler(parameters, cancellationToken);
    }

    /// <summary>
    /// Emit an event to all subscribers
    /// </summary>
    protected internal async Task EmitEventAsync(string eventName, object? eventArgs = null)
    {
        if (!_eventSubscriptions.TryGetValue(eventName, out var subscriptions) || !subscriptions.Any())
            return;

        Logger.LogDebug("Emitting event '{EventName}' from service '{ServiceName}' to {SubscriberCount} subscribers",
            eventName, Name, subscriptions.Count);

        var tasks = subscriptions.ToList().Select(async sub =>
        {
            try
            {
                if (sub.Filter?.Invoke(eventArgs) == false)
                    return;

                await sub.Handler(eventArgs);

                if (sub.IsOnce)
                {
                    subscriptions.Remove(sub);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Event handler failed for event '{EventName}'", eventName);
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Subscribe to an event
    /// </summary>
    public IDisposable SubscribeToEvent(string eventName, Func<object?, Task> handler, bool once = false, Func<object?, bool>? filter = null)
    {
        if (!_eventSubscriptions.ContainsKey(eventName))
            _eventSubscriptions[eventName] = new();

        var subscription = new ServiceEventSubscription
        {
            Handler = handler,
            IsOnce = once,
            Filter = filter
        };

        _eventSubscriptions[eventName].Add(subscription);

        return new EventSubscriptionDisposable(() =>
        {
            if (_eventSubscriptions.TryGetValue(eventName, out var subs))
                subs.Remove(subscription);
        });
    }

    private void RegisterActionsAndEvents()
    {
        var methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var method in methods)
        {
            var actionAttr = method.GetCustomAttribute<ServiceActionAttribute>();
            if (actionAttr != null)
            {
                RegisterAction(actionAttr.Name, method);
            }
        }
    }

    private void RegisterAction(string actionName, MethodInfo method)
    {
        _actionHandlers[actionName] = async (parameters, ct) =>
        {
            var methodParams = method.GetParameters();
            var args = new List<object?>();

            // Handle different parameter scenarios
            if (methodParams.Length == 0)
            {
                // No parameters
            }
            else if (methodParams.Length == 1 && methodParams[0].ParameterType == typeof(CancellationToken))
            {
                args.Add(ct);
            }
            else if (methodParams.Length == 1)
            {
                args.Add(parameters);
            }
            else if (methodParams.Length == 2 && methodParams[1].ParameterType == typeof(CancellationToken))
            {
                args.Add(parameters);
                args.Add(ct);
            }

            var result = method.Invoke(this, args.ToArray());

            if (result is Task task)
                await task;
        };
    }

    protected virtual void RegisterDefaultHandlers()
    {
        // Override in derived classes to add custom command handlers
    }

    protected virtual async Task HandleTriggerNow(TriggerNowCommand command, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Service {ServiceName} triggered immediately", Name);
        await OnTriggerNow(cancellationToken);
    }

    protected virtual async Task HandleHealthCheck(HealthCheckCommand command, CancellationToken cancellationToken)
    {
        var health = await CheckAsync(cancellationToken);
    Logger.LogInformation("Health check result for {ServiceName}: {Status}", Name, health.State);
    }

    protected virtual Task HandleStatus(StatusCommand command, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Service {ServiceName} status: Running since {StartTime}", Name, StartedAt);
        return Task.CompletedTask;
    }

    protected virtual Task OnTriggerNow(CancellationToken cancellationToken) => Task.CompletedTask;

    private record ServiceEventSubscription
    {
        public Func<object?, Task> Handler { get; init; } = null!;
        public bool IsOnce { get; init; }
        public Func<object?, bool>? Filter { get; init; }
    }

    private class EventSubscriptionDisposable : IDisposable
    {
        private readonly Action _dispose;
        public EventSubscriptionDisposable(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}

/// <summary>
/// Base class for periodic services with pokeable capabilities
/// </summary>
public abstract class SoraPokablePeriodicServiceBase : SoraFluentServiceBase, ISoraPeriodicService
{
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);

    public abstract TimeSpan Period { get; }
    public virtual TimeSpan InitialDelay => TimeSpan.Zero;
    public virtual bool RunOnStartup => false;

    protected SoraPokablePeriodicServiceBase(ILogger logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public override IReadOnlyCollection<Type> SupportedCommands { get; } = new[]
    {
        typeof(TriggerNowCommand),
        typeof(CheckQueueCommand),
        typeof(ProcessBatchCommand),
        typeof(HealthCheckCommand),
        typeof(StatusCommand)
    };

    protected sealed override async Task ExecuteAsync(CancellationToken cancellationToken) => await ExecuteCoreAsync(cancellationToken);

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        StartedAt = DateTimeOffset.UtcNow;

        if (RunOnStartup && !cancellationToken.IsCancellationRequested)
        {
            Logger.LogInformation("{ServiceName} executing startup run", Name);
            await ExecuteWork(cancellationToken);
        }

        if (InitialDelay > TimeSpan.Zero)
        {
            Logger.LogInformation("{ServiceName} waiting {Delay} before starting periodic execution", Name, InitialDelay);
            await Task.Delay(InitialDelay, cancellationToken);
        }

        using var periodicTimer = new PeriodicTimer(Period);

        while (!cancellationToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(cancellationToken))
        {
            await ExecuteWork(cancellationToken);
        }
    }

    private async Task ExecuteWork(CancellationToken cancellationToken)
    {
        // Prevent concurrent execution
        if (!await _executionSemaphore.WaitAsync(100, cancellationToken))
        {
            Logger.LogDebug("{ServiceName} skipping execution - already running", Name);
            return;
        }

        try
        {
            await ExecutePeriodicAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "{ServiceName} periodic execution failed", Name);
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    protected abstract Task ExecutePeriodicAsync(CancellationToken cancellationToken);

    protected override async Task OnTriggerNow(CancellationToken cancellationToken)
    {
        Logger.LogInformation("{ServiceName} triggered for immediate execution", Name);
        await ExecuteWork(cancellationToken);
    }

    public override async Task HandleCommandAsync(ServiceCommand command, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case CheckQueueCommand checkQueue:
                Logger.LogInformation("{ServiceName} checking queue on demand", Name);
                await ExecuteWork(cancellationToken);
                break;
            case ProcessBatchCommand processBatch:
                Logger.LogInformation("{ServiceName} processing batch (size: {BatchSize})", Name, processBatch.BatchSize);
                await OnProcessBatch(processBatch.BatchSize, processBatch.Filter, cancellationToken);
                break;
            default:
                await base.HandleCommandAsync(command, cancellationToken);
                break;
        }
    }

    protected virtual Task OnProcessBatch(int? batchSize, string? filter, CancellationToken cancellationToken)
    {
        // Default implementation - just trigger normal work
        return ExecuteWork(cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _executionSemaphore?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Base class for startup services
/// </summary>
public abstract class SoraStartupServiceBase : SoraFluentServiceBase, ISoraStartupService
{
    protected SoraStartupServiceBase(ILogger logger, IConfiguration configuration)
        : base(logger, configuration) { }

    public abstract int StartupOrder { get; }
    public override bool IsCritical => true; // Startup failures are typically critical
}