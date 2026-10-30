using Koan.Communication.Signals;
using Koan.Jobs.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs;

/// <summary>
/// Owns the Jobs meaning of "work may be ready". Communication carries the hint; the ledger and poll fallback
/// remain the complete correctness mechanism.
/// </summary>
internal sealed class JobWakeCoordinator(
    IFrameworkSignalPublisher signals,
    ILogger<JobWakeCoordinator> logger) :
    IHandleFrameworkSignal<JobReadySignal>,
    IDisposable
{
    private readonly SemaphoreSlim _pending = new(0, 1);

    public void Notify()
    {
        if (!signals.TryPublish(new JobReadySignal()))
        {
            logger.LogDebug(
                "Koan.Jobs wake hint was not accepted by the bounded Communication signal lane; " +
                "the worker will discover work at its next poll.");
        }
    }

    /// <summary>Wait until work may be ready or <paramref name="timeout"/> elapses. True when a hint arrived.</summary>
    public async Task<bool> WaitForWork(TimeSpan timeout, CancellationToken ct)
        => await _pending.WaitAsync(timeout, ct).ConfigureAwait(false);

    /// <summary>Hurry this node's dispatcher without crossing Communication: an execution settled, so capacity is free
    /// and a successor may be ready.</summary>
    public void Nudge()
    {
        try { _pending.Release(); }
        catch (SemaphoreFullException) { /* a wake is already pending — level-triggered coalescing */ }
        catch (ObjectDisposedException) { /* the host is stopping */ }
    }

    public ValueTask Handle(JobReadySignal signal, CancellationToken ct)
    {
        Nudge();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _pending.Dispose();
}

internal readonly record struct JobReadySignal : IFrameworkSignal<JobReadySignal>
{
    public static string ContractId => Constants.Wake.ContractId;
    public static string GroupId => Constants.Wake.GroupId;
}
