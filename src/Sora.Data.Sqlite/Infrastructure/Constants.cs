namespace Sora.Data.Sqlite.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public static class Keys
        {
            public const string Section = "Sora:Data:Sqlite";
            public const string AltSection = "Sora:Data:Sources:Default:sqlite";

            public const string ConnectionString = Section + ":ConnectionString";
            public const string AltConnectionString = AltSection + ":ConnectionString";
            public const string ConnectionStringsSqlite = "ConnectionStrings:Sqlite";
            public const string ConnectionStringsDefault = "ConnectionStrings:Default";
        }
    }

    public static class Bootstrap
    {
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
    }
}
