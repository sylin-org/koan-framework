namespace Koan.AI.Connector.HuggingFace.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the HuggingFace connector.
/// Eliminates magic "Koan:" string literals across HuggingFace configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Ai:HuggingFace";

    /// <summary>
    /// Builds full configuration path: "Koan:Ai:HuggingFace:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
