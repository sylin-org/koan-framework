namespace Koan.AI.Contracts.Options;

/// <summary>Options for video generation requests. Experimental.</summary>
public sealed record RenderOptions
{
    /// <summary>Target video duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Video width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Video height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Frames per second.</summary>
    public int? Fps { get; init; }

    /// <summary>Reference image for image-to-video.</summary>
    public byte[]? Reference { get; init; }

    /// <summary>Generate native audio.</summary>
    public bool? WithAudio { get; init; }

    /// <summary>Desired output format.</summary>
    public Models.VideoFormat? Format { get; init; }

    /// <summary>Seed for reproducibility.</summary>
    public long? Seed { get; init; }

    /// <summary>Timeout for generation (default: 5 minutes).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
