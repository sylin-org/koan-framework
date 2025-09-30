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
        }
    }
}

