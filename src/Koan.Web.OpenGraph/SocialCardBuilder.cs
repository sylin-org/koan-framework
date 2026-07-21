using Koan.Data.Core.Model;

namespace Koan.Web.OpenGraph;

/// <summary>
/// Per-type fluent builder. The selector methods return the builder (chain within a type); it also
/// exposes <see cref="For{U}"/> to begin the next type (chain across types). The builder is itself a
/// registrar, mirroring the shape sketched in the proposal.
/// </summary>
/// <remarks>
/// Selectors are sync and pure: the entity is already loaded, so inspecting the whole entity costs
/// nothing extra. That is what makes per-entity-state customization possible without an attribute
/// scheme (for example, choosing a different image when an entity has two or more images).
/// </remarks>
public sealed class SocialCardBuilder<T> where T : Entity<T>
{
    private Func<T, string?>? _title;
    private Func<T, string?>? _description;
    private Func<T, CardImage>? _image;
    private Func<T, string?>? _url;
    private string? _ogType;

    internal SocialCardBuilder()
    {
    }

    /// <summary>og:title and twitter:title (and the <c>&lt;title&gt;</c> element).</summary>
    public SocialCardBuilder<T> Title(Func<T, string?> selector)
    {
        _title = selector ?? throw new ArgumentNullException(nameof(selector));
        return this;
    }

    /// <summary>og:description and twitter:description.</summary>
    public SocialCardBuilder<T> Description(Func<T, string?> selector)
    {
        _description = selector ?? throw new ArgumentNullException(nameof(selector));
        return this;
    }

    /// <summary>og:image and twitter:image.</summary>
    public SocialCardBuilder<T> Image(Func<T, CardImage> selector)
    {
        _image = selector ?? throw new ArgumentNullException(nameof(selector));
        return this;
    }

    /// <summary>og:type (defaults to <c>OpenGraphOptions.DefaultType</c> when unset).</summary>
    public SocialCardBuilder<T> Type(string ogType)
    {
        _ogType = ogType;
        return this;
    }

    /// <summary>og:url and the canonical link (defaults to the request URL when unset).</summary>
    public SocialCardBuilder<T> Url(Func<T, string?> selector)
    {
        _url = selector ?? throw new ArgumentNullException(nameof(selector));
        return this;
    }

    /// <summary>Begin the card for the next type. Equivalent to <see cref="SocialCards.For{U}"/>.</summary>
    public SocialCardBuilder<U> For<U>(string routeTemplate, Func<string, Task<U?>> resolve) where U : Entity<U>
        => SocialCards.For(routeTemplate, resolve);

    internal SocialCard Project(T entity) => new(
        Title: _title?.Invoke(entity),
        Description: _description?.Invoke(entity),
        Image: _image?.Invoke(entity) ?? CardImage.Default,
        UrlPath: _url?.Invoke(entity),
        OgType: _ogType);
}
