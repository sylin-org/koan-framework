namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from classification with confidence scores.</summary>
public sealed record ClassifyResult<T>
{
    /// <summary>The winning label.</summary>
    public required T Label { get; init; }

    /// <summary>Confidence score for the winning label (0.0–1.0).</summary>
    public double? Confidence { get; init; }

    /// <summary>All labels with their scores (when Confidence option is enabled).</summary>
    public IReadOnlyDictionary<string, double>? AllScores { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }

    /// <summary>Processing time.</summary>
    public TimeSpan Latency { get; init; }
}
