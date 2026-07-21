namespace Koan.Data.Core.Direct;

/// <summary>
/// Centralized configuration key constants for direct data access.
/// Eliminates magic "Koan:" string literals across Direct connection-string resolution.
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
