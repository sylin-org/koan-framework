namespace Koan.Core.Adapters.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Core.Adapters module.
/// Eliminates magic "Koan:" string literals across adapter configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public static class Data
    {
        public const string Section = "Koan:Data";

        public static string ForProvider(string provider) => $"{Section}:{provider}";

        public static class Readiness
        {
            public const string Policy = "Koan:Data:Readiness:Policy";
            public const string Timeout = "Koan:Data:Readiness:Timeout";

            public static string PolicyForProvider(string provider) => $"Koan:Data:{provider}:Readiness:Policy";
            public static string TimeoutForProvider(string provider) => $"Koan:Data:{provider}:Readiness:Timeout";
            public static string EnableReadinessGatingForProvider(string provider) => $"Koan:Data:{provider}:Readiness:EnableReadinessGating";
        }

        public static class Paging
        {
            public const string DefaultPageSize = "Koan:Data:DefaultPageSize";

            public static string DefaultPageSizeForProvider(string provider) => $"Koan:Data:{provider}:DefaultPageSize";
        }
    }

    public static class Services
    {
        public const string Section = "Koan:Services";

        public static string ForAdapter(string adapterId) => $"{Section}:{adapterId}";
        public static string ConnectionString(string adapterId) => $"{Section}:{adapterId}:ConnectionString";
        public static string Enabled(string adapterId) => $"{Section}:{adapterId}:Enabled";
        public static string OrchestrationMode(string adapterId) => $"{Section}:{adapterId}:OrchestrationMode";
        public static string ServiceKind(string adapterId) => $"{Section}:{adapterId}:ServiceKind";
    }

    public static class Ai
    {
        public const string Section = "Koan:AI";

        public static string ForAdapter(string adapterId) => $"{Section}:{adapterId}";
        public static string BaseUrl(string adapterId) => $"{Section}:{adapterId}:BaseUrl";
        public static string Enabled(string adapterId) => $"{Section}:{adapterId}:Enabled";
    }

    public static class Cache
    {
        public const string Section = "Koan:Cache";

        public static string ForAdapter(string adapterId) => $"{Section}:{adapterId}";
        public static string ConnectionString(string adapterId) => $"{Section}:{adapterId}:ConnectionString";
        public static string Enabled(string adapterId) => $"{Section}:{adapterId}:Enabled";
    }

    public static class Adapters
    {
        public const string ReadinessSection = "Koan:Adapters:Readiness";
    }
}
