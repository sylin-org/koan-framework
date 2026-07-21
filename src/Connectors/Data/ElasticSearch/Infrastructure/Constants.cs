using Koan.Data.SearchEngine;

namespace Koan.Data.Connector.ElasticSearch.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Id = "elasticsearch";
        internal const string ConfigurationName = "ElasticSearch";
        internal const string Section = "Koan:Data:ElasticSearch";
        internal const string HttpClientName = "elasticsearch";
        internal const int Priority = 20;
    }

    internal static readonly SearchEngineConnectorDescriptor Descriptor = new(
        Provider.Id,
        Provider.ConfigurationName,
        Provider.Section,
        Provider.Id,
        ["elastic"],
        ["elastic", "es"],
        ["ELASTICSEARCH_URLS", "ELASTIC_URLS"],
        ["elasticsearch", "elastic"],
        "elasticsearch",
        Provider.HttpClientName,
        "http://localhost:9200",
        ElasticSearchTelemetry.Activity,
        static () => new ElasticSearchDialect());
}
