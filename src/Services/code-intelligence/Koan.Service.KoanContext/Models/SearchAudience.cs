using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Context.Models;

/// <summary>
/// Defines a search audience profile with category filtering and search tuning
/// </summary>
/// <remarks>
/// Audiences enable intent-based search optimization (learner, architect, PM, executive).
/// Each audience has preferred categories and semantic tuning.
/// Auto-seeded with defaults: learner, developer, architect, pm, executive, contributor
/// </remarks>
[McpEntity(
    Name = "SearchAudience",
    Description = "Manage search audience profiles with category filtering and search tuning",
    AllowMutations = true,
    Exposure = "full")]
public class SearchAudience : Entity<SearchAudience>
{
    /// <summary>
    /// Unique audience identifier (e.g., "learner", "architect", "pm")
    /// Used in API audience parameter
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name (e.g., "Developer Learning Koan", "Technical Architect")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of who this audience represents
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Category names to filter (e.g., ["guide", "sample", "test"])
    /// Empty list = all categories
    /// </summary>
    public List<string> CategoryNames { get; set; } = new();

    /// <summary>
    /// Default semantic vs keyword weight for this audience
    /// 0.0 = keyword-only, 1.0 = semantic-only, 0.5 = balanced
    /// Example: Executives prefer 0.2 (keyword-heavy for precise terms),
    /// developers prefer 0.6 (semantic for concept understanding)
    /// </summary>
    public float DefaultAlpha { get; set; } = 0.5f;

    /// <summary>
    /// Maximum token budget for results (controls result verbosity)
    /// Example: Executives might prefer 2000 (summaries), developers prefer 8000 (details)
    /// </summary>
    public int MaxTokens { get; set; } = 5000;

    /// <summary>
    /// Whether to include reasoning metadata in results
    /// Example: Architects might want true (understand retrieval), PMs might want false (cleaner)
    /// </summary>
    public bool IncludeReasoning { get; set; } = true;

    /// <summary>
    /// Whether to include insights metadata in results
    /// </summary>
    public bool IncludeInsights { get; set; } = true;

    /// <summary>
    /// Whether this audience is active (allows soft-delete)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Icon name for UI display (optional)
    /// Examples: "user-graduate" (learner), "user-tie" (executive)
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Creates a new search audience with validation
    /// </summary>
    public static SearchAudience Create(
        string name,
        string displayName,
        string description,
        List<string> categoryNames,
        float defaultAlpha = 0.5f,
        int maxTokens = 5000)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty", nameof(displayName));

        if (defaultAlpha < 0f || defaultAlpha > 1f)
            throw new ArgumentException("DefaultAlpha must be between 0.0 and 1.0", nameof(defaultAlpha));

        if (maxTokens < 1000 || maxTokens > 20000)
            throw new ArgumentException("MaxTokens must be between 1000 and 20000", nameof(maxTokens));

        return new SearchAudience
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            CategoryNames = categoryNames ?? new List<string>(),
            DefaultAlpha = defaultAlpha,
            MaxTokens = maxTokens,
            IsActive = true
        };
    }
}
