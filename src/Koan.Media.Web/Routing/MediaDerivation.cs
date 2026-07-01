using Koan.Storage.Infrastructure;
using Koan.Storage.Model;

namespace Koan.Media.Web.Routing;

/// <summary>
/// The framework-owned durable cache row for a recipe render (MEDIA-0007), used by
/// <see cref="MediaEntitySource{TEntity}"/>'s derivation-cache override. One row per
/// <c>(SourceMediaId, DerivationKey)</c> — <see cref="DerivationKey"/> is the recipe fingerprint the
/// controller computes — with the rendered bytes held as this <see cref="StorageEntity{TEntity}"/>'s
/// blob. Kept a distinct entity type (not a derived row of the app's <c>MediaEntity&lt;T&gt;</c>) so
/// cached renders never pollute the app's own queries, and tenant-scoped like any entity so a studio's
/// renders stay isolated. It is deliberately NOT <c>[AccessScoped]</c>: the source access gate already
/// fired in <see cref="MediaEntitySource{TEntity}.OpenAsync"/> (which the controller calls before any
/// derivation lookup), so the cache carries no access axis of its own.
///
/// <para>The bytes are keyed by (source, recipe fingerprint), not by source <i>content</i> — matching the
/// framework's MEDIA-0007 contract — so it assumes originals are immutable per id (true for
/// content-addressed media). Eviction of a source's renders on delete is the owning app's responsibility
/// (the source access gate makes an un-evicted render unreachable, so a stale row is a storage-cleanliness
/// concern, never a serving leak).</para>
/// </summary>
[StorageBinding(Container = "media-derivations")]
public sealed class MediaDerivation : StorageEntity<MediaDerivation>
{
    /// <summary>
    /// The source media id this render derives from (the <c>MediaEntity&lt;T&gt;</c> id). Queried by the
    /// owning app's eviction-on-delete path; an index can be declared there when that path lands.
    /// </summary>
    public string? SourceMediaId { get; set; }

    /// <summary>The recipe fingerprint (the recipe-side half of the cache key).</summary>
    public string? DerivationKey { get; set; }

    /// <summary>The named recipe that produced this render (null for ad-hoc URL renders).</summary>
    public string? RecipeName { get; set; }

    /// <summary>The recipe's <c>Version</c> at render time — lets a semantics-changed recipe be swept selectively.</summary>
    public string? RecipeVersion { get; set; }

    /// <summary>Deterministic row id for O(1) cache lookup by (source, recipe fingerprint).</summary>
    public static string KeyFor(string sourceId, string fingerprint) => $"{sourceId}:{fingerprint}";
}
