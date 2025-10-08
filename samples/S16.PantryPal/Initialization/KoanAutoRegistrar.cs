using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using S16.PantryPal.Services;

namespace S16.PantryPal.Initialization;

/// <summary>
/// Auto-registers PantryPal services following Koan Framework "Reference = Intent" pattern.
/// </summary>
public class KoanAutoRegistrar : IKoanInitializer
{
    public void Initialize(IServiceCollection services)
    {
        // Vision and parsing services
        services.AddPantryVision();
        services.AddScoped<IPantryConfirmationService, PantryConfirmationService>();
        services.AddScoped<IPantryInsightsService, PantryInsightsService>();
        services.AddScoped<IMealPlanningService, MealPlanningService>();
    }
}
