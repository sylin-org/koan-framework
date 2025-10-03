namespace Koan.Data.Connector.Sqlite.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public static class Keys
        {
            public const string Section = "Koan:Data:Sqlite";
            public const string AltSection = "Koan:Data:Sources:Default:sqlite";

            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = AltSection + ":ConnectionString";
            public const string ConnectionStringsSqlite = "ConnectionStrings:Sqlite";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";

            // Paging guardrails (ADR-0044)
            public const string DefaultPageSize = Section + ":DefaultPageSize";
            public const string AltDefaultPageSize = AltSection + ":DefaultPageSize";
            public const string MaxPageSize = Section + ":MaxPageSize";
            public const string AltMaxPageSize = AltSection + ":MaxPageSize";

            // Schema governance
            public const string DdlPolicy = Section + ":DdlPolicy"; // AutoCreate | Validate | NoDdl
            public const string AltDdlPolicy = AltSection + ":DdlPolicy";
            public const string SchemaMatchingMode = Section + ":SchemaMatchingMode"; // Relaxed | Strict
            public const string AltSchemaMatchingMode = AltSection + ":SchemaMatchingMode";
        }
    }

    public static class Bootstrap
    {
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
        public const string DefaultPageSize = "DefaultPageSize";
        public const string MaxPageSize = "MaxPageSize";
    }
}

