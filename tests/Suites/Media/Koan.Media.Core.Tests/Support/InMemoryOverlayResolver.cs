using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Test <see cref="IOverlayResolver"/> that opens overlay sources from
/// an in-memory dictionary. Production resolver lives in Koan.Media.Web
/// and routes through IMediaSource — this one keeps the engine tests
/// independent of the Web layer.
/// </summary>
public sealed class InMemoryOverlayResolver : IOverlayResolver
{
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);
    public int CallCount { get; private set; }
    public int? LastDepthSeen { get; private set; }

    public InMemoryOverlayResolver Register(string id, byte[] bytes)
    {
        _bytes[id] = bytes;
        return this;
    }

    public Task<Stream?> OpenAsync(MediaOverlaySource source, int depth, CancellationToken ct)
    {
        CallCount++;
        LastDepthSeen = depth;
        if (depth > OverlayCompositor.MaxRecipeDepth) return Task.FromResult<Stream?>(null);
        return _bytes.TryGetValue(source.MediaId, out var b)
            ? Task.FromResult<Stream?>(new MemoryStream(b, writable: false))
            : Task.FromResult<Stream?>(null);
    }
}
