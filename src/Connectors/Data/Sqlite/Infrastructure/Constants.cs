namespace Koan.Data.Connector.Sqlite.Infrastructure;

public static class Constants
{
    public const string Provider = "sqlite";
    public const string DefaultSource = "Default";
    public const string DefaultConnection = "Data Source=.koan/data/Koan.sqlite";
    public const int MaximumMemorySources = 256;
    public const int MaximumSchemaEntries = 512;

    public static class Configuration
    {
        public const string Section = "Koan:Data:Sqlite";
        public const string DefaultSourceSection = "Koan:Data:Sources:Default:sqlite";
        public const string SourceSection = "Koan:Data:Sources";

        public static class Keys
        {
            public const string ConnectionString = Section + ":ConnectionString";
            public const string DefaultSourceConnectionString = "Koan:Data:Sources:Default:ConnectionString";
            public const string ProviderSourceConnectionString = DefaultSourceSection + ":ConnectionString";
            public const string ConnectionStringsSqlite = "ConnectionStrings:Sqlite";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";
            public const string NamingStyle = Section + ":NamingStyle";
            public const string ProviderNamingStyle = DefaultSourceSection + ":NamingStyle";
            public const string Separator = Section + ":Separator";
            public const string ProviderSeparator = DefaultSourceSection + ":Separator";
            public const string DdlPolicy = Section + ":DdlPolicy";
            public const string ProviderDdlPolicy = DefaultSourceSection + ":DdlPolicy";
            public const string SchemaMatching = Section + ":SchemaMatchingMode";
            public const string ProviderSchemaMatching = DefaultSourceSection + ":SchemaMatchingMode";
            public const string DisableAutoDetection = Section + ":DisableAutoDetection";
        }
    }
}
