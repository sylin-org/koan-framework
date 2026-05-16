using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Options;

/// <summary>Options for typed extraction requests. The generic type parameter IS the schema.</summary>
public sealed record ExtractOptions
{
    /// <summary>Content modality when extracting from non-text content.</summary>
    public Modality? Modality { get; init; }

    /// <summary>Named prompt for extraction instructions (AI-0025).</summary>
    public string? Prompt { get; init; }

    /// <summary>Fail if schema can't be fully populated.</summary>
    public bool? Strict { get; init; }

    /// <summary>Page range for documents (e.g., 3..7).</summary>
    public Range? Pages { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
