using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using S5.Recs.Controllers;
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
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IRecommendationSettingsProvider _settingsProvider;

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

    public RecsService(IServiceProvider sp, IConfiguration configuration, IEmbeddingCache embeddingCache, IRecommendationSettingsProvider settingsProvider, ILogger<RecsService>? logger = null, IOptions<S5.Recs.Options.TagCatalogOptions>? tagOptions = null)
    {
        _sp = sp;
        _configuration = configuration;
        _logger = logger;
        _tagOptions = tagOptions;
        _embeddingCache = embeddingCache;
        _settingsProvider = settingsProvider;
    }

    public async Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(
        RecsQuery query,
        string? userIdOverride,
        CancellationToken ct)
    {
        // Determine effective userId (override from auth context takes precedence)
        var userId = userIdOverride ?? query.UserId;

        // Handle pagination: prefer Offset/Limit over legacy TopK
        var offset = query.Offset ?? 0;
        var limit = query.Limit ?? query.TopK;

        // Apply guardrails
        // Note: When both Offset and Limit are null, this is likely a band fetch from BandCacheService
        // In that case, allow larger limits (up to topK) for efficient band fetching
        var isBandFetch = query.Offset == null && query.Limit == null;
        if (offset < 0) offset = 0;
        if (limit <= 0) limit = 20;
        if (!isBandFetch && limit > 100) limit = 100;  // Only cap user-facing queries

        // For vector search, we need to fetch offset + limit results, then skip offset
        var topK = offset + limit;
        if (topK > 10000) topK = 10000; // Safety limit for band fetches

        _logger?.LogInformation("Multi-media query: text='{Text}' anchor='{Anchor}' mediaType='{MediaType}' offset={Offset} limit={Limit} (topK={TopK})",
            query.Text, query.AnchorMediaId, query.Filters?.MediaType, offset, limit, topK);

        try
        {
            // Use vector search if available (even for browse queries without text/anchor)
            if (Vector<Media>.IsAvailable)
            {
                float[]? queryVector = null;

                // Build query vectors if we have search intent or user preferences
                _logger?.LogDebug("Checking vector query conditions: hasText={HasText}, hasAnchor={HasAnchor}, hasUserId={HasUserId}, hasTags={HasTags}",
                    !string.IsNullOrWhiteSpace(query.Text),
                    !string.IsNullOrWhiteSpace(query.AnchorMediaId),
                    !string.IsNullOrWhiteSpace(userId),
                    query.Filters?.PreferTags is { Length: > 0 });

                if (!string.IsNullOrWhiteSpace(query.Text)
                    || !string.IsNullOrWhiteSpace(query.AnchorMediaId)
                    || !string.IsNullOrWhiteSpace(userId)
                    || (query.Filters?.PreferTags is { Length: > 0 }))
                {
                    _logger?.LogDebug("Building query vectors for userId={UserId}", userId);
                    // ADR-0051: Build separate search intent and user preference vectors
                    var (searchVector, userPrefVector) = await BuildQueryVectors(
                        query.Text,
                        query.AnchorMediaId,
                        userId,
                        query.Filters?.PreferTags,
                        ct);

                    _logger?.LogDebug("BuildQueryVectors result: searchVector={SearchLen}, userPrefVector={UserLen}",
                        searchVector?.Length ?? 0, userPrefVector?.Length ?? 0);

                    // Determine query vector via blending (66% search intent, 34% user preferences)
                    const double SEARCH_INTENT_WEIGHT = 0.66;

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
                }

                // Only use vector search if we have a meaningful query vector
                if (queryVector != null && queryVector.Length > 0)
                {
                    // ADR-0051: Use hybrid search with unified API
                    // Alpha controls semantic (1.0) vs keyword (0.0) balance, defaults to 0.5 if not provided
                    var effectiveAlpha = !string.IsNullOrWhiteSpace(query.Text) ? (query.Alpha ?? 0.5) : (double?)null;

                    // Build filter for push-down to vector database layer
                    var vectorFilter = await BuildVectorFilter(query, ct);

                    _logger?.LogInformation("Vector search: text={HasText}, alpha={Alpha}, topK={TopK}, hasFilters={HasFilters}",
                        !string.IsNullOrWhiteSpace(query.Text), effectiveAlpha, topK, vectorFilter != null);

                    var vectorResults = await Vector<Media>.Search(
                        vector: queryVector,
                        text: query.Text,  // Enables hybrid search if provided
                        alpha: effectiveAlpha,
                        topK: topK,
                        filter: vectorFilter,  // Filter push-down at vector DB layer
                        ct: ct
                    );

                    _logger?.LogDebug("Weaviate returned {Count} vector results before post-filtering", vectorResults.Matches.Count);

                    var idToScore = vectorResults.Matches.ToDictionary(m => m.Id, m => m.Score);

                    var mediaItems = new List<Media>();
                    foreach (var id in idToScore.Keys)
                    {
                        var media = await Media.Get(id, ct);
                        if (media != null) mediaItems.Add(media);
                    }

                    // Apply only library exclusion post-filter (requires DB query)
                    var filteredMedia = await ApplyLibraryExclusion(mediaItems, userId, ct);
                    _logger?.LogDebug("After library exclusion: {Count} items remain", filteredMedia.Count);

                    // Apply personalization and scoring
                    var recommendations = await ScoreAndPersonalize(
                        filteredMedia,
                        idToScore,
                        userId,
                        query.Text,
                        query.Filters?.PreferTags,
                        query.Filters?.PreferWeight,
                        query.Filters?.SpoilerSafe ?? true,
                        query.Filters?.ShowCensored ?? false,
                        ct);

                    // Apply sorting and apply offset/limit pagination
                    var sortedRecommendations = ApplySort(recommendations, query.Sort);
                    var finalResults = sortedRecommendations.Skip(offset).Take(limit).ToList();

                    var searchMode = !string.IsNullOrWhiteSpace(query.Text) ? "hybrid" : "vector";
                    _logger?.LogInformation("Multi-media {SearchMode} query returned {Count} results (offset={Offset}, limit={Limit}, total={Total})", searchMode, finalResults.Count, offset, limit, sortedRecommendations.Count());
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
            var filteredMedia = await ApplyFiltersInMemory(allMedia, query, userId, ct);

            // Apply censor penalty in fallback too (for consistency)
            var censoredTags = (query.Filters?.ShowCensored ?? false) ? Array.Empty<string>() : await GetCensoredTags(ct);
            var (_, _, _, censoredTagsPenaltyWeight) = _settingsProvider.GetEffective();
            var censorPenaltyMultiplier = 1.0 + censoredTagsPenaltyWeight; // e.g., 1 + (-0.7) = 0.3

            var fallbackRecommendations = filteredMedia.Select(media =>
            {
                var score = media.Popularity;
                var reasons = new List<string> { "popularity" };

                // Title matching boost (same as vector path)
                var titleBoost = CalculateTitleMatchBoost(media, query.Text);
                score += titleBoost;

                // Apply censor penalty if content has censored tags
                if (censoredTags.Length > 0 && HasCensoredTags(media, censoredTags))
                {
                    score *= censorPenaltyMultiplier;
                    reasons.Add($"censored_penalty:{censorPenaltyMultiplier:F2}");
                }

                if (titleBoost >= 2.0) reasons.Add("exact-title-match");
                else if (titleBoost >= 1.0) reasons.Add("title-contains-match");
                else if (titleBoost > 0) reasons.Add("fuzzy-title-match");

                return new Recommendation
                {
                    Media = media,
                    Score = score,
                    Reasons = reasons.ToArray()
                };
            }).ToList();

            var sortedFallback = ApplySort(fallbackRecommendations, query.Sort);
            var finalFallback = sortedFallback.Skip(offset).Take(limit).ToList();

            _logger?.LogInformation("Multi-media database fallback returned {Count} results (offset={Offset}, limit={Limit}, total={Total})", finalFallback.Count, offset, limit, sortedFallback.Count());
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
            }).Skip(offset).Take(limit).ToList();

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

            // Use cached user preference vector from profile
            if (!string.IsNullOrWhiteSpace(userId))
            {
                _logger?.LogDebug("Looking up UserProfileDoc for userId={UserId}", userId);
                var profile = await UserProfileDoc.Get(userId!, ct);
                _logger?.LogDebug("UserProfileDoc lookup: found={Found}, prefVectorLen={Len}",
                    profile != null, profile?.PrefVector?.Length ?? 0);

                // Use cached PrefVector if valid
                if (profile?.PrefVector is { Length: > 0 } pv)
                {
                    // If we have a searchVector, validate dimensions match
                    // Otherwise, accept any dimension (will be validated by vector provider)
                    if (searchVector != null)
                    {
                        _logger?.LogDebug("Validating PrefVector: length={PrefLen}, searchVector length={SearchLen}",
                            pv.Length, searchVector.Length);
                        if (pv.Length == searchVector.Length)
                        {
                            userPrefVector = pv;
                            _logger?.LogDebug("Using cached PrefVector for user {UserId}", userId);
                        }
                        else
                        {
                            _logger?.LogWarning("PrefVector dimension mismatch: got {Got}, searchVector has {Expected}",
                                pv.Length, searchVector.Length);
                        }
                    }
                    else
                    {
                        // No searchVector to compare against, use PrefVector as-is
                        userPrefVector = pv;
                        _logger?.LogDebug("Using cached PrefVector for user {UserId} (no searchVector for validation)", userId);
                    }
                }
                else
                {
                    _logger?.LogDebug("No valid PrefVector found for user {UserId}", userId);
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

    /// <summary>
    /// Rebuilds and caches the user preference vector from their library.
    /// Should be called whenever the user's library changes (add/remove/rate).
    /// Uses item vectors weighted by ratings (or default 0.8 for unrated items).
    /// </summary>
    public async Task RebuildUserPrefVectorAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            // Get user's library
            var libraryEntries = (await LibraryEntry.All(ct))
                .Where(e => e.UserId == userId)
                .ToList();

            if (libraryEntries.Count == 0)
            {
                _logger?.LogDebug("User {UserId} has no library items, skipping PrefVector generation", userId);
                return;
            }

            _logger?.LogDebug("Building PrefVector from {Count} library items for user {UserId}", libraryEntries.Count, userId);

            // Build weighted sum of item vectors
            float[]? prefVector = null;
            var totalWeight = 0.0;
            var successCount = 0;

            foreach (var entry in libraryEntries)
            {
                try
                {
                    var media = await Media.Get(entry.MediaId, ct);
                    if (media == null) continue;

                    // Build embedding text (MUST match SeedService.BuildEmbeddingText exactly!)
                    var titles = new List<string>();
                    if (!string.IsNullOrWhiteSpace(media.Title)) titles.Add(media.Title);
                    if (!string.IsNullOrWhiteSpace(media.TitleEnglish) && media.TitleEnglish != media.Title) titles.Add(media.TitleEnglish!);
                    if (!string.IsNullOrWhiteSpace(media.TitleRomaji) && media.TitleRomaji != media.Title) titles.Add(media.TitleRomaji!);
                    if (!string.IsNullOrWhiteSpace(media.TitleNative) && media.TitleNative != media.Title) titles.Add(media.TitleNative!);
                    if (media.Synonyms is { Length: > 0 }) titles.AddRange(media.Synonyms);

                    var tags = new List<string>();
                    if (media.Genres is { Length: > 0 }) tags.AddRange(media.Genres);
                    if (media.Tags is { Length: > 0 }) tags.AddRange(media.Tags);

                    var textBlend = $"{string.Join(" / ", titles.Distinct())}\n\n{media.Synopsis}\n\nTags: {string.Join(", ", tags.Distinct())}".Trim();
                    var contentHash = EmbeddingCache.ComputeContentHash(textBlend);

                    // Try to get cached embedding (use "default" to match SeedService)
                    var modelId = "default";
                    var cached = await _embeddingCache.GetAsync(contentHash, modelId, typeof(Media).FullName!, ct);

                    if (cached == null || cached.Embedding == null || cached.Embedding.Length == 0)
                    {
                        _logger?.LogDebug("Skipping library item {MediaId} - no cached embedding found", entry.MediaId);
                        continue;
                    }

                    var vec = cached.Embedding;

                    // Determine weight: rating if exists, else default 0.8 (assume "liked")
                    var weight = entry.Rating.HasValue
                        ? entry.Rating.Value / 5.0  // Normalize 1-5 to 0.2-1.0
                        : 0.8;  // Default: unrated = assumed 4/5 stars

                    // Accumulate weighted vector
                    if (prefVector == null)
                    {
                        prefVector = new float[vec.Length];
                    }

                    for (int i = 0; i < vec.Length; i++)
                    {
                        prefVector[i] += (float)(vec[i] * weight);
                    }

                    totalWeight += weight;
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to process library item {MediaId} for PrefVector", entry.MediaId);
                }
            }

            if (prefVector == null || successCount == 0)
            {
                _logger?.LogDebug("No vectorized items found in user {UserId} library", userId);

                // Clear PrefVector if library has no vectorized items
                var profile = await UserProfileDoc.Get(userId, ct);
                if (profile != null)
                {
                    profile.PrefVector = null;
                    await profile.Save(ct);
                }
                return;
            }

            // Normalize by total weight (weighted average)
            for (int i = 0; i < prefVector.Length; i++)
            {
                prefVector[i] /= (float)totalWeight;
            }

            // Normalize to unit vector for cosine similarity
            var magnitude = Math.Sqrt(prefVector.Sum(x => (double)x * x));
            if (magnitude > 1e-8)
            {
                for (int i = 0; i < prefVector.Length; i++)
                    prefVector[i] /= (float)magnitude;
            }

            _logger?.LogInformation("Built PrefVector for user {UserId} from {SuccessCount}/{TotalCount} library items (total weight: {Weight:F2})",
                userId, successCount, libraryEntries.Count, totalWeight);

            // Save to user profile
            var userProfile = await UserProfileDoc.Get(userId, ct);
            if (userProfile == null)
            {
                userProfile = new UserProfileDoc
                {
                    Id = userId,
                    UserId = userId,
                    PrefVector = prefVector,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _logger?.LogDebug("Creating new UserProfileDoc for user {UserId}", userId);
            }
            else
            {
                userProfile.PrefVector = prefVector;
                userProfile.UpdatedAt = DateTimeOffset.UtcNow;
                _logger?.LogDebug("Updating existing UserProfileDoc for user {UserId}", userId);
            }

            await userProfile.Save(ct);
            _logger?.LogInformation("Cached PrefVector for user {UserId} - saved successfully", userId);

            // Verify save succeeded
            var verification = await UserProfileDoc.Get(userId, ct);
            if (verification == null)
            {
                _logger?.LogError("VERIFICATION FAILED: UserProfileDoc was not saved for user {UserId}", userId);
            }
            else
            {
                _logger?.LogDebug("Verification successful: UserProfileDoc exists with {VectorLength} dimensions", verification.PrefVector?.Length ?? 0);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to rebuild PrefVector for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Builds VectorFilter for push-down filtering at the vector database layer.
    /// Handles all metadata-based filters except user library exclusion.
    /// </summary>
    private async Task<Koan.Data.Abstractions.Vector.Filtering.VectorFilter?> BuildVectorFilter(
        RecsQuery query,
        CancellationToken ct)
    {
        var filters = new List<Koan.Data.Abstractions.Vector.Filtering.VectorFilter>();

        // MediaType filter
        if (!string.IsNullOrWhiteSpace(query.Filters?.MediaType))
        {
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Eq("mediaTypeId", query.Filters.MediaType));
        }

        // Genre filter (must contain at least one of the specified genres)
        if (query.Filters?.Genres is { Length: > 0 })
        {
            var genreFilters = query.Filters.Genres
                .Select(g => Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Contains("genres", g))
                .ToArray();
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Or(genreFilters));
        }

        // Episodes filter (≤ max)
        if (query.Filters?.EpisodesMax is int emax)
        {
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Lte("episodes", emax));
        }

        // Rating filter (range) - NOTE: Use RatingValue which matches the computed Rating property
        if (query.Filters?.RatingMin.HasValue == true)
        {
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Gte("ratingValue", query.Filters.RatingMin.Value));
        }
        if (query.Filters?.RatingMax.HasValue == true)
        {
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Lte("ratingValue", query.Filters.RatingMax.Value));
        }

        // Year filter (range) - NOTE: Year is computed property, filter on StartDateInt (numeric YYYYMMDD)
        // Using numeric format for efficient integer comparison: 20230101, 20231231, etc.
        if (query.Filters?.YearMin.HasValue == true)
        {
            var startDateMin = query.Filters.YearMin.Value * 10000 + 101; // YYYY0101
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Gte("startDateInt", startDateMin));
        }
        if (query.Filters?.YearMax.HasValue == true)
        {
            var startDateMax = query.Filters.YearMax.Value * 10000 + 1231; // YYYY1231
            filters.Add(Koan.Data.Abstractions.Vector.Filtering.VectorFilter.Lte("startDateInt", startDateMax));
        }

        // NOTE: Censored tag filtering moved to scoring phase (soft penalty instead of hard filter)
        // This allows semantic similarity to work while still de-prioritizing censored content

        // Combine all filters with AND
        if (filters.Count == 0)
            return null;
        if (filters.Count == 1)
            return filters[0];
        return Koan.Data.Abstractions.Vector.Filtering.VectorFilter.And(filters.ToArray());
    }

    /// <summary>
    /// Applies library exclusion post-filter (excludes items already in user's library).
    /// This is the only filter applied post-vector-search since it requires a DB query.
    /// </summary>
    private async Task<List<Media>> ApplyLibraryExclusion(
        IEnumerable<Media> media,
        string? userId,
        CancellationToken ct)
    {
        // Exclude items already in user's library
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var libraryEntries = (await LibraryEntry.All(ct)).Where(e => e.UserId == userId).ToList();
            var excludeIds = libraryEntries.Select(e => e.MediaId).ToHashSet();
            return media.Where(m => !excludeIds.Contains(m.Id!)).ToList();
        }

        return media.ToList();
    }

    /// <summary>
    /// Applies all filters in-memory for fallback scenarios when vector search is not available.
    /// </summary>
    private async Task<List<Media>> ApplyFiltersInMemory(
        IEnumerable<Media> media,
        RecsQuery query,
        string? userId,
        CancellationToken ct)
    {
        var filtered = media.AsEnumerable();

        // NOTE: Censored tag filtering removed - now handled as soft penalty in scoring phase
        // This allows fallback to work similarly to vector search (soft penalty vs hard filter)

        // Media type filter
        if (!string.IsNullOrWhiteSpace(query.Filters?.MediaType))
        {
            filtered = filtered.Where(m => m.MediaTypeId == query.Filters.MediaType);
        }

        // Genre filter
        if (query.Filters?.Genres is { Length: > 0 })
        {
            filtered = filtered.Where(m => (m.Genres ?? Array.Empty<string>()).Intersect(query.Filters.Genres).Any());
        }

        // Episodes filter
        if (query.Filters?.EpisodesMax is int emax)
        {
            filtered = filtered.Where(m => m.Episodes is null || m.Episodes <= emax);
        }

        // Rating filter
        if (query.Filters?.RatingMin.HasValue == true || query.Filters?.RatingMax.HasValue == true)
        {
            filtered = filtered.Where(m => {
                var rating = m.Rating;
                if (!rating.HasValue) return false;

                if (query.Filters?.RatingMin.HasValue == true && rating.Value < query.Filters.RatingMin.Value)
                    return false;
                if (query.Filters?.RatingMax.HasValue == true && rating.Value > query.Filters.RatingMax.Value)
                    return false;
                return true;
            });
        }

        // Year filter
        if (query.Filters?.YearMin.HasValue == true || query.Filters?.YearMax.HasValue == true)
        {
            filtered = filtered.Where(m => {
                var year = m.Year;
                if (!year.HasValue) return false;

                if (query.Filters?.YearMin.HasValue == true && year.Value < query.Filters.YearMin.Value)
                    return false;
                if (query.Filters?.YearMax.HasValue == true && year.Value > query.Filters.YearMax.Value)
                    return false;
                return true;
            });
        }

        // Library exclusion
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

    private async Task<List<Recommendation>> ScoreAndPersonalize(List<Media> media, Dictionary<string, double> vectorScores, string? userId, string? text, string[]? preferTags, double? preferWeight, bool spoilerSafe, bool showCensored, CancellationToken ct)
    {
        UserProfileDoc? profile = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            profile = await UserProfileDoc.Get(userId, ct);
        }

        // Load censored tags for soft penalty (only if showCensored is false)
        var censoredTags = showCensored ? Array.Empty<string>() : await GetCensoredTags(ct);
        var (_, _, _, censoredTagsPenaltyWeight) = _settingsProvider.GetEffective();
        var censorPenaltyMultiplier = 1.0 + censoredTagsPenaltyWeight; // e.g., 1 + (-0.7) = 0.3 (70% reduction)

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

            // Censor penalty (soft ranking down instead of hard filter)
            var censorPenaltyFactor = 1.0;
            if (censoredTags.Length > 0 && HasCensoredTags(m, censoredTags))
            {
                censorPenaltyFactor = censorPenaltyMultiplier;
            }

            // Spoiler penalty
            var spoilerPenalty = 0.0;
            if (spoilerSafe && m.Synopsis?.Contains("spoiler", StringComparison.OrdinalIgnoreCase) == true)
            {
                spoilerPenalty = 0.3;
            }

            // Title matching boost (exact → contains → fuzzy)
            var titleBoost = CalculateTitleMatchBoost(m, text);

            // Combined score
            var hybridScore = (0.4 * vectorScore) + (0.3 * popularityScore) + (0.2 * genreBoost) + (preferBoost * preferTagsWeight) + titleBoost;
            hybridScore *= (1.0 - spoilerPenalty);
            hybridScore *= censorPenaltyFactor; // Apply censor penalty

            var reasons = new List<string> { "vector" };
            if (genreBoost > 0) reasons.Add("personalized");
            if (preferBoost > 0) reasons.Add("preferred-tags");
            if (popularityScore > 0.8) reasons.Add("popular");
            if (titleBoost >= 2.0) reasons.Add("exact-title-match");
            else if (titleBoost >= 1.0) reasons.Add("title-contains-match");
            else if (titleBoost > 0) reasons.Add("fuzzy-title-match");
            if (censorPenaltyFactor < 1.0) reasons.Add($"censored_penalty:{censorPenaltyMultiplier:F2}");

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

    /// <summary>
    /// Calculates title matching boost based on exact, contains, or fuzzy similarity.
    /// Checks all title variants: Title, TitleEnglish, TitleRomaji, TitleNative, Synonyms.
    /// Returns: 2.0 (exact), 1.0 (contains), 0.5 (fuzzy with distance ≤ 3), or 0.0 (no match)
    /// </summary>
    private static double CalculateTitleMatchBoost(Media media, string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText)) return 0.0;

        var normalized = queryText.Trim().ToLowerInvariant();

        // Collect all title variants
        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.Title)) titles.Add(media.Title);
        if (!string.IsNullOrWhiteSpace(media.TitleEnglish)) titles.Add(media.TitleEnglish);
        if (!string.IsNullOrWhiteSpace(media.TitleRomaji)) titles.Add(media.TitleRomaji);
        if (!string.IsNullOrWhiteSpace(media.TitleNative)) titles.Add(media.TitleNative);
        if (media.Synonyms != null) titles.AddRange(media.Synonyms);

        var maxBoost = 0.0;

        foreach (var title in titles)
        {
            if (string.IsNullOrWhiteSpace(title)) continue;

            var titleLower = title.ToLowerInvariant();

            // Exact match → massive boost
            if (titleLower == normalized)
            {
                return 2.0;
            }

            // Contains match → strong boost
            if (titleLower.Contains(normalized, StringComparison.Ordinal))
            {
                maxBoost = Math.Max(maxBoost, 1.0);
                continue;
            }

            // Fuzzy match (Levenshtein distance ≤ 3) → moderate boost
            var distance = LevenshteinDistance(titleLower, normalized);
            if (distance <= 3)
            {
                maxBoost = Math.Max(maxBoost, 0.5);
            }
        }

        return maxBoost;
    }

    /// <summary>
    /// Calculates Levenshtein (edit) distance between two strings.
    /// Used for fuzzy title matching.
    /// </summary>
    private static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (var i = 0; i <= s.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++)
            d[0, j] = j;

        for (var j = 1; j <= t.Length; j++)
        {
            for (var i = 1; i <= s.Length; i++)
            {
                var cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
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