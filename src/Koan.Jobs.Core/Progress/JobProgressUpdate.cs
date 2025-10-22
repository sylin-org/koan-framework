using System;

namespace Koan.Jobs.Progress;

public sealed class JobProgressUpdate
{
    public double Percentage { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? EstimatedCompletion { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
