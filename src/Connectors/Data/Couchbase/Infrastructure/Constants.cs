namespace Koan.Data.Connector.Couchbase.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "couchbase";
        internal const string ConfigurationName = "Couchbase";
        internal const int Priority = 30;
    }

    public const string Section = "Koan:Data:Couchbase";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string ConnectionStringsCouchbase = "ConnectionStrings:Couchbase";
            public const string DefaultSourceConnectionString = "Koan:Data:Sources:Default:couchbase:ConnectionString";
            public const string Bucket = Section + ":Bucket";
            public const string DefaultSourceBucket = "Koan:Data:Sources:Default:couchbase:Bucket";
            public const string Scope = Section + ":Scope";
            public const string Collection = Section + ":Collection";
            public const string Username = Section + ":Username";
            public const string DefaultSourceUsername = "Koan:Data:Sources:Default:couchbase:Username";
            public const string Password = Section + ":Password";
            public const string DefaultSourcePassword = "Koan:Data:Sources:Default:couchbase:Password";
            public const string QueryTimeout = Section + ":QueryTimeoutSeconds";
            public const string DurabilityLevel = Section + ":Durability";
            public const string Hosts = Section + ":Hosts";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
            public const string ManagementUrl = Section + ":ManagementUrl";
        }
    }

    internal static class Discovery
    {
        public const string ServiceName = "couchbase";
        public const string CouchbaseUrls = "COUCHBASE_URLS";
        public const string CouchbaseAliasUrls = "CB_URLS";
        public const string CouchbaseHosts = "COUCHBASE_HOSTS";
        public const int KvPort = 11210;
        public const int ManagerPort = 8091;
    }

    internal static class Bootstrap
    {
        public const string EnsureCreatedSupported = "data:couchbase:ensureCreated";
    }
}

