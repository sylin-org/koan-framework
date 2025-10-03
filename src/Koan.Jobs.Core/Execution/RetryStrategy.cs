namespace Koan.Jobs.Execution;

public enum RetryStrategy
{
    None = 0,
    Fixed = 1,
    ExponentialBackoff = 2
}
