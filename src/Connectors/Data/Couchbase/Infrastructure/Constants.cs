namespace Koan.Data.Connector.Couchbase.Infrastructure;

internal static class Constants
{
    internal const string Provider = "couchbase";
    internal const string Alias = "cb";
    internal const string DefaultSource = "Default";
    internal const string DefaultBucket = "Koan";
    internal const string DefaultScope = "_default";
    internal const int Priority = 30;
    internal const int MaximumRoutes = 128;
    internal const int MaximumContainersPerRoute = 1024;
    internal const int MaximumKeyBytes = 250;
    internal const int MaximumScopeBytes = 30;
    internal const int MaximumCollectionBytes = 251;

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Couchbase";
        internal const string ConnectionString = Section + ":ConnectionString";
        internal const string Bucket = Section + ":Bucket";
        internal const string Scope = Section + ":Scope";
        internal const string Collection = Section + ":Collection";
        internal const string Username = Section + ":Username";
        internal const string Password = Section + ":Password";
        internal const string QueryTimeout = Section + ":QueryTimeout";
        internal const string Durability = Section + ":Durability";
        internal const string DisableAutoDetection = Section + ":DisableAutoDetection";
        internal const string StandardConnectionString = "ConnectionStrings:Couchbase";
    }

    internal static class Discovery
    {
        internal const string ServiceName = "couchbase";
        internal const string CouchbaseUrls = "COUCHBASE_URLS";
        internal const string CouchbaseAliasUrls = "CB_URLS";
    }

    internal static class Storage
    {
        internal const string Identity = "id";
    }
}
