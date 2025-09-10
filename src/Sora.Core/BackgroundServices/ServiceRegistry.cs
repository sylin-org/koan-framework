using Microsoft.Extensions.DependencyInjection;

namespace Sora.Core.BackgroundServices;

/// <summary>
/// Default implementation of service registry
/// </summary>
public class ServiceRegistry : IServiceRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, ISoraBackgroundService> _servicesByName = new();
    private readonly Dictionary<Type, ISoraBackgroundService> _servicesByType = new();

    public ServiceRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T GetService<T>() where T : class, ISoraBackgroundService
    {
        if (_servicesByType.TryGetValue(typeof(T), out var cached))
            return (T)cached;

        var service = _serviceProvider.GetRequiredService<T>();
        RegisterService(service);
        return service;
    }

    public ISoraBackgroundService? GetService(string serviceName)
    {
        return _servicesByName.TryGetValue(serviceName, out var service) ? service : null;
    }

    public IEnumerable<ISoraBackgroundService> GetAllServices()
    {
        return _servicesByName.Values;
    }

    public void RegisterService<T>(T service) where T : class, ISoraBackgroundService
    {
        _servicesByName[service.Name] = service;
        _servicesByType[typeof(T)] = service;
    }
}