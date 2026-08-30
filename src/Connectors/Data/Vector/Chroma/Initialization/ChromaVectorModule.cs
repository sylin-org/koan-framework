using System.Globalization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Chroma.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Vector.Connector.Chroma.Initialization;

public sealed class ChromaVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<ChromaOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<ChromaOptions>, ChromaOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, ChromaHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, ChromaDiscoveryAdapter>());
        services.AddSingleton<ChromaVectorAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(static services =>
            services.GetRequiredService<ChromaVectorAdapterFactory>());
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        var defaults = new ChromaOptions();
        var endpoint = Configuration.ReadFirstWithSource(
            configuration,
            defaults.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            Infrastructure.Constants.Configuration.Keys.LegacyConnectionString,
            "ConnectionStrings:Chroma");
        var timeout = Configuration.ReadWithSource(
            configuration,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaults.TimeoutSeconds);
        module.AddSetting("Endpoint", Redaction.DeIdentify(endpoint.Value), source: endpoint.Source,
            consumers: [typeof(ChromaVectorAdapterFactory).FullName!], sourceKey: endpoint.ResolvedKey);
        module.AddSetting("TimeoutSeconds", timeout.Value.ToString(CultureInfo.InvariantCulture), source: timeout.Source,
            consumers: [typeof(ChromaClient).FullName!], sourceKey: timeout.ResolvedKey);
        module.AddNote("Vector shape and visibility come from the immutable Koan VectorSpacePlan; Chroma options own placement and bounds only.");
    }
}
