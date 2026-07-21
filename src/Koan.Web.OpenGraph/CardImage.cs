namespace Koan.Web.OpenGraph;

/// <summary>
/// A small value that composes an <c>og:image</c> URL without doing any IO. It binds a card to the
/// Koan.Media recipe pipeline (<c>GET /media/{id}/{seed}</c>) rather than re-owning image rendering.
/// This type references a URL; it never reads, renders, or validates an image.
/// </summary>
public readonly struct CardImage
{
    private readonly string? _value;
    private readonly Kind _kind;

    private CardImage(Kind kind, string? value)
    {
        _kind = kind;
        _value = value;
    }

    private enum Kind
    {
        Default = 0,
        Recipe,
        Raw,
        Url,
    }

    /// <summary>Points at a named Koan.Media recipe: <c>/media/{mediaId}/{recipe}</c>.
    /// A null or empty <paramref name="mediaId"/> collapses to <see cref="Default"/>.</summary>
    public static CardImage Recipe(string recipe, string? mediaId)
    {
        if (string.IsNullOrWhiteSpace(recipe) || string.IsNullOrWhiteSpace(mediaId))
        {
            return Default;
        }

        return new CardImage(Kind.Recipe, $"/media/{mediaId}/{recipe}");
    }

    /// <summary>Points at the original media bytes: <c>/media/{mediaId}</c>.
    /// A null or empty <paramref name="mediaId"/> collapses to <see cref="Default"/>.</summary>
    public static CardImage Raw(string? mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
        {
            return Default;
        }

        return new CardImage(Kind.Raw, $"/media/{mediaId}");
    }

    /// <summary>An explicit URL (already a path or absolute URL).
    /// A null or empty value collapses to <see cref="Default"/>.</summary>
    public static CardImage Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Default;
        }

        return new CardImage(Kind.Url, url);
    }

    /// <summary>Falls back to <c>OpenGraphOptions.DefaultImage</c> at emit time.</summary>
    public static CardImage Default => new(Kind.Default, null);

    /// <summary>
    /// Resolves the relative (or explicit) path, or null when this image wants the configured
    /// default. The middleware promotes a non-null result to an absolute URL at emit time; a null
    /// result is filled from <c>OpenGraphOptions.DefaultImage</c>. Options are deliberately not
    /// needed here so the warm-on-write path stays options-free.
    /// </summary>
    internal string? ToPath() => _kind == Kind.Default ? null : _value;
}
