using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using S5.Recs.Infrastructure;
using S5.Recs.Models;
using Koan.AI.Connector.Ollama;
using S5.Recs.Providers;
using Koan.AI;
using Koan.AI.Contracts;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Core.Pipelines;
using Koan.Core.Observability;
using Koan.Core;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;

namespace S5.Recs.Services;

internal sealed class SeedService : ISeedService
{
    private readonly string _cacheDir = Constants.Paths.SeedCache;
    private readonly Dictionary<string, (int Fetched, int Normalized, int Embedded, int Imported, bool Completed, string? Error)> _progress = new();
    private readonly Dictionary<string, CancellationTokenSource> _importCancellations = new();
    private readonly IServiceProvider _sp;
    private readonly ILogger<SeedService>? _logger;
    private readonly IReadOnlyDictionary<string, IMediaProvider> _providers;
    private readonly IEmbeddingCache _embeddingCache;
    private static readonly object _importLock = new object();
    private static volatile bool _importInProgress = false;
    private int _cacheHits = 0;
    private int _cacheMisses = 0;

    public bool IsImportInProgress => _importInProgress;

    public SeedService(IServiceProvider sp, IEmbeddingCache embeddingCache, ILogger<SeedService>? logger = null)
    {
        _sp = sp;
        _embeddingCache = embeddingCache;
        _logger = logger;
        // Discover providers via DI
        var provs = (IEnumerable<IMediaProvider>?)_sp.GetService(typeof(IEnumerable<IMediaProvider>)) ?? Array.Empty<IMediaProvider>();
        _providers = provs.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> StartAsync(string source, int? limit, bool overwrite, CancellationToken ct)
    {
        // Import all supported media types for comprehensive coverage
        var mediaTypes = await MediaType.All(ct);
        if (!mediaTypes.Any())
        {
            throw new InvalidOperationException("No MediaTypes found. Please seed reference data first.");
        }

        var jobId = Guid.CreateVersion7().ToString("n");
        _logger?.LogInformation("Starting multi-type import job {JobId} for source={Source} with {TypeCount} media types",
            jobId, source, mediaTypes.Count);

        // Import each media type separately but track under one job
        foreach (var mediaType in mediaTypes)
        {
            try
            {
                var typeJobId = await StartAsync(source, mediaType.Name, limit, overwrite, null, ct);
                _logger?.LogInformation("Completed import for MediaType '{MediaType}' as part of job {JobId}",
                    mediaType.Name, jobId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to import MediaType '{MediaType}' in job {JobId}: {Error}",
                    mediaType.Name, jobId, ex.Message);
            }
        }

        return jobId;
    }

    public async Task<string> StartAsync(string source, int? limit, bool overwrite, string? embeddingModel, CancellationToken ct)
    {
        // Import all supported media types for comprehensive coverage
        var mediaTypes = await MediaType.All(ct);
        if (!mediaTypes.Any())
        {
            throw new InvalidOperationException("No MediaTypes found. Please seed reference data first.");
        }

        var jobId = Guid.CreateVersion7().ToString("n");
        _logger?.LogInformation("Starting multi-type import job {JobId} for source={Source} with {TypeCount} media types and model={Model}",
            jobId, source, mediaTypes.Count, embeddingModel ?? "default");

        // Import each media type separately but track under one job
        foreach (var mediaType in mediaTypes)
        {
            try
            {
                var typeJobId = await StartAsync(source, mediaType.Name, limit, overwrite, embeddingModel, ct);
                _logger?.LogInformation("Completed import for MediaType '{MediaType}' as part of job {JobId}",
                    mediaType.Name, jobId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to import MediaType '{MediaType}' in job {JobId}: {Error}",
                    mediaType.Name, jobId, ex.Message);
            }
        }

        return jobId;
    }

    public Task<string> StartAsync(string source, string mediaTypeName, int? limit, bool overwrite, CancellationToken ct)
        => StartAsync(source, mediaTypeName, limit, overwrite, null, ct);

    public Task<string> StartAsync(string source, string mediaTypeName, int? limit, bool overwrite, string? embeddingModel, CancellationToken ct)
    {
        // Prevent concurrent imports to avoid service saturation
        lock (_importLock)
        {
            _logger?.LogInformation("[DEBUG] Import lock check: _importInProgress = {InProgress}", _importInProgress);
            if (_importInProgress)
            {
                _logger?.LogWarning("Import request rejected - another import is already in progress");
                throw new InvalidOperationException("An import is already in progress. Please wait for it to complete before starting a new one.");
            }
            _importInProgress = true;
            _logger?.LogInformation("[DEBUG] Import lock acquired, _importInProgress set to true");
        }

        Directory.CreateDirectory(_cacheDir);
        var jobId = Guid.CreateVersion7().ToString("n");
        _progress[jobId] = (0, 0, 0, 0, false, null);
        _logger?.LogInformation("Multi-media seeding job {JobId} started. source={Source} mediaType={MediaType} limit={Limit} overwrite={Overwrite}",
            jobId, source, mediaTypeName, limit?.ToString() ?? "unlimited", overwrite);

        // Create internal cancellation token source that won't be cancelled by browser navigation
        var internalCts = new CancellationTokenSource();
        _importCancellations[jobId] = internalCts;

        _ = Task.Run(async () =>
        {
            try
            {
                // Use internal cancellation token to decouple from browser
                var internalToken = internalCts.Token;

                // Handle "all" media types case
                List<MediaType> mediaTypesToImport;
                if (mediaTypeName.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    // Get all available media types
                    mediaTypesToImport = await GetAllMediaTypes(internalToken);
                    if (!mediaTypesToImport.Any())
                    {
                        throw new InvalidOperationException("No MediaTypes found. Please seed reference data first.");
                    }
                    _logger?.LogInformation("Importing all media types: {MediaTypes}", string.Join(", ", mediaTypesToImport.Select(mt => mt.Name)));
                }
                else
                {
                    // Resolve single MediaType
                    var mediaType = await ResolveMediaType(mediaTypeName, internalToken);
                    if (mediaType == null)
                    {
                        throw new InvalidOperationException($"MediaType '{mediaTypeName}' not found. Please seed reference data first.");
                    }
                    mediaTypesToImport = new List<MediaType> { mediaType };
                }

                // Use streaming processing for real-time import
                int totalFetched = 0, totalImported = 0, totalEmbedded = 0;
                var allData = new List<Media>(); // Keep for catalog building

                // Use a large default if no limit specified, otherwise use provided limit
                int effectiveLimit = limit ?? 100000; // Default to 100k items if no limit specified (increased from 10k)

                // Calculate per-media-type limit when importing all types
                int limitPerType = mediaTypesToImport.Count > 1 ? Math.Max(1, effectiveLimit / mediaTypesToImport.Count) : effectiveLimit;

                foreach (var mediaType in mediaTypesToImport)
                {
                    _logger?.LogInformation("Seeding job {JobId}: Starting import for media type '{MediaType}' with limit {Limit}",
                        jobId, mediaType.Name, limitPerType);

                    await foreach (var batch in FetchStreamFromProviderAsync(source, mediaType, limitPerType, internalToken))
                    {
                        totalFetched += batch.Count;
                        _progress[jobId] = (totalFetched, totalFetched, totalEmbedded, totalImported, false, null);

                        // Process this batch immediately
                        var batchImported = await ImportDataAsync(batch, internalToken);
                        totalImported += batchImported;

                        var batchEmbedded = await EmbedAndIndexAsync(batch, embeddingModel, internalToken);
                        totalEmbedded += batchEmbedded;

                        // Keep data for catalog building
                        allData.AddRange(batch);

                        // Update progress in real-time
                        _progress[jobId] = (totalFetched, totalFetched, totalEmbedded, totalImported, false, null);
                        _logger?.LogInformation("Seeding job {JobId} batch: imported {BatchImported}, embedded {BatchEmbedded}. Total: {TotalImported}/{TotalFetched}",
                            jobId, batchImported, batchEmbedded, totalImported, totalFetched);
                    }

                    _logger?.LogInformation("Seeding job {JobId}: Completed import for media type '{MediaType}'",
                        jobId, mediaType.Name);
                }

                _logger?.LogInformation("Seeding job {JobId}: streaming completed. Fetched={Fetched}, Imported={Imported}, Embedded={Embedded}",
                    jobId, totalFetched, totalImported, totalEmbedded);

                // Build catalogs once docs are imported
                try { await CatalogTagsAsync(allData, internalToken); } catch (Exception ex) { _logger?.LogWarning(ex, "Tag cataloging failed: {Message}", ex.Message); }
                try { await CatalogGenresAsync(allData, internalToken); } catch (Exception ex) { _logger?.LogWarning(ex, "Genre cataloging failed: {Message}", ex.Message); }

                // Auto-rebuild full tag catalog after large imports to ensure accurate counts across ALL media
                if (totalImported >= 100) // Rebuild if significant number of items imported
                {
                    _logger?.LogInformation("Auto-rebuilding full tag catalog after importing {Count} items", totalImported);
                    try
                    {
                        var catalogRebuildCount = await RebuildTagCatalogAsync(internalToken);
                        _logger?.LogInformation("Full tag catalog rebuilt with {TagCount} tags", catalogRebuildCount);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Full tag catalog rebuild failed: {Message}", ex.Message);
                    }
                }

                _progress[jobId] = (totalFetched, totalFetched, totalEmbedded, totalImported, true, null);
                _logger?.LogInformation("Seeding job {JobId}: imported {Imported} docs into Couchbase", jobId, totalImported);

                await File.WriteAllTextAsync(Path.Combine(_cacheDir, "manifest.json"),
                    JsonConvert.SerializeObject(new { jobId, count = totalFetched, mediaType = mediaTypeName, at = DateTimeOffset.UtcNow }), internalToken);
                _logger?.LogInformation("Seeding job {JobId} completed. Manifest written.", jobId);
            }
            catch (Exception ex)
            {
                _progress[jobId] = (_progress[jobId].Fetched, _progress[jobId].Normalized, _progress[jobId].Embedded, _progress[jobId].Imported, true, ex.Message);
                _logger?.LogError(ex, "Seeding job {JobId} failed: {Error}", jobId, ex.Message);
            }
            finally
            {
                // Clean up cancellation token source
                if (_importCancellations.TryGetValue(jobId, out var cts))
                {
                    _importCancellations.Remove(jobId);
                    cts.Dispose();
                }

                // Release the import lock when the job completes (success or failure)
                lock (_importLock)
                {
                    _importInProgress = false;
                }
                _logger?.LogInformation("Import lock released for job {JobId}", jobId);
            }
        }, ct);

        return Task.FromResult(jobId);
    }

    public Task<string> StartVectorUpsertAsync(IEnumerable<Media> items, CancellationToken ct)
        => StartVectorUpsertAsync(items, null, ct);

    public Task<string> StartVectorUpsertAsync(IEnumerable<Media> items, string? embeddingModel, CancellationToken ct)
    {
        var mediaItems = items.ToList();

        Directory.CreateDirectory(_cacheDir);
        var jobId = Guid.CreateVersion7().ToString("n");
        var count = mediaItems.Count;
        _progress[jobId] = (count, count, 0, 0, false, null);
        _logger?.LogInformation("Vector-only upsert job {JobId} started from provided items. count={Count}", jobId, count);

        _ = Task.Run(async () =>
        {
            try
            {
                var embedded = await UpsertVectorsAsync(mediaItems, embeddingModel, ct);
                _progress[jobId] = (count, count, embedded, 0, true, null);
                _logger?.LogInformation("Vector-only job {JobId}: embedded and indexed {Embedded} vectors", jobId, embedded);
                await File.WriteAllTextAsync(Path.Combine(_cacheDir, "manifest-vectors.json"),
                    JsonConvert.SerializeObject(new { jobId, count, at = DateTimeOffset.UtcNow }), ct);
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
        var state = p.Completed ? (p.Error is null ? "completed" : "failed") : "running";
        return Task.FromResult<object>(new { jobId, state, error = p.Error, progress = new { fetched = p.Fetched, normalized = p.Normalized, embedded = p.Embedded, imported = p.Imported } });
    }

    public async Task<(int media, int contentPieces, int vectors)> GetStatsAsync(CancellationToken ct)
    {
        var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
        if (dataSvc is null) { _logger?.LogWarning("Stats: IDataService unavailable"); return (0, 0, 0); }

        int mediaCount = 0;
        int vectorCount = 0;

        try
        {
            using (EntityContext.Partition(null))
            {
                var repo = dataSvc.GetRepository<Media, string>();
                mediaCount = await repo.CountAsync(query: null, ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Stats: failed to count Media");
        }

        try
        {
            using (EntityContext.Partition(null))
            {
                if (Vector<Media>.IsAvailable)
                {
                    vectorCount = await Vector<Media>.Stats(ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Stats: vector instruction not supported or failed");
        }

        return (mediaCount, mediaCount, vectorCount);
    }

    public async Task<int> RebuildTagCatalogAsync(CancellationToken ct)
    {
        try
        {
            // Use streaming to handle large datasets efficiently
            var flaggedTags = new List<string>();
            var cleanTags = new List<string>();
            var processedMediaCount = 0;
            var totalTagsExtracted = 0;

            _logger?.LogInformation("RebuildTagCatalog: Starting streaming rebuild of tag catalog");

            await foreach (var media in Media.AllStream(1000, ct))
            {
                processedMediaCount++;

                // Extract tags from this media item
                var mediaTags = ExtractTagsFromSingleMedia(media);
                totalTagsExtracted += mediaTags.Count();

                // Apply preemptive filtering
                foreach (var tag in mediaTags)
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

                // Log progress for large datasets
                if (processedMediaCount % 10000 == 0)
                {
                    _logger?.LogInformation("RebuildTagCatalog: Processed {MediaCount} media items, extracted {TagCount} total tags",
                        processedMediaCount, totalTagsExtracted);
                }
            }

            _logger?.LogInformation("RebuildTagCatalog: Completed processing {MediaCount} media documents", processedMediaCount);
            _logger?.LogInformation("RebuildTagCatalog: Extracted {TagCount} total tags (with duplicates) from media", totalTagsExtracted);

            // Auto-add flagged tags to censor list
            if (flaggedTags.Count > 0)
            {
                await AutoCensorTagsAsync(flaggedTags.Distinct().ToList(), ct);
                _logger?.LogInformation("Preemptive filter auto-censored {Count} unique tags during catalog rebuild", flaggedTags.Distinct().Count());
            }

            var counts = CountTags(cleanTags);
            var tagDocs = BuildTagDocs(counts).ToList();
            var n = tagDocs.Any() ? await TagStatDoc.UpsertMany(tagDocs, ct) : 0;
            _logger?.LogInformation("Rebuilt tag catalog: {Count} unique tags ({Censored} preemptively filtered)", counts.Count, flaggedTags.Distinct().Count());
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
            var docs = await Media.All(ct);
            var counts = CountGenres(ExtractGenres(docs));
            var genreDocs = BuildGenreDocs(counts).ToList();
            var n = genreDocs.Any() ? await GenreStatDoc.UpsertMany(genreDocs, ct) : 0;
            _logger?.LogInformation("Rebuilt genre catalog: {Count} genres", counts.Count);
            return n;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Rebuild genre catalog failed: {Message}", ex.Message);
            return 0;
        }
    }

    private async Task<MediaType?> ResolveMediaType(string mediaTypeName, CancellationToken ct)
    {
        var mediaTypes = await MediaType.All(ct);
        _logger?.LogInformation("ResolveMediaType: Looking for '{MediaTypeName}', found {Count} MediaTypes: {Names}",
            mediaTypeName, mediaTypes.Count, string.Join(", ", mediaTypes.Select(mt => $"'{mt.Name}'")));
        return mediaTypes.FirstOrDefault(mt => mt.Name.Equals(mediaTypeName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<MediaType>> GetAllMediaTypes(CancellationToken ct)
    {
        var mediaTypes = await MediaType.All(ct);
        _logger?.LogInformation("GetAllMediaTypes: Found {Count} MediaTypes: {Names}",
            mediaTypes.Count, string.Join(", ", mediaTypes.Select(mt => $"'{mt.Name}'")));
        return mediaTypes.ToList();
    }

    private async Task<List<Media>> FetchFromProviderAsync(string source, MediaType mediaType, int limit, CancellationToken ct)
    {
        if (_providers.TryGetValue(source, out var provider))
        {
            _logger?.LogInformation("Using provider {Code} ({Name}) to fetch {MediaType} items.", provider.Code, provider.Name, mediaType.Name);
            return await provider.FetchAsync(mediaType, limit, ct);
        }

        _logger?.LogWarning("Unknown provider '{Source}'. Falling back to 'local' if available.", source);
        if (_providers.TryGetValue("local", out var local))
        {
            return await local.FetchAsync(mediaType, limit, ct);
        }
        return new List<Media>();
    }

    private async IAsyncEnumerable<List<Media>> FetchStreamFromProviderAsync(string source, MediaType mediaType, int limit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_providers.TryGetValue(source, out var provider))
        {
            _logger?.LogInformation("Using provider {Code} ({Name}) to stream {MediaType} items.", provider.Code, provider.Name, mediaType.Name);
            await foreach (var batch in provider.FetchStreamAsync(mediaType, limit, ct))
            {
                yield return batch;
            }
            yield break;
        }

        _logger?.LogWarning("Unknown provider '{Source}'. Falling back to 'local' if available.", source);
        if (_providers.TryGetValue("local", out var local))
        {
            await foreach (var batch in local.FetchStreamAsync(mediaType, limit, ct))
            {
                yield return batch;
            }
        }
    }

    private async Task<int> ImportDataAsync(List<Media> items, CancellationToken ct)
    {
        try
        {
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (dataSvc is null) return 0;

            return await Media.UpsertMany(items, ct);
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> ExtractTags(IEnumerable<Media> items)
    {
        foreach (var m in items)
        {
            if (m.Genres is { Length: > 0 })
                foreach (var g in m.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
            if (m.Tags is { Length: > 0 })
                foreach (var t in m.Tags) if (!string.IsNullOrWhiteSpace(t)) yield return t.Trim();
        }
    }

    private static IEnumerable<string> ExtractTagsFromSingleMedia(Media media)
    {
        if (media.Genres is { Length: > 0 })
            foreach (var g in media.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
        if (media.Tags is { Length: > 0 })
            foreach (var t in media.Tags) if (!string.IsNullOrWhiteSpace(t)) yield return t.Trim();
    }

    private static IEnumerable<string> ExtractGenres(IEnumerable<Media> items)
    {
        foreach (var m in items)
        {
            if (m.Genres is { Length: > 0 })
                foreach (var g in m.Genres) if (!string.IsNullOrWhiteSpace(g)) yield return g.Trim();
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
            yield return new TagStatDoc { Id = kv.Key.ToLowerInvariant(), Tag = kv.Key, MediaCount = kv.Value, UpdatedAt = now };
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
            yield return new GenreStatDoc { Id = kv.Key.ToLowerInvariant(), Genre = kv.Key, MediaCount = kv.Value, UpdatedAt = now };
        }
    }

    private async Task CatalogTagsAsync(List<Media> items, CancellationToken ct)
    {
        var extractedTags = ExtractTags(items);

        // Apply preemptive filtering
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

        if (flaggedTags.Count > 0)
        {
            await AutoCensorTagsAsync(flaggedTags, ct);
            _logger?.LogInformation("Preemptive filter auto-censored {Count} tags during import", flaggedTags.Count);
        }

        var counts = CountTags(cleanTags);
        var docs = BuildTagDocs(counts).ToList();
        if (docs.Any())
        {
            await TagStatDoc.UpsertMany(docs, ct);
        }
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

    private async Task CatalogGenresAsync(List<Media> items, CancellationToken ct)
    {
        var counts = CountGenres(ExtractGenres(items));
        var docs = BuildGenreDocs(counts).ToList();
        if (docs.Any())
        {
            await GenreStatDoc.UpsertMany(docs, ct);
        }
        _logger?.LogInformation("Genre catalog updated with {Count} genres", counts.Count);
    }

    private async Task<int> EmbedAndIndexAsync(List<Media> items, string? embeddingModel, CancellationToken ct)
    {
        try
        {
            var ai = Ai.TryResolve();
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (ai is null || dataSvc is null) { _logger?.LogWarning("Embedding and vector index skipped: AI or data service unavailable"); return 0; }
            if (!Vector<Media>.IsAvailable)
            {
                //_logger?.LogWarning("Vector repository unavailable. Configure a vector adapter and ensure the service is running and reachable.");
                return 0;
            }

            var total = await RunVectorPipelineAsync(items, embeddingModel, ct);
            //_logger?.LogInformation("Vector pipeline stored {Stored} embeddings for recommendation content", total);
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> UpsertVectorsAsync(List<Media> docs, string? embeddingModel, CancellationToken ct)
    {
        try
        {
            var ai = Ai.TryResolve();
            var dataSvc = (IDataService?)_sp.GetService(typeof(IDataService));
            if (ai is null || dataSvc is null) { _logger?.LogWarning("Embedding and vector index skipped: AI or data service unavailable"); return 0; }
            if (!Vector<Media>.IsAvailable)
            {
                //_logger?.LogWarning("Vector repository unavailable. Configure a vector adapter and ensure the service is running and reachable.");
                return 0;
            }

            var total = await RunVectorPipelineAsync(docs, embeddingModel, ct);
            //_logger?.LogInformation("Vector pipeline stored {Stored} embeddings for admin rebuild", total);
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> RunVectorPipelineAsync(IEnumerable<Media> items, string? model, CancellationToken ct)
    {
        var stored = 0;
        var failures = 0;
        var itemsList = items.ToList();
        var modelId = model ?? "default";

        _logger?.LogDebug("Vector pipeline starting: {Count} items, model={Model}", itemsList.Count, modelId);

        // Step 1: Separate items into cached and uncached
        var cachedItems = new List<(Media media, float[] embedding)>();
        var uncachedItems = new List<Media>();

        foreach (var media in itemsList)
        {
            var embeddingText = BuildEmbeddingText(media);
            var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);
            var cached = await _embeddingCache.GetAsync(contentHash, modelId, typeof(Media).FullName!, ct);

            if (cached != null)
            {
                cachedItems.Add((media, cached.Embedding));
                Interlocked.Increment(ref _cacheHits);
            }
            else
            {
                uncachedItems.Add(media);
                Interlocked.Increment(ref _cacheMisses);
            }
        }

        _logger?.LogInformation("Embedding cache: {Hits} hits, {Misses} misses ({HitRate:P1} hit rate)",
            cachedItems.Count, uncachedItems.Count,
            itemsList.Count > 0 ? (double)cachedItems.Count / itemsList.Count : 0);

        // Step 2: Process cached items (fast path - no AI calls)
        foreach (var (media, embedding) in cachedItems)
        {
            try
            {
                var vectorMetadata = new Dictionary<string, object>
                {
                    ["title"] = media.Title,
                    ["genres"] = media.Genres,
                    ["popularity"] = media.Popularity,
                    ["searchText"] = BuildSearchText(media)  // Required for hybrid search
                };

                await Data<Media, string>.SaveWithVector(media, embedding, vectorMetadata, ct).ConfigureAwait(false);
                Interlocked.Increment(ref stored);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Vector pipeline (cached): failed to save vector for media {Id}", media.Id);
                Interlocked.Increment(ref failures);
            }
        }

        // Step 3: Process uncached items (slow path - requires AI calls)
        if (uncachedItems.Count > 0)
        {
            await uncachedItems
                .ToAsyncEnumerable()
                .Tokenize(m => BuildEmbeddingText(m), model != null ? new AiTokenizeOptions { Model = model } : new AiTokenizeOptions())
                .Branch(branch => branch
                    .OnSuccess(success => success
                        .Mutate(envelope =>
                        {
                            envelope.Features["vector:metadata"] = new
                            {
                                title = envelope.Entity.Title,
                                genres = envelope.Entity.Genres,
                                popularity = envelope.Entity.Popularity,
                                searchText = BuildSearchText(envelope.Entity)  // Required for hybrid search
                            };
                        })
                        .Do(async (envelope, ct) =>
                        {
                            if (envelope.Features.TryGetValue(PipelineFeatureKeys.Embedding, out var embeddingObj)
                                && embeddingObj is float[] embedding)
                            {
                                var vectorMetadata = envelope.Features.TryGetValue("vector:metadata", out var metadataObj)
                                                   ? metadataObj as IReadOnlyDictionary<string, object>
                                                   : null;

                                try
                                {
                                    await Data<Media, string>.SaveWithVector(envelope.Entity, embedding, vectorMetadata, ct).ConfigureAwait(false);
                                    envelope.Metadata["vector:affected"] = 1;
                                    Interlocked.Add(ref stored, 1);

                                    // Cache the embedding for future use
                                    var embeddingText = BuildEmbeddingText(envelope.Entity);
                                    var contentHash = EmbeddingCache.ComputeContentHash(embeddingText);
                                    await _embeddingCache.SetAsync(contentHash, modelId, embedding, typeof(Media).FullName!, ct);
                                }
                                catch (InvalidOperationException ex)
                                {
                                    envelope.Metadata["vector:affected"] = 0;
                                    Interlocked.Increment(ref failures);
                                }
                                catch (Exception ex)
                                {
                                    envelope.Metadata["vector:affected"] = 0;
                                    Interlocked.Increment(ref failures);
                                }
                            }
                            else
                            {
                                _logger?.LogWarning("Vector pipeline: no embeddings found for media {Id}", envelope.Entity.Id);
                                envelope.Metadata["vector:affected"] = 0;
                                Interlocked.Increment(ref failures);
                            }
                        }))
                    .OnFailure(failure => failure
                        .Tap(env => _logger?.LogWarning("Vector pipeline: tokenization failed for media {Id}: {Error}", env.Entity.Id, env.Error?.Message ?? "unknown"))
                        .Tap(_ => Interlocked.Increment(ref failures))
                        .Trace(env => $"Vector pipeline failed for media {env.Entity.Id}: {env.Error?.Message ?? "unknown"}")))
                .ExecuteAsync(ct);
        }

        if (failures > 0)
        {
            _logger?.LogWarning("Vector pipeline encountered {Failures} failures", failures);
        }

        _logger?.LogInformation("Vector pipeline completed: {Stored} vectors stored ({Cached} from cache, {Generated} newly generated)",
            stored, cachedItems.Count, uncachedItems.Count - failures);

        return stored;
    }


    public string BuildEmbeddingText(Media m)
    {
        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.Title)) titles.Add(m.Title);
        if (!string.IsNullOrWhiteSpace(m.TitleEnglish) && m.TitleEnglish != m.Title) titles.Add(m.TitleEnglish!);
        if (!string.IsNullOrWhiteSpace(m.TitleRomaji) && m.TitleRomaji != m.Title) titles.Add(m.TitleRomaji!);
        if (!string.IsNullOrWhiteSpace(m.TitleNative) && m.TitleNative != m.Title) titles.Add(m.TitleNative!);
        if (m.Synonyms is { Length: > 0 }) titles.AddRange(m.Synonyms);

        var tags = new List<string>();
        if (m.Genres is { Length: > 0 }) tags.AddRange(m.Genres);
        if (m.Tags is { Length: > 0 }) tags.AddRange(m.Tags);

        var text = $"{string.Join(" / ", titles.Distinct())}\n\n{m.Synopsis}\n\nTags: {string.Join(", ", tags.Distinct())}";
        var trimmedText = text.Trim();
        //_logger?.LogDebug("BuildEmbeddingText for media {Id}: {Length} chars, text preview: {Preview}", m.Id, trimmedText.Length, trimmedText.Length > 100 ? trimmedText.Substring(0, 100) + "..." : trimmedText);
        return trimmedText;
    }

    /// <summary>
    /// Builds search text for BM25 hybrid search - just title variants for keyword matching.
    /// </summary>
    public string BuildSearchText(Media m)
    {
        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.Title)) titles.Add(m.Title);
        if (!string.IsNullOrWhiteSpace(m.TitleEnglish) && m.TitleEnglish != m.Title) titles.Add(m.TitleEnglish!);
        if (!string.IsNullOrWhiteSpace(m.TitleRomaji) && m.TitleRomaji != m.Title) titles.Add(m.TitleRomaji!);
        if (!string.IsNullOrWhiteSpace(m.TitleNative) && m.TitleNative != m.Title) titles.Add(m.TitleNative!);
        if (m.Synonyms is { Length: > 0 }) titles.AddRange(m.Synonyms);

        return string.Join(" ", titles.Distinct());
    }
}
