namespace Koan.Identity.Roles;

/// <summary>Host-owned serialization for mutations of one scoped participant collection.</summary>
internal sealed class ScopedRoleMembershipCoordinator
{
    private readonly Dictionary<string, Gate> _gates = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public async ValueTask<T> Run<T>(string key, Func<CancellationToken, ValueTask<T>> action,
        CancellationToken ct)
    {
        Gate gate;
        lock (_sync)
        {
            if (!_gates.TryGetValue(key, out gate!)) _gates.Add(key, gate = new Gate());
            gate.RefCount++;
        }

        try
        {
            await gate.Semaphore.WaitAsync(ct).ConfigureAwait(false);
            try { return await action(ct).ConfigureAwait(false); }
            finally { gate.Semaphore.Release(); }
        }
        finally
        {
            lock (_sync)
            {
                if (--gate.RefCount == 0) _gates.Remove(key);
            }
        }
    }

    private sealed class Gate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount { get; set; }
    }
}
