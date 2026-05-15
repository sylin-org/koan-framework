namespace Koan.AI.Contracts.Models;

/// <summary>Request for image editing (image + instruction → image).</summary>
public sealed record EditRequest
{
    /// <summary>Source image bytes to edit.</summary>
    public required byte[] Image { get; init; }

    /// <summary>Text instruction describing the desired edit.</summary>
    public required string Instruction { get; init; }

    /// <summary>Model name (e.g., "flux-inpaint", "recommended:edit").</summary>
    public string? Model { get; init; }

    /// <summary>Optional mask bytes indicating the region to edit.</summary>
    public byte[]? Mask { get; init; }

    /// <summary>Edit strength (0.0–1.0). Lower preserves more of the original.</summary>
    public double? Strength { get; init; }

    /// <summary>Desired output format.</summary>
    public ImageFormat Format { get; init; } = ImageFormat.Png;

    /// <summary>Injected by router.</summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>Pass-through vendor options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Response from image editing.</summary>
public sealed record EditResponse
{
    /// <summary>Edited image bytes.</summary>
    public required byte[] Image { get; init; }

    /// <summary>Actual output format.</summary>
    public ImageFormat Format { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }
}
