namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from translation.</summary>
public sealed record TranslateResult
{
    public required string Text { get; init; }
    public string? DetectedSource { get; init; }
    public required string Target { get; init; }
    public string? Model { get; init; }
    public double? Confidence { get; init; }
    public TimeSpan Latency { get; init; }
}
