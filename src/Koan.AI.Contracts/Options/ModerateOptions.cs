using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Options;

/// <summary>Options for content moderation requests. Policy-driven.</summary>
public sealed record ModerateOptions
{
    /// <summary>Content modality when moderating non-text content.</summary>
    public Modality? Modality { get; init; }

    /// <summary>Named moderation policy from the Prompt catalog.</summary>
    public string? Policy { get; init; }

    /// <summary>Strictness threshold (0.0–1.0). Higher = stricter.</summary>
    public double? Threshold { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
