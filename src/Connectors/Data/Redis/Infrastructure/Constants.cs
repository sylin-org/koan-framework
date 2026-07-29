namespace Koan.Data.Connector.Redis.Infrastructure;

internal static class Constants
{
    internal const string Provider = "redis";
    internal const string Alias = "redis-kv";
    internal const string DefaultSource = "Default";
    internal const int Priority = 5;
    internal const int MaximumQueryEntries = 10_000;
    internal const int MaximumBulkEntries = 1_000;
    internal const int MaximumConditionalAttempts = 5;

    internal static class Configuration
    {
        internal const string Section = "Koan:Data:Redis";
        internal const string Database = Section + ":Database";
        internal const string MaxQueryEntries = Section + ":MaxQueryEntries";
        internal const string MaxBulkEntries = Section + ":MaxBulkEntries";
    }
}
