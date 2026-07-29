namespace Koan.Data.Abstractions;

public readonly record struct CountResult(long Value, bool IsEstimate)
{
    /// <summary>The count work the provider actually performed.</summary>
    public CountExecutionKind Execution { get; init; } = CountExecutionKind.Exact;

    public static CountResult Exact(long value) => new(value, false) { Execution = CountExecutionKind.Exact };
    public static CountResult Estimate(long value) => new(value, true) { Execution = CountExecutionKind.Fast };
    public static CountResult Fast(long value, bool isEstimate = true)
        => new(value, isEstimate) { Execution = CountExecutionKind.Fast };
    public static CountResult Optimized(long value)
        => new(value, false) { Execution = CountExecutionKind.Optimized };
}
