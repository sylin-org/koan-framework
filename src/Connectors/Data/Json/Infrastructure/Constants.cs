namespace Koan.Data.Connector.Json.Infrastructure;

public static class Constants
{
    public static class Provider
    {
        public const string Name = "json";
    }

    public static class Configuration
    {
        public const string Section_Data = "Koan:Data:Json";
        public const string Section_Sources_Default = "Koan:Data:Sources:Default:json";

        public static class Keys
        {
            public const string DirectoryPath = nameof(DirectoryPath);
        }
    }

    public static class Bootstrap
    {
        public const string DirectoryPath = "data.json.directory";
    }
}

