namespace Koan.Recipe.Abstractions.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Recipe.Abstractions module.
/// Eliminates magic "Koan:" string literals across recipe configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Recipes";

    public static class Keys
    {
        public const string AllowOverrides = Section + ":AllowOverrides";
    }

    /// <summary>
    /// Pattern: Koan:Recipes:{recipeName}:ForceOverrides
    /// </summary>
    public static string ForceOverrides(string recipeName) => $"{Section}:{recipeName}:ForceOverrides";

    /// <summary>
    /// Builds full configuration path: "Koan:Recipes:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
