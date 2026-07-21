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

    public async Task WaitForWork(TimeSpan timeout, CancellationToken ct)
        => await _pending.WaitAsync(timeout, ct).ConfigureAwait(false);

    public ValueTask Handle(JobReadySignal signal, CancellationToken ct)
    {
        try { _pending.Release(); }
        catch (SemaphoreFullException) { /* a wake is already pending — level-triggered coalescing */ }
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _pending.Dispose();
}

internal readonly record struct JobReadySignal : IFrameworkSignal<JobReadySignal>
{
    public static string ContractId => Constants.Wake.ContractId;
    public static string GroupId => Constants.Wake.GroupId;
}
