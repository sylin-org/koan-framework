using Microsoft.Extensions.Hosting;

namespace Sora.Core.BackgroundServices;

/// <summary>
/// Primary interface for Sora background services with auto-discovery and management
/// </summary>
public interface ISoraBackgroundService
{
    /// <summary>
    /// Unique name of the service (defaults to class name)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Main execution method for the background service
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Check if the service is ready to start execution
    /// </summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>
/// Services that can be triggered on-demand via commands
/// </summary>
public interface ISoraPokableService : ISoraBackgroundService
{
    /// <summary>
    /// Handles external commands/triggers sent to this service
    /// </summary>
    Task HandleCommandAsync(ServiceCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the command types this service can handle
    /// </summary>
    IReadOnlyCollection<Type> SupportedCommands { get; }
}

/// <summary>
/// Services that run periodically on a schedule
/// </summary>
public interface ISoraPeriodicService : ISoraBackgroundService
{
    /// <summary>
    /// Interval between periodic executions
    /// </summary>
    TimeSpan Period { get; }

    /// <summary>
    /// Initial delay before first execution
    /// </summary>
    TimeSpan InitialDelay => TimeSpan.Zero;

    /// <summary>
    /// Whether to run once immediately on startup
    /// </summary>
    bool RunOnStartup => false;
}

/// <summary>
/// Services that run once during application startup in a specific order
/// </summary>
public interface ISoraStartupService : ISoraBackgroundService
{
    /// <summary>
    /// Execution order (lower numbers run first)
    /// </summary>
    int StartupOrder { get; }
}