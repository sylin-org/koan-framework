namespace Koan.Orchestration.Aspire.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Orchestration.Aspire module.
/// Eliminates magic "Koan:" string literals across orchestration configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Orchestration";

    public static class Keys
    {
        public const string Mode = Section + ":Mode";
        public const string SessionId = Section + ":SessionId";
        public const string ConnectionTimeout = Section + ":ConnectionTimeout";
        public const string NetworkMode = Section + ":NetworkMode";
        public const string Namespace = Section + ":Namespace";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Orchestration:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
