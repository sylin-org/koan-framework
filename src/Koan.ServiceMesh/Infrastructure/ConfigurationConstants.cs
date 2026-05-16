namespace Koan.ServiceMesh.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.ServiceMesh module.
/// Eliminates magic "Koan:" string literals across service mesh configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Service";

    public static class Keys
    {
        public const string Port = nameof(Port);
        public const string HeartbeatInterval = nameof(HeartbeatInterval);
        public const string StaleThreshold = nameof(StaleThreshold);
        public const string ContainerImage = nameof(ContainerImage);
    }

    public static string ForService(string serviceId) => $"{Section}:{serviceId}";

    public static string ServiceKey(string serviceId, string key) => $"{Section}:{serviceId}:{key}";

    /// <summary>
    /// Builds full configuration path: "Koan:Service:{serviceId}:{key}".
    /// </summary>
    public static string FullKey(string serviceId, string key) => $"{Section}:{serviceId}:{key}";
}
