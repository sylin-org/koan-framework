using System;

namespace Koan.Jobs.Web;

/// <summary>
/// Lightweight projection of a Job for REST API responses.
/// </summary>
public sealed record JobSummaryDto(
    string Id,
    string Name,
    string Status,
    double Progress,
    string? ProgressMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? LastError,
    int ExecutionCount);
