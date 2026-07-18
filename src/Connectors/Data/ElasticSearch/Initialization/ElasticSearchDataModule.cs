using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.ElasticSearch.Discovery;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.ElasticSearch.Initialization;

public sealed class ElasticSearchDataModule : KoanModule
{
    public override void Register(IServiceCollection services) =>
        services.AddSearchEngineConnector<ElasticSearchOptions, ElasticSearchVectorAdapterFactory>(
            Infrastructure.Constants.Descriptor);

    public override void Report(
        Koan.Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        module.ReportSearchEngineConnector<ElasticSearchOptions, ElasticSearchVectorAdapterFactory>(
            configuration,
            Infrastructure.Constants.Descriptor,
            cfg => new ElasticSearchDiscoveryAdapter(
                cfg,
                NullLogger<ElasticSearchDiscoveryAdapter>.Instance));
    }
}
