namespace Koan.Data.Connector.OpenSearch.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Data:OpenSearch";
    public const string HttpClientName = "opensearch";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string Endpoint = Section + ":Endpoint";
            public const string BaseUrl = Section + ":BaseUrl";
            public const string ApiKey = Section + ":ApiKey";
            public const string Username = Section + ":Username";
            public const string Password = Section + ":Password";
            public const string IndexPrefix = Section + ":IndexPrefix";
            public const string IndexName = Section + ":IndexName";
            public const string VectorField = Section + ":VectorField";
            public const string MetadataField = Section + ":MetadataField";
            public const string IdField = Section + ":IdField";
            public const string SimilarityMetric = Section + ":SimilarityMetric";
            public const string RefreshMode = Section + ":RefreshMode";
            public const string TimeoutSeconds = Section + ":TimeoutSeconds";
            public const string Dimension = Section + ":Dimension";
            public const string DisableIndexAutoCreate = Section + ":DisableIndexAutoCreate";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }
    }

    internal static class Logging
    {
        public const string Health = "data.opensearch.health";
    }
}

