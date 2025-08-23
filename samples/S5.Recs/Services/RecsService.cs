using System;
using System.Linq;
using System.Collections.Generic;
using S5.Recs.Models;
using Sora.Data.Core;
using Sora.Data.Abstractions;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Models;
using S5.Recs.Infrastructure;
using Microsoft.Extensions.Logging;
using Sora.Data.Vector.Abstractions;
using Sora.Data.Vector;

namespace S5.Recs.Services;

internal sealed class RecsService : IRecsService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<RecsService>? _logger;

    private readonly List<Anime> _demo = new()
    {
        new Anime { Id = "local:haikyuu", Title = "Haikyuu!!", Genres = new[]{"Sports","School Life"}, Episodes = 25, Synopsis = "A high-energy volleyball ensemble.", Popularity = 0.95 },
        new Anime { Id = "local:kon", Title = "K-On!", Genres = new[]{"Slice of Life","Music"}, Episodes = 12, Synopsis = "Cozy after-school band hijinks.", Popularity = 0.88 },
        new Anime { Id = "local:yuru", Title = "Laid-Back Camp", Genres = new[]{"Slice of Life"}, Episodes = 12, Synopsis = "Wholesome camping and friendship.", Popularity = 0.90 }
    };

    public RecsService(IServiceProvider sp, ILogger<RecsService>? logger = null)
    {
        _sp = sp; _logger = logger;
    }

    public async Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(
        string? text,
        string? anchorAnimeId,
        string[]? genres,
        int? episodesMax,
        bool spoilerSafe,
        int topK,
        string? userId,
        string[]? preferTags,
        double? preferWeight,
        CancellationToken ct)
    {
        // Guardrails: sensible defaults and caps for K
        if (topK <= 0) topK = 10; // avoid empty results when a caller sends 0
        if (topK > 50) topK = 50; // keep requests light for the sample
        // Try vector-first if text or anchor provided
        _logger?.LogInformation("Query: text='{Text}' anchor='{Anchor}' genres=[{Genres}] episodesMax={EpisodesMax} spoilerSafe={SpoilerSafe} topK={TopK} user={UserId}",
            text, anchorAnimeId, genres is null ? string.Empty : string.Join(',', genres), episodesMax, spoilerSafe, topK, userId);
        try
        {
            if (!string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(anchorAnimeId) || !string.IsNullOrWhiteSpace(userId))
            {
                float[] query;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Derive simple embedding using AI core for the query; avoid blocking on seed
                    var ai = Sora.AI.Ai.TryResolve();
                    _logger?.LogDebug("AI resolve: {Resolved}", ai is null ? "null" : ai.GetType().Name);
                    if (ai is not null)
                    {
                        _logger?.LogDebug("Embedding query text len={Len}", text!.Length);
                        var emb = await ai.EmbedAsync(new AiEmbeddingsRequest { Input = new() { text! } }, ct);
                        query = emb.Vectors.FirstOrDefault() ?? Array.Empty<float>();
                        _logger?.LogDebug("Embedding result: dim={Dim}", query.Length);
                    }
                    else query = Array.Empty<float>();
                }
                else if (!string.IsNullOrWhiteSpace(anchorAnimeId))
                {
                    // For now, approximate by embedding the anchor's synopsis+title
                    var anchorDoc = await AnimeDoc.Get(anchorAnimeId!, ct);
                    AnimeDoc? anchor = anchorDoc;
                    if (anchor is null)
                    {
                        Anime? aDemo = _demo.FirstOrDefault(d => d.Id == anchorAnimeId);
                        if (aDemo is not null)
                            anchor = new AnimeDoc { Id = aDemo.Id, Title = aDemo.Title, Genres = aDemo.Genres, Episodes = aDemo.Episodes, Synopsis = aDemo.Synopsis, Popularity = aDemo.Popularity };
                    }
                    var textAnchor = anchor is null ? null : $"{anchor.Title}\n\n{anchor.Synopsis}\nTags: {string.Join(", ", anchor.Genres ?? Array.Empty<string>())}";
                    var ai = Sora.AI.Ai.TryResolve();
                    AiEmbeddingsResponse? emb = textAnchor is not null && ai is not null
                        ? await ai.EmbedAsync(new AiEmbeddingsRequest { Input = new() { textAnchor } }, ct)
                        : null;
                    _logger?.LogDebug("Anchor embedding: text?={HasText} dim={Dim}", textAnchor is not null, emb?.Vectors.FirstOrDefault()?.Length ?? 0);
                    query = emb?.Vectors.FirstOrDefault() ?? Array.Empty<float>();
                }
                else
                {
                    var prof = !string.IsNullOrWhiteSpace(userId) ? await UserProfileDoc.Get(userId!, ct) : null;
                    query = prof?.PrefVector ?? Array.Empty<float>();
                }

                if (query.Length > 0 && Vector<AnimeDoc>.IsAvailable)
                {
                    // If user profile vector exists, blend with query vector for personalization
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        var prof = await UserProfileDoc.Get(userId, ct);
                        if (prof?.PrefVector is { Length: > 0 } pv && pv.Length == query.Length)
                        {
                            _logger?.LogDebug("Query: blending profile vector for user {UserId}", userId);
                            var blended = new float[query.Length];
                            for (int i = 0; i < query.Length; i++)
                            {
                                blended[i] = (float)(Constants.Scoring.ProfileBlend * query[i] + (1 - Constants.Scoring.ProfileBlend) * pv[i]);
                            }
                            query = blended;
                        }
                    }
                    var res = await Vector<AnimeDoc>.Search(new VectorQueryOptions(query, TopK: topK), ct);
                    var idToScore = res.Matches.ToDictionary(m => m.Id, m => m.Score);
                    var docs = new List<AnimeDoc>();
                    foreach (var id in idToScore.Keys)
                    {
                        var d = await AnimeDoc.Get(id, ct);
                        if (d is not null) docs.Add(d);
                    }
                    IEnumerable<AnimeDoc> q = docs;
                    if (genres is { Length: > 0 }) q = q.Where(a => (a.Genres ?? Array.Empty<string>()).Intersect(genres).Any());
                    if (episodesMax is int emax) q = q.Where(a => a.Episodes is null || a.Episodes <= emax);
                    // Personalization: fetch profile if present
                    UserProfileDoc? profile = null;
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        profile = await UserProfileDoc.Get(userId, ct);
                        // Exclude any items already present in the user's Library (watched/favorite/dropped/rated)
                        var entries = (await LibraryEntryDoc.All(ct)).Where(e => e.UserId == userId).ToList();
                        var exclude = entries.Select(e => e.AnimeId).ToHashSet();
                        q = q.Where(a => !exclude.Contains(a.Id));
                    }
                    // Load effective settings
                    var eff = (_sp.GetService(typeof(IRecommendationSettingsProvider)) as IRecommendationSettingsProvider)?.GetEffective()
                              ?? (Infrastructure.Constants.Scoring.PreferTagsWeightDefault, Infrastructure.Constants.Scoring.MaxPreferredTagsDefault, Infrastructure.Constants.Scoring.DiversityWeightDefault);
                    var preferTagsWeightEff = Math.Clamp(preferWeight ?? eff.PreferTagsWeight, 0, 1.0);
                    var preferTagsSet = new HashSet<string>((preferTags ?? Array.Empty<string>()).Take(Math.Max(1, eff.MaxPreferredTags)), StringComparer.OrdinalIgnoreCase);

                    var mapped = q.Select(a =>
                    {
                        var vs = idToScore.TryGetValue(a.Id, out var s) ? s : 0d;
                        var pop = a.Popularity;
                        var genreBoost = 0d;
                        if (profile?.GenreWeights is { Count: > 0 } && a.Genres is { Length: > 0 })
                        {
                            foreach (var g in a.Genres)
                            {
                                if (profile.GenreWeights.TryGetValue(g, out var w)) genreBoost += w;
                            }
                            genreBoost /= Math.Max(1, a.Genres.Length);
                        }
                        // Include tags into preference (centered)
                        if (profile?.GenreWeights is { Count: > 0 } && a.Tags is { Length: > 0 })
                        {
                            var add = 0d; int cnt = 0;
                            foreach (var t in a.Tags)
                            {
                                if (profile.GenreWeights.TryGetValue(t, out var w)) { add += (w - 0.5); cnt++; }
                            }
                            if (cnt > 0) genreBoost += add / cnt;
                        }
                        // Prefer-tags soft boost
                        double preferBoost = 0d;
                        if (preferTagsSet.Count > 0)
                        {
                            var keys = new List<string>();
                            if (a.Genres is { Length: > 0 }) keys.AddRange(a.Genres);
                            if (a.Tags is { Length: > 0 }) keys.AddRange(a.Tags);
                            if (keys.Count > 0)
                            {
                                var hits = keys.Count(k => preferTagsSet.Contains(k));
                                if (hits > 0) preferBoost = (double)hits / keys.Count; // 0..1
                            }
                        }
                        var hasSpoiler = spoilerSafe && Constants.Spoilers.Keywords.Any(k => a.Synopsis?.Contains(k, StringComparison.OrdinalIgnoreCase) == true);
                        var spoilerPenalty = hasSpoiler ? Constants.Scoring.SpoilerPenalty : 0.0;
                        var hybrid = Constants.Scoring.VectorWeight * vs + Constants.Scoring.PopularityWeight * pop + Constants.Scoring.GenreWeight * genreBoost + preferBoost * preferTagsWeightEff;
                        hybrid *= (1.0 - spoilerPenalty);

                        var reasons = new List<string> { "vector" };
                        if (genreBoost > 0) reasons.Add("genre");
                        if (preferBoost > 0) reasons.Add("boost");
                        if (pop > Constants.Scoring.PopularityHotThreshold) reasons.Add("popular");
                        if (spoilerPenalty > 0) reasons.Add("spoiler-safe");

                        return new Recommendation
                        {
                            Anime = new Anime
                            {
                                Id = a.Id,
                                Title = a.Title,
                                TitleEnglish = a.TitleEnglish,
                                TitleRomaji = a.TitleRomaji,
                                TitleNative = a.TitleNative,
                                Synonyms = a.Synonyms ?? Array.Empty<string>(),
                                Genres = a.Genres ?? Array.Empty<string>(),
                                Tags = a.Tags ?? Array.Empty<string>(),
                                Episodes = a.Episodes,
                                Synopsis = a.Synopsis,
                                Popularity = a.Popularity,
                                CoverUrl = a.CoverUrl,
                                BannerUrl = a.BannerUrl,
                                CoverColorHex = a.CoverColorHex
                            },
                            Score = hybrid,
                            Reasons = reasons.ToArray()
                        };
                    })
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();
                    _logger?.LogInformation("Query: vector path returned {Count} items (degraded=false)", mapped.Count);
                    return (mapped, false);
                }
            }
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Query: vector path failed, degrading to demo"); }

        // Fallback: try Mongo (if seeded) else return empty degraded set
        try
        {
            var allDocs = await AnimeDoc.All(ct);
            var docs = allDocs.AsEnumerable();
            // Exclude items in user's Library if provided
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var entries = (await LibraryEntryDoc.All(ct)).Where(e => e.UserId == userId).ToList();
                var exclude = entries.Select(e => e.AnimeId).ToHashSet();
                docs = docs.Where(a => !exclude.Contains(a.Id));
            }
            if (genres is { Length: > 0 }) docs = docs.Where(a => (a.Genres ?? Array.Empty<string>()).Intersect(genres).Any());
            if (episodesMax is int em) docs = docs.Where(a => a.Episodes is null || a.Episodes <= em);
            var mapped = docs.Select(a => new Recommendation
            {
                Anime = new Anime
                {
                    Id = a.Id,
                    Title = a.Title,
                    TitleEnglish = a.TitleEnglish,
                    TitleRomaji = a.TitleRomaji,
                    TitleNative = a.TitleNative,
                    Synonyms = a.Synonyms ?? Array.Empty<string>(),
                    Genres = a.Genres ?? Array.Empty<string>(),
                    Tags = a.Tags ?? Array.Empty<string>(),
                    Episodes = a.Episodes,
                    Synopsis = a.Synopsis,
                    Popularity = a.Popularity,
                    CoverUrl = a.CoverUrl,
                    BannerUrl = a.BannerUrl,
                    CoverColorHex = a.CoverColorHex
                },
                Score = a.Popularity,
                Reasons = new[] { "popularity" }
            }).OrderByDescending(r => r.Score).Take(topK).ToList();
            _logger?.LogInformation("Query: mongo fallback returned {Count} items (degraded=true)", mapped.Count);
            return (mapped, true);
        }
        catch
        {
            _logger?.LogWarning("Query: no data available; returning empty set (degraded=true)");
            return (Array.Empty<Recommendation>(), true);
        }
    }

    public async Task RateAsync(string userId, string animeId, int rating, CancellationToken ct)
    {
        _logger?.LogInformation("Rating: user={UserId} anime={AnimeId} rating={Rating}", userId, animeId, rating);
    var data = (IDataService?)_sp.GetService(typeof(IDataService));
    if (data is null) return;
    // Upsert status+rating in LibraryEntry
    var id = $"{userId}:{animeId}";
    var entry = await LibraryEntryDoc.Get(id, ct) ?? new LibraryEntryDoc { Id = id, UserId = userId, AnimeId = animeId, AddedAt = DateTimeOffset.UtcNow };
    entry.Rating = Math.Max(0, Math.Min(5, rating));
    if (!entry.Watched && !entry.Dropped) entry.Watched = true; // auto-set watched
    entry.UpdatedAt = DateTimeOffset.UtcNow;
    await LibraryEntryDoc.UpsertMany(new[] { entry }, ct);

        // Update profile preferences using simple EWMA over genres and tags
        var a = await AnimeDoc.Get(animeId, ct);
        if (a is null) return;
        var profile = await UserProfileDoc.Get(userId, ct) ?? new UserProfileDoc { Id = userId };
        const double alpha = 0.3; // smoothing factor
        void Nudge(string key, double target)
        {
            profile.GenreWeights.TryGetValue(key, out var oldW);
            var updated = (1 - alpha) * oldW + alpha * target;
            profile.GenreWeights[key] = Math.Clamp(updated, 0, 1);
        }
        foreach (var g in a.Genres ?? Array.Empty<string>()) Nudge(g, rating / 5.0);
        foreach (var t in a.Tags ?? Array.Empty<string>()) Nudge(t, rating / 5.0);
        // Update preference vector via EWMA if embedding is available
        try
        {
            var ai = Sora.AI.Ai.TryResolve();
            if (ai is not null)
            {
                var textBlend = $"{a.Title}\n\n{a.Synopsis}\nTags: {string.Join(", ", (a.Genres ?? Array.Empty<string>()).Concat(a.Tags ?? Array.Empty<string>()))}";
                var emb = await ai.EmbedAsync(new AiEmbeddingsRequest { Input = new() { textBlend } }, ct);
                var vec = emb.Vectors.FirstOrDefault();
                if (vec is { Length: > 0 })
                {
                    if (profile.PrefVector is { Length: > 0 } pv && pv.Length == vec.Length)
                    {
                        for (int i = 0; i < pv.Length; i++) pv[i] = (float)((1 - alpha) * pv[i] + alpha * vec[i]);
                        profile.PrefVector = pv;
                    }
                    else
                    {
                        profile.PrefVector = vec;
                    }
                }
            }
        }
        catch
        {
            // non-fatal
        }
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await UserProfileDoc.UpsertMany(new[] { profile }, ct);
    }
}
