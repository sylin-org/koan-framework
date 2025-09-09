using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using S8.Location.Core.Options;
using S8.Location.Core.Services;
using S8.Location.Core.Health;
using Sora.Core;
using Sora.Core.Hosting.Bootstrap;
using Sora.Core.Modules;
using Sora.Core.Observability.Health;

namespace S8.Location.Core.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "S8.Location";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register configuration options
        services.AddSoraOptions<LocationOptions>();
        
        // Register HttpClient for GoogleMaps service
        services.AddHttpClient();
        
        // Register core services as singletons for health check compatibility
        services.AddSingleton<IAddressResolutionService, AddressResolutionService>();
        services.AddSingleton<IGeocodingService, GoogleMapsGeocodingService>();
        
        // Register health contributor
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, LocationHealthContributor>());
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        
        // Configuration discovery
        var defaultRegion = Configuration.ReadFirst(cfg, "US",
            "S8:Location:DefaultRegion",
            "LOCATION_DEFAULT_REGION");
        report.AddSetting("DefaultRegion", defaultRegion);
        
        var cacheEnabled = Configuration.ReadFirst(cfg, "true",
            "S8:Location:Resolution:CacheEnabled",
            "LOCATION_CACHE_ENABLED");
        report.AddSetting("CacheEnabled", cacheEnabled);
        
        // Google Maps API configuration check
        var gmapsKey = Configuration.ReadFirst(cfg, null,
            "S8:Location:Geocoding:GoogleMapsApiKey",
            "GOOGLE_MAPS_API_KEY");
        report.AddSetting("GoogleMapsConfigured", (gmapsKey != null).ToString());
    }
}