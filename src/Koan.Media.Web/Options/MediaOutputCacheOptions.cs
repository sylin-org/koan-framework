namespace Koan.Media.Web.Options;

/// <summary>
/// Persistent render-output cache for the recipe pipeline. Bound from
/// <c>Koan:Media:Web:OutputCache</c>.
///
/// <para>When enabled, <see cref="Controllers.MediaController"/> stores each
/// rendered variant under <see cref="Path"/> keyed by
/// <c>(media id, recipe fingerprint)</c> and serves it on subsequent requests,
/// skipping the resize/re-encode pipeline. The key already encodes every
/// pipeline-affecting input (the fingerprint covers steps, format, quality and
/// the recipe <c>Version</c>), so editing a recipe yields a new key and the old
/// entry is simply orphaned — no explicit invalidation is needed.</para>
///
/// <para>Disabled by default: the controller behaves exactly as before unless an
/// app opts in via configuration.</para>
///
/// <para><strong>Obsolete — see MEDIA-0007.</strong> Persistent derivations now
/// live alongside the source in storage; configure your <c>IMediaSource</c>
/// implementation instead. Removed in MEDIA-0008.</para>
/// </summary>
[Obsolete("Use IMediaSource.TryStoreDerivationAsync; see MEDIA-0007. Removed in MEDIA-0008.", error: false)]
public sealed class MediaOutputCacheOptions
{
    /// <summary>Enable the persistent render cache. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Directory that holds cached render output. Relative paths resolve under
    /// the app's <c>ContentRootPath</c>. The cache no-ops when this is empty,
    /// even if <see cref="Enabled"/> is true.
    /// </summary>
    public string? Path { get; set; }
}
