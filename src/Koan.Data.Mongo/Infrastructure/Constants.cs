namespace Koan.Data.Mongo.Infrastructure;

public static class Constants
{
    public static class Discovery
    {
        public const string EnvList = "Koan_DATA_MONGO_URLS"; // comma/semicolon-separated list of URIs
        public const string WellKnownServiceName = "mongodb";
        public const int DefaultPort = 27017;
    }
    public static class Configuration
    {
        public static class Keys
        {
            public const string Section = "Koan:Data:Mongo";
            public const string AltSection = "Koan:Data:Sources:Default:mongo";

            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = AltSection + ":ConnectionString";
            public const string ConnectionStringsMongo = "ConnectionStrings:Mongo";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";

            public const string Database = Section + ":Database";
            public const string AltDatabase = AltSection + ":Database";

            // Paging guardrails
            public const string DefaultPageSize = Section + ":DefaultPageSize";
            public const string MaxPageSize = Section + ":MaxPageSize";
            public const string AltDefaultPageSize = AltSection + ":DefaultPageSize";
            public const string AltMaxPageSize = AltSection + ":MaxPageSize";
        }
    }

    public static class Bootstrap
    {
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
        public const string DefaultPageSize = "DefaultPageSize";
        public const string MaxPageSize = "MaxPageSize";
    }
}
