using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Document;

/// <summary>
/// Run an idempotent setup action <b>once per key</b> and remember it succeeded — the shared schema-ready gate for the
/// <see cref="DocumentStore{TEntity,TKey}"/> family (ARCH-0103 document-store catalogue §3.B). Collapses the three
/// overlapping static caches + key-builders the Mongo adapter accreted into one mechanism: a per-key lock serializes
/// concurrent first-callers, success is memoized so the action never re-runs, and a failure is <b>not</b> memoized so a
/// transient setup error (a momentary connectivity blip) is retried on the next call rather than poisoning the key.
/// </summary>
public sealed class OnceGate
{
    private readonly ConcurrentDictionary<string, bool> _done = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Run <paramref name="action"/> exactly once for <paramref name="key"/> (the first caller wins; the rest
    /// await it), then short-circuit on every later call. A throwing action is not memoized — the next call retries.</summary>
    public async Task RunOnceAsync(string key, Func<Task> action, CancellationToken ct = default)
    {
        if (_done.TryGetValue(key, out var ok) && ok) return;

        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_done.TryGetValue(key, out ok) && ok) return;
            await action().ConfigureAwait(false);
            _done[key] = true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forget that <paramref name="key"/> succeeded — the next <see cref="RunOnceAsync"/> re-runs the action
    /// (e.g. after a fast-remove drops &amp; recreates the container so its indexes must be re-ensured).</summary>
    public void Invalidate(string key) => _done.TryRemove(key, out _);
}
