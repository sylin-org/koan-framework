using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using S5.Recs.Models;

namespace S5.Recs.Providers;

internal sealed class AniListAnimeProvider(IHttpClientFactory httpFactory, ILogger<AniListAnimeProvider>? logger = null) : IAnimeProvider
{
    public string Code => "anilist";
    public string Name => "AniList";

    private static readonly Uri AniListEndpoint = new("https://graphql.anilist.co/");

    public async Task<List<Anime>> FetchAsync(int limit, CancellationToken ct)
    {
        var http = httpFactory;
        if (http is null) return [];
        using var client = http.CreateClient();
        var list = new List<Anime>(capacity: Math.Max(100, Math.Min(2000, limit)));

        var query = @"query ($page:Int,$perPage:Int){ Page(page:$page,perPage:$perPage){ pageInfo{ hasNextPage currentPage lastPage perPage total } media(type:ANIME,isAdult:false,sort:POPULARITY_DESC){ id title{romaji english native} episodes genres description(asHtml:false) averageScore popularity } } }";
        int pageNum = 1;
        bool hasNext = true;
        int perPage = Math.Clamp(limit >= 50 ? 50 : limit, 1, 50);
        int retries = 0;

        while (hasNext && list.Count < limit && !ct.IsCancellationRequested)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, AniListEndpoint);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var payload = new { query, variables = new { page = pageNum, perPage } };
            req.Content = new StringContent(JsonSerializer.Serialize(payload));
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var res = await client.SendAsync(req, ct);
            if ((int)res.StatusCode == 429)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(5000, 400 * Math.Pow(2, retries++)));
                logger?.LogWarning("AniList rate limited on page {Page}. Backing off for {DelayMs} ms (retry {Retry}).", pageNum, delay.TotalMilliseconds, retries);
                await Task.Delay(delay, ct);
                if (retries <= 5) continue;
            }
            res.EnsureSuccessStatusCode();
            retries = 0;

            using var s = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl) || !dataEl.TryGetProperty("Page", out var pageEl)) break;

            if (pageEl.TryGetProperty("pageInfo", out var pi))
            {
                hasNext = pi.TryGetProperty("hasNextPage", out var hnp) && hnp.ValueKind == JsonValueKind.True;
            }
            else { hasNext = false; }

            if (pageEl.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in media.EnumerateArray())
                {
                    var id = m.GetProperty("id").GetInt32().ToString();
                    var title = m.GetProperty("title").GetProperty("english").GetString()
                                ?? m.GetProperty("title").GetProperty("romaji").GetString()
                                ?? m.GetProperty("title").GetProperty("native").GetString()
                                ?? $"AniList {id}";
                    var episodes = m.TryGetProperty("episodes", out var ep) && ep.ValueKind == JsonValueKind.Number ? ep.GetInt32() : (int?)null;
                    var genres = m.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array ? g.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray() : Array.Empty<string>();
                    var desc = m.TryGetProperty("description", out var d) ? d.GetString() : null;
                    var synopsis = string.IsNullOrWhiteSpace(desc) ? null : Regex.Replace(desc!, "<.*?>", string.Empty).Replace("\n", " ").Trim();

                    double popularity = 0.0;
                    if (m.TryGetProperty("averageScore", out var avg) && avg.ValueKind == JsonValueKind.Number)
                    {
                        popularity = Math.Clamp(avg.GetInt32() / 100.0, 0.0, 1.0);
                    }
                    else if (m.TryGetProperty("popularity", out var pop) && pop.ValueKind == JsonValueKind.Number)
                    {
                        var p = pop.GetInt32();
                        popularity = Math.Clamp(Math.Log10(Math.Max(1, p)) / 5.0, 0.0, 1.0);
                    }

                    list.Add(new Anime { Id = $"anilist:{id}", Title = title!, Episodes = episodes, Genres = genres, Synopsis = synopsis, Popularity = popularity });

                    if (list.Count >= limit) break;
                }
            }

            logger?.LogInformation("AniList page {Page} fetched. Accumulated: {Count}/{Limit}", pageNum, list.Count, limit);
            pageNum++;
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        }

        return list;
    }
}
