namespace Koan.Data.Direct.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Data.Direct module.
/// Eliminates magic "Koan:" string literals across Direct data access configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string DataSection = "Koan:Data";

    public static class Keys
    {
        /// <summary>
        /// Pattern: Koan:Data:Sources:{sourceName}:ConnectionString
        /// </summary>
        public static string SourceConnectionString(string sourceName) =>
            $"{DataSection}:Sources:{sourceName}:ConnectionString";

        /// <summary>
        /// Pattern: Koan:Data:{adapter}:ConnectionString
        /// </summary>
        public static string AdapterConnectionString(string adapter) =>
            $"{DataSection}:{adapter}:ConnectionString";
    }
}
