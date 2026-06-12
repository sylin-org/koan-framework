namespace Koan.Media.Abstractions.Recipes;

/// <summary>
/// Resolves the bytes of a media overlay source for the pipeline's
/// compositor. The default implementation (Koan.Media.Web) routes
/// through <c>IMediaSource</c> so overlay ids share the same id space
/// as the host media; applications can provide a different
/// implementation (e.g. an in-process logo store) by registering their
/// own <see cref="IOverlayResolver"/>.
///
/// <para>Per MEDIA-0004 §7, recipe-on-overlay nesting is depth-capped
/// at 2 — the compositor passes the nesting depth via the resolver's
/// <c>depth</c> argument, and resolvers should reject calls beyond
/// the cap (or simply not recurse).</para>
/// </summary>
public interface IOverlayResolver
{
    /// <summary>
    /// Open the overlay source's bytes. Returns null when the id is
    /// unknown so the compositor can decide whether to fail the
    /// composition or skip the layer. Caller owns the returned stream.
    /// </summary>
    Task<Stream?> OpenAsync(MediaOverlaySource source, int depth, CancellationToken ct);
}
