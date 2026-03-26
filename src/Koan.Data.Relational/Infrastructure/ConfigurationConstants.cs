namespace Koan.Data.Relational.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Data.Relational module.
/// Eliminates magic "Koan:" string literals across relational data configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Data:Relational";

    public static class Keys
    {
        public const string Materialization = Section + ":Materialization";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Data:Relational:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
