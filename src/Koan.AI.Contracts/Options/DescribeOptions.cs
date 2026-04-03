using Koan.AI.Contracts.Models;

namespace Koan.AI.Contracts.Options;

/// <summary>Options for media description requests (image/video/audio → text). Delegates via Chat.</summary>
public sealed record DescribeOptions
{
    /// <summary>Content modality (Image, Video, Audio).</summary>
    public Modality? Modality { get; init; }

    /// <summary>Description detail level.</summary>
    public DescribeDetail? Detail { get; init; }

    /// <summary>Description purpose — shapes the output style.</summary>
    public DescribePurpose? Purpose { get; init; }

    /// <summary>Free-text focus hint (e.g., "accessibility", "e-commerce").</summary>
    public string? Focus { get; init; }

    /// <summary>Output language.</summary>
    public string? Language { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Description detail level.</summary>
public enum DescribeDetail { Brief, Standard, Detailed }

/// <summary>Description purpose — changes the system prompt.</summary>
public enum DescribePurpose { General, AltText, Caption, ProductListing, SearchIndex }
