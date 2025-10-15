namespace Koan.Data.Backup.Infrastructure;

internal static class Constants
{
    public const string Section = "Koan:Backup";

    internal static class Configuration
    {
        internal static class Keys
        {
            public const string DefaultStorageProfile = "Koan:Backup:DefaultStorageProfile";
            public const string DefaultBatchSize = "Koan:Backup:DefaultBatchSize";
            public const string WarmupEntitiesOnStartup = "Koan:Backup:WarmupEntitiesOnStartup";
            public const string EnableBackgroundMaintenance = "Koan:Backup:EnableBackgroundMaintenance";
            public const string MaintenanceInterval = "Koan:Backup:MaintenanceInterval";
            public const string MaxConcurrency = "Koan:Backup:MaxConcurrency";
            public const string AutoValidateBackups = "Koan:Backup:AutoValidateBackups";
            public const string CompressionLevel = "Koan:Backup:CompressionLevel";
        }

        internal static class Retention
        {
            public const string Section = "Koan:Backup:Retention";
            public const string KeepDaily = "Koan:Backup:Retention:KeepDaily";
            public const string KeepWeekly = "Koan:Backup:Retention:KeepWeekly";
            public const string KeepMonthly = "Koan:Backup:Retention:KeepMonthly";
            public const string KeepYearly = "Koan:Backup:Retention:KeepYearly";
            public const string ExcludeFromCleanup = "Koan:Backup:Retention:ExcludeFromCleanup";
        }
    }
}
