using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Vector.Connector.RedisVector.Initialization;

/// <summary>Registers Redis Search vector behavior while Koan.Redis retains connection lifecycle ownership.</summary>
public sealed class RedisVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<RedisVectorOptions>(RedisVectorOptions.Section);
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RedisVectorHealthContributor>());
        services.AddSingleton<RedisVectorVectorAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(static provider =>
            provider.GetRequiredService<RedisVectorVectorAdapterFactory>());
    }

    public override void Report(
        ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        var endpoint = configuration.GetConnectionString("Redis") ?? "shared Koan.Redis route";
        module.AddSetting("Store", Redaction.DeIdentify(endpoint));
        module.AddSetting("Database", Infrastructure.Constants.Defaults.Database.ToString());
        module.AddNote("Exact Redis Search FLAT vectors; Koan.Redis remains the sole discovery, multiplexer, and disposal owner.");
    }
}
