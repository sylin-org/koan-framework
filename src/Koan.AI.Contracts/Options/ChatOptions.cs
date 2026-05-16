using System.Collections.Generic;
using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Options;

/// <summary>
/// Options for AI chat/text generation requests.
/// Supports multimodal (vision) via <see cref="Image"/> property.
/// </summary>
public sealed record ChatOptions
{
    /// <summary>User message (required for simple usage).</summary>
    public string? Message { get; init; }

    /// <summary>
    /// Full conversation history (for multi-turn conversations).
    /// If provided, takes precedence over Message.
    /// </summary>
    public List<AiMessage>? Messages { get; init; }

    /// <summary>System prompt to guide model behavior.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Temperature for randomness (0.0–2.0, typically). Lower = more deterministic.</summary>
    public double? Temperature { get; init; }

    /// <summary>Maximum output tokens to generate.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>Top-p sampling (nucleus sampling). Example: 0.9</summary>
    public double? TopP { get; init; }

    /// <summary>Stop sequences to halt generation.</summary>
    public string[]? Stop { get; init; }

    /// <summary>Seed for reproducible outputs (if provider supports it).</summary>
    public int? Seed { get; init; }

    /// <summary>Enable reasoning/thinking mode (for models that support it).</summary>
    public bool? Think { get; init; }

    /// <summary>
    /// Request structured output format. Common values: "json", "text".
    /// Provider support varies:
    /// - Ollama: Maps to top-level "format" parameter
    /// - OpenAI/LMStudio: Maps to "response_format": { "type": "json_object" }
    /// </summary>
    public string? ResponseFormat { get; init; }

    /// <summary>Image data for multimodal/vision requests (absorbs former AiVisionOptions).</summary>
    public byte[]? Image { get; init; }

    /// <summary>MIME type of the image (e.g., "image/png", "image/jpeg").</summary>
    public string? ImageMimeType { get; init; }

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
