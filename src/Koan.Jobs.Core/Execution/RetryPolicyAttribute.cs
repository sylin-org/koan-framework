using System;

namespace Koan.Jobs.Execution;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class RetryPolicyAttribute : Attribute
{
    public int MaxAttempts { get; init; } = 1;
    public RetryStrategy Strategy { get; init; } = RetryStrategy.None;
    public double BackoffMultiplier { get; init; } = 2.0;
    public int InitialDelaySeconds { get; init; } = 30;
    public int MaxDelaySeconds { get; init; } = 300;
    public bool RetryOnCancellation { get; init; }
}
