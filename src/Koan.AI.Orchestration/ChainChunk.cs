namespace Koan.AI.Orchestration;

/// <summary>
/// A streaming chunk from a chain execution.
/// </summary>
public sealed record ChainChunk
{
    /// <summary>Text content of the chunk (null for non-text events).</summary>
    public string? Text { get; init; }

    /// <summary>Current chain step name (null if unchanged).</summary>
    public string? Step { get; init; }

    /// <summary>Whether this chunk represents a tool call event.</summary>
    public bool IsToolCall { get; init; }
}
