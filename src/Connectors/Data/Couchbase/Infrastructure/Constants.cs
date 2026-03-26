namespace Koan.Data.Connector.Couchbase.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Data:Couchbase";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string ConnectionStringsCouchbase = "ConnectionStrings:Couchbase";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";
            public const string ConnectionStringsDatabase = "ConnectionStrings:Database";
            public const string Bucket = Section + ":Bucket";
            public const string AltBucket = "Koan:Data:Bucket";
            public const string Scope = Section + ":Scope";
            public const string Collection = Section + ":Collection";
            public const string Username = Section + ":Username";
            public const string AltUsername = "Koan:Data:Username";
            public const string Password = Section + ":Password";
            public const string AltPassword = "Koan:Data:Password";
            public const string DefaultPageSize = Section + ":DefaultPageSize";
            public const string MaxPageSize = Section + ":MaxPageSize";
            public const string QueryTimeout = Section + ":QueryTimeoutSeconds";
            public const string DurabilityLevel = Section + ":Durability";
            public const string Hosts = Section + ":Hosts";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }
    }

    internal static class Discovery
    {
        public const string ServiceName = "couchbase";
        public const int KvPort = 11210;
        public const int ManagerPort = 8091;
    }

    internal static class Bootstrap
    {
        public const string EnsureCreatedSupported = "data:couchbase:ensureCreated";
        public const string DefaultPageSize = "data:couchbase:defaultPageSize";
        public const string MaxPageSize = "data:couchbase:maxPageSize";
    }
}

