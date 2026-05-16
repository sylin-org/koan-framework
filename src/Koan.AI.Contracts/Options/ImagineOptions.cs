namespace Koan.AI.Contracts.Options;

/// <summary>Options for image generation requests.</summary>
public sealed record ImagineOptions
{
    /// <summary>Image width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>CFG / guidance scale for diffusion models.</summary>
    public double? Guidance { get; init; }

    /// <summary>Number of sampling steps.</summary>
    public int? Steps { get; init; }

    /// <summary>Seed for reproducible generation.</summary>
    public long? Seed { get; init; }

    /// <summary>Negative prompt — what to avoid.</summary>
    public string? Negative { get; init; }

    /// <summary>Desired output format.</summary>
    public Models.ImageFormat? Format { get; init; }

    /// <summary>Reference image bytes for img2img.</summary>
    public byte[]? Reference { get; init; }

    /// <summary>How closely to follow the reference (0.0–1.0).</summary>
    public double? ReferenceWeight { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
