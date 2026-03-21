using System.Text.Json;
using System.Text.Json.Serialization;
using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// Pulls latest releases from GitHub repos via the public API.
/// </summary>
public sealed class GitHubPullAdapter : ISourcePullAdapter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GitHubPullAdapter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubPullAdapter(IHttpClientFactory httpFactory, ILogger<GitHubPullAdapter> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public SourceType SupportedType => SourceType.GitHub;

    public async Task<List<Note>> PullAsync(Source source, CancellationToken ct)
    {
        var config = SourceConfigParser.Parse<GitHubConfig>(source.Configuration);

        if (config.Repos.Count == 0)
        {
            _logger.LogWarning("GitHub source {SourceId} has no repos configured", source.Id);
            return [];
        }

        var notes = new List<Note>();

        using var http = _httpFactory.CreateClient();

        foreach (var repo in config.Repos)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var repoNotes = config.WatchReleases
                    ? await PullLatestReleaseAsync(http, repo, source, ct)
                    : await PullReadmeAsync(http, repo, source, ct);

                notes.AddRange(repoNotes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to pull GitHub repo {Repo}", repo);
            }
        }

        return notes;
    }

    private async Task<List<Note>> PullLatestReleaseAsync(
        HttpClient http, string repo, Source source, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Koan-Prism/1.0");
        request.Headers.Add("Accept", "application/vnd.github.v3+json");

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("No release found for {Repo} (status={Status})", repo, response.StatusCode);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);

        if (release is null)
            return [];

        var releaseUrl = release.HtmlUrl ?? $"https://github.com/{repo}/releases/tag/{release.TagName}";

        // Skip if already ingested
        var existing = await Note.Query(n => n.SourceUrl == releaseUrl, ct);
        if (existing.Count > 0)
            return [];

        // Skip if release is older than last pull
        if (source.LastPulledAt is not null && release.PublishedAt is not null
            && release.PublishedAt.Value <= source.LastPulledAt.Value)
            return [];

        var content = $"Release: {release.TagName}\nRepo: {repo}\n\n{release.Body ?? "(No release notes)"}";

        var note = new Note
        {
            Title = $"{repo} — {release.TagName}",
            SpaceId = source.SpaceId,
            Origin = NoteOrigin.Source,
            AutoIngested = true,
            SourceId = source.Id.ToString(),
            SourceUrl = releaseUrl,
            SourcePublishedAt = release.PublishedAt,
            Blocks =
            [
                new ContentBlock
                {
                    Kind = ContentKind.Text,
                    Content = content,
                    Order = 0,
                    Source = new ContentSource(
                        repo,
                        Extractor: nameof(GitHubPullAdapter))
                }
            ]
        };

        await note.Save(ct);

        _logger.LogDebug("Created note {NoteId} from GitHub release: {Repo} {Tag}",
            note.Id, repo, release.TagName);

        return [note];
    }

    private async Task<List<Note>> PullReadmeAsync(
        HttpClient http, string repo, Source source, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{repo}/readme";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Koan-Prism/1.0");
        request.Headers.Add("Accept", "application/vnd.github.v3.raw");

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug("No README found for {Repo}", repo);
            return [];
        }

        var readmeUrl = $"https://github.com/{repo}#readme";

        // Skip if already ingested
        var existing = await Note.Query(n => n.SourceUrl == readmeUrl, ct);
        if (existing.Count > 0)
            return [];

        var readmeContent = await response.Content.ReadAsStringAsync(ct);

        var note = new Note
        {
            Title = $"{repo} — README",
            SpaceId = source.SpaceId,
            Origin = NoteOrigin.Source,
            AutoIngested = true,
            SourceId = source.Id.ToString(),
            SourceUrl = readmeUrl,
            Blocks =
            [
                new ContentBlock
                {
                    Kind = ContentKind.Text,
                    Content = readmeContent,
                    Order = 0,
                    Source = new ContentSource(
                        repo,
                        "text/markdown",
                        Extractor: nameof(GitHubPullAdapter))
                }
            ]
        };

        await note.Save(ct);

        _logger.LogDebug("Created note {NoteId} from GitHub README: {Repo}", note.Id, repo);

        return [note];
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; init; }
    }
}
