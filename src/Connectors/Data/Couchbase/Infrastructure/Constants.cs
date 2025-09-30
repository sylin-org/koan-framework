namespace Koan.Data.Connector.Couchbase.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        internal static class Keys
        {
            public const string ConnectionString = "Koan:Data:Couchbase:ConnectionString";
            public const string AltConnectionString = "Koan:Data:ConnectionString";
            public const string ConnectionStringsCouchbase = "ConnectionStrings:Couchbase";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";
            public const string Bucket = "Koan:Data:Couchbase:Bucket";
            public const string Scope = "Koan:Data:Couchbase:Scope";
            public const string Collection = "Koan:Data:Couchbase:Collection";
            public const string Username = "Koan:Data:Couchbase:Username";
            public const string Password = "Koan:Data:Couchbase:Password";
            public const string DefaultPageSize = "Koan:Data:Couchbase:DefaultPageSize";
            public const string MaxPageSize = "Koan:Data:Couchbase:MaxPageSize";
            public const string QueryTimeout = "Koan:Data:Couchbase:QueryTimeoutSeconds";
            public const string DurabilityLevel = "Koan:Data:Couchbase:Durability";
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

