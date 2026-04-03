namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from image generation, including metadata.</summary>
public sealed record ImagineResult
{
    public required byte[] Image { get; init; }
    public ImageFormat Format { get; init; }
    public string? Model { get; init; }
    public long? Seed { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? RevisedPrompt { get; init; }
    public TimeSpan Latency { get; init; }
}
