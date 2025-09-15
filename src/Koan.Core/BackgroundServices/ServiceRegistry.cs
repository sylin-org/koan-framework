using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.BackgroundServices;

/// <summary>
/// Default implementation of service registry
/// </summary>
public class ServiceRegistry : IServiceRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, IKoanBackgroundService> _servicesByName = new();
    private readonly Dictionary<Type, IKoanBackgroundService> _servicesByType = new();

    public ServiceRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T GetService<T>() where T : class, IKoanBackgroundService
    {
        if (_servicesByType.TryGetValue(typeof(T), out var cached))
            return (T)cached;

        var service = _serviceProvider.GetRequiredService<T>();
        RegisterService(service);
        return service;
    }

    public IKoanBackgroundService? GetService(string serviceName)
    {
        return _servicesByName.TryGetValue(serviceName, out var service) ? service : null;
    }

    public IEnumerable<IKoanBackgroundService> GetAllServices()
    {
        return _servicesByName.Values;
    }

    public void RegisterService<T>(T service) where T : class, IKoanBackgroundService
    {
        _servicesByName[service.Name] = service;
        _servicesByType[typeof(T)] = service;
    }
}