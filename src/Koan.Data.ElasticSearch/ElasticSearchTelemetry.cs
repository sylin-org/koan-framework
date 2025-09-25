using System.Diagnostics;

namespace Koan.Data.ElasticSearch;

internal static class ElasticSearchTelemetry
{
    public static readonly ActivitySource Activity = new("Koan.Data.ElasticSearch");
}
