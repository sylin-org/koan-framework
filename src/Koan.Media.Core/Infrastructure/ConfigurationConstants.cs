namespace Koan.Media.Core.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Media.Core module.
/// Eliminates magic "Koan:" string literals across media configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Media";

    public static class Keys
    {
        public const string Transforms = Section + ":Transforms";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Media:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
