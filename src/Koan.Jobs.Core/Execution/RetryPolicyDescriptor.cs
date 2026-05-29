using System;

namespace Koan.Jobs.Execution;

public sealed record RetryPolicyDescriptor(
    int MaxAttempts,
    RetryStrategy Strategy,
    TimeSpan InitialDelay,
    TimeSpan MaxDelay,
    double BackoffMultiplier,
    bool RetryOnCancellation)
{
    public static RetryPolicyDescriptor None { get; } = new(
        MaxAttempts: 1,
        Strategy: RetryStrategy.None,
        InitialDelay: TimeSpan.Zero,
        MaxDelay: TimeSpan.Zero,
        BackoffMultiplier: 1,
        RetryOnCancellation: false);

    /// <summary>Exponential-backoff retry policy (JOBS-0003 per-type policy ergonomics).</summary>
    public static RetryPolicyDescriptor Exponential(
        int maxAttempts = 3,
        int initialDelaySeconds = 5,
        int maxDelaySeconds = 300,
        double backoffMultiplier = 2.0,
        bool retryOnCancellation = false)
        => new(
            Math.Max(1, maxAttempts),
            RetryStrategy.ExponentialBackoff,
            TimeSpan.FromSeconds(Math.Max(0, initialDelaySeconds)),
            TimeSpan.FromSeconds(Math.Max(initialDelaySeconds, maxDelaySeconds)),
            Math.Max(1.1, backoffMultiplier),
            retryOnCancellation);

    /// <summary>Fixed-delay retry policy.</summary>
    public static RetryPolicyDescriptor FixedDelay(
        int maxAttempts = 3,
        int delaySeconds = 5,
        bool retryOnCancellation = false)
        => new(
            Math.Max(1, maxAttempts),
            RetryStrategy.Fixed,
            TimeSpan.FromSeconds(Math.Max(0, delaySeconds)),
            TimeSpan.FromSeconds(Math.Max(0, delaySeconds)),
            1,
            retryOnCancellation);

    public TimeSpan ComputeDelay(int attempt)
    {
        if (Strategy == RetryStrategy.None || attempt <= 1)
            return InitialDelay;

        return Strategy switch
        {
            RetryStrategy.Fixed => InitialDelay,
            RetryStrategy.ExponentialBackoff =>
                TimeSpan.FromSeconds(Math.Min(MaxDelay.TotalSeconds, InitialDelay.TotalSeconds * Math.Pow(BackoffMultiplier, attempt - 1))),
            _ => InitialDelay
        };
    }
}
