namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from text-to-speech, including metadata.</summary>
public sealed record SpeakResult
{
    public required byte[] Audio { get; init; }
    public AudioFormat Format { get; init; }
    public string? Model { get; init; }
    public string? Voice { get; init; }
    public TimeSpan? Duration { get; init; }
    public TimeSpan Latency { get; init; }
}
