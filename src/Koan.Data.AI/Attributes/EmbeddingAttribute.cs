namespace Koan.Data.AI.Attributes;

/// <summary>
/// Marks an entity for automatic embedding generation and vectorization.
/// Supports three modes: Policy-based auto-discovery, Template-based composition, or explicit Properties list.
/// Precedence: Template > Properties > Policy
/// Worker throughput and retry policy are configured once for the host, not per Entity type.
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
    /// AI source or group name for routing embeddings to specific providers.
    /// Flows through the embedding-category source scope to route to the appropriate AI service.
    /// Examples: "ollama-primary", "openai-prod", "azure-embeddings"
    /// If null, uses default routing.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Maximum tokens allowed for embedding text (provider-specific limit).
    /// Examples: 8191 for text-embedding-3-large, 512 for all-MiniLM-L6-v2.
    /// If exceeded, text is intelligently truncated with optional warning.
    /// If 0, no truncation is applied (default).
    /// </summary>
    public int MaxTokens { get; set; } = 0;

    /// <summary>
    /// Maximum depth for nested object traversal when using EmbeddingPolicy.FullJson.
    /// Prevents infinite recursion in circular references.
    /// Default: 3 levels deep.
    /// </summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// Properties to exclude from embedding (complements [EmbeddingIgnore]).
    /// Useful for runtime exclusions without modifying entity class.
    /// Example: new[] { "InternalId", "Metadata" }
    /// </summary>
    public string[]? Exclude { get; set; }

    /// <summary>
    /// Emit warning in development when embedding text is truncated due to MaxTokens limit.
    /// Helps developers identify when important content is being cut off.
    /// Only applies when MaxTokens > 0.
    /// Default: true.
    /// </summary>
    public bool WarnOnTruncation { get; set; } = true;

    /// <summary>
    /// Schema version for this embedding configuration.
    /// Increment when changing Template, Properties, Policy, or content structure.
    /// Forces re-embedding of all entities when version changes.
    /// Default: 1.
    /// </summary>
    public int Version { get; set; } = 1;
}
