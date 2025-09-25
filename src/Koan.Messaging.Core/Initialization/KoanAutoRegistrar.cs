using Microsoft.Extensions.DependencyInjection;
using Koan.Core;

namespace Koan.Messaging.Core.Initialization;

/// <summary>
/// Auto-registers the core Koan messaging services when Koan.Messaging.Core is referenced.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Register core messaging services
        services.AddKoanMessaging();
    }
}