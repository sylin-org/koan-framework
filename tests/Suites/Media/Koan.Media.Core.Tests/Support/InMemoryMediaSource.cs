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
    private readonly ConcurrentDictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a fixture under <paramref name="id"/>. Hash is computed once
    /// and cached. <paramref name="contentType"/> is exposed verbatim via
    /// <see cref="MediaSourceHandle.ContentType"/> so the controller's
    /// no-transform fast-path can serve non-image sources (video, etc.)
    /// with the right MIME.
    /// </summary>
    public async Task<string> AddAsync(
        string id,
        Stream content,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var bytes = await Fixtures.Snapshot(content, ct).ConfigureAwait(false);
        _bytes[id] = bytes;
        await using var ms = new MemoryStream(bytes);
        var hash = await Fixtures.Sha256Hex(ms, ct).ConfigureAwait(false);
        _hashes[id] = hash;
        // Real storage layers record ContentType at upload time (sniffed
        // from the multipart body, set by the producer, or detected
        // server-side). The test fixture mirrors that by sniffing magic
        // bytes for the common formats when the caller didn't pass an
        // explicit type. Tests that need a non-detected type pass it
        // explicitly via the contentType parameter.
        _contentTypes[id] = contentType ?? SniffContentType(bytes);
        return hash;
    }

    /// <summary>
    /// Magic-byte sniffer covering the image formats the existing
    /// MediaControllerSpec fixtures produce (JPEG, PNG, GIF, WebP) plus
    /// the synthetic MP4 used by the no-recipe fast-path regression.
    /// Returns <c>application/octet-stream</c> when no signature matches.
    /// </summary>
    private static string SniffContentType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return "image/gif";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";
        if (bytes.Length >= 12 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
            return "video/mp4";
        return "application/octet-stream";
    }

    public Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default)
    {
        if (!_bytes.TryGetValue(id, out var bytes))
            return Task.FromResult<MediaSourceHandle?>(null);
        var hash = _hashes.TryGetValue(id, out var h) ? h : "";
        var contentType = _contentTypes.TryGetValue(id, out var c) ? c : "application/octet-stream";
        return Task.FromResult<MediaSourceHandle?>(new MediaSourceHandle(
            Id: id,
            Bytes: new MemoryStream(bytes, writable: false),
            ContentHashHex: hash,
            LastModified: null,
            ContentType: contentType));
    }

    public bool Contains(string id) => _bytes.ContainsKey(id);

    public int Count => _bytes.Count;

    public void Clear()
    {
        _bytes.Clear();
        _hashes.Clear();
        _contentTypes.Clear();
    }
}
