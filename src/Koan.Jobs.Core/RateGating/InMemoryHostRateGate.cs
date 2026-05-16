using System.Collections.Concurrent;

namespace Koan.Jobs.RateGating;

/// <summary>
/// Default <see cref="IHostRateGate"/> — process-local in-memory store keyed by host tag. Fast,
/// no external dependency, loses state on process restart. Suitable for single-process deployments
/// and for cross-process workloads where transient gate loss after restart is acceptable (gates
/// re-establish themselves naturally on the next rate-limit response).
/// </summary>
public sealed class InMemoryHostRateGate : IHostRateGate
{
    private readonly ConcurrentDictionary<string, RateGateEntry> _gates = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetGate(string hostTag, out RateGateEntry gate)
    {
        gate = default!;
        if (string.IsNullOrWhiteSpace(hostTag)) return false;

        if (_gates.TryGetValue(hostTag, out var entry))
        {
            if (entry.ReleaseAt > DateTimeOffset.UtcNow)
            {
                gate = entry;
                return true;
            }
            // Lazily evict expired entries — keeps the dictionary from accumulating dead rows in
            // long-running processes.
            _gates.TryRemove(new KeyValuePair<string, RateGateEntry>(hostTag, entry));
        }
        return false;
    }

    public Task GateHost(string hostTag, TimeSpan duration, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hostTag)) return Task.CompletedTask;
        if (duration <= TimeSpan.Zero) return Task.CompletedTask;

        var now = DateTimeOffset.UtcNow;
        var releaseAt = now.Add(duration);

        _gates.AddOrUpdate(
            hostTag,
            // No existing gate — set a fresh one.
            _ => new RateGateEntry(hostTag, now, releaseAt, reason ?? string.Empty),
            // Existing gate — "latest release wins" so an overlapping rate-limit response can only
            // EXTEND the gate, never shorten it.
            (_, existing) => existing.ReleaseAt >= releaseAt
                ? existing
                : new RateGateEntry(hostTag, existing.SetAt, releaseAt, reason ?? existing.Reason));

        return Task.CompletedTask;
    }

    public Task ClearGate(string hostTag, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(hostTag))
        {
            _gates.TryRemove(hostTag, out _);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RateGateEntry>> GetActiveGates(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var live = new List<RateGateEntry>(_gates.Count);
        foreach (var kvp in _gates)
        {
            if (kvp.Value.ReleaseAt > now)
            {
                live.Add(kvp.Value);
            }
            else
            {
                _gates.TryRemove(kvp);
            }
        }
        return Task.FromResult((IReadOnlyList<RateGateEntry>)live);
    }
}
