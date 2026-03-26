namespace Koan.Data.Backup.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Data.Backup module.
/// Eliminates magic "Koan:" string literals across backup configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Backup";

    public static class Keys
    {
        public const string DefaultStorageProfile = Section + ":DefaultStorageProfile";
        public const string DefaultBatchSize = Section + ":DefaultBatchSize";
        public const string WarmupEntitiesOnStartup = Section + ":WarmupEntitiesOnStartup";
        public const string EnableBackgroundMaintenance = Section + ":EnableBackgroundMaintenance";
        public const string MaintenanceInterval = Section + ":MaintenanceInterval";
        public const string MaxConcurrency = Section + ":MaxConcurrency";
        public const string AutoValidateBackups = Section + ":AutoValidateBackups";
        public const string CompressionLevel = Section + ":CompressionLevel";
    }

    public static class Retention
    {
        public const string Section = ConfigurationConstants.Section + ":Retention";
        public const string KeepDaily = Section + ":KeepDaily";
        public const string KeepWeekly = Section + ":KeepWeekly";
        public const string KeepMonthly = Section + ":KeepMonthly";
        public const string KeepYearly = Section + ":KeepYearly";
        public const string ExcludeFromCleanup = Section + ":ExcludeFromCleanup";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Backup:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
