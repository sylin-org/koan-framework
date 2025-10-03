using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using S5.Recs.Models;
using System.Net;
using System.Text.RegularExpressions;

namespace S5.Recs.Providers;

/// <summary>
/// Parser for AniList GraphQL responses.
/// </summary>
internal sealed class AniListParser : Services.IMediaParser
{
    private readonly ILogger<AniListParser>? _logger;

    public string SourceCode => "anilist";

    public AniListParser(ILogger<AniListParser>? logger = null)
    {
        _logger = logger;
    }

    public async Task<List<Media>> ParsePageAsync(string rawJson, MediaType mediaType, CancellationToken ct = default)
    {
        var result = new List<Media>();

        try
        {
            var doc = JToken.Parse(rawJson);
            var dataEl = doc["data"];
            var pageEl = dataEl?["Page"];

            if (pageEl == null)
            {
                _logger?.LogWarning("AniList: missing data.Page in response");
                return result;
            }

            var media = pageEl["media"] as JArray;
            if (media == null)
            {
                return result;
            }

            foreach (var m in media)
            {
                if (ct.IsCancellationRequested) break;

                var mediaItem = await ParseMediaItemAsync(m, mediaType, ct);
                if (mediaItem != null)
                {
                    result.Add(mediaItem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse AniList page: {Error}", ex.Message);
        }

        return result;
    }

    private Task<Media?> ParseMediaItemAsync(JToken item, MediaType mediaType, CancellationToken ct = default)
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

            // UpdatedAt timestamp (Unix timestamp from API)
            var updatedAtValue = DateTimeOffset.UtcNow; // Default fallback
            var updatedAtTok = item["updatedAt"];
            if (updatedAtTok?.Type == JTokenType.Integer)
            {
                try
                {
                    var unixTimestamp = updatedAtTok.Value<long>();
                    updatedAtValue = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
                }
                catch
                {
                    // Fall back to UtcNow if conversion fails
                }
            }

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
                UpdatedAt = updatedAtValue
            };

            return Task.FromResult<Media?>(media);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse AniList item to Media: {Item}", item.ToString());
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
}
