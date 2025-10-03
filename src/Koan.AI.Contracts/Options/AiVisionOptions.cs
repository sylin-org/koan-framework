namespace Koan.AI.Contracts.Options;

/// <summary>
/// Options for AI vision/image understanding requests.
/// Extends AiOptionsBase with vision-specific parameters.
/// </summary>
public sealed record AiVisionOptions : AiOptionsBase
{
    /// <summary>
    /// Image data as byte array (required)
    /// </summary>
    public required byte[] ImageBytes { get; init; }

    /// <summary>
    /// Prompt/question about the image (required)
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Image format hint (if known). Examples: "image/png", "image/jpeg"
    /// Some providers can auto-detect, but providing this may improve performance.
    /// </summary>
    public string? ImageFormat { get; init; }

    /// <summary>
    /// System prompt to guide model behavior
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Temperature for randomness (0.0-2.0, typically). Lower = more deterministic.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Maximum output tokens to generate
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Vendor-specific options (forwarded to adapter as-is)
    /// </summary>
    public System.Collections.Generic.IDictionary<string, object>? VendorOptions { get; init; }
}
