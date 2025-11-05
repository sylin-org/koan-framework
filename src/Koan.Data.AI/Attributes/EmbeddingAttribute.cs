namespace Koan.Data.AI.Attributes;

/// <summary>
/// Marks an entity for automatic embedding generation and vectorization.
/// Supports three modes: Policy-based auto-discovery, Template-based composition, or explicit Properties list.
/// Precedence: Template > Properties > Policy
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class EmbeddingAttribute : Attribute
{
    /// <summary>
    /// Auto-discovery policy for determining which properties to embed (default: AllStrings).
    /// Only applies when Template and Properties are null.
    /// </summary>
    public EmbeddingPolicy Policy { get; set; } = EmbeddingPolicy.AllStrings;

    /// <summary>
    /// Template string for composing embedding text (e.g., "{Title}\n\n{Content}").
    /// Property names in braces are replaced with their values.
    /// Takes precedence over Properties and Policy.
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// Explicit list of property names to include in embedding text.
    /// Takes precedence over Policy, but Template takes precedence over this.
    /// </summary>
    public string[]? Properties { get; set; }

    /// <summary>
    /// Queue for async background processing instead of blocking Save() (default: false).
    /// When true, embedding generation is deferred to a background worker.
    /// </summary>
    public bool Async { get; set; } = false;

    /// <summary>
    /// AI model override for this entity type (optional).
    /// If null, uses the default embedding model from configuration.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Batch size for async queue processing (default: 10).
    /// Only applies when Async = true.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Rate limit per minute for this entity type (optional).
    /// If 0, uses global rate limit from configuration.
    /// Only applies when Async = true.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 0;
}
