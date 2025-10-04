using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using S5.Recs.Infrastructure;
using S5.Recs.Models;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Models;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;

namespace S5.Recs.Services;

internal sealed class RecsService : IRecsService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecsService>? _logger;
    private readonly IOptions<S5.Recs.Options.TagCatalogOptions>? _tagOptions;

    // Demo data for fallback scenarios
    private readonly List<Media> _demoMedia = new()
    {
        new Media
        {
            MediaTypeId = "demo-media-type",
            MediaFormatId = "demo-tv-format",
            ProviderCode = "local",
            ExternalId = "haikyuu",
            Title = "Haikyuu!!",
            Genres = new[] { "Sports", "School Life" },
            Episodes = 25,
            Synopsis = "A high-energy volleyball ensemble.",
            Popularity = 0.95,
            ImportedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        },
        new Media
        {
            MediaTypeId = "demo-media-type",
            MediaFormatId = "demo-tv-format",
            ProviderCode = "local",
            ExternalId = "kon",
            Title = "K-On!",
            Genres = new[] { "Slice of Life", "Music" },
            Episodes = 12,
            Synopsis = "Cozy after-school band hijinks.",
            Popularity = 0.88,
            ImportedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        },
        new Media
        {
            MediaTypeId = "demo-media-type",
            MediaFormatId = "demo-tv-format",
            ProviderCode = "local",
            ExternalId = "yuru",
            Title = "Laid-Back Camp",
            Genres = new[] { "Slice of Life" },
            Episodes = 12,
            Synopsis = "Wholesome camping and friendship.",
            Popularity = 0.90,
            ImportedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }
    };

    public RecsService(IServiceProvider sp, IConfiguration configuration, ILogger<RecsService>? logger = null, IOptions<S5.Recs.Options.TagCatalogOptions>? tagOptions = null)
    {
        _sp = sp;
        _configuration = configuration;
        _logger = logger;
        _tagOptions = tagOptions;
    }

    public async Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(
        string? text,
        string? anchorMediaId,
        string[]? genres,
        int? episodesMax,
        bool spoilerSafe,
        int topK,
        string? userId,
        string[]? preferTags,
        double? preferWeight,
        string? sort,
        string? mediaTypeFilter,
        double? ratingMin,
        double? ratingMax,
        int? yearMin,
        int? yearMax,
        CancellationToken ct)
    {
        // Guardrails
        if (topK <= 0) topK = 10;
        if (topK > 100) topK = 100;

        _logger?.LogInformation("Multi-media query: text='{Text}' anchor='{Anchor}' mediaType='{MediaType}' topK={TopK}",
            text, anchorMediaId, mediaTypeFilter, topK);

        try
        {
            if (!string.IsNullOrWhiteSpace(text)
                || !string.IsNullOrWhiteSpace(anchorMediaId)
                || !string.IsNullOrWhiteSpace(userId)
                || (preferTags is { Length: > 0 }))
            {
                // ADR-0051: Build separate search intent and user preference vectors
                var (searchVector, userPrefVector) = await BuildQueryVectors(text, anchorMediaId, userId, preferTags, ct);

                // Determine query vector via blending (66% search intent, 34% user preferences)
                const double SEARCH_INTENT_WEIGHT = 0.66;
                float[]? queryVector = null;

                if (searchVector != null && userPrefVector != null)
                {
                    // Blend both vectors: prioritize search intent over learned preferences
                    queryVector = BlendVectors(searchVector, userPrefVector, SEARCH_INTENT_WEIGHT);
                }
                else if (searchVector != null)
                {
                    queryVector = searchVector;
                }
                else if (userPrefVector != null)
                {
                    queryVector = userPrefVector;
                }

                if (queryVector != null && queryVector.Length > 0 && Vector<Media>.IsAvailable)
                {
                    // ADR-0051: Use hybrid search with unified API
                    // Alpha = 0.5 (balanced semantic + keyword) when text query is provided
                    var vectorResults = await Vector<Media>.Search(
                        vector: queryVector,
                        text: text,  // Enables hybrid search if provided
                        alpha: !string.IsNullOrWhiteSpace(text) ? 0.5 : null,
                        topK: topK,
                        ct: ct
                    );

                    var idToScore = vectorResults.Matches.ToDictionary(m => m.Id, m => m.Score);

                    var mediaItems = new List<Media>();
                    foreach (var id in idToScore.Keys)
                    {
                        var media = await Media.Get(id, ct);
                        if (media != null) mediaItems.Add(media);
                    }

                    // Apply filters
                    var filteredMedia = await ApplyFilters(mediaItems, genres, episodesMax, mediaTypeFilter, userId, ratingMin, ratingMax, yearMin, yearMax, ct);

                    // Apply personalization and scoring
                    var recommendations = await ScoreAndPersonalize(filteredMedia, idToScore, userId, preferTags, preferWeight, spoilerSafe, ct);

                    // Apply sorting
                    var sortedRecommendations = ApplySort(recommendations, sort);
                    var finalResults = sortedRecommendations.Take(topK).ToList();

                    _logger?.LogInformation("Multi-media {SearchMode} query returned {Count} results",
                        !string.IsNullOrWhiteSpace(text) ? "hybrid" : "vector",
                        finalResults.Count);
                    return (finalResults, false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Multi-media vector query failed, falling back to database query");
        }

        // Fallback to database query
        try
        {
            var allMedia = await Media.All(ct);
            var filteredMedia = await ApplyFilters(allMedia, genres, episodesMax, mediaTypeFilter, userId, ratingMin, ratingMax, yearMin, yearMax, ct);

            var fallbackRecommendations = filteredMedia.Select(media => new Recommendation
            {
                Media = media,
                Score = media.Popularity,
                Reasons = new[] { "popularity" }
            }).ToList();

            var sortedFallback = ApplySort(fallbackRecommendations, sort);
            var finalFallback = sortedFallback.Take(topK).ToList();

            _logger?.LogInformation("Multi-media database fallback returned {Count} results", finalFallback.Count);
            return (finalFallback, true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Database fallback failed, returning demo data");

            // Final fallback to demo data
            var demoRecommendations = _demoMedia.Select(media => new Recommendation
            {
                Media = media,
                Score = media.Popularity,
                Reasons = new[] { "demo" }
            }).Take(topK).ToList();

            return (demoRecommendations, true);
        }
    }

    public async Task RateAsync(string userId, string mediaId, int rating, CancellationToken ct)
    {
        _logger?.LogInformation("Rating: user={UserId} media={MediaId} rating={Rating}", userId, mediaId, rating);

        var data = (IDataService?)_sp.GetService(typeof(IDataService));
        if (data is null) return;

        // Update or create library entry
        var entryId = LibraryEntry.MakeId(userId, mediaId);
        var entry = await LibraryEntry.Get(entryId, ct) ?? new LibraryEntry
        {
            Id = entryId,
            UserId = userId,
            MediaId = mediaId,
            AddedAt = DateTimeOffset.UtcNow
        };

        entry.Rating = Math.Max(1, Math.Min(5, rating)); // 1-5 scale
        if (entry.Status == MediaStatus.PlanToConsume)
        {
            entry.Status = MediaStatus.Completed; // Auto-mark as completed when rating
        }
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        await LibraryEntry.UpsertMany(new[] { entry }, ct);

        // Update user profile preferences
        await UpdateUserPreferences(userId, mediaId, rating, ct);
    }

    /// <summary>
    /// Blends two vectors with specified weight for the first vector.
    /// Result is normalized to preserve cosine similarity semantics.
    /// </summary>
    private static float[] BlendVectors(float[] vec1, float[] vec2, double weight1)
    {
        if (vec1.Length != vec2.Length)
            throw new ArgumentException($"Vector dimension mismatch: {vec1.Length} vs {vec2.Length}");

        var result = new float[vec1.Length];
        var weight2 = 1.0 - weight1;

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (float)((weight1 * vec1[i]) + (weight2 * vec2[i]));
        }

        // Normalize to unit length to preserve cosine similarity
        var magnitude = Math.Sqrt(result.Sum(x => (double)x * x));
        if (magnitude > 1e-8)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] /= (float)magnitude;
        }

        return result;
    }

    private async Task<(float[]? searchVector, float[]? userPrefVector)> BuildQueryVectors(
        string? text,
        string? anchorMediaId,
        string? userId,
        string[]? preferTags,
        CancellationToken ct)
    {
        // ADR-0051: Build separate vectors for search intent and user preferences
        float[]? searchVector = null;
        float[]? userPrefVector = null;

        try
        {
            // Build search intent vector
            if (!string.IsNullOrWhiteSpace(text))
            {
                searchVector = await Koan.AI.Ai.Embed(text!, ct);
            }
            else if (!string.IsNullOrWhiteSpace(anchorMediaId))
            {
                var anchorMedia = await Media.Get(anchorMediaId!, ct);
                if (anchorMedia != null)
                {
                    var textAnchor = $"{anchorMedia.Title}\n\n{anchorMedia.Synopsis}\nGenres: {string.Join(", ", anchorMedia.Genres ?? Array.Empty<string>())}";
                    searchVector = await Koan.AI.Ai.Embed(textAnchor, ct);
                }
            }
            else if (preferTags is { Length: > 0 })
            {
                var tagText = $"Tags: {string.Join(", ", preferTags.Where(t => !string.IsNullOrWhiteSpace(t)))}";
                searchVector = await Koan.AI.Ai.Embed(tagText, ct);
            }

            // Build user preference vector
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var profile = await UserProfileDoc.Get(userId!, ct);
                if (profile?.PrefVector is { Length: > 0 } pv)
                {
                    // Validate cached vector dimensions match current model expectations
                    const int expectedDimensions = 384; // all-minilm expected dimensions
                    if (pv.Length == expectedDimensions)
                    {
                        userPrefVector = pv;
                    }
                    else
                    {
                        // Invalidate the cached profile vector by clearing it
                        profile.PrefVector = null;
                        await profile.Save();
                    }
                }
            }

            return (searchVector, userPrefVector);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to generate embedding vectors");
            return (null, null);
        }
    }


    private async Task<List<Media>> ApplyFilters(
        IEnumerable<Media> media,
        string[]? genres,
        int? episodesMax,
        string? mediaTypeFilter,
        string? userId,
        double? ratingMin,
        double? ratingMax,
        int? yearMin,
        int? yearMax,
        CancellationToken ct)
    {
        var filtered = media.AsEnumerable();

        // Censor tags filter - load from config and database, then filter out media with censored tags
        var censoredTags = await GetCensoredTags(ct);
        if (censoredTags.Length > 0)
        {
            filtered = filtered.Where(m => !HasCensoredTags(m, censoredTags));
        }

        // Media type filter - now accepts media type ID directly
        if (!string.IsNullOrWhiteSpace(mediaTypeFilter))
        {
            filtered = filtered.Where(m => m.MediaTypeId == mediaTypeFilter);
        }

        // Genre filter
        if (genres is { Length: > 0 })
        {
            filtered = filtered.Where(m => (m.Genres ?? Array.Empty<string>()).Intersect(genres).Any());
        }

        // Episodes filter
        if (episodesMax is int emax)
        {
            filtered = filtered.Where(m => m.Episodes is null || m.Episodes <= emax);
        }

        // Rating filter (blended 80/20 score)
        if (ratingMin.HasValue || ratingMax.HasValue)
        {
            filtered = filtered.Where(m => {
                var rating = m.Rating; // Uses computed property: (AverageScore × 0.8) + Popularity
                if (!rating.HasValue) return false; // Exclude items without ratings

                if (ratingMin.HasValue && rating.Value < ratingMin.Value)
                    return false;
                if (ratingMax.HasValue && rating.Value > ratingMax.Value)
                    return false;
                return true;
            });
        }

        // Year filter
        if (yearMin.HasValue || yearMax.HasValue)
        {
            filtered = filtered.Where(m => {
                var year = m.Year; // Uses computed property: StartDate?.Year
                if (!year.HasValue) return false; // Exclude items without year

                if (yearMin.HasValue && year.Value < yearMin.Value)
                    return false;
                if (yearMax.HasValue && year.Value > yearMax.Value)
                    return false;
                return true;
            });
        }

        // Exclude items already in user's library
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var libraryEntries = (await LibraryEntry.All(ct)).Where(e => e.UserId == userId).ToList();
            var excludeIds = libraryEntries.Select(e => e.MediaId).ToHashSet();
            filtered = filtered.Where(m => !excludeIds.Contains(m.Id!));
        }

        return filtered.ToList();
    }

    private async Task<string[]> GetCensoredTags(CancellationToken ct)
    {
        // Load from both configuration and database (same logic as TagsController)
        var configTags = _tagOptions?.Value?.CensorTags ?? Array.Empty<string>();
        var doc = await CensorTagsDoc.Get("recs:censor-tags", ct);
        var dbTags = doc?.Tags?.ToArray() ?? Array.Empty<string>();

        // Merge and deduplicate with case-insensitive comparison
        var merged = configTags.Concat(dbTags)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return merged;
    }

    private static bool HasCensoredTags(Media media, string[] censoredTags)
    {
        // Check if media has any tags or genres that match the censored list (case-insensitive)
        var allMediaTags = (media.Genres ?? Array.Empty<string>())
            .Concat(media.Tags ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t));

        foreach (var mediaTag in allMediaTags)
        {
            foreach (var censoredTag in censoredTags)
            {
                if (mediaTag.Equals(censoredTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<List<Recommendation>> ScoreAndPersonalize(List<Media> media, Dictionary<string, double> vectorScores, string? userId, string[]? preferTags, double? preferWeight, bool spoilerSafe, CancellationToken ct)
    {
        UserProfileDoc? profile = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            profile = await UserProfileDoc.Get(userId, ct);
        }

        var preferTagsWeight = Math.Clamp(preferWeight ?? 0.2, 0, 1.0);
        var preferTagsSet = new HashSet<string>((preferTags ?? Array.Empty<string>()).Take(3), StringComparer.OrdinalIgnoreCase);

        return media.Select(m =>
        {
            var vectorScore = vectorScores.TryGetValue(m.Id!, out var vs) ? vs : 0.0;
            var popularityScore = m.Popularity;

            // Genre/tag personalization
            var genreBoost = 0.0;
            if (profile?.GenreWeights is { Count: > 0 })
            {
                foreach (var genre in m.Genres ?? Array.Empty<string>())
                {
                    if (profile.GenreWeights.TryGetValue(genre, out var weight))
                        genreBoost += weight;
                }
                if ((m.Genres?.Length ?? 0) > 0)
                    genreBoost /= m.Genres!.Length;

                foreach (var tag in m.Tags ?? Array.Empty<string>())
                {
                    if (profile.GenreWeights.TryGetValue(tag, out var weight))
                        genreBoost += (weight - 0.5);
                }
            }

            // Preferred tags boost
            var preferBoost = 0.0;
            if (preferTagsSet.Count > 0)
            {
                var allKeys = (m.Genres ?? Array.Empty<string>()).Concat(m.Tags ?? Array.Empty<string>()).ToList();
                if (allKeys.Count > 0)
                {
                    var hits = allKeys.Count(k => preferTagsSet.Contains(k));
                    if (hits > 0) preferBoost = (double)hits / allKeys.Count;
                }
            }

            // Spoiler penalty
            var spoilerPenalty = 0.0;
            if (spoilerSafe && m.Synopsis?.Contains("spoiler", StringComparison.OrdinalIgnoreCase) == true)
            {
                spoilerPenalty = 0.3;
            }

            // Combined score
            var hybridScore = (0.4 * vectorScore) + (0.3 * popularityScore) + (0.2 * genreBoost) + (preferBoost * preferTagsWeight);
            hybridScore *= (1.0 - spoilerPenalty);

            var reasons = new List<string> { "vector" };
            if (genreBoost > 0) reasons.Add("personalized");
            if (preferBoost > 0) reasons.Add("preferred-tags");
            if (popularityScore > 0.8) reasons.Add("popular");

            return new Recommendation
            {
                Media = m,
                Score = hybridScore,
                Reasons = reasons.ToArray()
            };
        }).ToList();
    }

    private static IEnumerable<Recommendation> ApplySort(IEnumerable<Recommendation> recommendations, string? sort)
    {
        return (sort ?? string.Empty).ToLowerInvariant() switch
        {
            "rating" => recommendations.OrderByDescending(r => r.Media?.AverageScore ?? 0),
            "popular" => recommendations.OrderByDescending(r => r.Media?.Popularity ?? 0),
            "relevance" or _ => recommendations.OrderByDescending(r => r.Score)
        };
    }

    private async Task UpdateUserPreferences(string userId, string mediaId, int rating, CancellationToken ct)
    {
        try
        {
            var media = await Media.Get(mediaId, ct);
            if (media is null) return;

            var profile = await UserProfileDoc.Get(userId, ct) ?? new UserProfileDoc
            {
                Id = userId,
                UserId = userId
            };

            const double alpha = 0.3; // Learning rate

            // Update genre/tag preferences
            foreach (var genre in media.Genres ?? Array.Empty<string>())
            {
                profile.GenreWeights.TryGetValue(genre, out var oldWeight);
                var target = (rating - 1) / 4.0; // Convert 1-5 to 0-1 scale
                var updated = (1 - alpha) * oldWeight + alpha * target;
                profile.GenreWeights[genre] = Math.Clamp(updated, 0, 1);
            }

            foreach (var tag in media.Tags ?? Array.Empty<string>())
            {
                profile.GenreWeights.TryGetValue(tag, out var oldWeight);
                var target = ((rating - 1) / 4.0) - 0.5; // Convert 1-5, center around 0.5
                var updated = (1 - alpha) * oldWeight + alpha * target;
                profile.GenreWeights[tag] = Math.Clamp(updated, 0, 1);
            }

            // Update preference vector using capability-first API (ADR-0014)
            try
            {
                var textBlend = $"{media.Title}\n\n{media.Synopsis}\nTags: {string.Join(", ", (media.Genres ?? Array.Empty<string>()).Concat(media.Tags ?? Array.Empty<string>()))}";
                var vec = await Koan.AI.Ai.Embed(textBlend, ct);

                if (vec is { Length: > 0 })
                {
                    if (profile.PrefVector is { Length: > 0 } pv && pv.Length == vec.Length)
                    {
                        for (int i = 0; i < pv.Length; i++)
                            pv[i] = (float)((1 - alpha) * pv[i] + alpha * vec[i]);
                        profile.PrefVector = pv;
                    }
                    else
                    {
                        profile.PrefVector = vec;
                    }
                }
            }
            catch (Exception embEx)
            {
                _logger?.LogWarning(embEx, "Failed to update preference vector for user {UserId}", userId);
            }

            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await UserProfileDoc.UpsertMany(new[] { profile }, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update user preferences for user {UserId}", userId);
        }
    }
}