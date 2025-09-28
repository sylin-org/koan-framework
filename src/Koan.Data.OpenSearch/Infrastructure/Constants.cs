namespace Koan.Data.OpenSearch.Infrastructure;

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
        }
    }
}
