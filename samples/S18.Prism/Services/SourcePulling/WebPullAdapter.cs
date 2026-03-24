using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// One-off URL fetch: downloads a web page, strips HTML, and creates a Note.
/// </summary>
public sealed class WebPullAdapter : ISourcePullAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WebPullAdapter> _logger;

    private const int MaxContentLength = 50_000;

    public WebPullAdapter(IHttpClientFactory httpFactory, ILogger<WebPullAdapter> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public SourceType SupportedType => SourceType.Web;

    public async Task<List<Note>> Pull(Source source, CancellationToken ct)
    {
        var config = SourceConfigParser.Parse<WebConfig>(source.Configuration);

        if (string.IsNullOrWhiteSpace(config.Url))
        {
            _logger.LogWarning("Web source {SourceId} has no URL configured", source.Id);
            return [];
        }

        if (!UrlValidator.IsSafeUrl(config.Url))
        {
            _logger.LogWarning("Blocked unsafe URL: {Url}", config.Url);
            return [];
        }

        // Skip if already fetched (Web is one-off)
        var existing = await Note.Query(n => n.SourceUrl == config.Url, ct);
        if (existing.Count > 0)
        {
            _logger.LogDebug("Web URL already ingested: {Url}", config.Url);
            return [];
        }

        _logger.LogInformation("Fetching web page: {Url}", config.Url);

        using var http = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, config.Url);
        request.Headers.Add("User-Agent", "Koan-Prism/1.0");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        var text = HtmlStripper.StripHtml(html);

        if (text.Length > MaxContentLength)
            text = text[..MaxContentLength] + "...";

        // Try to extract title from <title> tag
        var title = ExtractTitle(html) ?? config.Url;

        var note = new Note
        {
            Title = title,
            SpaceId = source.SpaceId,
            Origin = NoteOrigin.Source,
            AutoIngested = true,
            SourceId = source.Id.ToString(),
            SourceUrl = config.Url,
            SourcePublishedAt = DateTime.UtcNow,
            Blocks =
            [
                new ContentBlock
                {
                    Kind = ContentKind.Text,
                    Content = text,
                    Order = 0,
                    Source = new ContentSource(
                        config.Url,
                        "text/html",
                        Extractor: nameof(WebPullAdapter))
                }
            ]
        };

        await note.Save(ct);

        _logger.LogDebug("Created note {NoteId} from web page: {Url}", note.Id, config.Url);

        return [note];
    }

    private static string? ExtractTitle(string html)
    {
        var startIdx = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return null;

        var tagClose = html.IndexOf('>', startIdx);
        if (tagClose < 0) return null;

        var endIdx = html.IndexOf("</title>", tagClose, StringComparison.OrdinalIgnoreCase);
        if (endIdx < 0) return null;

        var title = html[(tagClose + 1)..endIdx].Trim();
        return string.IsNullOrWhiteSpace(title) ? null : HtmlStripper.StripHtml(title);
    }
}
