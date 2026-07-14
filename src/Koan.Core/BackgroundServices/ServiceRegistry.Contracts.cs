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
