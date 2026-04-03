namespace Koan.AI.Contracts.Options;

/// <summary>Options for image editing requests.</summary>
public sealed record EditOptions
{
    /// <summary>Optional mask bytes indicating the region to edit.</summary>
    public byte[]? Mask { get; init; }

    /// <summary>Desired output format.</summary>
    public Models.ImageFormat? Format { get; init; }

    /// <summary>Edit strength (0.0–1.0). Lower preserves more of the original.</summary>
    public double? Strength { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
