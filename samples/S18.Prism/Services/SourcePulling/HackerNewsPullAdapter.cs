using System.Text.Json;
using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// Pulls top stories from HackerNews API, filtered by score and optional keywords.
/// </summary>
public sealed class HackerNewsPullAdapter : ISourcePullAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HackerNewsPullAdapter> _logger;

    private const string TopStoriesUrl = "https://hacker-news.firebaseio.com/v0/topstories.json";
    private const string ItemUrl = "https://hacker-news.firebaseio.com/v0/item/{0}.json";
    private const int MaxStoriesToFetch = 50;

    public HackerNewsPullAdapter(IHttpClientFactory httpFactory, ILogger<HackerNewsPullAdapter> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public SourceType SupportedType => SourceType.HackerNews;

    public async Task<List<Note>> PullAsync(Source source, CancellationToken ct)
    {
        var config = SourceConfigParser.Parse<HackerNewsConfig>(source.Configuration);

        _logger.LogInformation(
            "Fetching HackerNews top stories (minScore={MinScore}, keywords={Keywords})",
            config.MinScore, string.Join(", ", config.Keywords));

        using var http = _httpFactory.CreateClient();
        var idsJson = await http.GetStringAsync(TopStoriesUrl, ct);
        var ids = JsonSerializer.Deserialize<List<long>>(idsJson) ?? [];

        // Take first N to avoid hammering the API
        var candidateIds = ids.Take(MaxStoriesToFetch).ToList();

        var notes = new List<Note>();

        foreach (var id in candidateIds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var story = await FetchStoryAsync(http, id, ct);
                if (story is null)
                    continue;

                if (story.Score < config.MinScore)
                    continue;

                if (config.Keywords.Count > 0 && !MatchesKeywords(story, config.Keywords))
                    continue;

                // Skip duplicates
                var storyUrl = story.Url ?? $"https://news.ycombinator.com/item?id={id}";
                var existing = await Note.Query(n => n.SourceUrl == storyUrl, ct);
                if (existing.Count > 0)
                    continue;

                var contentText = $"Score: {story.Score} | Comments: {story.Descendants}\n\n";

                // Optionally fetch page content if URL present
                if (!string.IsNullOrEmpty(story.Url))
                {
                    try
                    {
                        var pageHtml = await http.GetStringAsync(story.Url, ct);
                        var pageText = HtmlStripper.StripHtml(pageHtml);
                        if (pageText.Length > 5000)
                            pageText = pageText[..5000] + "...";
                        contentText += pageText;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not fetch page content for HN story {StoryId}", id);
                        contentText += story.Text is not null
                            ? HtmlStripper.StripHtml(story.Text)
                            : "(No content available)";
                    }
                }
                else if (story.Text is not null)
                {
                    contentText += HtmlStripper.StripHtml(story.Text);
                }

                var note = new Note
                {
                    Title = story.Title ?? $"HN Story #{id}",
                    SpaceId = source.SpaceId,
                    Origin = NoteOrigin.Source,
                    AutoIngested = true,
                    SourceId = source.Id.ToString(),
                    SourceUrl = storyUrl,
                    SourcePublishedAt = story.Time > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(story.Time).UtcDateTime
                        : null,
                    Blocks =
                    [
                        new ContentBlock
                        {
                            Kind = ContentKind.Text,
                            Content = contentText,
                            Order = 0,
                            Source = new ContentSource(
                                "hacker-news",
                                Extractor: nameof(HackerNewsPullAdapter))
                        }
                    ]
                };

                await note.Save(ct);
                notes.Add(note);

                _logger.LogDebug("Created note {NoteId} from HN story: {Title} (score={Score})",
                    note.Id, note.Title, story.Score);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to process HN story {StoryId}", id);
            }
        }

        return notes;
    }

    private static async Task<HnStory?> FetchStoryAsync(HttpClient http, long id, CancellationToken ct)
    {
        var url = string.Format(ItemUrl, id);
        var json = await http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<HnStory>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static bool MatchesKeywords(HnStory story, List<string> keywords)
    {
        var titleLower = story.Title?.ToLowerInvariant() ?? "";
        var textLower = story.Text?.ToLowerInvariant() ?? "";
        return keywords.Any(kw =>
            titleLower.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            textLower.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record HnStory
    {
        public long Id { get; init; }
        public string? Title { get; init; }
        public string? Url { get; init; }
        public string? Text { get; init; }
        public int Score { get; init; }
        public int Descendants { get; init; }
        public long Time { get; init; }
        public string? Type { get; init; }
    }
}
