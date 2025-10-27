using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.AI.Contracts.Models;

public record AiPromptOptions
{
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public double? TopP { get; init; }
    public string[]? Stop { get; init; }
    public int? Seed { get; init; }
    public string? Profile { get; init; }
    /// <summary>
    /// Optional reasoning/"thinking" mode toggle for providers that support it (e.g., Ollama models like Qwen3/R1, DeepSeek V3.1 templates).
    /// When set, adapters may emit a provider-specific flag (for Ollama, top-level "think": true/false).
    /// </summary>
    public bool? Think { get; init; }

    /// <summary>
    /// Request structured output format. Common values: "json", "text".
    /// Provider support varies:
    /// - Ollama: Maps to top-level "format" parameter (e.g., "json" enforces valid JSON output)
    /// - OpenAI/LMStudio: Maps to "response_format": { "type": "json_object" }
    /// - Anthropic: Gracefully ignored (relies on prompt engineering)
    /// Use "json" to request JSON-formatted responses from compatible models.
    /// </summary>
    public string? ResponseFormat { get; init; }

    /// <summary>
    /// Vendor-specific passthrough options. Any unknown fields posted alongside known options
    /// will be captured here and forwarded by adapters that support vendor option bags.
    /// Example (Ollama): { "mirostat": 2, "repeat_penalty": 1.1 }.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? VendorOptions { get; init; }
}