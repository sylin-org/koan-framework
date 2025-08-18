using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;

namespace Sora.Data.Json.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Json";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind options from config and register adapter + health contributor
        services.AddOptions<JsonDataOptions>().ValidateDataAnnotations();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<JsonDataOptions>, JsonDataOptionsConfigurator>();
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        services.AddHealthContributor<JsonHealthContributor>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new JsonDataOptions();
        cfg.GetSection("Sora:Data:Json").Bind(o);
        // Also check default data source pattern
        var alt = new JsonDataOptions();
        cfg.GetSection("Sora:Data:Sources:Default:json").Bind(alt);
        var dir = !string.IsNullOrWhiteSpace(o.DirectoryPath) ? o.DirectoryPath : alt.DirectoryPath;
        report.AddSetting("DirectoryPath", dir);
    }
}
