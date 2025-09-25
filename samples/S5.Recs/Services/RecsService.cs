using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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

    public RecsService(IServiceProvider sp, IConfiguration configuration, ILogger<RecsService>? logger = null)
    {
        _sp = sp;
        _configuration = configuration;
        _logger = logger;
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
                float[] query = await BuildQueryVector(text, anchorMediaId, userId, preferTags, ct);

                if (query.Length > 0 && Vector<Media>.IsAvailable)
                {
                    var vectorResults = await Vector<Media>.Search(new VectorQueryOptions(query, TopK: topK), ct);
                    var idToScore = vectorResults.Matches.ToDictionary(m => m.Id, m => m.Score);

                    var mediaItems = new List<Media>();
                    foreach (var id in idToScore.Keys)
                    {
                        var media = await Media.Get(id, ct);
                        if (media != null) mediaItems.Add(media);
                    }

                    // Apply filters
                    var filteredMedia = await ApplyFilters(mediaItems, genres, episodesMax, mediaTypeFilter, userId, ct);

                    // Apply personalization and scoring
                    var recommendations = await ScoreAndPersonalize(filteredMedia, idToScore, userId, preferTags, preferWeight, spoilerSafe, ct);

                    // Apply sorting
                    var sortedRecommendations = ApplySort(recommendations, sort);
                    var finalResults = sortedRecommendations.Take(topK).ToList();

                    _logger?.LogInformation("Multi-media vector query returned {Count} results", finalResults.Count);
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
            var filteredMedia = await ApplyFilters(allMedia, genres, episodesMax, mediaTypeFilter, userId, ct);

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

    private async Task<float[]> BuildQueryVector(string? text, string? anchorMediaId, string? userId, string[]? preferTags, CancellationToken ct)
    {
        var ai = Koan.AI.Ai.TryResolve();
        if (ai is null) return Array.Empty<float>();

        var model = GetConfiguredModel();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var request = new AiEmbeddingsRequest { Input = new() { text! }, Model = model };
            var emb = await ai.EmbedAsync(request, ct);
            var vector = emb.Vectors.FirstOrDefault() ?? Array.Empty<float>();
            return vector;
        }

        if (!string.IsNullOrWhiteSpace(anchorMediaId))
        {
            var anchorMedia = await Media.Get(anchorMediaId!, ct);
            if (anchorMedia != null)
            {
                var textAnchor = $"{anchorMedia.Title}\n\n{anchorMedia.Synopsis}\nGenres: {string.Join(", ", anchorMedia.Genres ?? Array.Empty<string>())}";
                var request = new AiEmbeddingsRequest { Input = new() { textAnchor }, Model = model };
                var emb = await ai.EmbedAsync(request, ct);
                var vector = emb.Vectors.FirstOrDefault() ?? Array.Empty<float>();
                return vector;
            }
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var profile = await UserProfileDoc.Get(userId!, ct);
            if (profile?.PrefVector is { Length: > 0 } pv)
            {
                // Validate cached vector dimensions match current model expectations
                // If we're using all-minilm (384 dims) but cached vector is llama2 (4096 dims), invalidate cache
                const int expectedDimensions = 384; // all-minilm expected dimensions
                if (pv.Length == expectedDimensions)
                {
                    return pv;
                }
                else
                {

                    // Invalidate the cached profile vector by clearing it
                    profile.PrefVector = null;
                    await profile.Save();

                    // Continue to regenerate the vector below instead of using cached one
                }
            }
        }

        if (preferTags is { Length: > 0 })
        {
            var tagText = $"Tags: {string.Join(", ", preferTags.Where(t => !string.IsNullOrWhiteSpace(t)))}";
            var request = new AiEmbeddingsRequest { Input = new() { tagText }, Model = model };
            var emb = await ai.EmbedAsync(request, ct);
            var vector = emb.Vectors.FirstOrDefault() ?? Array.Empty<float>();
            return vector;
        }

        return Array.Empty<float>();
    }

    private string GetConfiguredModel()
    {
        try
        {
            // Use Koan.Core Configuration helpers to read from multiple possible locations
            var result = Configuration.ReadFirst(_configuration, "all-minilm",
                "Koan:Services:ollama:DefaultModel",
                "Koan:Ai:Ollama:DefaultModel",
                "Koan:Ai:Ollama:RequiredModels:0"  // First element of RequiredModels array
            );

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in GetConfiguredModel()");
            return "all-minilm";
        }
    }

    private async Task<List<Media>> ApplyFilters(IEnumerable<Media> media, string[]? genres, int? episodesMax, string? mediaTypeFilter, string? userId, CancellationToken ct)
    {
        var filtered = media.AsEnumerable();

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

        // Exclude items already in user's library
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var libraryEntries = (await LibraryEntry.All(ct)).Where(e => e.UserId == userId).ToList();
            var excludeIds = libraryEntries.Select(e => e.MediaId).ToHashSet();
            filtered = filtered.Where(m => !excludeIds.Contains(m.Id!));
        }

        return filtered.ToList();
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

            // Update preference vector if AI is available
            var ai = Koan.AI.Ai.TryResolve();
            if (ai != null)
            {
                var textBlend = $"{media.Title}\n\n{media.Synopsis}\nTags: {string.Join(", ", (media.Genres ?? Array.Empty<string>()).Concat(media.Tags ?? Array.Empty<string>()))}";
                var emb = await ai.EmbedAsync(new AiEmbeddingsRequest { Input = new() { textBlend } }, ct);
                var vec = emb.Vectors.FirstOrDefault();

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

            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await UserProfileDoc.UpsertMany(new[] { profile }, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update user preferences for user {UserId}", userId);
        }
    }
}