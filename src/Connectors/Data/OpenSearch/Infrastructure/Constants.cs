using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.OpenSearch.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Id = "opensearch";
        internal const string ConfigurationName = "OpenSearch";
        internal const string Section = "Koan:Data:OpenSearch";
        internal const string HttpClientName = "opensearch";
        internal const int Priority = 20;
    }

    internal static readonly SearchEngineConnectorDescriptor Descriptor = new(
        Provider.Id,
        Provider.ConfigurationName,
        Provider.Section,
        Provider.Id,
        [],
        ["open-search", "os"],
        ["OPENSEARCH_URLS", "OPEN_SEARCH_URLS"],
        ["opensearch", "open-search"],
        "opensearch",
        Provider.HttpClientName,
        "http://localhost:9200",
        OpenSearchTelemetry.Activity,
        static () => new OpenSearchDialect());
}
