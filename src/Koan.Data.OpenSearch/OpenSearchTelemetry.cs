using System.Diagnostics;

namespace Koan.Data.OpenSearch;

internal static class OpenSearchTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.OpenSearch");
}
