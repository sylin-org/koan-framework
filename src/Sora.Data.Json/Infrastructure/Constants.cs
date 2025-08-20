namespace Sora.Data.Json.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section_Data = "Sora:Data:Json";
        public const string Section_Sources_Default = "Sora:Data:Sources:Default:json";

        public static class Keys
        {
            public const string DirectoryPath = nameof(DirectoryPath);
            public const string DefaultPageSize = nameof(DefaultPageSize);
            public const string MaxPageSize = nameof(MaxPageSize);
            public const string ConnectionString = nameof(ConnectionString);
            public const string Source = nameof(Source);
            public const string Enabled = nameof(Enabled);
        }
    }

    public static class Bootstrap
    {
        public const string DirectoryPath = "data.json.directory";
    }
}
