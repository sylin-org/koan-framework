using System.Xml.Linq;
using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// Pulls content from RSS/Atom feeds using XDocument parsing.
/// </summary>
public sealed class RssPullAdapter : ISourcePullAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<RssPullAdapter> _logger;

    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    public RssPullAdapter(IHttpClientFactory httpFactory, ILogger<RssPullAdapter> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public SourceType SupportedType => SourceType.Rss;

    public async Task<List<Note>> PullAsync(Source source, CancellationToken ct)
    {
        var config = SourceConfigParser.Parse<RssConfig>(source.Configuration);

        if (string.IsNullOrWhiteSpace(config.FeedUrl))
        {
            _logger.LogWarning("RSS source {SourceId} has no feedUrl configured", source.Id);
            return [];
        }

        _logger.LogInformation("Fetching RSS feed from {FeedUrl}", config.FeedUrl);

        using var http = _httpFactory.CreateClient();
        var xml = await http.GetStringAsync(config.FeedUrl, ct);
        var doc = XDocument.Parse(xml);

        var root = doc.Root;
        if (root is null)
            return [];

        // Detect RSS vs Atom
        var items = root.Name.LocalName == "feed"
            ? ParseAtomEntries(root)
            : ParseRssItems(root);

        // Filter items newer than last pull
        if (source.LastPulledAt is not null)
        {
            items = items
                .Where(i => i.PublishedAt is null || i.PublishedAt > source.LastPulledAt.Value)
                .ToList();
        }

        // Check for duplicates by URL
        var existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (items.Count > 0)
        {
            var urlsToCheck = items
                .Where(i => !string.IsNullOrEmpty(i.Url))
                .Select(i => i.Url!)
                .ToList();

            foreach (var url in urlsToCheck)
            {
                var existing = await Note.Query(n => n.SourceUrl == url, ct);
                if (existing.Count > 0)
                    existingUrls.Add(url);
            }
        }

        var notes = new List<Note>();

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Url) && existingUrls.Contains(item.Url))
            {
                _logger.LogDebug("Skipping duplicate RSS item: {Url}", item.Url);
                continue;
            }

            var textContent = HtmlStripper.StripHtml(item.Content);
            if (string.IsNullOrWhiteSpace(textContent) && !string.IsNullOrWhiteSpace(item.Title))
                textContent = item.Title;

            var note = new Note
            {
                Title = item.Title ?? "Untitled RSS Item",
                SpaceId = source.SpaceId,
                Origin = NoteOrigin.Source,
                AutoIngested = true,
                SourceId = source.Id.ToString(),
                SourceUrl = item.Url,
                SourcePublishedAt = item.PublishedAt,
                Blocks =
                [
                    new ContentBlock
                    {
                        Kind = ContentKind.Text,
                        Content = textContent,
                        Order = 0,
                        Source = new ContentSource(
                            config.FeedUrl,
                            "application/rss+xml",
                            Extractor: nameof(RssPullAdapter))
                    }
                ]
            };

            await note.Save(ct);
            notes.Add(note);

            _logger.LogDebug("Created note {NoteId} from RSS item: {Title}", note.Id, note.Title);
        }

        return notes;
    }

    private static List<FeedItem> ParseRssItems(XElement root)
    {
        var channel = root.Element("channel");
        if (channel is null)
            return [];

        return channel.Elements("item")
            .Select(item =>
            {
                var contentEncoded = item.Element(ContentNs + "encoded")?.Value;
                var description = item.Element("description")?.Value;
                var pubDate = item.Element("pubDate")?.Value;
                var dcDate = item.Element(DcNs + "date")?.Value;

                DateTime? published = null;
                if (pubDate is not null && DateTime.TryParse(pubDate, out var pd))
                    published = pd.ToUniversalTime();
                else if (dcDate is not null && DateTime.TryParse(dcDate, out var dd))
                    published = dd.ToUniversalTime();

                return new FeedItem(
                    Title: item.Element("title")?.Value,
                    Url: item.Element("link")?.Value,
                    Content: contentEncoded ?? description ?? "",
                    PublishedAt: published);
            })
            .ToList();
    }

    private static List<FeedItem> ParseAtomEntries(XElement root)
    {
        return root.Elements(AtomNs + "entry")
            .Select(entry =>
            {
                var link = entry.Elements(AtomNs + "link")
                    .FirstOrDefault(l => l.Attribute("rel")?.Value is "alternate" or null)?
                    .Attribute("href")?.Value;

                var content = entry.Element(AtomNs + "content")?.Value
                              ?? entry.Element(AtomNs + "summary")?.Value
                              ?? "";

                var updated = entry.Element(AtomNs + "updated")?.Value
                              ?? entry.Element(AtomNs + "published")?.Value;

                DateTime? published = null;
                if (updated is not null && DateTime.TryParse(updated, out var dt))
                    published = dt.ToUniversalTime();

                return new FeedItem(
                    Title: entry.Element(AtomNs + "title")?.Value,
                    Url: link,
                    Content: content,
                    PublishedAt: published);
            })
            .ToList();
    }

    private sealed record FeedItem(
        string? Title,
        string? Url,
        string Content,
        DateTime? PublishedAt);
}
