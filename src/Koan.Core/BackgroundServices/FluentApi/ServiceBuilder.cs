using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.BackgroundServices;
using Koan.Core.Observability.Health;

namespace Koan.Core.BackgroundServices;

/// <summary>
/// Unified service builder that implements all fluent interfaces
/// </summary>
/// <typeparam name="T">The background service type</typeparam>
public class ServiceBuilder<T> : IServiceBuilder<T>, IServiceActionBuilder, IServiceEventBuilder
    where T : class, IKoanBackgroundService
{
    private readonly List<ActionConfig> _actions = new();
    private readonly List<EventConfig> _events = new();
    private EventConfig? _currentEvent;

    // Action implementation
    public IServiceActionBuilder Do(string action, object? parameters = null)
    {
        _actions.Add(new ActionConfig
        {
            Action = action,
            Parameters = parameters
        });
        return this;
    }

    // Event subscription implementation
    public IServiceEventBuilder On(string eventName)
    {
        _currentEvent = new EventConfig { EventName = eventName };
        _events.Add(_currentEvent);
        return this;
    }

    // Event handler implementation
    public IServiceEventBuilder Do<TEventArgs>(Func<TEventArgs, Task> handler)
    {
        if (_currentEvent == null)
            throw new InvalidOperationException("Do() must be preceded by On()");

        _currentEvent.Handler = args => handler((TEventArgs)args!);
        return this;
    }

    public IServiceEventBuilder Do(Func<Task> handler)
    {
        if (_currentEvent == null)
            throw new InvalidOperationException("Do() must be preceded by On()");

        _currentEvent.Handler = _ => handler();
        return this;
    }

    // Event configuration
    public IServiceEventBuilder Once()
    {
        if (_currentEvent != null)
            _currentEvent.IsOnce = true;
        return this;
    }

    public IServiceEventBuilder WithFilter<TEventArgs>(Func<TEventArgs, bool> filter)
    {
        if (_currentEvent != null)
            _currentEvent.Filter = args => args is TEventArgs typed && filter(typed);
        return this;
    }

    // Action configuration
    public IServiceActionBuilder WithPriority(int priority)
    {
        if (_actions.Any())
            _actions.Last().Priority = priority;
        return this;
    }

    public IServiceActionBuilder WithTimeout(TimeSpan timeout)
    {
        if (_actions.Any())
            _actions.Last().Timeout = timeout;
        return this;
    }

    public IServiceActionBuilder WithCorrelationId(string correlationId)
    {
        if (_actions.Any())
            _actions.Last().CorrelationId = correlationId;
        return this;
    }

    // Action execution
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
    var serviceRegistry = ServiceLocator.GetService<IServiceRegistry>();
    var service = serviceRegistry.GetService<T>();

        if (service is not KoanFluentServiceBase fluentService)
        {
            throw new InvalidOperationException($"Service {typeof(T).Name} must inherit from KoanFluentServiceBase for fluent API");
        }

        // Execute all actions in sequence
        foreach (var action in _actions)
        {
            using var cts = action.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;

            if (cts != null && action.Timeout.HasValue)
                cts.CancelAfter(action.Timeout.Value);

            var effectiveCt = cts?.Token ?? cancellationToken;

            await fluentService.ExecuteActionAsync(action.Action, action.Parameters, effectiveCt);
        }
    }

    // Event subscription execution
    public Task<IDisposable> SubscribeAsync()
    {
    var serviceRegistry = ServiceLocator.GetService<IServiceRegistry>();
    var service = serviceRegistry.GetService<T>();

        if (service is not KoanFluentServiceBase fluentService)
        {
            throw new InvalidOperationException($"Service {typeof(T).Name} must inherit from KoanFluentServiceBase for fluent API");
        }

        var subscriptions = new List<IDisposable>();

        // Subscribe to all events
        foreach (var eventConfig in _events)
        {
            if (eventConfig.Handler == null)
                continue;

            var subscription = fluentService.SubscribeToEvent(
                eventConfig.EventName,
                eventConfig.Handler,
                eventConfig.IsOnce,
                eventConfig.Filter);

            subscriptions.Add(subscription);
        }

        // Return composite disposable
    return Task.FromResult<IDisposable>(new CompositeDisposable(subscriptions));
    }

    // Query implementation
    public IServiceQueryBuilder Query() => new ServiceQueryBuilder<T>();

    // Configuration classes
    private class ActionConfig
    {
        public string Action { get; set; } = "";
        public object? Parameters { get; set; }
        public int Priority { get; set; }
        public TimeSpan? Timeout { get; set; }
        public string? CorrelationId { get; set; }
    }

    private class EventConfig
    {
        public string EventName { get; set; } = "";
        public Func<object?, Task>? Handler { get; set; }
        public bool IsOnce { get; set; }
        public Func<object?, bool>? Filter { get; set; }
    }

    private class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _disposables;

        public CompositeDisposable(List<IDisposable> disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                try { disposable?.Dispose(); }
                catch { /* Ignore disposal errors */ }
            }
        }
    }
}

/// <summary>
/// Service query builder implementation
/// </summary>
/// <typeparam name="T">The background service type</typeparam>
public class ServiceQueryBuilder<T> : IServiceQueryBuilder where T : class, IKoanBackgroundService
{
    public Task<ServiceStatus> GetStatusAsync()
    {
        var serviceRegistry = ServiceLocator.GetService<IServiceRegistry>();
        var service = serviceRegistry.GetService<T>();

        var status = new ServiceStatus
        {
            Name = service.Name,
            IsRunning = true, // TODO: Implement actual status tracking
            StartedAt = DateTimeOffset.UtcNow, // TODO: Get actual start time
            Type = typeof(T).Name
        };

        return Task.FromResult(status);
    }

    public async Task<ServiceHealth> GetHealthAsync()
    {
        var serviceRegistry = ServiceLocator.GetService<IServiceRegistry>();
        var service = serviceRegistry.GetService<T>();

        if (service is IHealthContributor healthContributor)
        {
            var healthReport = await healthContributor.CheckAsync();
            return new ServiceHealth
            {
                Name = service.Name,
                Status = healthReport.State switch
                {
                    HealthState.Healthy => ServiceHealthStatus.Healthy,
                    HealthState.Degraded => ServiceHealthStatus.Degraded,
                    HealthState.Unhealthy => ServiceHealthStatus.Unhealthy,
                    _ => ServiceHealthStatus.Unknown
                },
                Description = healthReport.Description,
                LastChecked = DateTimeOffset.UtcNow
            };
        }

        return new ServiceHealth
        {
            Name = service.Name,
            Status = ServiceHealthStatus.Healthy,
            Description = "No health check available",
            LastChecked = DateTimeOffset.UtcNow
        };
    }

    public Task<ServiceInfo> GetInfoAsync()
    {
        var serviceRegistry = ServiceLocator.GetService<IServiceRegistry>();
        var service = serviceRegistry.GetService<T>();

        var info = new ServiceInfo
        {
            Name = service.Name,
            Type = typeof(T).Name,
            Assembly = typeof(T).Assembly.GetName().Name ?? "",
            IsRunning = true, // TODO: Implement actual status tracking
            SupportedActions = GetSupportedActions(service),
            SupportedEvents = GetSupportedEvents(service),
            SupportedCommands = service is IKoanPokableService pokable ? pokable.SupportedCommands.Select(c => c.Name).ToArray() : Array.Empty<string>()
        };

        return Task.FromResult(info);
    }

    private string[] GetSupportedActions(IKoanBackgroundService service)
    {
        var type = service.GetType();
        var methods = type.GetMethods();
        var actions = methods
            .Where(m => m.GetCustomAttributes(typeof(ServiceActionAttribute), true).Any())
            .Select(m => ((ServiceActionAttribute)m.GetCustomAttributes(typeof(ServiceActionAttribute), true).First()).Name)
            .ToArray();

        return actions;
    }

    private string[] GetSupportedEvents(IKoanBackgroundService service)
    {
        var type = service.GetType();
        var events = type.GetCustomAttributes(typeof(ServiceEventAttribute), true)
            .Cast<ServiceEventAttribute>()
            .Select(attr => attr.Name)
            .ToArray();

        return events;
    }
}