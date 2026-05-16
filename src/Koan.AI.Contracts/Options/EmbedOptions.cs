using System.Collections.Generic;

namespace Koan.AI.Contracts.Options;

/// <summary>
/// Options for AI embedding/vector generation requests.
/// </summary>
public sealed record EmbedOptions
{
    /// <summary>Single text to embed (for simple usage).</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Multiple texts to embed (batch operation).
    /// If provided, takes precedence over Text.
    /// </summary>
    public string[]? Texts { get; init; }

    /// <summary>Normalize embeddings to unit length (if provider supports it).</summary>
    public bool? Normalize { get; init; }

    /// <summary>Truncate input text if exceeds model's token limit (if provider supports it).</summary>
    public bool? Truncate { get; init; }

    /// <summary>
    /// Override source for this request.
    /// Takes precedence over scope and routing configuration.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options (forwarded to adapter as-is).</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
