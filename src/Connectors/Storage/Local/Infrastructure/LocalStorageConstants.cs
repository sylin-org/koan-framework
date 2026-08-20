namespace Koan.Storage.Connector.Local.Infrastructure;

public static class LocalStorageConstants
{
    public const string ProviderName = "local";

    /// <summary>
    /// Where local storage puts bytes when the application configures nothing. Referencing a
    /// capability is the intent to use it, so the reference alone must compose — matching the SQLite
    /// connector, which defaults to <c>.koan/data/Koan.sqlite</c> rather than demanding a path.
    /// Relative paths resolve against the process working directory when the provider is constructed.
    /// </summary>
    public const string DefaultBasePath = ".koan/storage";

    public static class Configuration
    {
        public const string Section = "Koan:Storage:Providers:Local";
        public static class Keys
        {
            public const string BasePath = nameof(BasePath);
        }
    }
}
