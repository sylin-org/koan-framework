namespace Koan.Web.OpenGraph;

/// <summary>
/// What one card says, before it is encoded, truncated and absolutized. A declared card projects one from an Entity its
/// reader may read; a server-rendered page hands one to <see cref="IOpenGraphCardRenderer.RenderHead"/> from what it has
/// already read. A value left null takes the configured default: the title takes the site name, the description
/// <see cref="OpenGraphOptions.DefaultDescription"/>, the image <see cref="OpenGraphOptions.DefaultImage"/>, the URL the
/// request's path, the type <see cref="OpenGraphOptions.DefaultType"/> and the site name <see cref="OpenGraphOptions.SiteName"/>.
/// <see cref="Url"/> is root-relative or absolute.
/// </summary>
public sealed record SocialCard(
    string? Title = null,
    string? Description = null,
    CardImage Image = default,
    string? Url = null,
    string? Type = null,
    string? SiteName = null);
