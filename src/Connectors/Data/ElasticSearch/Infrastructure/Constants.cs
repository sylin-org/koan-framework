namespace Koan.Data.Connector.ElasticSearch.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Data:ElasticSearch";
    public const string HttpClientName = "elasticsearch";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = "Koan:Data:ElasticSearch:ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string Endpoint = "Koan:Data:ElasticSearch:Endpoint";
            public const string BaseUrl = "Koan:Data:ElasticSearch:BaseUrl";
            public const string IndexPrefix = "Koan:Data:ElasticSearch:IndexPrefix";
            public const string IndexName = "Koan:Data:ElasticSearch:IndexName";
            public const string VectorField = "Koan:Data:ElasticSearch:VectorField";
            public const string MetadataField = "Koan:Data:ElasticSearch:MetadataField";
            public const string SimilarityMetric = "Koan:Data:ElasticSearch:SimilarityMetric";
            public const string TimeoutSeconds = "Koan:Data:ElasticSearch:TimeoutSeconds";
        }
    }
}

