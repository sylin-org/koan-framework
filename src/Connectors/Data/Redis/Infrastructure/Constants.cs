namespace Koan.Data.Connector.Redis.Infrastructure;

public static class Constants
{
    public const string Section = "Koan:Data:Redis";

    public static class Configuration
    {
        public const string Section_Data = Constants.Section;
        public const string Section_Sources_Default = "Koan:Data:Sources:Default:redis";
        public static class Keys
        {
            public const string Database = Constants.Section + ":Database";
            public const string AltDatabase = "Koan:Data:Database";
            public const string EnsureCreatedSupported = nameof(EnsureCreatedSupported);
        }
    }

    public static class Bootstrap
    {
        public const string EnsureCreatedSupported = "EnsureCreatedSupported";
    }

}

