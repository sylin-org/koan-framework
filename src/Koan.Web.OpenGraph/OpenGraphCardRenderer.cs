using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Logging;
using Koan.Data.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Web.OpenGraph;

internal sealed class OpenGraphCardRenderer : IOpenGraphCardRenderer
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<OpenGraphCardRenderer>();
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    private readonly IOptionsMonitor<OpenGraphOptions> _options;
    private readonly ShellCache _shellCache;
    private readonly SocialCardRegistry _cards;

    public OpenGraphCardRenderer(
        IOptionsMonitor<OpenGraphOptions> options,
        ShellCache shellCache,
        SocialCardRegistry cards)
    {
        _options = options;
        _shellCache = shellCache;
        _cards = cards;
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

        var snapshot = await ResolveSnapshotAsync(request.Path.Value ?? "/", ct).ConfigureAwait(false);
        var head = BuildHead(snapshot, options, request);
        return Inject(shell, head, options.PlaceholderMarker);
    }

    private async Task<SocialCardSnapshot?> ResolveSnapshotAsync(string path, CancellationToken ct)
    {
        foreach (var registration in _cards.Registrations)
        {
            if (!registration.Matcher.TryExtractToken(path, out var token))
            {
                continue;
            }

            var key = registration.KeyFor(token);
            var snapshot = await SocialCardSnapshot.Get(key, ct).ConfigureAwait(false);
            if (snapshot is not null)
            {
                return snapshot;
            }

            // Cache miss: resolve once, build, and save (lazy fill). A null result (unknown
            // id/slug) falls through to the default card, never an error.
            var card = await registration.ResolveAndProject(token).ConfigureAwait(false);
            if (card is null)
            {
                return null;
            }

            snapshot = SocialCardSnapshot.FromCard(key, card);
            try
            {
                await snapshot.Save(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The card still renders this request; only the cache write failed.
                Log.DataWarning("opengraph.fill", "failed", ("key", key), ("error", ex.Message));
            }

            return snapshot;
        }

        return null; // no matching route: default card
    }

    private static string BuildHead(SocialCardSnapshot? snapshot, OpenGraphOptions options, HttpRequest request)
    {
        var title = Truncate(snapshot?.Title ?? options.SiteName, options.MaxTitleLength);
        var description = Truncate(snapshot?.Description ?? options.DefaultDescription, options.MaxDescriptionLength);
        var imageUrl = Absolutize(snapshot?.ImagePath ?? options.DefaultImage, request);
        var url = Absolutize(snapshot?.UrlPath ?? request.Path.Value, request);
        var ogType = snapshot?.OgType ?? options.DefaultType;

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
        if (!string.IsNullOrEmpty(options.SiteName)) Property(sb, "og:site_name", options.SiteName);
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

    private static void Meta(StringBuilder sb, string tag, bool isLink, string rel, string href)
        => sb.Append("<link rel=\"").Append(rel).Append("\" href=\"").Append(Encode(href)).Append("\" />\n");

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

    private static string? Absolutize(string? pathOrUrl, HttpRequest request)
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
        return $"{request.Scheme}://{request.Host.Value}{path}";
    }
}
