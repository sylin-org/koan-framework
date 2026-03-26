using System;

namespace Koan.Jobs.Execution;

/// <summary>
/// Custom retry policy for jobs that need non-standard retry behavior.
/// Implement this interface on a job type to override the default [RetryPolicy] attribute.
/// </summary>
public interface ICustomRetryPolicy
{
    /// <summary>
    /// Determines whether the failed job should be retried.
    /// </summary>
    /// <param name="attemptNumber">The current attempt number (1-based).</param>
    /// <param name="error">The exception that caused the failure.</param>
    /// <returns>True to retry, false to fail permanently.</returns>
    bool ShouldRetry(int attemptNumber, Exception error);

    /// <summary>
    /// Computes the delay before the next retry attempt.
    /// </summary>
    /// <param name="attemptNumber">The current attempt number (1-based).</param>
    /// <param name="error">The exception that caused the failure.</param>
    /// <returns>Delay before next retry. TimeSpan.Zero for immediate retry.</returns>
    TimeSpan ComputeDelay(int attemptNumber, Exception error);
}
