namespace Koan.Data.SqlServer.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public static class Keys
        {
            public const string Section = "Koan:Data:SqlServer";
            public const string AltSection = "Koan:Data:Sources:Default:sqlserver";

            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = AltSection + ":ConnectionString";
            public const string ConnectionStringsSqlServer = "ConnectionStrings:SqlServer";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";

            public const string DefaultPageSize = Section + ":DefaultPageSize";
            public const string AltDefaultPageSize = AltSection + ":DefaultPageSize";
            public const string MaxPageSize = Section + ":MaxPageSize";
            public const string AltMaxPageSize = AltSection + ":MaxPageSize";

            public const string DdlPolicy = Section + ":DdlPolicy";
            public const string AltDdlPolicy = AltSection + ":DdlPolicy";
            public const string SchemaMatchingMode = Section + ":SchemaMatchingMode";
            public const string AltSchemaMatchingMode = AltSection + ":SchemaMatchingMode";

            // Materialization/serialization options
            public const string JsonCaseInsensitive = Section + ":JsonCaseInsensitive";
            public const string JsonWriteIndented = Section + ":JsonWriteIndented";
            public const string JsonIgnoreNullValues = Section + ":JsonIgnoreNullValues";
        }
    }
}
