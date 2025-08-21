using System.Text.Json;
using System.Text.RegularExpressions;
using S5.Recs.Infrastructure;
using S5.Recs.Models;
using S5.Recs.Options;
using Microsoft.Extensions.Options;
using Sora.AI.Contracts;
using Sora.Data.Core;
using Sora.Data.Abstractions;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using S5.Recs.Providers;

namespace S5.Recs.Services;

public interface ISeedService
{
    Task<string> StartAsync(string source, int limit, bool overwrite, CancellationToken ct);
    Task<object> GetStatusAsync(string jobId, CancellationToken ct);
    Task<(int anime, int contentPieces, int vectors)> GetStatsAsync(CancellationToken ct);
}

internal sealed class SeedService : ISeedService
{
    private readonly string _cacheDir = Constants.Paths.SeedCache;
    private readonly string _offlinePath = Constants.Paths.OfflineData;
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
    _progress[jobId] = (0,0,0,0,false,null);
    _logger?.LogInformation("Seeding job {JobId} started. source={Source} limit={Limit} overwrite={Overwrite}", jobId, source, limit, overwrite);
        _ = Task.Run(async () =>
        {
            try
            {
                var data = await FetchFromProviderAsync(source, limit, ct);
        _progress[jobId] = (data.Count, data.Count, 0, 0,false,null);
        _logger?.LogInformation("Seeding job {JobId}: fetched and normalized {Count} items", jobId, data.Count);
                var embedded = await EmbedAndIndexAsync(data, ct);
        _logger?.LogInformation("Seeding job {JobId}: embedded and indexed {Embedded} vectors", jobId, embedded);
                var imported = await ImportMongoAsync(data, ct);
        _progress[jobId] = (data.Count, data.Count, embedded, imported,true,null);
        _logger?.LogInformation("Seeding job {JobId}: imported {Imported} docs into Mongo", jobId, imported);
                await File.WriteAllTextAsync(Path.Combine(_cacheDir, "manifest.json"), JsonSerializer.Serialize(new { jobId, count = data.Count, at = DateTimeOffset.UtcNow }), ct);
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

    public Task<object> GetStatusAsync(string jobId, CancellationToken ct)
    {
    var p = _progress.TryGetValue(jobId, out var prog) ? prog : (Fetched:0, Normalized:0, Embedded:0, Imported:0, Completed:false, Error:(string?)null);
    _logger?.LogDebug("Seeding job {JobId} status requested: state={State} fetched={Fetched} normalized={Normalized} embedded={Embedded} imported={Imported}", jobId, p.Completed ? (p.Error is null ? "completed" : "failed") : "running", p.Fetched, p.Normalized, p.Embedded, p.Imported);
    var state = p.Completed ? (p.Error is null ? "completed" : "failed") : "running";
    return Task.FromResult<object>(new { jobId, state, error = p.Error, progress = new { fetched = p.Fetched, normalized = p.Normalized, embedded = p.Embedded, imported = p.Imported } });
    }

    public async Task<(int anime, int contentPieces, int vectors)> GetStatsAsync(CancellationToken ct)
    {
        try
        {
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (dataSvc is null) return (0, 0, 0);
            var repo = dataSvc.GetRepository<AnimeDoc, string>();
            var animeCount = await repo.CountAsync(query: null, ct);
            var vecRepo = dataSvc.TryGetVectorRepository<AnimeDoc, string>();
            int vectorCount = 0;
            if (vecRepo is Sora.Data.Abstractions.Instructions.IInstructionExecutor<AnimeDoc> exec)
            {
                vectorCount = await exec.ExecuteAsync<int>(new Sora.Data.Abstractions.Instructions.Instruction(Sora.Data.Vector.VectorInstructions.IndexStats), ct);
            }
            return (animeCount, animeCount, vectorCount);
        }
        catch { return (0, 0, 0); }
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
            var dataSvc = (Sora.Data.Core.IDataService?)_sp.GetService(typeof(Sora.Data.Core.IDataService));
            if (dataSvc is null) return 0;
            var repo = dataSvc.GetRepository<S5.Recs.Models.AnimeDoc, string>();
            var docs = items.Select(a => new S5.Recs.Models.AnimeDoc
            {
                Id = a.Id,
                Title = a.Title,
                Genres = a.Genres,
                Episodes = a.Episodes,
                Synopsis = a.Synopsis,
                Popularity = a.Popularity
            });
            return await repo.UpsertManyAsync(docs, ct);
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> EmbedAndIndexAsync(List<Anime> items, CancellationToken ct)
    {
        try
        {
            var ai = (IAi?)_sp.GetService(typeof(IAi));
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (ai is null || dataSvc is null) { _logger?.LogWarning("Embedding and vector index skipped: AI or data service unavailable"); return 0; }
            var vec = dataSvc.TryGetVectorRepository<AnimeDoc, string>();
            if (vec is null) { _logger?.LogWarning("Vector repository unavailable. Skipping vector upsert."); return 0; }

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
                var up = await vec.UpsertManyAsync(tuples, ct);
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

    private static string BuildEmbeddingText(Anime a)
        => ($"{a.Title}\n\n{a.Synopsis}\n\nTags: {string.Join(", ", a.Genres ?? Array.Empty<string>())}").Trim();
}
