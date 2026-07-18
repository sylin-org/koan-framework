using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Connector.OpenSearch.Discovery;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.OpenSearch.Initialization;

public sealed class OpenSearchDataModule : KoanModule
{
    public override void Register(IServiceCollection services) =>
        services.AddSearchEngineConnector<OpenSearchOptions, OpenSearchVectorAdapterFactory>(
            Infrastructure.Constants.Descriptor);

    public override void Report(
        Koan.Core.Provenance.ProvenanceModuleWriter module,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        module.Describe(Version);
        module.ReportSearchEngineConnector<OpenSearchOptions, OpenSearchVectorAdapterFactory>(
            configuration,
            Infrastructure.Constants.Descriptor,
            cfg => new OpenSearchDiscoveryAdapter(
                cfg,
                NullLogger<OpenSearchDiscoveryAdapter>.Instance));
    }
}
