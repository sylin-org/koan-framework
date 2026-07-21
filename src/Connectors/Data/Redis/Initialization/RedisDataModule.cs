using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RedisItems = Koan.Data.Connector.Redis.Infrastructure.RedisProvenanceItems;

namespace Koan.Data.Connector.Redis.Initialization;

/// <summary>Contributes Redis repository mechanics; <c>Koan.Redis</c> owns the shared backend lifecycle.</summary>
public sealed class RedisDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<RedisOptions>();
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RedisHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, RedisAdapterFactory>();
    }

    public override void Report(
        Koan.Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        module.AddNote("Redis endpoint discovery and connection lifetime are owned by Sylin.Koan.Redis.");
        module.AddNote("AODB isolation: RowScoped + ContainerScoped + DatabaseScoped (conformance: AodbConformanceSpecsBase)");

        var defaults = new RedisOptions();
        var database = Configuration.ReadFirstWithSource(
            configuration,
            defaults.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:Database");
        var ensureCreated = Configuration.ReadFirstWithSource(
            configuration,
            true,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.EnsureCreatedSupported}");

        module.PublishConfigValue(RedisItems.Database, database);
        module.PublishConfigValue(RedisItems.EnsureCreatedSupported, ensureCreated);
    }
}
