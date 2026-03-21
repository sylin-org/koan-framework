using System.Text.Json;

namespace S18.Prism.Services.SourcePulling;

/// <summary>
/// Parses source Configuration JSON into typed config objects.
/// </summary>
public static class SourceConfigParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static T Parse<T>(string configJson) where T : new()
    {
        if (string.IsNullOrWhiteSpace(configJson) || configJson == "{}")
            return new T();

        return JsonSerializer.Deserialize<T>(configJson, JsonOptions) ?? new T();
    }
}

public sealed record RssConfig
{
    public string FeedUrl { get; init; } = "";
}

public sealed record HackerNewsConfig
{
    public int MinScore { get; init; } = 100;
    public List<string> Keywords { get; init; } = [];
}

public sealed record GitHubConfig
{
    public List<string> Repos { get; init; } = [];
    public bool WatchReleases { get; init; } = true;
}

public sealed record FolderWatchConfig
{
    public string Path { get; init; } = "";
    public string Pattern { get; init; } = "**/*.md";
}

public sealed record WebConfig
{
    public string Url { get; init; } = "";
}
