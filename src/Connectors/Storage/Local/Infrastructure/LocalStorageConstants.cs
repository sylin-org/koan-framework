namespace Koan.Storage.Connector.Local.Infrastructure;

public static class LocalStorageConstants
{
    public const string ProviderName = "local";

    public static class Configuration
    {
        public const string Section = "Koan:Storage:Providers:Local";
        public static class Keys
        {
            public const string BasePath = nameof(BasePath);
        }
    }
}
