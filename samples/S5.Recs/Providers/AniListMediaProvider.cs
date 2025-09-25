using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using S5.Recs.Models;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Net;
using System.Runtime.CompilerServices;

namespace S5.Recs.Providers;

internal sealed class AniListMediaProvider(IHttpClientFactory httpFactory, ILogger<AniListMediaProvider>? logger = null) : IMediaProvider
{
    public string Code => "anilist";
    public string Name => "AniList";

    // TODO: Will be populated after MediaType entities are seeded
    public MediaType[] SupportedTypes => Array.Empty<MediaType>();

    private static readonly Uri AniListEndpoint = new("https://graphql.anilist.co/");

    public async Task<List<Media>> FetchAsync(MediaType mediaType, int limit, CancellationToken ct)
    {
        // AniList only supports ANIME and MANGA media types
        if (!IsMediaTypeSupported(mediaType))
        {
            logger?.LogInformation("AniList does not support media type '{MediaType}', returning empty result", mediaType.Name);
            return new List<Media>();
        }

        var list = new List<Media>(capacity: Math.Max(100, Math.Min(2000, limit)));

        await foreach (var batch in FetchStreamAsync(mediaType, limit, ct))
        {
            list.AddRange(batch);
            if (list.Count >= limit) break;
        }

        return list.Take(limit).ToList();
    }

    public async IAsyncEnumerable<List<Media>> FetchStreamAsync(MediaType mediaType, int limit, [EnumeratorCancellation] CancellationToken ct)
    {
        // AniList only supports ANIME and MANGA media types
        if (!IsMediaTypeSupported(mediaType))
        {
            logger?.LogInformation("AniList does not support media type '{MediaType}', returning empty stream", mediaType.Name);
            yield break;
        }

        var http = httpFactory;
        if (http is null) yield break;

        using var client = http.CreateClient();
        var query = BuildGraphQLQuery(mediaType);
        int pageNum = 1;
        bool hasNext = true;
        int perPage = Math.Clamp(limit >= 50 ? 50 : limit, 1, 50);
        int totalFetched = 0;

        // Backoff controls
        int rateLimitRetries = 0;
        int transientRetries = 0;
        const int maxRateLimitRetries = 6;
        const int maxTransientRetries = 4;

        while (hasNext && totalFetched < limit && !ct.IsCancellationRequested)
        {
            List<Media>? batch = null;
            bool shouldBreak = false;

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
                    var delay = GetRetryAfterDelay(res) ?? ComputeBackoff(rateLimitRetries, 6000);
                    rateLimitRetries++;
                    logger?.LogWarning("AniList 429 on page {Page}. Backing off {DelayMs} ms (retry {Retry}).", pageNum, delay.TotalMilliseconds, rateLimitRetries);
                    await Task.Delay(delay, ct);
                    if (rateLimitRetries <= maxRateLimitRetries) continue;
                    logger?.LogWarning("AniList: max 429 retries reached on page {Page}. Stopping stream at {Count} items.", pageNum, totalFetched);
                    shouldBreak = true;
                }
                else if (!res.IsSuccessStatusCode)
                {
                    var delay = ComputeBackoff(transientRetries, 8000);
                    transientRetries++;
                    var body = await SafeReadBodyAsync(res, ct);
                    logger?.LogWarning("AniList non-success {Status} on page {Page}. Backing off {DelayMs} ms (retry {Retry}). Body={Body}",
                        (int)res.StatusCode, pageNum, delay.TotalMilliseconds, transientRetries, body);
                    await Task.Delay(delay, ct);
                    if (transientRetries <= maxTransientRetries) continue;
                    logger?.LogWarning("AniList: max transient retries reached on page {Page}. Stopping stream at {Count} items.", pageNum, totalFetched);
                    shouldBreak = true;
                }
                else
                {
                    // Reset retries after success
                    rateLimitRetries = 0; transientRetries = 0;

                    var txt = await res.Content.ReadAsStringAsync(ct);
                    var doc = JToken.Parse(txt);
                    var dataEl = doc["data"];
                    var pageEl = dataEl?["Page"];
                    if (pageEl is null)
                    {
                        logger?.LogWarning("AniList: missing data.Page on page {Page}. Stopping stream.", pageNum);
                        shouldBreak = true;
                    }
                    else
                    {
                        var pi = pageEl["pageInfo"];
                        if (pi is not null)
                        {
                            hasNext = (bool?)(pi["hasNextPage"]?.Value<bool>()) == true;
                        }
                        else { hasNext = false; }

                        var media = pageEl["media"] as JArray;
                        if (media is not null)
                        {
                            batch = new List<Media>();
                            int skipped = 0;
                            foreach (var m in media)
                            {
                                var mediaItem = await MapToMedia(m, mediaType, ct);
                                if (mediaItem != null)
                                {
                                    batch.Add(mediaItem);
                                    totalFetched++;
                                }
                                else
                                {
                                    skipped++;
                                }

                                if (totalFetched >= limit) break;
                            }
                            if (skipped > 0)
                                logger?.LogDebug("AniList page {Page}: skipped {Skipped} malformed media entries", pageNum, skipped);
                        }

                        logger?.LogInformation("AniList page {Page} fetched. Accumulated: {Count}/{Limit}", pageNum, totalFetched, limit);
                        pageNum++;
                        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var delay = ComputeBackoff(transientRetries, 8000);
                transientRetries++;
                logger?.LogWarning("AniList timeout on page {Page}. Backing off {DelayMs} ms (retry {Retry}).", pageNum, delay.TotalMilliseconds, transientRetries);
                await Task.Delay(delay, ct);
                if (transientRetries > maxTransientRetries)
                {
                    logger?.LogWarning("AniList: max timeout retries reached on page {Page}. Stopping stream at {Count} items.", pageNum, totalFetched);
                    shouldBreak = true;
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
                    logger?.LogWarning("AniList: max error retries reached on page {Page}. Stopping stream at {Count} items.", pageNum, totalFetched);
                    shouldBreak = true;
                }
            }

            // Yield outside of try-catch
            if (batch is { Count: > 0 })
            {
                yield return batch;
            }

            if (shouldBreak)
            {
                yield break;
            }
        }
    }

    private string BuildGraphQLQuery(MediaType mediaType)
    {
        var typeFilter = mediaType.Name.ToUpperInvariant(); // "ANIME" or "MANGA"
        return $@"
            query ($page: Int, $perPage: Int) {{
                Page(page: $page, perPage: $perPage) {{
                    pageInfo {{
                        hasNextPage
                        currentPage
                        lastPage
                        perPage
                        total
                    }}
                    media(type: {typeFilter}, sort: POPULARITY_DESC) {{
                        id
                        siteUrl
                        title {{
                            romaji
                            english
                            native
                        }}
                        synonyms
                        episodes
                        chapters
                        volumes
                        duration
                        genres
                        tags {{
                            name
                            rank
                        }}
                        description(asHtml: false)
                        averageScore
                        popularity
                        coverImage {{
                            extraLarge
                            large
                            medium
                            color
                        }}
                        bannerImage
                        startDate {{
                            year
                            month
                            day
                        }}
                        endDate {{
                            year
                            month
                            day
                        }}
                        status
                        format
                        isAdult
                    }}
                }}
            }}";
    }

    private Task<Media?> MapToMedia(JToken item, MediaType mediaType, CancellationToken ct)
    {
        try
        {
            // Robust ID extraction
            var idTok = item["id"];
            if (idTok?.Type != JTokenType.Integer) return Task.FromResult<Media?>(null);
            var rawId = idTok.Value<int>();
            var externalId = rawId.ToString();

            // Titles (fallback chain)
            string? tEn = item["title"]?["english"]?.Value<string>();
            string? tRo = item["title"]?["romaji"]?.Value<string>();
            string? tNa = item["title"]?["native"]?.Value<string>();
            var title = tEn ?? tRo ?? tNa ?? $"AniList {rawId}";

            // Media-specific metrics
            var episodes = ToNullableInt(item["episodes"]);
            var chapters = ToNullableInt(item["chapters"]);
            var volumes = ToNullableInt(item["volumes"]);
            var duration = ToNullableInt(item["duration"]);

            // Arrays (genres, synonyms, tags) with filtering
            var genres = item["genres"] is JArray gArr
                ? gArr.Select(x => x?.Value<string>() ?? string.Empty).Where(NotNullOrWhite).Select(NormalizeTokenString).ToArray()
                : Array.Empty<string>();
            var synonyms = item["synonyms"] is JArray syn
                ? syn.Select(x => x?.Value<string>() ?? string.Empty).Where(NotNullOrWhite).Select(NormalizeTokenString).ToArray()
                : Array.Empty<string>();
            var tags = item["tags"] is JArray tg
                ? tg.Select(x => x?["name"]?.Value<string>() ?? string.Empty).Where(NotNullOrWhite).Select(NormalizeTokenString).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();

            // Synthetic NSFW tag from top-level isAdult property
            if (item["isAdult"]?.Value<bool>() == true)
            {
                tags = tags.Append("NSFW").ToArray();
            }

            // Description cleanup
            var descRaw = item["description"]?.Value<string>();
            string? synopsis = null;
            if (!string.IsNullOrWhiteSpace(descRaw))
            {
                var decoded = WebUtility.HtmlDecode(descRaw);
                var stripped = Regex.Replace(decoded, "<.*?>", string.Empty);
                synopsis = string.IsNullOrWhiteSpace(stripped) ? null : stripped.Replace("\n", " ").Trim();
            }

            // Popularity normalization
            double popularity = 0.0;
            var avgTok = item["averageScore"];
            if (avgTok?.Type == JTokenType.Integer)
            {
                popularity = Math.Clamp(avgTok.Value<int>() / 100.0, 0.0, 1.0);
            }
            else
            {
                var popTok = item["popularity"];
                if (popTok?.Type == JTokenType.Integer)
                {
                    var pVal = popTok.Value<int>();
                    popularity = Math.Clamp(Math.Log10(Math.Max(1, pVal)) / 5.0, 0.0, 1.0);
                }
            }

            // Average score
            double? averageScore = null;
            if (avgTok?.Type == JTokenType.Integer)
            {
                averageScore = (avgTok.Value<int>() / 100.0) * 4 + 1; // Convert from 0-100 to 1-5 scale
            }

            // Images
            string? cover = null, banner = null, color = null;
            if (item["coverImage"] is JObject ci)
            {
                cover = ci["large"]?.Value<string>()
                    ?? ci["extraLarge"]?.Value<string>()
                    ?? ci["medium"]?.Value<string>();
                color = ci["color"]?.Value<string>();
            }
            banner = item["bannerImage"]?.Value<string>();

            // Dates
            DateOnly? startDate = ParseAniListDate(item["startDate"]);
            DateOnly? endDate = ParseAniListDate(item["endDate"]);

            // Status and Format
            var status = item["status"]?.Value<string>();
            var format = item["format"]?.Value<string>();

            // Resolve MediaFormat - for now, use a placeholder
            // TODO: Implement proper MediaFormat resolution after seeding
            var mediaFormatId = "placeholder-format-id";

            var media = new Media
            {
                Id = Media.MakeId("anilist", externalId, mediaType.Id!),
                MediaTypeId = mediaType.Id!,
                MediaFormatId = mediaFormatId,
                ProviderCode = "anilist",
                ExternalId = externalId,
                Title = title,
                TitleEnglish = tEn,
                TitleRomaji = tRo,
                TitleNative = tNa,
                Synonyms = synonyms,
                Genres = genres,
                Tags = tags,
                Episodes = episodes,
                Chapters = chapters,
                Volumes = volumes,
                Duration = duration,
                Synopsis = synopsis,
                Popularity = popularity,
                AverageScore = averageScore,
                CoverUrl = cover,
                BannerUrl = banner,
                CoverColorHex = color,
                StartDate = startDate,
                EndDate = endDate,
                Status = status,
                ExternalIds = new Dictionary<string, string>
                {
                    ["anilist"] = externalId,
                    ["anilist_url"] = $"https://anilist.co/{mediaType.Name.ToLowerInvariant()}/{rawId}"
                },
                ImportedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return Task.FromResult<Media?>(media);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to map AniList item to Media: {Item}", item.ToString());
            return Task.FromResult<Media?>(null);
        }
    }

    private static DateOnly? ParseAniListDate(JToken? dateToken)
    {
        try
        {
            if (dateToken is not JObject dateObj) return null;

            // Handle null values more defensively
            var yearToken = dateObj["year"];
            var monthToken = dateObj["month"];
            var dayToken = dateObj["day"];

            // Skip if year is null or not a valid integer
            if (yearToken == null || yearToken.Type == JTokenType.Null) return null;

            int? year = null;
            int? month = null;
            int? day = null;

            // Parse year safely
            if (yearToken.Type == JTokenType.Integer)
                year = yearToken.Value<int>();

            // Parse month safely (default to 1 if null)
            if (monthToken != null && monthToken.Type == JTokenType.Integer)
                month = monthToken.Value<int>();

            // Parse day safely (default to 1 if null)
            if (dayToken != null && dayToken.Type == JTokenType.Integer)
                day = dayToken.Value<int>();

            if (year is null) return null;

            return new DateOnly(year.Value, month ?? 1, day ?? 1);
        }
        catch
        {
            // If any parsing fails, return null to be as permissive as possible
            return null;
        }
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

    private static int? ToNullableInt(JToken? t)
    {
        if (t is null) return null;
        if (t.Type == JTokenType.Integer)
        {
            try { return t.Value<int>(); } catch { return null; }
        }
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
        return Regex.Replace(trimmed, "\\s+", " ");
    }

    private static bool IsMediaTypeSupported(MediaType mediaType)
    {
        // AniList only supports ANIME and MANGA
        return mediaType.Name.Equals("Anime", StringComparison.OrdinalIgnoreCase) ||
               mediaType.Name.Equals("Manga", StringComparison.OrdinalIgnoreCase);
    }
}