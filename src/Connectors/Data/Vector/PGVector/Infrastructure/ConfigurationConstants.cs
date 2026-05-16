namespace Koan.Data.Connector.PGVector.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the PGVector connector.
/// Eliminates magic "Koan:" string literals across PGVector configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Vector:PGVector";

    /// <summary>
    /// Builds full configuration path: "Koan:Vector:PGVector:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
