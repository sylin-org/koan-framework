namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from video generation.</summary>
public sealed record RenderResult
{
    public required byte[] Video { get; init; }
    public VideoFormat Format { get; init; }
    public string? Model { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Fps { get; init; }
    public long? Seed { get; init; }
    public TimeSpan Latency { get; init; }
}
