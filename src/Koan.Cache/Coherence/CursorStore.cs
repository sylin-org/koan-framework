using System.Collections.Concurrent;

namespace Koan.Cache.Coherence;

/// <summary>
/// In-memory cursor store for catch-up-capable channels. v1 keeps cursors per
/// transport in-process; on process restart, L1 is also cold so missed messages
/// during the restart window don't matter. M9+ may add a persistent backend.
/// </summary>
internal sealed class CursorStore
{
    private readonly ConcurrentDictionary<string, string?> _cursors = new(System.StringComparer.OrdinalIgnoreCase);

    public string? Load(string transportName) => _cursors.TryGetValue(transportName, out var cursor) ? cursor : null;

    public void Save(string transportName, string? cursor) => _cursors[transportName] = cursor;
}
