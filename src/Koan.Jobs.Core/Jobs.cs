using Koan.Jobs.Recipes;

namespace Koan.Jobs;

/// <summary>
/// Static entry point for job operations and recipe creation.
/// </summary>
public static class Jobs
{
    /// <summary>
    /// Create a new job recipe builder for capturing reusable configuration defaults.
    /// </summary>
    public static JobRecipeBuilder Recipe() => new();
}
