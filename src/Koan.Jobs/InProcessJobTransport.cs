namespace Koan.Jobs;

/// <summary>
/// Default push-dispatch within a single process: a coalescing signal the worker awaits. <see cref="Notify"/>
/// releases the signal (capped at one pending — many notifies collapse to one wake); <see cref="WaitForWork"/>
/// returns as soon as a signal is pending or the timeout elapses. A missed signal is harmless — the ledger is
/// the truth and the worker still polls at the configured interval.
/// </summary>
public sealed class InProcessJobTransport : IJobTransport
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* a wake is already pending — coalesce */ }
    }

    public async Task WaitForWork(TimeSpan timeout, CancellationToken ct)
    {
        try { await _signal.WaitAsync(timeout, ct); }
        catch (OperationCanceledException) { /* shutdown */ }
    }
}
