using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.BackgroundServices;

/// <summary>
/// Service registry for discovery and management
/// </summary>
public interface IServiceRegistry
{
    T GetService<T>() where T : class, IKoanBackgroundService;
    IKoanBackgroundService? GetService(string serviceName);
    IEnumerable<IKoanBackgroundService> GetAllServices();
    void RegisterService<T>(T service) where T : class, IKoanBackgroundService;
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
