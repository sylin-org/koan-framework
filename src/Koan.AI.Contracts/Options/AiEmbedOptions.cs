namespace Koan.AI.Contracts.Options;

/// <summary>
/// Options for AI embedding/vector generation requests.
/// Extends AiOptionsBase with embedding-specific parameters.
/// </summary>
public sealed record AiEmbedOptions : AiOptionsBase
{
    /// <summary>
    /// Single text to embed (for simple usage)
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Multiple texts to embed (batch operation).
    /// If provided, takes precedence over Text.
    /// </summary>
    public string[]? Texts { get; init; }

    /// <summary>
    /// Normalize embeddings to unit length (if provider supports it).
    /// Default: false
    /// </summary>
    public bool? Normalize { get; init; }

    /// <summary>
    /// Truncate input text if exceeds model's token limit (if provider supports it).
    /// Default: true
    /// </summary>
    public bool? Truncate { get; init; }

    /// <summary>
    /// Vendor-specific options (forwarded to adapter as-is)
    /// </summary>
    public System.Collections.Generic.IDictionary<string, object>? VendorOptions { get; init; }
}
