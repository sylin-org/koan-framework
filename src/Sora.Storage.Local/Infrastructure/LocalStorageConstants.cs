namespace Sora.Storage.Local.Infrastructure;

public static class LocalStorageConstants
{
    public static class Configuration
    {
        public const string Section = "Sora:Storage:Providers:Local";
        public static class Keys
        {
            public const string BasePath = nameof(BasePath);
        }
    }
}
