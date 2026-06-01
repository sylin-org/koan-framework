using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Web.Routing;

/// <summary>
/// Resolves a media id to the source bytes. The MediaController
/// depends on this abstraction (not on <see cref="Koan.Media.Abstractions.Model.MediaEntity{T}"/>
/// or <see cref="Koan.Storage.Abstractions.IStorageService"/> directly)
/// so the controller is testable against in-memory fixtures and any
/// app can plug in alternative storage backends.
///
/// <para>Per MEDIA-0007 the source also acts as the durable home for derived
/// renders. Implementations may opt in by overriding
/// <see cref="OpenDerivationAsync"/> and <see cref="TryStoreDerivationAsync"/>
/// to expose the recipe pipeline's outputs as ordinary storage entities under
/// the same profile as the source.</para>
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
    /// behavior where derivations were served by <c>IMediaOutputCache</c>
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

    /// <summary>
    /// Sweep derivations whose source no longer exists. Invoked by
    /// <see cref="Sweep.MediaDerivationSweepService"/> on its configured
    /// cadence. Implementations enumerate derivation rows
    /// (<c>SourceMediaId != null</c>), probe each source via <c>Head()</c>,
    /// and delete derivations whose source is gone.
    ///
    /// <para>Default implementation returns
    /// <see cref="MediaDerivationSweepResult.Empty"/>, matching sources that
    /// do not persist derivations.</para>
    /// </summary>
    /// <returns>Counts of derivations examined and deleted by the sweep.</returns>
    Task<MediaDerivationSweepResult> SweepOrphanedDerivationsAsync(CancellationToken ct = default)
        => Task.FromResult(MediaDerivationSweepResult.Empty);
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

/// <summary>
/// Counts produced by
/// <see cref="IMediaSource.SweepOrphanedDerivationsAsync"/>. Surfaced through
/// the sweep service's structured logs so operators can spot anomalies.
/// </summary>
/// <param name="Examined">Total number of derivation rows visited.</param>
/// <param name="Deleted">Subset whose source was missing and were removed.</param>
public sealed record MediaDerivationSweepResult(int Examined, int Deleted)
{
    public static MediaDerivationSweepResult Empty { get; } = new(0, 0);
}
