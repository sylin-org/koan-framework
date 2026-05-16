namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from image editing.</summary>
public sealed record EditResult
{
    public required byte[] Image { get; init; }
    public ImageFormat Format { get; init; }
    public string? Model { get; init; }
    public TimeSpan Latency { get; init; }
}
