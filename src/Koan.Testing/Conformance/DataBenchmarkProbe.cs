namespace Koan.Testing;

/// <summary>Fixture-owned counters for exact provider dispatch and provider work.</summary>
public sealed class DataBenchmarkProbe
{
    private int _dispatches;
    private long _providerWork;

    public int ProviderDispatches => Volatile.Read(ref _dispatches);
    public long ProviderWork => Interlocked.Read(ref _providerWork);

    public void Dispatch(long providerWork = 0)
    {
        if (providerWork < 0) throw new ArgumentOutOfRangeException(nameof(providerWork));
        Interlocked.Increment(ref _dispatches);
        Interlocked.Add(ref _providerWork, providerWork);
    }

    public void AddProviderWork(long amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Interlocked.Add(ref _providerWork, amount);
    }
}
