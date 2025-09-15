using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Json.Infrastructure;

namespace Koan.Data.Json.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Json";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Json.Initialization.KoanAutoRegistrar");
    logger?.Log(LogLevel.Debug, "Koan.Data.Json KoanAutoRegistrar loaded.");
        // Bind options from config and register adapter + health contributor
        services.AddKoanOptions<JsonDataOptions>();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<JsonDataOptions>, JsonDataOptionsConfigurator>();
        services.AddSingleton<IDataAdapterFactory, JsonAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, JsonHealthContributor>());
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
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
