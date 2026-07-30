using System.Globalization;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Orchestration.Abstractions;
using Koan.Core.Provenance;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.OpenSearch.Discovery;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.OpenSearch.Initialization;

public sealed class OpenSearchDataModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<OpenSearchOptions>(Infrastructure.Constants.Configuration.Section);
        services.AddSingleton<IConfigureOptions<OpenSearchOptions>, OpenSearchOptionsConfigurator>();
        services.TryAddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, OpenSearchHealthContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDiscoveryAdapter, OpenSearchDiscoveryAdapter>());
        services.AddSingleton<OpenSearchVectorAdapterFactory>();
        services.AddSingleton<IVectorAdapterFactory>(static provider =>
            provider.GetRequiredService<OpenSearchVectorAdapterFactory>());
        services.AddHttpClient(Infrastructure.Constants.HttpClientName);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version);
        var defaults = new OpenSearchOptions();
        var endpoint = Configuration.ReadFirstWithSource(
            configuration,
            defaults.Endpoint,
            Infrastructure.Constants.Configuration.Keys.Endpoint,
            Infrastructure.Constants.Configuration.Keys.LegacyConnectionString,
            "ConnectionStrings:OpenSearch");
        var timeout = Configuration.ReadWithSource(
            configuration,
            Infrastructure.Constants.Configuration.Keys.TimeoutSeconds,
            defaults.TimeoutSeconds);
        module.AddSetting("Endpoint", Redaction.DeIdentify(endpoint.Value), source: endpoint.Source,
            consumers: [typeof(OpenSearchVectorAdapterFactory).FullName!], sourceKey: endpoint.ResolvedKey);
        module.AddSetting("TimeoutSeconds", timeout.Value.ToString(CultureInfo.InvariantCulture), source: timeout.Source,
            consumers: [typeof(OpenSearchClient).FullName!], sourceKey: timeout.ResolvedKey);
        module.AddNote(
            "Vector shape and visibility come from the immutable Koan VectorSpacePlan; OpenSearch options own placement and bounds only.");
    }
}
