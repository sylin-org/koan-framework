namespace Koan.AI.Contracts.Models;

/// <summary>Request for video generation (text → video). Experimental.</summary>
public sealed record RenderRequest
{
    /// <summary>Text prompt describing the desired video.</summary>
    public required string Prompt { get; init; }

    /// <summary>Model name (e.g., "animatediff-v3", "recommended:render").</summary>
    public string? Model { get; init; }

    /// <summary>Target video duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Video width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Video height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Frames per second.</summary>
    public int? Fps { get; init; }

    /// <summary>Reference image for image-to-video workflows.</summary>
    public byte[]? Reference { get; init; }

    /// <summary>Whether to generate native audio.</summary>
    public bool? WithAudio { get; init; }

    /// <summary>Seed for reproducibility.</summary>
    public long? Seed { get; init; }

    /// <summary>Desired output format.</summary>
    public VideoFormat Format { get; init; } = VideoFormat.Mp4;

    /// <summary>Timeout for generation (default: 5 minutes).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Injected by router.</summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>Pass-through vendor options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Response from video generation.</summary>
public sealed record RenderResponse
{
    /// <summary>Generated video bytes.</summary>
    public required byte[] Video { get; init; }

    /// <summary>Actual output format.</summary>
    public VideoFormat Format { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }

    /// <summary>Video duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Video width.</summary>
    public int? Width { get; init; }

    /// <summary>Video height.</summary>
    public int? Height { get; init; }

    /// <summary>Frames per second.</summary>
    public int? Fps { get; init; }

    /// <summary>Seed used.</summary>
    public long? Seed { get; init; }
}

/// <summary>Output video format.</summary>
public enum VideoFormat { Mp4, WebM }
