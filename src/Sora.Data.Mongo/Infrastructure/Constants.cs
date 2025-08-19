namespace Sora.Data.Mongo.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public static class Keys
        {
            public const string Section = "Sora:Data:Mongo";
            public const string AltSection = "Sora:Data:Sources:Default:mongo";

            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = AltSection + ":ConnectionString";
            public const string ConnectionStringsMongo = "ConnectionStrings:Mongo";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";

            public const string Database = Section + ":Database";
            public const string AltDatabase = AltSection + ":Database";
        }
    }

    public static class Bootstrap
    {
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
    }
}
