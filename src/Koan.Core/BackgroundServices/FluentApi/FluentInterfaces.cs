using Koan.Core.BackgroundServices;
namespace Koan.Core.BackgroundServices;

/// <summary>
/// Service information queries
/// </summary>
public interface IServiceQueryBuilder
{
    /// <summary>
    /// Get current service status
    /// </summary>
    Task<ServiceStatus> GetStatusAsync();

    /// <summary>
    /// Get service health information
    /// </summary>
    Task<ServiceHealth> GetHealthAsync();

    /// <summary>
    /// Get detailed service information
    /// </summary>
    Task<ServiceInfo> GetInfoAsync();
}

/// <summary>
/// Unified builder interface supporting chaining between actions, events, and queries
/// </summary>
/// <typeparam name="T">The background service type</typeparam>
public interface IServiceBuilder<T> where T : IKoanBackgroundService
{
    /// <summary>
    /// Execute a service action
    /// </summary>
    IServiceActionBuilder Do(string action, object? parameters = null);

    /// <summary>
    /// Subscribe to a service event
    /// </summary>
    IServiceEventBuilder On(string eventName);

    /// <summary>
    /// Query service information
    /// </summary>
    IServiceQueryBuilder Query();
}

/// <summary>
/// Fluent builder for service action execution
/// </summary>
public interface IServiceActionBuilder
{
    /// <summary>
    /// Execute the configured actions
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Set action priority
    /// </summary>
    IServiceActionBuilder WithPriority(int priority);

    /// <summary>
    /// Set action timeout
    /// </summary>
    IServiceActionBuilder WithTimeout(TimeSpan timeout);

    /// <summary>
    /// Set correlation ID for tracking
    /// </summary>
    IServiceActionBuilder WithCorrelationId(string correlationId);
}

/// <summary>
/// Fluent builder for event subscription
/// </summary>
public interface IServiceEventBuilder
{
    IServiceEventBuilder On(string eventName);
    IServiceEventBuilder Do<TEventArgs>(Func<TEventArgs, Task> handler);
    IServiceEventBuilder Do(Func<Task> handler);
    IServiceEventBuilder Once();
    IServiceEventBuilder WithFilter<TEventArgs>(Func<TEventArgs, bool> filter);
    Task<IDisposable> SubscribeAsync();
// ...existing code...

/// <summary>
/// Service information queries
/// </summary>
public interface IServiceQueryBuilder
{
    /// <summary>
    /// Get current service status
    /// </summary>
    Task<ServiceStatus> GetStatusAsync();

    /// <summary>
    /// Get service health information
    /// </summary>
    Task<ServiceHealth> GetHealthAsync();

    /// <summary>
    /// Get detailed service information
    /// </summary>
    Task<ServiceInfo> GetInfoAsync();
}
}