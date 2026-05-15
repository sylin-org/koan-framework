namespace Koan.AI.Contracts.Models;

/// <summary>Request for image generation (text → image).</summary>
public sealed record ImagineRequest
{
    /// <summary>Text prompt describing the desired image.</summary>
    public required string Prompt { get; init; }

    /// <summary>Model name or moniker (e.g., "flux-dev", "recommended:imagine").</summary>
    public string? Model { get; init; }

    /// <summary>Negative prompt — what to avoid in the image.</summary>
    public string? Negative { get; init; }

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

    /// <summary>Reference image bytes for img2img workflows.</summary>
    public byte[]? Reference { get; init; }

    /// <summary>How closely to follow the reference (0.0–1.0).</summary>
    public double? ReferenceWeight { get; init; }

    /// <summary>Desired output format.</summary>
    public ImageFormat Format { get; init; } = ImageFormat.Png;

    /// <summary>Injected by router — target endpoint.</summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>Pass-through vendor options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Response from image generation.</summary>
public sealed record ImagineResponse
{
    /// <summary>Generated image bytes.</summary>
    public required byte[] Image { get; init; }

    /// <summary>Actual output format.</summary>
    public ImageFormat Format { get; init; }

    /// <summary>Model that generated the image.</summary>
    public string? Model { get; init; }

    /// <summary>Seed used (for reproducibility).</summary>
    public long? Seed { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Revised prompt (if model modified it).</summary>
    public string? RevisedPrompt { get; init; }
}

/// <summary>Output image format.</summary>
public enum ImageFormat { Png, Jpeg, WebP }
