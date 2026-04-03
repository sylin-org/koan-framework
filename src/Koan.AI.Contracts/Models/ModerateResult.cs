namespace Koan.AI.Contracts.Models;

/// <summary>Rich result from content moderation.</summary>
public sealed record ModerateResult
{
    /// <summary>Whether the content is allowed under the policy.</summary>
    public required bool Allowed { get; init; }

    /// <summary>Flags raised by the moderation (e.g., "violence", "hate").</summary>
    public IReadOnlyList<string>? Flags { get; init; }

    /// <summary>Per-category scores (keyed by category name).</summary>
    public IReadOnlyDictionary<string, double>? CategoryScores { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }

    /// <summary>Processing time.</summary>
    public TimeSpan Latency { get; init; }
}

/// <summary>Simple moderation verdict returned by Client.Moderate().</summary>
public sealed record ModerationVerdict
{
    /// <summary>Whether the content is allowed.</summary>
    public required bool Allowed { get; init; }

    /// <summary>Flags raised.</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];
}
