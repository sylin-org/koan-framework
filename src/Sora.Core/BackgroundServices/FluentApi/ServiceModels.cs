using Microsoft.Extensions.DependencyInjection;
namespace Sora.Core.BackgroundServices;

/// <summary>
/// Service status information
/// </summary>
public class ServiceStatus
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsRunning { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastActivity { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Service health information
/// </summary>
public enum ServiceHealthStatus { Healthy, Degraded, Unhealthy, Unknown }

public class ServiceHealth
{
    public string Name { get; set; } = "";
    public ServiceHealthStatus Status { get; set; }
    public string? Description { get; set; }
    public Exception? Exception { get; set; }
    public DateTimeOffset LastChecked { get; set; }
}

/// <summary>
/// Comprehensive service information
/// </summary>
public class ServiceInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Assembly { get; set; } = "";
    public bool IsRunning { get; set; }
    public string[] SupportedActions { get; set; } = Array.Empty<string>();
    public string[] SupportedEvents { get; set; } = Array.Empty<string>();
    public string[] SupportedCommands { get; set; } = Array.Empty<string>();
    public Dictionary<string, object> Configuration { get; set; } = new();
}

/// <summary>
/// Service registry for discovery and management
/// </summary>
public interface IServiceRegistry
{
    T GetService<T>() where T : class, ISoraBackgroundService;
    ISoraBackgroundService? GetService(string serviceName);
    IEnumerable<ISoraBackgroundService> GetAllServices();
    void RegisterService<T>(T service) where T : class, ISoraBackgroundService;
}

/// <summary>
/// Simple service locator for internal use
/// </summary>
internal static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;

    public static void SetProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static T GetService<T>() where T : class
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not initialized");
        
        return _serviceProvider.GetRequiredService<T>();
    }
}