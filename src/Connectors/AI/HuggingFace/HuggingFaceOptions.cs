namespace Koan.AI.Connector.HuggingFace;

/// <summary>
/// Configuration for the HuggingFace Hub integration.
/// Bound from <c>Koan:Ai:HuggingFace</c> via AddKoanOptions.
/// </summary>
public sealed record HuggingFaceOptions
{
    /// <summary>HuggingFace API token for accessing private/gated models. Falls back to HF_TOKEN env var.</summary>
    public string? Token { get; init; }

    /// <summary>Local directory for cached model files. Relative paths resolve from app root.</summary>
    public string CacheDirectory { get; init; } = ".Koan/models";

    /// <summary>HuggingFace Hub base URL. Override for self-hosted Hub instances.</summary>
    public string BaseUrl { get; init; } = "https://huggingface.co";
}
