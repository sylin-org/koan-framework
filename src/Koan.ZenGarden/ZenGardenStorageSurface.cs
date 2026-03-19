using Koan.Core.Hosting.App;
using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

public sealed class ZenGardenStorageSurface
{
    public IDisposable On(
        string seedBank,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        var subscription = ZenGardenSubscription.ForStorage(seedBank);
        return ZenGarden.Client.Subscribe(subscription, handler, options);
    }

    public IDisposable OnAny(
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        var subscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank
        };
        return ZenGarden.Client.Subscribe(subscription, handler, options);
    }

    public Task<IReadOnlyList<ZenGardenToolSnapshot>> Catalog(
        CancellationToken cancellationToken = default)
    {
        var subscription = new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank
        };
        return ZenGarden.Client.CatalogAsync(subscription, cancellationToken);
    }

    public async Task<ZenGardenToolSnapshot?> Catalog(
        string seedBank,
        CancellationToken cancellationToken = default)
    {
        var subscription = ZenGardenSubscription.ForStorage(seedBank);
        var tools = await ZenGarden.Client.CatalogAsync(subscription, cancellationToken);
        return tools.FirstOrDefault();
    }

    /// <summary>
    /// Subscribe to content change events on a storage seed-bank.
    ///
    /// Under the hood:
    /// 1. Subscribes to StorageTick SSE at GET /api/v1/stone/storage/stream.
    /// 2. On tick, pulls GET /api/v1/stone/storage/banks/{name}/changes?since={cursor}.
    /// 3. Parses changelog entries into typed <see cref="StorageContentChange"/> records.
    /// 4. Filters by bucket prefix matching <see cref="AppHost.Identity"/>.Code.
    /// 5. Dispatches to the handler.
    ///
    /// This is opt-in — only subscribes when called.
    /// </summary>
    /// <param name="seedBank">The seed-bank name (e.g., "storage").</param>
    /// <param name="handler">Callback receiving a batch of changes per tick.</param>
    /// <param name="bucketPrefixFilter">
    /// Optional bucket prefix to filter changes. Defaults to <see cref="AppHost.Identity"/>.Code.
    /// Pass null to receive all changes.
    /// </param>
    /// <returns>A disposable subscription handle. Dispose to stop listening.</returns>
    public IDisposable OnContentChange(
        string seedBank,
        Func<IReadOnlyList<StorageContentChange>, CancellationToken, ValueTask> handler,
        string? bucketPrefixFilter = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var filter = bucketPrefixFilter ?? AppHost.Identity.Code;
        return new StorageContentChangeSubscription(seedBank, handler, filter);
    }
}

/// <summary>
/// Manages SSE subscription to StorageTick and pulls changes on each tick.
/// </summary>
internal sealed class StorageContentChangeSubscription : IDisposable
{
    private readonly string _seedBank;
    private readonly Func<IReadOnlyList<StorageContentChange>, CancellationToken, ValueTask> _handler;
    private readonly string? _bucketPrefixFilter;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    private long _cursor;
    private bool _disposed;

    public StorageContentChangeSubscription(
        string seedBank,
        Func<IReadOnlyList<StorageContentChange>, CancellationToken, ValueTask> handler,
        string? bucketPrefixFilter)
    {
        _seedBank = seedBank;
        _handler = handler;
        _bucketPrefixFilter = bucketPrefixFilter;
        _logger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
            .CreateLogger<StorageContentChangeSubscription>();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Start the SSE listener loop
        _ = Task.Run(() => ListenForTicksAsync(_cts.Token));
    }

    private async Task ListenForTicksAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var boundEndpoint = ZenGarden.Client.BoundEndpoint;
                if (string.IsNullOrWhiteSpace(boundEndpoint))
                {
                    // Wait for ZenGarden to bind before subscribing
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    continue;
                }

                var streamUrl = $"{boundEndpoint.TrimEnd('/')}/api/v1/stone/storage/stream";

                using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // SSE format: "data: ..." or "event: storage-tick"
                    if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                    {
                        var eventType = line["event:".Length..].Trim();
                        if (eventType.Equals("storage-tick", StringComparison.OrdinalIgnoreCase))
                        {
                            // Next data line contains tick info; pull changes
                            await PullChangesAsync(boundEndpoint, ct);
                        }
                    }
                    else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        // A data event without explicit event type — treat as tick
                        await PullChangesAsync(boundEndpoint, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StorageContentChangeSubscription: SSE stream error, reconnecting in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { break; }
            }
        }
    }

    private async Task PullChangesAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            var changesUrl = $"{endpoint.TrimEnd('/')}/api/v1/stone/storage/banks/{_seedBank}/changes?since={_cursor}";
            var json = await _httpClient.GetStringAsync(changesUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;

            // Response can be envelope { data: [...] } or bare array
            var entries = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("data", out var dataProp) ? dataProp : root;

            if (entries.ValueKind != JsonValueKind.Array)
                return;

            var changes = new List<StorageContentChange>();
            foreach (var entry in entries.EnumerateArray())
            {
                var change = ParseChange(entry);
                if (change is null) continue;

                // Filter by bucket prefix if configured
                if (!string.IsNullOrWhiteSpace(_bucketPrefixFilter)
                    && !change.Path.StartsWith(_bucketPrefixFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                changes.Add(change);

                // Track cursor
                if (change.Sequence > _cursor)
                    _cursor = change.Sequence;
            }

            if (changes.Count > 0)
            {
                await _handler(changes, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StorageContentChangeSubscription: failed to pull changes for bank {Bank}", _seedBank);
        }
    }

    private StorageContentChange? ParseChange(JsonElement entry)
    {
        try
        {
            var path = entry.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(path)) return null;

            var opString = entry.TryGetProperty("op", out var opProp) ? opProp.GetString() : "create";
            var op = opString?.ToLowerInvariant() switch
            {
                "create" or "put" => StorageChangeOp.Create,
                "modify" or "update" => StorageChangeOp.Modify,
                "delete" or "remove" => StorageChangeOp.Delete,
                "rename" or "move" => StorageChangeOp.Rename,
                _ => StorageChangeOp.Create
            };

            var oldPath = entry.TryGetProperty("old_path", out var oldPathProp) ? oldPathProp.GetString() : null;
            long? size = entry.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                ? sizeProp.GetInt64() : null;
            var timestamp = entry.TryGetProperty("timestamp", out var tsProp) && tsProp.ValueKind == JsonValueKind.String
                ? DateTimeOffset.TryParse(tsProp.GetString(), out var ts) ? ts : DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow;
            var sequence = entry.TryGetProperty("sequence", out var seqProp) && seqProp.ValueKind == JsonValueKind.Number
                ? seqProp.GetInt64()
                : entry.TryGetProperty("seq", out var seqProp2) && seqProp2.ValueKind == JsonValueKind.Number
                    ? seqProp2.GetInt64() : 0;

            return new StorageContentChange
            {
                Op = op,
                Path = path,
                OldPath = oldPath,
                Size = size,
                Timestamp = timestamp,
                Sequence = sequence,
                BankName = _seedBank
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _httpClient.Dispose();
    }
}
