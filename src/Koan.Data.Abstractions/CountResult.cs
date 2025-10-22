namespace Koan.Data.Abstractions;

public readonly record struct CountResult(long Value, bool IsEstimate)
{
    public static CountResult Exact(long value) => new(value, false);
    public static CountResult Estimate(long value) => new(value, true);
}
