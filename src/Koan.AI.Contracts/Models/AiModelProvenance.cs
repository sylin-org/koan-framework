using System;
using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

/// <summary>Optional metadata describing why a model-management action occurred.</summary>
public sealed record AiModelProvenance
{
    /// <summary>The actor initiating the operation (user, service, automated agent).</summary>
    public string? RequestedBy { get; init; }

    /// <summary>High-level reason or workflow name (e.g., "readiness", "manual-request").</summary>
    public string? Reason { get; init; }

    /// <summary>Correlation identifier for downstream observability.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>UTC timestamp for when the request originated.</summary>
    public DateTimeOffset? RequestedAt { get; init; }

    /// <summary>Additional provider- or workflow-specific metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
