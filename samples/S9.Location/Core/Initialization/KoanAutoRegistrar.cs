using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using S9.Location.Core.Interceptors;
using S9.Location.Core.Options;
using S9.Location.Core.Processing;
using S9.Location.Core.Services;

namespace S9.Location.Core.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S9.Location";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<LocationOptions>();
        services.AddHttpClient();

        services.AddSingleton<INormalizationService, NormalizationService>();
        services.AddSingleton<ILocationMetricsService, LocationMetricsService>();
        services.AddSingleton<IResolutionPipeline, ResolutionPipeline>();

        services.AddHostedService<LocationIntakeConfigurator>();
        services.AddHostedService<LocationFlowProcessor>();
    }

    public void Describe(BootReport report, IConfiguration configuration, IHostEnvironment environment)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var country = Configuration.ReadFirst(configuration, "US",
            "S9:Location:Normalization:DefaultCountry",
            "S9_LOCATION_DEFAULT_COUNTRY");
        report.AddSetting("Normalization.DefaultCountry", country);

        var cacheEnabled = Configuration.ReadFirst(configuration, "true",
            "S9:Location:Cache:Enabled",
            "S9_LOCATION_CACHE_ENABLED");
        report.AddSetting("Cache.Enabled", cacheEnabled);

        var aiEnabled = Configuration.ReadFirst(configuration, "false",
            "S9:Location:AiAssist:Enabled",
            "S9_LOCATION_AI_ENABLED");
        report.AddSetting("AiAssist.Enabled", aiEnabled);
    }
}
