namespace Koan.Core.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Core module.
/// Eliminates magic "Koan:" string literals across the codebase.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan";

    public static class Application
    {
        public const string Section = "Koan:Application";
        public const string ContactEmail = Section + ":ContactEmail";
        public const string SupportUrl = Section + ":SupportUrl";
    }

    public static class BackgroundServices
    {
        public const string Section = "Koan:BackgroundServices";
    }

    public static class Health
    {
        public const string AggregatorSection = "Koan:Health:Aggregator";
    }

    public static class Data
    {
        public const string Section = "Koan:Data";

        public static string ForService(string serviceName) => $"{Section}:{serviceName}";
        public static string ConnectionString(string serviceName) => $"{Section}:{serviceName}:ConnectionString";
        public const string DefaultConnectionString = Section + ":ConnectionString";
    }

    public static class Services
    {
        public const string Section = "Koan:Services";

        public static string ForService(string serviceName) => $"{Section}:{serviceName}";
    }

    public static class Ai
    {
        public const string Section = "Koan:AI";

        public static string ForService(string serviceName) => $"{Section}:{serviceName}";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
