using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Options;

/// <summary>Options for classification requests. Template-driven: requires labels, prompt, or generic type.</summary>
public sealed record ClassifyOptions
{
    /// <summary>Content modality when classifying non-text content.</summary>
    public Modality? Modality { get; init; }

    /// <summary>Inline label set for classification.</summary>
    public string[]? Labels { get; init; }

    /// <summary>Named prompt from the Prompt catalog (AI-0025).</summary>
    public string? Prompt { get; init; }

    /// <summary>Enable multi-label classification.</summary>
    public bool? MultiLabel { get; init; }

    /// <summary>Return confidence scores.</summary>
    public bool? Confidence { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
