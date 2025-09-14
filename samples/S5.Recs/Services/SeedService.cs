using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S5.Recs.Infrastructure;
using S5.Recs.Models;
using S5.Recs.Options;
using S5.Recs.Providers;
using Sora.AI.Contracts;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Vector;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace S5.Recs.Services;

internal sealed class SeedService : ISeedService
{
    private readonly string _cacheDir = Constants.Paths.SeedCache;
    private readonly Dictionary<string, (int Fetched, int Normalized, int Embedded, int Imported, bool Completed, string? Error)> _progress = new();
    private readonly IServiceProvider _sp;
    private readonly ILogger<SeedService>? _logger;
    private readonly IReadOnlyDictionary<string, IAnimeProvider> _providers;

    public SeedService(IServiceProvider sp, ILogger<SeedService>? logger = null)
    {
        _sp = sp;
        _logger = logger;
        // Discover providers via DI
        var provs = (IEnumerable<IAnimeProvider>?)_sp.GetService(typeof(IEnumerable<IAnimeProvider>)) ?? Array.Empty<IAnimeProvider>();
        _providers = provs.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
    }

    public Task<string> StartAsync(string source, int limit, bool overwrite, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheDir);
        var jobId = Guid.NewGuid().ToString("n");
        _progress[jobId] = (0, 0, 0, 0, false, null);
        _logger?.LogInformation("Seeding job {JobId} started. source={Source} limit={Limit} overwrite={Overwrite}", jobId, source, limit, overwrite);
        _ = Task.Run(async () =>
        {
            try
            {
                var data = await FetchFromProviderAsync(source, limit, ct);
                _progress[jobId] = (data.Count, data.Count, 0, 0, false, null);
                _logger?.LogInformation("Seeding job {JobId}: fetched and normalized {Count} items", jobId, data.Count);
                var embedded = await EmbedAndIndexAsync(data, ct);
                _logger?.LogInformation("Seeding job {JobId}: embedded and indexed {Embedded} vectors", jobId, embedded);
                var imported = await ImportMongoAsync(data, ct);
                // Build catalogs once docs are imported
                try { await CatalogTagsAsync(data, ct); } catch (Exception ex) { _logger?.LogWarning(ex, "Tag cataloging failed: {Message}", ex.Message); }
                try { await CatalogGenresAsync(data, ct); } catch (Exception ex) { _logger?.LogWarning(ex, "Genre cataloging failed: {Message}", ex.Message); }
                _progress[jobId] = (data.Count, data.Count, embedded, imported, true, null);
                _logger?.LogInformation("Seeding job {JobId}: imported {Imported} docs into Mongo", jobId, imported);
                await File.WriteAllTextAsync(Path.Combine(_cacheDir, "manifest.json"), JsonConvert.SerializeObject(new { jobId, count = data.Count, at = DateTimeOffset.UtcNow }), ct);
                _logger?.LogInformation("Seeding job {JobId} completed. Manifest written.", jobId);
            }
            catch (Exception ex)
            {
                _progress[jobId] = (_progress[jobId].Fetched, _progress[jobId].Normalized, _progress[jobId].Embedded, _progress[jobId].Imported, true, ex.Message);
                _logger?.LogError(ex, "Seeding job {JobId} failed: {Error}", jobId, ex.Message);
            }
        }, ct);
        return Task.FromResult(jobId);
    }

    // Overload: Start a vector-only job from a provided list of AnimeDoc entities
    public Task<string> StartVectorUpsertAsync(IEnumerable<AnimeDoc> itemss, CancellationToken ct)
    {
        var items = itemss.ToList();

        Directory.CreateDirectory(_cacheDir);
        var jobId = Guid.NewGuid().ToString("n");
        var count = items?.Count ?? 0;
        _progress[jobId] = (count, count, 0, 0, false, null);
        _logger?.LogInformation("Vector-only upsert job {JobId} started from provided items. count={Count}", jobId, count);
        _ = Task.Run(async () =>
        {
            try
            {
                var embedded = await UpsertVectorsAsync(items ?? [], ct);
                _progress[jobId] = (count, count, embedded, 0, true, null);
                _logger?.LogInformation("Vector-only job {JobId}: embedded and indexed {Embedded} vectors", jobId, embedded);
                await File.WriteAllTextAsync(Path.Combine(_cacheDir, "manifest-vectors.json"), JsonConvert.SerializeObject(new { jobId, count = count, at = DateTimeOffset.UtcNow }), ct);
            }
            catch (Exception ex)
            {
                _progress[jobId] = (_progress[jobId].Fetched, _progress[jobId].Normalized, _progress[jobId].Embedded, _progress[jobId].Imported, true, ex.Message);
                _logger?.LogError(ex, "Vector-only job {JobId} failed: {Error}", jobId, ex.Message);
            }
        }, ct);
        return Task.FromResult(jobId);
    }

    public Task<object> GetStatusAsync(string jobId, CancellationToken ct)
    {
        var p = _progress.TryGetValue(jobId, out var prog) ? prog : (Fetched: 0, Normalized: 0, Embedded: 0, Imported: 0, Completed: false, Error: null);
        //_logger?.LogDebug("Seeding job {JobId} status requested: state={State} fetched={Fetched} normalized={Normalized} embedded={Embedded} imported={Imported}", jobId, p.Completed ? (p.Error is null ? "completed" : "failed") : "running", p.Fetched, p.Normalized, p.Embedded, p.Imported);
        var state = p.Completed ? (p.Error is null ? "completed" : "failed") : "running";
        return Task.FromResult<object>(new { jobId, state, error = p.Error, progress = new { fetched = p.Fetched, normalized = p.Normalized, embedded = p.Embedded, imported = p.Imported } });
    }

    public async Task<(int anime, int contentPieces, int vectors)> GetStatsAsync(CancellationToken ct)
    {
        var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
        if (dataSvc is null) { _logger?.LogWarning("Stats: IDataService unavailable"); return (0, 0, 0); }

        int animeCount = 0;
        int vectorCount = 0;

        // Count documents (best-effort)
        try
        {
            using (DataSetContext.With(null))
            {
                var repo = dataSvc.GetRepository<AnimeDoc, string>();
                animeCount = await repo.CountAsync(query: null, ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Stats: failed to count AnimeDoc");
        }

        // Count vectors if provider supports instructions (best-effort)
        try
        {
            using (DataSetContext.With(null))
            {
                if (Vector<AnimeDoc>.IsAvailable)
                {
                    vectorCount = await Vector<AnimeDoc>.Stats(ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Stats: vector instruction not supported or failed");
        }

        return (animeCount, animeCount, vectorCount);
    }

    public async Task<int> RebuildTagCatalogAsync(CancellationToken ct)
    {
        try
        {
            var docs = await AnimeDoc.All(ct);
            var extractedTags = ExtractTags(docs);

            // Apply preemptive filtering during rebuild
            var flaggedTags = new List<string>();
            var cleanTags = new List<string>();

            foreach (var tag in extractedTags)
            {
                if (Infrastructure.PreemptiveTagFilter.ShouldCensor(tag))
                {
                    flaggedTags.Add(tag);
                }
                else
                {
                    cleanTags.Add(tag);
                }
            }

            // Auto-add flagged tags to censor list
            if (flaggedTags.Count > 0)
            {
                await AutoCensorTagsAsync(flaggedTags, ct);
                _logger?.LogInformation("Preemptive filter auto-censored {Count} tags during catalog rebuild", flaggedTags.Count);
            }

            var counts = CountTags(cleanTags);
            var tagDocs = BuildTagDocs(counts);
            var n = await TagStatDoc.UpsertMany(tagDocs, ct);
            _logger?.LogInformation("Rebuilt tag catalog: {Count} tags ({Censored} preemptively filtered)", counts.Count, flaggedTags.Count);
            return n;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Rebuild tag catalog failed: {Message}", ex.Message);
            return 0;
        }
    }

    public async Task<int> RebuildGenreCatalogAsync(CancellationToken ct)
    {
        try
        {
            var docs = await AnimeDoc.All(ct);
            var counts = CountGenres(ExtractGenres(docs));
            var genreDocs = BuildGenreDocs(counts);
            var n = await GenreStatDoc.UpsertMany(genreDocs, ct);
            _logger?.LogInformation("Rebuilt genre catalog: {Count} genres", counts.Count);
            return n;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Rebuild genre catalog failed: {Message}", ex.Message);
            return 0;
        }
    }

    private Task<List<Anime>> FetchFromProviderAsync(string source, int limit, CancellationToken ct)
    {
        if (_providers.TryGetValue(source, out var provider))
        {
            _logger?.LogInformation("Using provider {Code} ({Name}) to fetch items.", provider.Code, provider.Name);
            return provider.FetchAsync(limit, ct);
        }

        _logger?.LogWarning("Unknown provider '{Source}'. Falling back to 'local' if available.", source);
        if (_providers.TryGetValue("local", out var local))
        {
            return local.FetchAsync(limit, ct);
        }
        return Task.FromResult(new List<Anime>());
    }

    private async Task<int> ImportMongoAsync(List<Anime> items, CancellationToken ct)
    {
        try
        {
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (dataSvc is null) return 0;
            var docs = items.Select(a => new AnimeDoc
            {
                Id = a.Id,
                Title = a.Title,
                TitleEnglish = a.TitleEnglish,
                TitleRomaji = a.TitleRomaji,
                TitleNative = a.TitleNative,
                Synonyms = a.Synonyms,
                Genres = a.Genres,
                Tags = a.Tags,
                Episodes = a.Episodes,
                Synopsis = a.Synopsis,
                Popularity = a.Popularity,
                CoverUrl = a.CoverUrl,
                BannerUrl = a.BannerUrl,
                CoverColorHex = a.CoverColorHex
            });
            return await AnimeDoc.UpsertMany(docs, ct);
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> ExtractTags(IEnumerable<Anime> items)
    {
        foreach (var a in items)
        {
            if (a.Genres is { Length: > 0 })
                foreach (var g in a.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
            if (a.Tags is { Length: > 0 })
                foreach (var t in a.Tags) if (!string.IsNullOrWhiteSpace(t)) yield return t.Trim();
        }
    }

    private static IEnumerable<string> ExtractGenres(IEnumerable<Anime> items)
    {
        foreach (var a in items)
        {
            if (a.Genres is { Length: > 0 })
                foreach (var g in a.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
        }
    }

    private static IEnumerable<string> ExtractTags(IEnumerable<AnimeDoc> items)
    {
        foreach (var a in items)
        {
            if (a.Genres is { Length: > 0 })
                foreach (var g in a.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
            if (a.Tags is { Length: > 0 })
                foreach (var t in a.Tags) if (!string.IsNullOrWhiteSpace(t)) yield return t.Trim();
        }
    }

    private static IEnumerable<string> ExtractGenres(IEnumerable<AnimeDoc> items)
    {
        foreach (var a in items)
        {
            if (a.Genres is { Length: > 0 })
                foreach (var g in a.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
        }
    }

    private static Dictionary<string, int> CountTags(IEnumerable<string> tags)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tags)
        {
            var key = t.Trim();
            if (key.Length == 0) continue;
            map.TryGetValue(key, out var c);
            map[key] = c + 1;
        }
        return map;
    }

    private static IEnumerable<TagStatDoc> BuildTagDocs(Dictionary<string, int> counts)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in counts)
        {
            yield return new TagStatDoc { Id = kv.Key.ToLowerInvariant(), Tag = kv.Key, AnimeCount = kv.Value, UpdatedAt = now };
        }
    }

    private static Dictionary<string, int> CountGenres(IEnumerable<string> genres)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in genres)
        {
            var key = g.Trim();
            if (key.Length == 0) continue;
            map.TryGetValue(key, out var c);
            map[key] = c + 1;
        }
        return map;
    }

    private static IEnumerable<GenreStatDoc> BuildGenreDocs(Dictionary<string, int> counts)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in counts)
        {
            yield return new GenreStatDoc { Id = kv.Key.ToLowerInvariant(), Genre = kv.Key, AnimeCount = kv.Value, UpdatedAt = now };
        }
    }

    private async Task CatalogTagsAsync(List<Anime> items, CancellationToken ct)
    {
        var extractedTags = ExtractTags(items);

        // Apply preemptive filtering - automatically censor flagged tags
        var flaggedTags = new List<string>();
        var cleanTags = new List<string>();

        foreach (var tag in extractedTags)
        {
            if (Infrastructure.PreemptiveTagFilter.ShouldCensor(tag))
            {
                flaggedTags.Add(tag);
            }
            else
            {
                cleanTags.Add(tag);
            }
        }

        // Auto-add flagged tags to censor list
        if (flaggedTags.Count > 0)
        {
            await AutoCensorTagsAsync(flaggedTags, ct);
            _logger?.LogInformation("Preemptive filter auto-censored {Count} tags during import", flaggedTags.Count);
        }

        // Only catalog clean tags
        var counts = CountTags(cleanTags);
        var docs = BuildTagDocs(counts);
        await TagStatDoc.UpsertMany(docs, ct);
        _logger?.LogInformation("Tag catalog updated with {Count} tags ({Censored} preemptively filtered)", counts.Count, flaggedTags.Count);
    }

    private async Task AutoCensorTagsAsync(List<string> tags, CancellationToken ct)
    {
        try
        {
            var doc = await Models.CensorTagsDoc.Get("recs:censor-tags", ct) ?? new Models.CensorTagsDoc { Id = "recs:censor-tags" };
            var existingTags = new HashSet<string>(doc.Tags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var newlyAdded = 0;
            foreach (var tag in tags)
            {
                if (existingTags.Add(tag.Trim()))
                {
                    newlyAdded++;
                }
            }

            if (newlyAdded > 0)
            {
                doc.Tags = existingTags.OrderBy(s => s).ToList();
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                await Models.CensorTagsDoc.UpsertMany(new[] { doc }, ct);
                _logger?.LogInformation("Auto-added {NewCount} new tags to censor list via preemptive filter", newlyAdded);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to auto-censor tags via preemptive filter: {Message}", ex.Message);
        }
    }

    private async Task CatalogGenresAsync(List<Anime> items, CancellationToken ct)
    {
        var counts = CountGenres(ExtractGenres(items));
        var docs = BuildGenreDocs(counts);
        await GenreStatDoc.UpsertMany(docs, ct);
        _logger?.LogInformation("Genre catalog updated with {Count} genres", counts.Count);
    }

    // Utility to rebuild catalog from the current AnimeDoc collection (not used in normal flow)
    private static async Task CatalogTagsFromDocsAsync(CancellationToken ct)
    {
        var docs = await AnimeDoc.All(ct);
        var counts = CountTags(ExtractTags(docs));
        var tagDocs = BuildTagDocs(counts);
        await TagStatDoc.UpsertMany(tagDocs, ct);
    }

    // (Removed) Mongo fetch helpers replaced by direct use of AnimeDoc.All(ct) for small collections

    private async Task<int> EmbedAndIndexAsync(List<Anime> items, CancellationToken ct)
    {
        try
        {
            var ai = Sora.AI.Ai.TryResolve();
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (ai is null || dataSvc is null) { _logger?.LogWarning("Embedding and vector index skipped: AI or data service unavailable"); return 0; }
            if (!Vector<AnimeDoc>.IsAvailable)
            {
                _logger?.LogWarning("Vector repository unavailable. Configure a vector engine (e.g., Weaviate) and set Sora:Data:Weaviate:Endpoint. In Docker compose, ensure service 'weaviate' is running and reachable.");
                return 0;
            }
            // Use the facade; degrade gracefully if no vector adapter is configured

            var opts = (IOptions<OllamaOptions>?)_sp.GetService(typeof(IOptions<OllamaOptions>));
            var model = opts?.Value?.Model ?? "all-minilm";

            // Batch to avoid huge payloads
            const int batchSize = 32;
            int total = 0;
            for (int i = 0; i < items.Count; i += batchSize)
            {
                var batch = items.Skip(i).Take(batchSize).ToList();
                var inputs = batch.Select(a => BuildEmbeddingText(a)).ToList();
                var emb = await ai.EmbedAsync(new Sora.AI.Contracts.Models.AiEmbeddingsRequest { Input = inputs, Model = model }, ct);
                var tuples = new List<(string Id, float[] Embedding, object? Metadata)>(batch.Count);
                for (int j = 0; j < batch.Count && j < emb.Vectors.Count; j++)
                {
                    var a = batch[j];
                    tuples.Add((a.Id, emb.Vectors[j], new { title = a.Title, genres = a.Genres, popularity = a.Popularity }));
                }
                int up;
                try
                {
                    up = await Vector<AnimeDoc>.Save(tuples, ct);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogWarning(ex, "Vector repository unavailable or rejected upsert. Skipping vector upsert. Ensure Sora:Data:Weaviate:Endpoint is configured and reachable. Details: {Message}", ex.Message);
                    return 0;
                }
                total += up;
                _logger?.LogInformation("Vector upsert: batch {BatchStart}-{BatchEnd} size={Size} upserted={Upserted}", i + 1, Math.Min(i + batch.Count, items.Count), batch.Count, up);
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    // New: Upsert vectors for an existing set of AnimeDoc entities in one go
    private async Task<int> UpsertVectorsAsync(List<AnimeDoc> docss, CancellationToken ct)
    {
        try
        {

            var docs = docss.ToList();

            var ai = Sora.AI.Ai.TryResolve();
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (ai is null || dataSvc is null) { _logger?.LogWarning("Embedding and vector index skipped: AI or data service unavailable"); return 0; }
            if (!Vector<AnimeDoc>.IsAvailable)
            {
                _logger?.LogWarning("Vector repository unavailable. Configure a vector engine (e.g., Weaviate) and set Sora:Data:Weaviate:Endpoint. In Docker compose, ensure service 'weaviate' is running and reachable.");
                return 0;
            }

            var opts = (IOptions<OllamaOptions>?)_sp.GetService(typeof(IOptions<OllamaOptions>));
            var model = opts?.Value?.Model ?? "all-minilm";

            const int batchSize = 32;
            int total = 0;
            for (int i = 0; i < docs.Count; i += batchSize)
            {
                var batch = docs.Skip(i).Take(batchSize).ToList();
                // Build enriched embedding text (titles + synonyms + genres + tags)
                var inputs = batch.Select(d =>
                {
                    var titles = new List<string>();
                    if (!string.IsNullOrWhiteSpace(d.Title)) titles.Add(d.Title!);
                    if (!string.IsNullOrWhiteSpace(d.TitleEnglish) && d.TitleEnglish != d.Title) titles.Add(d.TitleEnglish!);
                    if (!string.IsNullOrWhiteSpace(d.TitleRomaji) && d.TitleRomaji != d.Title) titles.Add(d.TitleRomaji!);
                    if (!string.IsNullOrWhiteSpace(d.TitleNative) && d.TitleNative != d.Title) titles.Add(d.TitleNative!);
                    if (d.Synonyms is { Length: > 0 }) titles.AddRange(d.Synonyms);

                    var tags = new List<string>();
                    if (d.Genres is { Length: > 0 }) tags.AddRange(d.Genres);
                    if (d.Tags is { Length: > 0 }) tags.AddRange(d.Tags);

                    return ($"{string.Join(" / ", titles.Distinct())}\n\n{d.Synopsis}\n\nTags: {string.Join(", ", tags.Distinct())}").Trim();
                }).ToList();
                var emb = await ai.EmbedAsync(new Sora.AI.Contracts.Models.AiEmbeddingsRequest { Input = inputs, Model = model }, ct);
                var tuples = new List<(string Id, float[] Embedding, object? Metadata)>(batch.Count);
                for (int j = 0; j < batch.Count && j < emb.Vectors.Count; j++)
                {
                    var d = batch[j];
                    tuples.Add((d.Id!, emb.Vectors[j], new { title = d.Title, genres = d.Genres, popularity = d.Popularity }));
                }
                int up;
                try
                {
                    up = await Vector<AnimeDoc>.Save(tuples, ct);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogWarning(ex, "Vector repository unavailable or rejected upsert. Skipping vector upsert. Ensure Sora:Data:Weaviate:Endpoint is configured and reachable. Details: {Message}", ex.Message);
                    return total;
                }
                total += up;
                _logger?.LogInformation("Vector upsert (docs): batch {BatchStart}-{BatchEnd} size={Size} upserted={Upserted}", i + 1, Math.Min(i + batch.Count, docs.Count), batch.Count, up);
            }
            return total;
        }
        catch
        {
            return 0;
        }
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
