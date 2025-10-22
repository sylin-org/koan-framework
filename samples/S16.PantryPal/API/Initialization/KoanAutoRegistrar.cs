using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using S16.PantryPal.Services;
using Microsoft.Extensions.Configuration;

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
        services.AddScoped<IPantrySearchService, PantrySearchService>();
        services.AddOptions<IngestionOptions>().BindConfiguration("S16:Ingestion");
        services.AddHostedService<PantrySeedHostedService>();

        // Photo storage (bind S16:Photos or fall back to defaults). Simple pattern: options configured in appsettings if desired.
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var opts = new PhotoStorageOptions();
            cfg.GetSection("S16:Photos").Bind(opts);
            return opts;
        });
        services.AddScoped<IPhotoStorage, PhotoStorage>();
    }
}
