namespace Koan.Media.Web.Routing;

/// <summary>
/// Resolves a media id to the source bytes. The MediaController
/// depends on this abstraction (not on <see cref="Koan.Media.Abstractions.Model.MediaEntity{T}"/>
/// or <see cref="Koan.Storage.Abstractions.IStorageService"/> directly)
/// so the controller is testable against in-memory fixtures and any
/// app can plug in alternative storage backends.
/// </summary>
public interface IMediaSource
{
    /// <summary>
    /// Open a read-only stream for the source media. Returns null when
    /// the id is unknown. The caller takes ownership of the stream and
    /// disposes it.
    /// </summary>
    Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// Source handle returned by <see cref="IMediaSource.OpenAsync"/>.
/// Carries enough metadata to populate the response's diagnostic
/// headers and cache key without re-decoding.
/// </summary>
public sealed record MediaSourceHandle(
    string Id,
    Stream Bytes,
    string ContentHashHex,
    DateTimeOffset? LastModified) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}
