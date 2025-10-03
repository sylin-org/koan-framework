using System;

namespace Koan.Jobs.Progress;

public interface IJobProgress
{
    void Report(double percentage, string? message = null, DateTimeOffset? estimatedCompletion = null);
    void Report(int current, int total, string? message = null, DateTimeOffset? estimatedCompletion = null);
    DateTimeOffset? EstimatedCompletion { get; }
    bool CancellationRequested { get; }
}
