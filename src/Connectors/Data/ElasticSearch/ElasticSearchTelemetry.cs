using System.Diagnostics;

namespace Koan.Data.Connector.ElasticSearch;

internal static class ElasticSearchTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Connector.ElasticSearch");
}

