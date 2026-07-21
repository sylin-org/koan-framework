namespace Koan.Data.Connector.Postgres.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public static class Keys
        {
            public const string Section = "Koan:Data:Postgres";
            public const string AltSection = "Koan:Data:Sources:Default:postgres";

            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = AltSection + ":ConnectionString";
            public const string ConnectionStringsPostgres = "ConnectionStrings:Postgres";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";

            public const string DdlPolicy = Section + ":DdlPolicy";
            public const string AltDdlPolicy = AltSection + ":DdlPolicy";
            public const string SchemaMatchingMode = Section + ":SchemaMatchingMode";
            public const string AltSchemaMatchingMode = AltSection + ":SchemaMatchingMode";
            public const string SearchPath = Section + ":SearchPath";
            public const string NamingStyle = Section + ":NamingStyle";
            public const string AltNamingStyle = AltSection + ":NamingStyle";
            public const string Separator = Section + ":Separator";
            public const string AltSeparator = AltSection + ":Separator";
            public const string EnsureCreatedSupported = Section + ":EnsureCreatedSupported";

            public const string Database = Section + ":Database";
            public const string Username = Section + ":Username";
            public const string Password = Section + ":Password";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }

        public static class DataFallback
        {
            public const string ConnectionString = "Koan:Data:ConnectionString";
            public const string Database = "Koan:Data:Database";
            public const string Username = "Koan:Data:Username";
            public const string Password = "Koan:Data:Password";
        }
    }
}

