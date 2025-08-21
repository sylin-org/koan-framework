using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Vector.Abstractions;

namespace Sora.Data.Weaviate.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Weaviate";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<WeaviateOptions>().BindConfiguration("Sora:Data:Weaviate").ValidateDataAnnotations();
        services.TryAddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Sora.Data.Abstractions.Naming.INamingDefaultsProvider), typeof(WeaviateNamingDefaultsProvider), ServiceLifetime.Singleton));
    services.AddSingleton<Sora.Data.Vector.Abstractions.IVectorAdapterFactory, WeaviateVectorAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, WeaviateHealthContributor>());
        services.AddHttpClient("weaviate");
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var endpoint = Sora.Core.Configuration.Read(cfg, "Sora:Data:Weaviate:Endpoint", null) ?? "http://localhost:8085";
        report.AddSetting("Weaviate:Endpoint", endpoint, isSecret: false);
    }
}
