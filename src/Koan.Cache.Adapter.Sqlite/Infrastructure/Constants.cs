namespace Koan.Cache.Adapter.Sqlite.Infrastructure;

internal static class Constants
{
    internal const string ProviderId = "sqlite";
    internal const int ProviderPriority = 50;

    internal static class Configuration
    {
        internal const string Section = "Koan:Cache:Adapters:Sqlite";
        internal const string DatabasePath = Section + ":DatabasePath";
        internal const string DefaultDatabasePath = ".Koan/cache/cache.db";
    }
}
