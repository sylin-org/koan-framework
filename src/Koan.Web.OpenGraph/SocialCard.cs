namespace Koan.Web.OpenGraph;

/// <summary>
/// The projected, pre-encode, pre-absolutize result of applying a card's selectors to an entity the
/// request's reader may read. Rendering encodes, truncates and absolutizes it.
/// </summary>
internal sealed record SocialCard(
    string? Title,
    string? Description,
    CardImage Image,
    string? UrlPath,
    string? OgType);
