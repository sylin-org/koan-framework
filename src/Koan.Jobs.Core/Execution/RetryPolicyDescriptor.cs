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

    public static RetryPolicyDescriptor FromAttribute(RetryPolicyAttribute attribute)
        => new(
            Math.Max(1, attribute.MaxAttempts),
            attribute.Strategy,
            attribute.Strategy == RetryStrategy.None ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, attribute.InitialDelaySeconds)),
            attribute.Strategy == RetryStrategy.None ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(attribute.InitialDelaySeconds, attribute.MaxDelaySeconds)),
            attribute.Strategy == RetryStrategy.ExponentialBackoff ? Math.Max(1.1, attribute.BackoffMultiplier) : 1,
            attribute.RetryOnCancellation);

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
