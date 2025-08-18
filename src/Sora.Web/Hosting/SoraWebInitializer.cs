using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;

namespace Sora.Web;

// Self-hook into Sora.AddSoraDataCore() discovery
public sealed class SoraWebInitializer : ISoraInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Idempotent registration of options, controllers, and startup filter
        services.AddSoraWeb();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.SoraWebStartupFilter>());
    }
}
