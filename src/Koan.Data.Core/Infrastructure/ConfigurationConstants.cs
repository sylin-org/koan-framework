namespace Koan.Data.Core.Infrastructure;

internal static class ConfigurationConstants
{
    public const string Section = "Koan:Data";

    public static class Sources
    {
        public const string Section = ConfigurationConstants.Section + ":Sources";

        /// <summary>
        /// Builds "Koan:Data:Sources:{sourceName}".
        /// </summary>
        public static string ForSource(string sourceName) => $"{Section}:{sourceName}";

        /// <summary>
        /// Builds "Koan:Data:Sources:{sourceName}:{providerId}:ConnectionString".
        /// </summary>
        public static string ConnectionString(string sourceName, string providerId) =>
            $"{Section}:{sourceName}:{providerId}:ConnectionString";

        /// <summary>
        /// Builds "Koan:Data:Sources:{sourceName}:{providerId}:{settingKey}".
        /// </summary>
        public static string Setting(string sourceName, string providerId, string settingKey) =>
            $"{Section}:{sourceName}:{providerId}:{settingKey}";
    }

    public static class Adapter
    {
        /// <summary>
        /// Builds "Koan:Data:{providerId}:ConnectionString".
        /// </summary>
        public static string ConnectionString(string providerId) =>
            $"{ConfigurationConstants.Section}:{providerId}:ConnectionString";

        /// <summary>
        /// Builds "Koan:Data:{providerId}:{settingKey}".
        /// </summary>
        public static string Setting(string providerId, string settingKey) =>
            $"{ConfigurationConstants.Section}:{providerId}:{settingKey}";
    }

    public static class Runtime
    {
        public const string Section = ConfigurationConstants.Section + ":Runtime";
        public const string EnsureSchemaOnStart = Runtime.Section + ":EnsureSchemaOnStart";
    }
}
