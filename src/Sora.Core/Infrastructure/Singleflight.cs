using System.Collections.Concurrent;

namespace Sora.Core.Infrastructure;

/// <summary>
/// Singleflight deduplication for in-flight asynchronous operations keyed by string.
/// Useful to ensure only one expensive operation runs per key at a time, with other callers awaiting the same task.
/// </summary>
public static class Singleflight
{
    private static readonly ConcurrentDictionary<string, Lazy<Task>> _inflight = new(StringComparer.Ordinal);

    public static async Task RunAsync(string key, Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        if (work == null) throw new ArgumentNullException(nameof(work));

        Lazy<Task> MakeLazy() => new(() => work(ct), LazyThreadSafetyMode.ExecutionAndPublication);

        var lazy = _inflight.GetOrAdd(key, _ => MakeLazy());
        var task = lazy.Value;
        try
        {
            await task.ConfigureAwait(false);
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
        _ = RunAsync(key, async kct =>
        {
            try
            {
                var result = await work(kct).ConfigureAwait(false);
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
