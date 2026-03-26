namespace Koan.Data.AI.Infrastructure;

/// <summary>
/// Centralized configuration key constants for the Koan.Data.AI module.
/// Eliminates magic "Koan:" string literals across Data.AI configuration.
/// </summary>
internal static class ConfigurationConstants
{
    public const string Section = "Koan:Data:AI";

    public static class Keys
    {
        public const string EmbeddingWorker = Section + ":EmbeddingWorker";
        public const string MediaAnalysisWorker = Section + ":MediaAnalysis";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Data:AI:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
