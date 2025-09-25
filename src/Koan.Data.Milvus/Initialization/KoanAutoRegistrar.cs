
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Milvus.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Milvus";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var loggerFactory = services.BuildServiceProvider().GetService<ILoggerFactory>();
        loggerFactory?.CreateLogger("Koan.Data.Milvus.Initialization").LogDebug("Milvus adapter registrar loaded");

        services.AddKoanOptions<MilvusOptions>(Infrastructure.Constants.Section);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MilvusOptions>, MilvusOptionsConfigurator>());

        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Abstractions.Naming.INamingDefaultsProvider, MilvusNamingDefaultsProvider>());

        services.AddSingleton<IVectorAdapterFactory, MilvusVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MilvusHealthContributor>());

        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Configuration.Read(cfg, $"{Infrastructure.Constants.Section}:Endpoint", "http://localhost:19530");
        report.AddSetting("Milvus:Endpoint", endpoint, isSecret: false);
        report.AddSetting("Milvus:Database", Configuration.Read(cfg, $"{Infrastructure.Constants.Section}:Database", "default"), isSecret: false);
    }
}
