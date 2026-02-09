using System;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// Rich result from a chat operation, returned by <c>Client.ChatResult()</c>.
/// </summary>
public sealed record ChatResult
{
    /// <summary>Generated text response.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Model that served the request.</summary>
    public string? Model { get; init; }

    /// <summary>Total tokens consumed (input + output).</summary>
    public int? TokensUsed { get; init; }

    /// <summary>Input token count.</summary>
    public int? TokensIn { get; init; }

    /// <summary>Output token count.</summary>
    public int? TokensOut { get; init; }

    /// <summary>End-to-end latency.</summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>Adapter that handled the request.</summary>
    public string? AdapterId { get; init; }

    /// <summary>Reason generation stopped (e.g., "stop", "length").</summary>
    public string? FinishReason { get; init; }
}
