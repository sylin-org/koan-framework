using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using S5.Recs.Models;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Net;

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

        var query = @"query ($page:Int,$perPage:Int){ Page(page:$page,perPage:$perPage){ pageInfo{ hasNextPage currentPage lastPage perPage total } media(type:ANIME,isAdult:false,sort:POPULARITY_DESC){ id siteUrl title{romaji english native} synonyms episodes genres tags{name rank} description(asHtml:false) averageScore popularity coverImage{extraLarge large medium color} bannerImage } } }";
        int pageNum = 1;
        bool hasNext = true;
        int perPage = Math.Clamp(limit >= 50 ? 50 : limit, 1, 50);
        // Backoff controls
        int rateLimitRetries = 0; // specific to 429
        int transientRetries = 0; // network/5xx/json errors
        const int maxRateLimitRetries = 6;
        const int maxTransientRetries = 4;

        while (hasNext && list.Count < limit && !ct.IsCancellationRequested)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, AniListEndpoint);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var payload = new { query, variables = new { page = pageNum, perPage } };
                req.Content = new StringContent(JsonConvert.SerializeObject(payload));
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using var res = await client.SendAsync(req, ct);
                if ((int)res.StatusCode == 429)
                {
                    // Immediate back-off on rate limit; prefer Retry-After when present
                    var delay = GetRetryAfterDelay(res) ?? ComputeBackoff(rateLimitRetries, 6000);
                    rateLimitRetries++;
                    logger?.LogWarning("AniList 429 on page {Page}. Backing off {DelayMs} ms (retry {Retry}).", pageNum, delay.TotalMilliseconds, rateLimitRetries);
                    await Task.Delay(delay, ct);
                    if (rateLimitRetries <= maxRateLimitRetries) continue; // retry same page
                    logger?.LogWarning("AniList: max 429 retries reached on page {Page}. Preserving {Count} items and stopping.", pageNum, list.Count);
                    break; // give up, return what we have
                }

                if (!res.IsSuccessStatusCode)
                {
                    // Back-off for other non-success (e.g., 5xx)
                    var delay = ComputeBackoff(transientRetries, 8000);
                    transientRetries++;
                    var body = await SafeReadBodyAsync(res, ct);
                    logger?.LogWarning("AniList non-success {Status} on page {Page}. Backing off {DelayMs} ms (retry {Retry}). Body={Body}",
                        (int)res.StatusCode, pageNum, delay.TotalMilliseconds, transientRetries, body);
                    await Task.Delay(delay, ct);
                    if (transientRetries <= maxTransientRetries) continue; // retry same page
                    logger?.LogWarning("AniList: max transient retries reached on page {Page}. Preserving {Count} items and stopping.", pageNum, list.Count);
                    break;
                }

                // Reset retries after a success
                rateLimitRetries = 0; transientRetries = 0;

                var txt = await res.Content.ReadAsStringAsync(ct);
                var doc = JToken.Parse(txt);
                var dataEl = doc["data"];
                var pageEl = dataEl?["Page"];
                if (pageEl is null)
                {
                    logger?.LogWarning("AniList: missing data.Page on page {Page}. Preserving {Count} items and stopping.", pageNum, list.Count);
                    break;
                }

                var pi = pageEl["pageInfo"];
                if (pi is not null)
                {
                    hasNext = (bool?)(pi["hasNextPage"]?.Value<bool>()) == true;
                }
                else { hasNext = false; }

                var media = pageEl["media"] as JArray;
                if (media is not null)
                {
                    int skipped = 0;
                    foreach (var m in media)
                    {
                        // Robust ID extraction
                        var idTok = m["id"];
                        if (idTok?.Type != JTokenType.Integer)
                        {
                            skipped++; continue;
                        }
                        var rawId = idTok.Value<int>();
                        var id = $"anilist:{rawId}";

                        // Titles (fallback chain)
                        string? tEn = m["title"]?["english"]?.Value<string>();
                        string? tRo = m["title"]?["romaji"]?.Value<string>();
                        string? tNa = m["title"]?["native"]?.Value<string>();
                        var title = tEn ?? tRo ?? tNa ?? $"AniList {rawId}";

                        // Episodes (nullable)
                        var episodes = ToNullableInt(m["episodes"]);

                        // Arrays (genres, synonyms, tags) with filtering
                        var genres = m["genres"] is JArray gArr
                            ? gArr.Select(x => x?.Value<string>() ?? string.Empty).Where(NotNullOrWhite).Select(NormalizeTokenString).ToArray()
                            : Array.Empty<string>();
                        var synonyms = m["synonyms"] is JArray syn
                            ? syn.Select(x => x?.Value<string>() ?? string.Empty).Where(NotNullOrWhite).Select(NormalizeTokenString).ToArray()
                            : Array.Empty<string>();
                        var tags = m["tags"] is JArray tg
                            ? tg.Select(x => x?["name"]?.Value<string>() ?? string.Empty).Where(NotNullOrWhite).Select(NormalizeTokenString).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : Array.Empty<string>();

                        // Description cleanup: HTML decode then strip tags
                        var descRaw = m["description"]?.Value<string>();
                        string? synopsis = null;
                        if (!string.IsNullOrWhiteSpace(descRaw))
                        {
                            var decoded = WebUtility.HtmlDecode(descRaw);
                            var stripped = Regex.Replace(decoded, "<.*?>", string.Empty);
                            synopsis = string.IsNullOrWhiteSpace(stripped) ? null : stripped.Replace("\n", " ").Trim();
                        }

                        // Popularity normalization (defensive)
                        double popularity = 0.0;
                        var avgTok = m["averageScore"];
                        if (avgTok?.Type == JTokenType.Integer)
                        {
                            popularity = Math.Clamp(avgTok.Value<int>() / 100.0, 0.0, 1.0);
                        }
                        else
                        {
                            var popTok = m["popularity"];
                            if (popTok?.Type == JTokenType.Integer)
                            {
                                var pVal = popTok.Value<int>();
                                popularity = Math.Clamp(Math.Log10(Math.Max(1, pVal)) / 5.0, 0.0, 1.0);
                            }
                        }

                        // Images
                        string? cover = null, banner = null, color = null;
                        if (m["coverImage"] is JObject ci)
                        {
                            cover = ci["large"]?.Value<string>()
                                ?? ci["extraLarge"]?.Value<string>()
                                ?? ci["medium"]?.Value<string>();
                            color = ci["color"]?.Value<string>();
                        }
                        banner = m["bannerImage"]?.Value<string>();

                        list.Add(new Anime
                        {
                            Id = id,
                            Title = title!,
                            TitleEnglish = tEn,
                            TitleRomaji = tRo,
                            TitleNative = tNa,
                            Synonyms = synonyms,
                            Episodes = episodes,
                            Genres = genres,
                            Tags = tags,
                            Synopsis = synopsis,
                            Popularity = popularity,
                            CoverUrl = cover,
                            BannerUrl = banner,
                            CoverColorHex = color
                        });

                        if (list.Count >= limit) break;
                    }
                    if (skipped > 0)
                        logger?.LogDebug("AniList page {Page}: skipped {Skipped} malformed media entries", pageNum, skipped);
                }

                logger?.LogInformation("AniList page {Page} fetched. Accumulated: {Count}/{Limit}", pageNum, list.Count, limit);
                pageNum++;
                // gentle pacing between successful pages
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient timeout without external cancellation
                var delay = ComputeBackoff(transientRetries, 8000);
                transientRetries++;
                logger?.LogWarning("AniList timeout on page {Page}. Backing off {DelayMs} ms (retry {Retry}).", pageNum, delay.TotalMilliseconds, transientRetries);
                await Task.Delay(delay, ct);
                if (transientRetries > maxTransientRetries)
                {
                    logger?.LogWarning("AniList: max timeout retries reached on page {Page}. Preserving {Count} items and stopping.", pageNum, list.Count);
                    break;
                }
            }
            catch (Exception ex)
            {
                var delay = ComputeBackoff(transientRetries, 8000);
                transientRetries++;
                logger?.LogWarning(ex, "AniList error on page {Page}. Backing off {DelayMs} ms (retry {Retry}).", pageNum, delay.TotalMilliseconds, transientRetries);
                await Task.Delay(delay, ct);
                if (transientRetries > maxTransientRetries)
                {
                    logger?.LogWarning("AniList: max error retries reached on page {Page}. Preserving {Count} items and stopping.", pageNum, list.Count);
                    break;
                }
            }
        }

        return list;
    }

    private static TimeSpan ComputeBackoff(int attempt, int maxMs)
    {
        var baseMs = Math.Min(maxMs, (int)Math.Round(500 * Math.Pow(2, Math.Clamp(attempt, 0, 6))));
        var jitter = Random.Shared.Next(100, 400);
        return TimeSpan.FromMilliseconds(Math.Min(maxMs, baseMs + jitter));
    }

    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter is null) return null;
        if (res.Headers.RetryAfter.Delta.HasValue) return res.Headers.RetryAfter.Delta;
        if (res.Headers.RetryAfter.Date.HasValue)
        {
            var delta = res.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
            if (delta > TimeSpan.Zero) return delta;
        }
        return null;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return await res.Content.ReadAsStringAsync(ct); } catch { return string.Empty; }
    }

    // Helper: safe nullable int extraction
    private static int? ToNullableInt(JToken? t)
    {
        if (t is null) return null;
        if (t.Type == JTokenType.Integer)
        {
            try { return t.Value<int>(); } catch { return null; }
        }
        // Some AniList numeric fields might arrive as strings; attempt parse
        if (t.Type == JTokenType.String)
        {
            var s = t.Value<string>();
            if (int.TryParse(s, out var v)) return v;
        }
        return null;
    }

    private static bool NotNullOrWhite(string s) => !string.IsNullOrWhiteSpace(s);

    private static string NormalizeTokenString(string s)
    {
        var trimmed = s.Trim();
        // Collapse internal whitespace
    return Regex.Replace(trimmed, "\\s+", " ");
    }
}
