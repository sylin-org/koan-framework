using System.Diagnostics;

namespace Koan.Testing;

/// <summary>Captures every primer-required metric without imposing a global performance threshold.</summary>
public static class DataBenchmarkRunner
{
    public static async ValueTask<DataBenchmarkObservation> Observe(
        DataBenchmarkFixture fixture,
        string cell,
        DataBenchmarkPhase phase,
        Func<DataBenchmarkProbe, CancellationToken, ValueTask> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateFixture(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(cell);
        ArgumentNullException.ThrowIfNull(operation);
        ct.ThrowIfCancellationRequested();
        var probe = new DataBenchmarkProbe();
        var allocated = GC.GetTotalAllocatedBytes(precise: false);
        var start = Stopwatch.GetTimestamp();
        await operation(probe, ct).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(start);
        var bytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocated);
        return new DataBenchmarkObservation(
            fixture,
            cell.Trim(),
            phase,
            elapsed,
            bytes,
            probe.ProviderDispatches,
            probe.ProviderWork);
    }

    private static void ValidateFixture(DataBenchmarkFixture fixture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture.ProviderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture.DriverVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixture.Runner);
    }
}
