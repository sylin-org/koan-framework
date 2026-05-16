namespace Koan.AI.Infrastructure;

internal static class ConfigurationConstants
{
    public const string Section = "Koan:Ai";

    public static class Keys
    {
        public const string AutoDiscoveryEnabled = nameof(AutoDiscoveryEnabled);
        public const string AllowDiscoveryInNonDev = nameof(AllowDiscoveryInNonDev);
        public const string DefaultPolicy = nameof(DefaultPolicy);
    }

    public static class Recipe
    {
        public const string ActiveRecipe = ConfigurationConstants.Section + ":ActiveRecipe";

        /// <summary>
        /// Pattern: Koan:Ai:Recipes:{recipeName}
        /// </summary>
        public static string ForRecipe(string recipeName) => $"{ConfigurationConstants.Section}:Recipes:{recipeName}";
    }

    public static class Category
    {
        /// <summary>
        /// Pattern: Koan:Ai:{category}:Source
        /// </summary>
        public static string Source(string category) => $"{ConfigurationConstants.Section}:{category}:Source";

        /// <summary>
        /// Pattern: Koan:Ai:{category}:Model
        /// </summary>
        public static string Model(string category) => $"{ConfigurationConstants.Section}:{category}:Model";

        /// <summary>
        /// Pattern: Koan:Ai:{category}:Via
        /// </summary>
        public static string Via(string category) => $"{ConfigurationConstants.Section}:{category}:Via";
    }

    public static class Sources
    {
        public const string Section = ConfigurationConstants.Section + ":Sources";
    }

    public static class Ollama
    {
        public const string Section = ConfigurationConstants.Section + ":Ollama";
        public const string BaseUrl = Ollama.Section + ":BaseUrl";
        public const string DefaultModel = Ollama.Section + ":DefaultModel";
        public const string Capabilities = Ollama.Section + ":Capabilities";
    }

    /// <summary>
    /// Builds full configuration path: "Koan:Ai:{key}".
    /// </summary>
    public static string FullKey(string key) => $"{Section}:{key}";
}
