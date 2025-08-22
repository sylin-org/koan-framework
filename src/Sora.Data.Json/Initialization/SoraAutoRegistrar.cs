using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Json.Infrastructure;

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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, JsonHealthContributor>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        // ADR-0040: use helper to read DirectoryPath from either primary or default source sections
        var dir = Configuration.ReadFirst(cfg, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DirectoryPath}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DirectoryPath}"
        });
        report.AddSetting(Constants.Bootstrap.DirectoryPath, dir ?? string.Empty);
    }
}
