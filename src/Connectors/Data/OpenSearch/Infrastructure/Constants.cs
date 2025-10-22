namespace Koan.Data.Connector.OpenSearch.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Data:OpenSearch";
    public const string HttpClientName = "opensearch";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = "Koan:Data:OpenSearch:ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string Endpoint = "Koan:Data:OpenSearch:Endpoint";
            public const string BaseUrl = "Koan:Data:OpenSearch:BaseUrl";
            public const string IndexPrefix = "Koan:Data:OpenSearch:IndexPrefix";
            public const string IndexName = "Koan:Data:OpenSearch:IndexName";
            public const string VectorField = "Koan:Data:OpenSearch:VectorField";
            public const string MetadataField = "Koan:Data:OpenSearch:MetadataField";
            public const string SimilarityMetric = "Koan:Data:OpenSearch:SimilarityMetric";
            public const string TimeoutSeconds = "Koan:Data:OpenSearch:TimeoutSeconds";
        }
    }
}

