using System.Collections.Concurrent;

namespace Koan.Core.Infrastructure;

/// <summary>
/// In-flight <b>coalescer</b>: deduplicates concurrent asynchronous operations keyed by string so
/// that exactly one execution runs per key and every concurrent caller of that key awaits and shares
/// the <i>same</i> result. There is no lease timeout — callers wait as long as the shared execution
/// takes.
/// </summary>
/// <remarks>
/// This is a distinct primitive from the per-key serialize-and-lease gate
/// <see cref="Koan.Core.Concurrency.IKeyedLeaseGate"/>. The coalescer collapses identical work into
/// one shared execution (use it when the result is fungible across callers, e.g. one-time schema
/// ensure, cache fill of an identical value). The gate instead admits callers one-at-a-time per key,
/// runs each caller's <i>own</i> action, and fails with a <see cref="TimeoutException"/> when the
/// lease cannot be acquired within a bounded window (use it when each caller must do its own work but
/// only one may proceed per key at a time). They are not interchangeable — keep both.
/// </remarks>
public static class Singleflight
{
    private static readonly ConcurrentDictionary<string, Lazy<Task>> _inflight = new(StringComparer.Ordinal);

    public static async Task Run(string key, Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (work == null) throw new ArgumentNullException(nameof(work));

        Lazy<Task> MakeLazy() => new(() => work(ct), LazyThreadSafetyMode.ExecutionAndPublication);

        var lazy = _inflight.GetOrAdd(key, _ => MakeLazy());
        var task = lazy.Value;
        try
        {
            await task;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    public static Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (work == null) throw new ArgumentNullException(nameof(work));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Run(key, async kct =>
        {
            try
            {
                var result = await work(kct);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, ct);
        return tcs.Task;
    }

    public static void Invalidate(string key)
    {
        _inflight.TryRemove(key, out _);
    }

    // Intentionally no ExecuteAndCleanup helper; cleanup is handled in RunAsync finally block.
}
