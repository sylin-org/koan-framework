namespace Koan.Service.KoanContext.Infrastructure;

/// <summary>
/// Centralized constants for the Koan context service.
/// </summary>
public static class Constants
{
    public static class Security
    {
        /// <summary>
        /// Default maximum allowed project path length.
        /// The limit keeps paths comfortably below common platform limits while
        /// guarding against traversal exploits with excessively long inputs.
        /// </summary>
        public const int MaxProjectPathLength = 512;

        public const string RestrictiveValidationFlag = "Koan:Context:Security:EnableRestrictivePathValidation";
        public const string AllowedDirectoriesSection = "Koan:Context:Security:AllowedDirectories";
        public const string MaxPathLengthKey = "Koan:Context:Security:MaxPathLength";
    }

    public static class Routes
    {
        /// <summary>
        /// Base path for tag vocabulary CRUD endpoints.
        /// </summary>
        public const string Tags = "api/tags";

        /// <summary>
        /// Base path for tag rule management endpoints.
        /// </summary>
        public const string TagRules = "api/tag-rules";

        /// <summary>
        /// Base path for tag pipeline management endpoints.
        /// </summary>
        public const string TagPipelines = "api/tag-pipelines";

        /// <summary>
        /// Base path for search persona management endpoints.
        /// </summary>
        public const string SearchPersonas = "api/search-personas";
    }

    public static class CacheKeys
    {
        /// <summary>
        /// Global cache slot for tag vocabulary snapshots.
        /// </summary>
        public const string TagVocabulary = "tag-vocabulary";

        /// <summary>
        /// Formats the cache key for tag pipeline snapshots.
        /// </summary>
        public static string TagPipeline(string? pipelineName)
            => $"tag-pipeline:{(string.IsNullOrWhiteSpace(pipelineName) ? "default" : pipelineName.Trim().ToLowerInvariant())}";

        /// <summary>
        /// Formats the cache key for resolved personas.
        /// </summary>
        public static string Persona(string? personaName)
            => $"persona:{(string.IsNullOrWhiteSpace(personaName) ? "general" : personaName.Trim().ToLowerInvariant())}";
    }
}
