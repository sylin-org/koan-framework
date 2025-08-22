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

    public async Task<(IReadOnlyList<Recommendation> items, bool degraded)> QueryAsync(string? text, string? anchorAnimeId, string[]? genres, int? episodesMax, bool spoilerSafe, int topK, string? userId, CancellationToken ct)
    {
        // Guardrails: sensible defaults and caps for K
        if (topK <= 0) topK = 10; // avoid empty results when a caller sends 0
        if (topK > 50) topK = 50; // keep requests light for the sample
        // Try vector-first if text or anchor provided
        _logger?.LogInformation("Query: text='{Text}' anchor='{Anchor}' genres=[{Genres}] episodesMax={EpisodesMax} spoilerSafe={SpoilerSafe} topK={TopK} user={UserId}",
            text, anchorAnimeId, genres is null ? string.Empty : string.Join(',', genres), episodesMax, spoilerSafe, topK, userId);
        try
        {
            if (!string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(anchorAnimeId))
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
                else query = Array.Empty<float>();

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
                    if (genres is { Length: > 0 }) q = q.Where(a => a.Genres.Intersect(genres).Any());
                    if (episodesMax is int emax) q = q.Where(a => a.Episodes is null || a.Episodes <= emax);
                    // Personalization: fetch profile if present
                    UserProfileDoc? profile = null;
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        profile = await UserProfileDoc.Get(userId, ct);
                    }

                    var mapped = q
                                .Select(a =>
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
                                    var hasSpoiler = spoilerSafe && Constants.Spoilers.Keywords.Any(k => a.Synopsis?.Contains(k, StringComparison.OrdinalIgnoreCase) == true);
                                    var spoilerPenalty = hasSpoiler ? Constants.Scoring.SpoilerPenalty : 0.0;
                                    var hybrid = Constants.Scoring.VectorWeight * vs + Constants.Scoring.PopularityWeight * pop + Constants.Scoring.GenreWeight * genreBoost;
                                    hybrid *= (1.0 - spoilerPenalty);
                                    var reasons = new List<string> { "vector" };
                                    if (genreBoost > 0) reasons.Add("genre");
                                    if (pop > Constants.Scoring.PopularityHotThreshold) reasons.Add("popular");
                                    if (spoilerPenalty > 0) reasons.Add("spoiler-safe");
                                    return new Recommendation
                                    {
                                        Anime = new Anime {
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

        // Fallback: local demo popularity
        IEnumerable<Anime> f = _demo;
        if (genres is { Length: > 0 }) f = f.Where(a => a.Genres.Intersect(genres).Any());
        if (episodesMax is int em) f = f.Where(a => a.Episodes is null || a.Episodes <= em);
        var items = f.Select(a => new Recommendation { Anime = a, Score = a.Popularity, Reasons = new[] { "demo", "popularity" } }).Take(topK).ToList();
        _logger?.LogInformation("Query: demo fallback returned {Count} items (degraded=true)", items.Count);
        return (items, true);
    }

    public async Task RateAsync(string userId, string animeId, int rating, CancellationToken ct)
    {
        _logger?.LogInformation("Rating: user={UserId} anime={AnimeId} rating={Rating}", userId, animeId, rating);
        var data = (IDataService?)_sp.GetService(typeof(IDataService));
        if (data is null) return;
        // Upsert rating
        await RatingDoc.UpsertMany(new[]{ new RatingDoc
        {
            Id = $"{userId}:{animeId}",
            UserId = userId,
            AnimeId = animeId,
            Rating = Math.Max(0, Math.Min(5, rating)),
            UpdatedAt = DateTimeOffset.UtcNow
    } }, ct);

        // Update profile genre weights using simple EWMA
        var a = await AnimeDoc.Get(animeId, ct);
        if (a is null) return;
        var profile = await UserProfileDoc.Get(userId, ct) ?? new UserProfileDoc { Id = userId };
        const double alpha = 0.3; // smoothing factor
        foreach (var g in a.Genres ?? Array.Empty<string>())
        {
            profile.GenreWeights.TryGetValue(g, out var oldW);
            var target = rating / 5.0; // normalize 0..1
            var updated = (1 - alpha) * oldW + alpha * target;
            profile.GenreWeights[g] = Math.Clamp(updated, 0, 1);
        }
        // Update preference vector via EWMA if embedding is available
        try
        {
            var ai = Sora.AI.Ai.TryResolve();
            if (ai is not null)
            {
                var text = BuildEmbeddingText(new Anime { Id = a.Id, Title = a.Title, Genres = a.Genres ?? Array.Empty<string>(), Episodes = a.Episodes, Synopsis = a.Synopsis, Popularity = a.Popularity });
                var emb = await ai.EmbedAsync(new AiEmbeddingsRequest { Input = new() { text } }, ct);
                var vec = emb.Vectors.FirstOrDefault() ?? Array.Empty<float>();
                if (vec.Length > 0)
                {
                    var target = (float)(rating / 5.0);
                    if (profile.PrefVector is null || profile.PrefVector.Length != vec.Length)
                    {
                        profile.PrefVector = new float[vec.Length];
                    }
                    for (int i = 0; i < vec.Length; i++)
                    {
                        var old = profile.PrefVector[i];
                        var upd = (float)((1 - alpha) * old + alpha * (vec[i] * target));
                        profile.PrefVector[i] = upd;
                    }
                }
            }
        }
        catch { /* optional */ }
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await UserProfileDoc.UpsertMany(new[] { profile }, ct);
        _logger?.LogDebug("Rating: updated profile for user {UserId} (genres={Count}, vec={VecLen})", userId, profile.GenreWeights.Count, profile.PrefVector?.Length ?? 0);
    }

    private static string BuildEmbeddingText(Anime a)
    {
        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.Title)) titles.Add(a.Title);
        if (!string.IsNullOrWhiteSpace(a.TitleEnglish) && a.TitleEnglish != a.Title) titles.Add(a.TitleEnglish!);
        if (!string.IsNullOrWhiteSpace(a.TitleRomaji) && a.TitleRomaji != a.Title) titles.Add(a.TitleRomaji!);
        if (!string.IsNullOrWhiteSpace(a.TitleNative) && a.TitleNative != a.Title) titles.Add(a.TitleNative!);
        if (a.Synonyms is { Length: > 0 }) titles.AddRange(a.Synonyms);
        var tags = new List<string>();
        if (a.Genres is { Length: > 0 }) tags.AddRange(a.Genres);
        if (a.Tags is { Length: > 0 }) tags.AddRange(a.Tags);
        var text = $"{string.Join(" / ", titles.Distinct())}\n\n{a.Synopsis}\n\nTags: {string.Join(", ", tags.Distinct())}";
        return text.Trim();
    }
}
