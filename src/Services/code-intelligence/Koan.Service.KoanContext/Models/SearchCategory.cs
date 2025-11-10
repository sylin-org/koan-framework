using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Context.Models;

/// <summary>
/// Defines a search content category with path-based auto-classification
/// </summary>
/// <remarks>
/// Categories enable content type discrimination (guides vs. source code).
/// Path patterns use glob syntax: "docs/guides/**", "src/**/*.cs"
/// Auto-seeded with defaults: guide, adr, sample, test, documentation, source, reference
/// </remarks>
[McpEntity(
    Name = "SearchCategory",
    Description = "Manage search content categories with path-based auto-classification",
    AllowMutations = true,
    Exposure = "full")]
public class SearchCategory : Entity<SearchCategory>
{
    /// <summary>
    /// Unique category identifier (e.g., "guide", "adr", "source")
    /// Used in API filters and audience mappings
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name (e.g., "Developer Guides", "Architecture Decisions")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this category represents
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Glob patterns for auto-classification during indexing
    /// Examples: ["docs/guides/**", "guides/*.md"], ["src/**/*.cs"]
    /// First matching pattern wins (ordered by Priority)
    /// </summary>
    public List<string> PathPatterns { get; set; } = new();

    /// <summary>
    /// Priority for pattern matching and result boosting (higher = first)
    /// Used when multiple patterns could match (e.g., "docs/api/guide.md")
    /// Also used for result re-ranking (boost documentation over code)
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// Default semantic vs keyword weight for this category
    /// 0.0 = keyword-only, 1.0 = semantic-only, 0.5 = balanced
    /// Example: ADRs might prefer 0.3 (keyword-heavy), code might prefer 0.7 (semantic-heavy)
    /// </summary>
    public float DefaultAlpha { get; set; } = 0.5f;

    /// <summary>
    /// Whether this category is active (allows soft-delete)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Icon name for UI display (optional)
    /// Examples: "book", "code", "lightbulb", "flask"
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Color hex code for UI display (optional)
    /// Examples: "#3B82F6" (blue), "#10B981" (green)
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Creates a new search category with validation
    /// </summary>
    public static SearchCategory Create(
        string name,
        string displayName,
        string description,
        List<string> pathPatterns,
        int priority = 5,
        float defaultAlpha = 0.5f)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty", nameof(displayName));

        if (pathPatterns == null || pathPatterns.Count == 0)
            throw new ArgumentException("At least one path pattern required", nameof(pathPatterns));

        if (defaultAlpha < 0f || defaultAlpha > 1f)
            throw new ArgumentException("DefaultAlpha must be between 0.0 and 1.0", nameof(defaultAlpha));

        return new SearchCategory
        {
            Name = name,
            DisplayName = displayName,
            Description = description,
            PathPatterns = pathPatterns,
            Priority = priority,
            DefaultAlpha = defaultAlpha,
            IsActive = true
        };
    }
}
