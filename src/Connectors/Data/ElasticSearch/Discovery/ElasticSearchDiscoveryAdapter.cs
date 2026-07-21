using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.ElasticSearch.Discovery;

internal sealed class ElasticSearchDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<ElasticSearchDiscoveryAdapter> logger)
    : SearchEngineDiscoveryAdapter(configuration, logger, Infrastructure.Constants.Descriptor)
{
    protected override Type FactoryType => typeof(ElasticSearchVectorAdapterFactory);
}
