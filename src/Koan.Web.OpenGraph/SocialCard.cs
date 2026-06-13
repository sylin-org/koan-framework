namespace Koan.Web.OpenGraph;

/// <summary>
/// The projected, pre-encode, pre-absolutize result of applying a card's selectors to an entity.
/// This is the in-memory shape the registry produces; it is materialized into a
/// <see cref="SocialCardSnapshot"/> for caching and read back at emit time.
/// </summary>
internal sealed record SocialCard(
    string? Title,
    string? Description,
    CardImage Image,
    string? UrlPath,
    string? OgType);
