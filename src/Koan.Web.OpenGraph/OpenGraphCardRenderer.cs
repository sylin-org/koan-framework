using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Web.OpenGraph;

internal sealed class OpenGraphCardRenderer : IOpenGraphCardRenderer
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    private readonly IOptionsMonitor<OpenGraphOptions> _options;
    private readonly ShellCache _shellCache;
    private readonly SocialCardRegistry _cards;
    private readonly KoanWebBaseUrl _baseUrl;

    public OpenGraphCardRenderer(
        IOptionsMonitor<OpenGraphOptions> options,
        ShellCache shellCache,
        SocialCardRegistry cards,
        KoanWebBaseUrl baseUrl)
    {
        _options = options;
        _shellCache = shellCache;
        _cards = cards;
        _baseUrl = baseUrl;
    }

    public async Task<string?> RenderShellAsync(HttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return null;
        }

        var shell = await _shellCache.GetShellAsync(options.ShellPath, ct).ConfigureAwait(false);
        if (shell is null)
        {
            return null; // shell unavailable: let the app's own fallback serve it
        }

        var card = await ResolveCardAsync(request, ct).ConfigureAwait(false);
        var head = BuildHead(card, options, request);
        return Inject(shell, head, options.PlaceholderMarker);
    }

    public string RenderHead(SocialCard card, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.CurrentValue;
        return options.Enabled ? BuildHead(card, options, request) : string.Empty;
    }

    // WEB-0083: read for this request's reader, every time. A token nothing resolves and a row the reader may not read
    // both fall through to the default card, so a card says nothing of what exists beyond the reader's sight.
    private async Task<SocialCard?> ResolveCardAsync(HttpRequest request, CancellationToken ct)
    {
        var path = request.Path.Value ?? "/";
        foreach (var registration in _cards.Registrations)
        {
            if (registration.Matcher.TryExtractToken(path, out var token))
            {
                return await registration.CardFor(token, request.HttpContext, ct).ConfigureAwait(false);
            }
        }

        return null; // no matching route: default card
    }

    private string BuildHead(SocialCard? card, OpenGraphOptions options, HttpRequest request)
    {
        var siteName = card?.SiteName ?? options.SiteName;
        var title = Truncate(card?.Title ?? siteName, options.MaxTitleLength);
        var description = Truncate(card?.Description ?? options.DefaultDescription, options.MaxDescriptionLength);
        var imageUrl = Absolutize(card?.Image.ToPath() ?? options.DefaultImage, request);
        var url = Absolutize(card?.Url ?? request.Path.Value, request);
        var ogType = card?.Type ?? options.DefaultType;

        var sb = new StringBuilder();

        if (options.EmitTitleElement && !string.IsNullOrEmpty(title))
        {
            sb.Append("<title>").Append(Encode(title)).Append("</title>\n");
        }

        if (options.EmitCanonical && !string.IsNullOrEmpty(url))
        {
            sb.Append("<link rel=\"canonical\" href=\"").Append(Encode(url)).Append("\" />\n");
        }

        if (!string.IsNullOrEmpty(title)) Property(sb, "og:title", title);
        if (!string.IsNullOrEmpty(description)) Property(sb, "og:description", description);
        if (!string.IsNullOrEmpty(imageUrl)) Property(sb, "og:image", imageUrl);
        if (!string.IsNullOrEmpty(url)) Property(sb, "og:url", url);
        if (!string.IsNullOrEmpty(ogType)) Property(sb, "og:type", ogType);
        if (!string.IsNullOrEmpty(siteName)) Property(sb, "og:site_name", siteName);
        if (!string.IsNullOrEmpty(options.Locale)) Property(sb, "og:locale", options.Locale);

        if (options.EmitTwitterTags)
        {
            if (!string.IsNullOrEmpty(options.TwitterCard)) Name(sb, "twitter:card", options.TwitterCard);
            if (!string.IsNullOrEmpty(title)) Name(sb, "twitter:title", title);
            if (!string.IsNullOrEmpty(description)) Name(sb, "twitter:description", description);
            if (!string.IsNullOrEmpty(imageUrl)) Name(sb, "twitter:image", imageUrl);
        }

        return sb.ToString();
    }

    private static void Property(StringBuilder sb, string property, string content)
        => sb.Append("<meta property=\"").Append(property).Append("\" content=\"").Append(Encode(content)).Append("\" />\n");

    private static void Name(StringBuilder sb, string name, string content)
        => sb.Append("<meta name=\"").Append(name).Append("\" content=\"").Append(Encode(content)).Append("\" />\n");

    private static string Inject(string shell, string head, string marker)
    {
        if (!string.IsNullOrEmpty(marker) && shell.Contains(marker, StringComparison.Ordinal))
        {
            return shell.Replace(marker, head);
        }

        var idx = shell.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return shell.Insert(idx, head);
        }

        // No head and no marker: serve the shell unchanged rather than corrupting it.
        return shell;
    }

    private static string Encode(string value) => Encoder.Encode(value);

    private static string? Truncate(string? value, int max)
    {
        if (max <= 0 || string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return max == 1 ? "…" : value[..(max - 1)].TrimEnd() + "…";
    }

    // WEB-0078: a root-relative path is rooted at the application's base URL (Koan:Web:ApplicationUrl when set,
    // path included; else the URL the application accepted, WEB-0081; else the request's scheme, host and path base),
    // read at render, never at the bare request host.
    private string? Absolutize(string? pathOrUrl, HttpRequest request)
    {
        if (string.IsNullOrEmpty(pathOrUrl))
        {
            return pathOrUrl;
        }

        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return pathOrUrl;
        }

        var path = pathOrUrl[0] == '/' ? pathOrUrl : "/" + pathOrUrl;
        return _baseUrl.Resolve(request) + path;
    }
}
