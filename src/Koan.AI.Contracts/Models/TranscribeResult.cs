namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from transcription, including segments and metadata.</summary>
public sealed record TranscribeResult
{
    public required string Text { get; init; }
    public string? Language { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? Model { get; init; }
    public IReadOnlyList<TranscribeSegment>? Segments { get; init; }
    public TimeSpan Latency { get; init; }
}
