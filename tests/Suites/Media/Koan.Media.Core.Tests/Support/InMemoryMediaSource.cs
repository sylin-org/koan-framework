using System.Collections.Concurrent;
using Koan.Media.Web.Routing;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Test <see cref="IMediaSource"/> backed by an in-memory dictionary.
/// Lets the controller integration tests inject fixtures without
/// needing the real Koan.Storage backend.
/// </summary>
public sealed class InMemoryMediaSource : IMediaSource
{
    private readonly ConcurrentDictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a fixture under <paramref name="id"/>. Hash is computed once and cached.</summary>
    public async Task<string> AddAsync(string id, Stream content, CancellationToken ct = default)
    {
        var bytes = await Fixtures.Snapshot(content, ct).ConfigureAwait(false);
        _bytes[id] = bytes;
        await using var ms = new MemoryStream(bytes);
        var hash = await Fixtures.Sha256Hex(ms, ct).ConfigureAwait(false);
        _hashes[id] = hash;
        return hash;
    }

    public Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default)
    {
        if (!_bytes.TryGetValue(id, out var bytes))
            return Task.FromResult<MediaSourceHandle?>(null);
        var hash = _hashes.TryGetValue(id, out var h) ? h : "";
        return Task.FromResult<MediaSourceHandle?>(new MediaSourceHandle(
            Id: id,
            Bytes: new MemoryStream(bytes, writable: false),
            ContentHashHex: hash,
            LastModified: null));
    }

    public bool Contains(string id) => _bytes.ContainsKey(id);

    public int Count => _bytes.Count;

    public void Clear()
    {
        _bytes.Clear();
        _hashes.Clear();
    }
}
