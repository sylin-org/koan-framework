using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;

namespace Koan.Web.OpenGraph;

/// <summary>
/// The materialized read model for one card. Modelled as an entity so it reuses the entity-cache
/// pillar: <c>[Cacheable]</c> gives it L1/L2 caching and cross-node coherence when a cache adapter is
/// referenced, and degrades to a plain persisted read when one is not (Reference = Intent). The
/// pillar writes no bespoke cache machinery; the only out-of-band wiring is the
/// <c>Entity&lt;T&gt;.Events</c> warm/evict hook that keeps this in sync with its source entity.
/// </summary>
/// <remarks>
/// The <see cref="Koan.Data.Abstractions.IEntity{TKey}.Id"/> is a composite key,
/// <c>"{typeDiscriminator}:{sourceId}"</c>,
/// e.g. <c>"Work:019ebf08636e"</c>. Values are stored raw-but-hard-capped; HTML-encoding and
/// absolute-URL promotion happen at emit time (the latter depends on the request scheme and host).
/// Display-length truncation is also an emit-time concern, so the configured maxima can change
/// without re-projecting.
/// </remarks>
[Cacheable(600)]
public sealed class SocialCardSnapshot : Entity<SocialCardSnapshot>
{
    // Generous hard caps keep the snapshot tiny without coupling it to the configurable display
    // maxima (which are applied at emit time, where the request and options are in hand).
    internal const int MaxStoredTitleLength = 256;
    internal const int MaxStoredDescriptionLength = 1024;

    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Relative image path (e.g. <c>/media/{id}/share-card</c>), or null to use the default image.</summary>
    public string? ImagePath { get; set; }

    /// <summary>Relative canonical/og:url path (e.g. <c>/work/{id}</c>), or null to use the request URL.</summary>
    public string? UrlPath { get; set; }

    public string? OgType { get; set; }

    internal static SocialCardSnapshot FromCard(string id, SocialCard card) => new()
    {
        Id = id,
        Title = HardCap(card.Title, MaxStoredTitleLength),
        Description = HardCap(card.Description, MaxStoredDescriptionLength),
        ImagePath = card.Image.ToPath(),
        UrlPath = card.UrlPath,
        OgType = card.OgType,
    };

    private static string? HardCap(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }
}
