using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.OpenSearch.Discovery;

internal sealed class OpenSearchDiscoveryAdapter(
    IConfiguration configuration,
    ILogger<OpenSearchDiscoveryAdapter> logger)
    : SearchEngineDiscoveryAdapter(configuration, logger, Infrastructure.Constants.Descriptor)
{
    protected override Type FactoryType => typeof(OpenSearchVectorAdapterFactory);
}
