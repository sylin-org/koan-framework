using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Web.Routing;

/// <summary>
/// Resolves a media id to the source bytes. The MediaController
/// depends on this abstraction (not on <see cref="Koan.Media.MediaEntity{T}"/>
/// or <see cref="Koan.Storage.Abstractions.IStorageService"/> directly)
/// so the controller is testable against in-memory fixtures and any
/// app can plug in alternative storage backends.
///
/// <para>The source may also persist derived renders. Implementations opt in by overriding
/// <see cref="OpenDerivationAsync"/> and <see cref="TryStoreDerivationAsync"/>
/// to expose recipe-pipeline outputs through their own storage policy.</para>
/// </summary>
public interface IMediaSource
{
    /// <summary>
    /// Open a read-only stream for the source media. Returns null when
    /// the id is unknown. The caller takes ownership of the stream and
    /// disposes it.
    /// </summary>
    Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Open a read-only stream for a previously stored derivation. Returns
    /// null when no derivation has been persisted for the
    /// (<paramref name="sourceId"/>, <paramref name="recipeFingerprint"/>)
    /// tuple, or when this source kind does not persist derivations.
    ///
    /// <para>Default implementation returns null, preserving the legacy
    /// behavior where derivations were served by the legacy output cache
    /// rather than the storage layer.</para>
    /// </summary>
    Task<MediaDerivationHandle?> OpenDerivationAsync(
        string sourceId,
        string recipeFingerprint,
        CancellationToken ct = default)
        => Task.FromResult<MediaDerivationHandle?>(null);

    /// <summary>
    /// Persist <paramref name="output"/> as a derivation of
    /// <paramref name="sourceId"/> keyed by <paramref name="recipeFingerprint"/>.
    /// Best-effort: failures are logged and swallowed by the caller so the
    /// pipeline still serves the rendered bytes.
    ///
    /// <para><paramref name="recipeName"/> carries the configured recipe
    /// name (e.g. <c>"package-card"</c>) when invoked by name, or
    /// <c>null</c> for ad-hoc renders. <paramref name="recipeVersion"/>
    /// carries the recipe's <c>Version</c> field so a fingerprint-stable
    /// but semantics-changed recipe can be swept selectively.</para>
    ///
    /// <para>Default implementation is a no-op for sources that do not
    /// persist derivations.</para>
    /// </summary>
    Task TryStoreDerivationAsync(
        string sourceId,
        string recipeFingerprint,
        MediaOutput output,
        string? recipeName,
        string? recipeVersion,
        CancellationToken ct = default)
        => Task.CompletedTask;

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
    DateTimeOffset? LastModified,
    string ContentType = "application/octet-stream") : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}

/// <summary>
/// Derivation handle returned by <see cref="IMediaSource.OpenDerivationAsync"/>.
/// Wraps the stored bytes for a previously persisted recipe render.
/// </summary>
/// <param name="Bytes">Readable stream over the stored derivation bytes.</param>
/// <param name="ContentType">MIME type of the stored derivation.</param>
public sealed record MediaDerivationHandle(Stream Bytes, string ContentType) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Bytes.DisposeAsync();
}
