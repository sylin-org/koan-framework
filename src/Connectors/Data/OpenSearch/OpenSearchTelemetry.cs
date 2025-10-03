using System.Diagnostics;

namespace Koan.Data.Connector.OpenSearch;

internal static class OpenSearchTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.Connector.OpenSearch");
}

