namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from typed extraction.</summary>
public sealed record ExtractResult<T>
{
    /// <summary>The extracted typed value.</summary>
    public required T Value { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }

    /// <summary>Overall confidence (0.0–1.0).</summary>
    public double? Confidence { get; init; }

    /// <summary>Per-field confidence scores (keyed by property name).</summary>
    public IReadOnlyDictionary<string, double>? FieldConfidence { get; init; }

    /// <summary>Processing time.</summary>
    public TimeSpan Latency { get; init; }
}
