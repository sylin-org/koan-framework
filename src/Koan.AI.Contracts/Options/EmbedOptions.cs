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

    /// <summary>
    /// AI-0035: Caller-supplied URL override. When set, the router bypasses source / member
    /// resolution and dispatches directly to this endpoint. Pair with
    /// <see cref="OverrideProvider"/> (defaults to <c>"ollama"</c>) to select the adapter.
    /// </summary>
    public string? OverrideUrl { get; init; }

    /// <summary>
    /// AI-0035: Provider identifier paired with <see cref="OverrideUrl"/>. Defaults to
    /// <c>"ollama"</c> when omitted.
    /// </summary>
    public string? OverrideProvider { get; init; }

    /// <summary>Vendor-specific options (forwarded to adapter as-is).</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
