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
}
