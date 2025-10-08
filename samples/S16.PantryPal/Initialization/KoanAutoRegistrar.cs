using Koan.Core;
using Koan.Core.Initialization;
using Microsoft.Extensions.DependencyInjection;
using S16.PantryPal.Services;

namespace S16.PantryPal.Initialization;

/// <summary>
/// Auto-registers PantryPal services following Koan Framework "Reference = Intent" pattern.
/// </summary>
public class KoanAutoRegistrar : IKoanInitializer
{
    public void Register(IServiceCollection services, IKoanEnv env)
    {
        // Vision and parsing services
        services.AddPantryVision();
    }
}
