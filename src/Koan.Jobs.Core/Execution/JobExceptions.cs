using System;

namespace Koan.Jobs.Execution;

public sealed class JobFailedException : Exception
{
    public JobFailedException(string jobId, string? message)
        : base(message ?? $"Job '{jobId}' failed.")
    {
        JobId = jobId;
    }

    public string JobId { get; }
}

public sealed class JobCancelledException : OperationCanceledException
{
    public JobCancelledException(string jobId)
        : base($"Job '{jobId}' was cancelled.")
    {
        JobId = jobId;
    }

    public string JobId { get; }
}
