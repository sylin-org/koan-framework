using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Greenfield tools-domain runtime for Zen Garden.
/// Consumes snapshot + SSE stream and emits derived availability signals.
/// </summary>
public sealed class ZenGardenClient : IZenGardenClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ZenGardenClient> _logger;
    private readonly ZenGardenOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _ownsHttpClient;

    private readonly ConcurrentDictionary<string, ZenGardenToolSnapshot> _tools =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, SubscriptionRegistration> _subscriptions = new();
    private readonly ConcurrentDictionary<string, CachedMossStone> _stoneCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _seenEventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _seenEventOrder = new();
    private readonly object _seenEventLock = new();
    private readonly object _disposeLock = new();
    private readonly object _bindLock = new();

    private CancellationTokenSource _lifetimeCts = new();
    private Task? _streamLoopTask;
    private int _streamStarted;
    private bool _disposed;

    private long? _cursor;
    private string? _lastEventId;
    private CachedMossStone? _boundStone;

    public ZenGardenClient(
        HttpClient httpClient,
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options = null)
        : this(httpClient, logger, options, ownsHttpClient: false)
    {
    }

    public ZenGardenClient(
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options = null)
        : this(new HttpClient(), logger, options, ownsHttpClient: true)
    {
    }

    private ZenGardenClient(
        HttpClient httpClient,
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options,
        bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? new ZenGardenOptions();
        _ownsHttpClient = ownsHttpClient;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);
    }

    public IDisposable Subscribe(
        ZenGardenSubscription subscription,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
        ZenGardenWatchOptions? options = null)
    {
        ThrowIfDisposed();

        if (subscription is null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var registration = new SubscriptionRegistration(
            Guid.NewGuid(),
            subscription,
            options ?? new ZenGardenWatchOptions(),
            handler);

        _subscriptions[registration.Id] = registration;
        EnsureStreamLoopStarted();

        if (registration.Options.EmitInitialState)
        {
            _ = EmitInitialStateAsync(registration, _lifetimeCts.Token);
        }

        return new SubscriptionHandle(this, registration.Id);
    }

    public async Task<IReadOnlyList<ZenGardenToolSnapshot>> CatalogAsync(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (subscription is null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        using var response = await GetSnapshotWithRecoveryAsync(subscription, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);

        var payload = ExtractDataOrSelf(document.RootElement);
        var cursor = ReadLong(payload, "cursor");
        if (cursor is not null)
        {
            _cursor = cursor.Value;
        }

        var snapshots = ParseToolList(payload);
        foreach (var snapshot in snapshots)
        {
            _tools[snapshot.ToolFqid] = snapshot;
        }

        var filtered = snapshots
            .Where(subscription.Matches)
            .Where(subscription.RequirementsSatisfiedBy)
            .OrderBy(x => x.ToolFqid, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return filtered;
    }

    public bool TryGetCurrent(string toolFqid, out ZenGardenToolSnapshot snapshot)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(toolFqid))
        {
            snapshot = default!;
            return false;
        }

        return _tools.TryGetValue(toolFqid, out snapshot!);
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetimeCts.Cancel();
        }

        try
        {
            _streamLoopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // best effort
        }

        _lifetimeCts.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task EmitInitialStateAsync(SubscriptionRegistration registration, CancellationToken ct)
    {
        try
        {
            var tools = await CatalogAsync(registration.Subscription, ct);
            foreach (var snapshot in tools)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                ZenGardenAvailabilityEventKind kind;
                if (registration.Subscription.Requires.Count > 0)
                {
                    kind = registration.Subscription.RequirementsSatisfiedBy(snapshot)
                        ? ZenGardenAvailabilityEventKind.CapabilitiesSatisfied
                        : ZenGardenAvailabilityEventKind.CapabilitiesUnsatisfied;
                }
                else
                {
                    kind = snapshot.Ready
                        ? ZenGardenAvailabilityEventKind.Online
                        : ZenGardenAvailabilityEventKind.Offline;
                }

                var evt = new ZenGardenAvailabilityEvent
                {
                    Kind = kind,
                    Current = snapshot,
                    Previous = null,
                    Cursor = _cursor,
                    EventId = null,
                    Timestamp = DateTimeOffset.UtcNow
                };

                await registration.Handler(evt, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to emit initial state for subscription {SubscriptionId}", registration.Id);
        }
    }

    private void EnsureStreamLoopStarted()
    {
        if (Interlocked.CompareExchange(ref _streamStarted, 1, 0) != 0)
        {
            return;
        }

        _streamLoopTask = Task.Run(() => RunStreamLoopAsync(_lifetimeCts.Token), _lifetimeCts.Token);
    }

    private async Task RunStreamLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConsumeStreamAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ZenGarden tools stream disconnected; reconnecting.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.StreamReconnectDelaySeconds), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ConsumeStreamAsync(CancellationToken ct)
    {
        using var response = await OpenStreamWithRecoveryAsync(ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string eventName = "message";
        string? eventId = null;
        var data = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var payload = data.ToString();
                    await ProcessStreamEventAsync(eventName, eventId, payload, ct);
                }

                eventName = "message";
                eventId = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                eventId = line[3..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }
                data.Append(line[5..].TrimStart());
            }
        }
    }

    private async Task ProcessStreamEventAsync(string eventName, string? eventId, string payloadJson, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            if (!RememberEventId(eventId))
            {
                return;
            }

            _lastEventId = eventId;
        }

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var eventPayload = ExtractDataOrSelf(root);

        var cursor = ReadLong(eventPayload, "cursor");
        if (cursor is not null)
        {
            _cursor = cursor.Value;
        }

        switch (eventName.Trim())
        {
            case "tools.snapshot":
                await ApplySnapshotAsync(eventPayload, eventId, ct);
                break;
            case "tool.upsert":
                if (TryParseSnapshot(eventPayload, out var upsertSnapshot))
                {
                    await ApplyUpsertAsync(upsertSnapshot, eventId, cursor, ct);
                }
                break;
            case "tool.remove":
                await ApplyRemoveAsync(eventPayload, eventId, cursor, ct);
                break;
            case "tools.heartbeat":
                break;
            default:
                await TryProcessGenericEventAsync(eventPayload, eventId, cursor, ct);
                break;
        }
    }

    private async Task ApplySnapshotAsync(JsonElement payload, string? eventId, CancellationToken ct)
    {
        foreach (var snapshot in ParseToolList(payload))
        {
            await ApplyUpsertAsync(snapshot, eventId, _cursor, ct);
        }

        if (TryGetProperty(payload, "replay", out var replay) && replay.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in replay.EnumerateArray())
            {
                await TryProcessGenericEventAsync(item, eventId, _cursor, ct);
            }
        }
    }

    private async Task TryProcessGenericEventAsync(JsonElement payload, string? eventId, long? cursor, CancellationToken ct)
    {
        var inner = ExtractDataOrSelf(payload);
        var candidateEvent = ReadString(payload, "event") ?? ReadString(payload, "type");

        if (string.Equals(candidateEvent, "tool.remove", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyRemoveAsync(inner, eventId, cursor, ct);
            return;
        }

        if (TryParseSnapshot(inner, out var snapshot))
        {
            await ApplyUpsertAsync(snapshot, eventId, cursor, ct);
        }
    }

    private async Task ApplyUpsertAsync(
        ZenGardenToolSnapshot current,
        string? eventId,
        long? cursor,
        CancellationToken ct)
    {
        ZenGardenToolSnapshot? previous = null;
        if (_tools.TryGetValue(current.ToolFqid, out var existing))
        {
            previous = existing;
            if (current.Revision < existing.Revision)
            {
                return;
            }
        }

        _tools[current.ToolFqid] = current;
        await PublishDerivedEventsAsync(current, previous, eventId, cursor, ct);
    }

    private async Task ApplyRemoveAsync(JsonElement payload, string? eventId, long? cursor, CancellationToken ct)
    {
        var toolFqid = ReadString(payload, "tool_fqid");
        if (string.IsNullOrWhiteSpace(toolFqid))
        {
            return;
        }

        _tools.TryGetValue(toolFqid, out var previous);
        _tools.TryRemove(toolFqid, out _);

        var revision = ReadLong(payload, "revision")
            ?? (previous is null ? 0 : previous.Revision + 1);

        var current = new ZenGardenToolSnapshot
        {
            ToolFqid = toolFqid,
            ToolUid = ReadString(payload, "tool_uid") ?? previous?.ToolUid,
            ToolType = ParseToolType(ReadString(payload, "tool_type")) ?? previous?.ToolType ?? ZenGardenToolType.Unknown,
            State = ZenGardenToolState.Unavailable,
            Ready = false,
            Revision = revision,
            StoneId = previous?.StoneId,
            StoneName = previous?.StoneName,
            Connection = previous?.Connection,
            Capabilities = previous?.Capabilities
                ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            CapabilityRevision = previous?.CapabilityRevision,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await PublishDerivedEventsAsync(current, previous, eventId, cursor, ct);
    }

    private async Task PublishDerivedEventsAsync(
        ZenGardenToolSnapshot current,
        ZenGardenToolSnapshot? previous,
        string? eventId,
        long? cursor,
        CancellationToken ct)
    {
        var registrations = _subscriptions.Values.ToArray();
        foreach (var registration in registrations)
        {
            if (!registration.Subscription.Matches(current))
            {
                continue;
            }

            var requiredNow = registration.Subscription.RequirementsSatisfiedBy(current);
            var requiredPrevious = previous is not null &&
                registration.Subscription.RequirementsSatisfiedBy(previous);

            var events = new List<ZenGardenAvailabilityEvent>();

            if (previous is null)
            {
                events.Add(new ZenGardenAvailabilityEvent
                {
                    Kind = current.Ready ? ZenGardenAvailabilityEventKind.Online : ZenGardenAvailabilityEventKind.Offline,
                    Current = current,
                    Previous = previous,
                    EventId = eventId,
                    Cursor = cursor,
                    Timestamp = DateTimeOffset.UtcNow
                });

                if (registration.Subscription.Requires.Count > 0)
                {
                    events.Add(new ZenGardenAvailabilityEvent
                    {
                        Kind = requiredNow
                            ? ZenGardenAvailabilityEventKind.CapabilitiesSatisfied
                            : ZenGardenAvailabilityEventKind.CapabilitiesUnsatisfied,
                        Current = current,
                        Previous = previous,
                        EventId = eventId,
                        Cursor = cursor,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }
            else
            {
                if (!previous.Ready && current.Ready)
                {
                    events.Add(new ZenGardenAvailabilityEvent
                    {
                        Kind = ZenGardenAvailabilityEventKind.Online,
                        Current = current,
                        Previous = previous,
                        EventId = eventId,
                        Cursor = cursor,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                if (previous.Ready && !current.Ready)
                {
                    events.Add(new ZenGardenAvailabilityEvent
                    {
                        Kind = ZenGardenAvailabilityEventKind.Offline,
                        Current = current,
                        Previous = previous,
                        EventId = eventId,
                        Cursor = cursor,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                if (registration.Subscription.Requires.Count > 0 && requiredPrevious != requiredNow)
                {
                    events.Add(new ZenGardenAvailabilityEvent
                    {
                        Kind = requiredNow
                            ? ZenGardenAvailabilityEventKind.CapabilitiesSatisfied
                            : ZenGardenAvailabilityEventKind.CapabilitiesUnsatisfied,
                        Current = current,
                        Previous = previous,
                        EventId = eventId,
                        Cursor = cursor,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                if (events.Count == 0 && current.Revision != previous.Revision)
                {
                    events.Add(new ZenGardenAvailabilityEvent
                    {
                        Kind = ZenGardenAvailabilityEventKind.Changed,
                        Current = current,
                        Previous = previous,
                        EventId = eventId,
                        Cursor = cursor,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
            }

            foreach (var evt in events)
            {
                try
                {
                    await registration.Handler(evt, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "ZenGarden subscription handler failed for {SubscriptionId}", registration.Id);
                }
            }
        }
    }

    private bool RememberEventId(string eventId)
    {
        lock (_seenEventLock)
        {
            if (_seenEventIds.Contains(eventId))
            {
                return false;
            }

            _seenEventIds.Add(eventId);
            _seenEventOrder.Enqueue(eventId);

            while (_seenEventOrder.Count > Math.Max(1, _options.DedupeWindowSize))
            {
                var old = _seenEventOrder.Dequeue();
                _seenEventIds.Remove(old);
            }

            return true;
        }
    }

    private async Task<HttpResponseMessage> GetSnapshotWithRecoveryAsync(
        ZenGardenSubscription subscription,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = await EnsureBoundEndpointAsync(ct, forceRediscovery: attempt > 0);
            var uri = BuildSnapshotUri(endpoint, subscription, since: null);

            try
            {
                var response = await _httpClient.GetAsync(uri, ct);
                if (attempt == 0 && ShouldRetryWithRediscovery(response.StatusCode))
                {
                    response.Dispose();
                    InvalidateBoundStone($"Snapshot call failed with {(int)response.StatusCode}.");
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (attempt == 0 && ShouldRetryWithRediscovery(ex))
            {
                InvalidateBoundStone("Snapshot call failed with connection exception.", ex);
            }
        }

        var fallbackEndpoint = await EnsureBoundEndpointAsync(ct, forceRediscovery: true);
        return await _httpClient.GetAsync(BuildSnapshotUri(fallbackEndpoint, subscription, since: null), ct);
    }

    private async Task<HttpResponseMessage> OpenStreamWithRecoveryAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = await EnsureBoundEndpointAsync(ct, forceRediscovery: attempt > 0);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildStreamUri(endpoint, _cursor));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            if (!string.IsNullOrWhiteSpace(_lastEventId))
            {
                request.Headers.TryAddWithoutValidation("Last-Event-ID", _lastEventId);
            }

            try
            {
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (attempt == 0 && ShouldRetryWithRediscovery(response.StatusCode))
                {
                    response.Dispose();
                    InvalidateBoundStone($"Stream open failed with {(int)response.StatusCode}.");
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (attempt == 0 && ShouldRetryWithRediscovery(ex))
            {
                InvalidateBoundStone("Stream open failed with connection exception.", ex);
            }
        }

        var finalEndpoint = await EnsureBoundEndpointAsync(ct, forceRediscovery: true);
        using var finalRequest = new HttpRequestMessage(HttpMethod.Get, BuildStreamUri(finalEndpoint, _cursor));
        finalRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(_lastEventId))
        {
            finalRequest.Headers.TryAddWithoutValidation("Last-Event-ID", _lastEventId);
        }

        return await _httpClient.SendAsync(finalRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task<string> EnsureBoundEndpointAsync(CancellationToken ct, bool forceRediscovery = false)
    {
        ThrowIfDisposed();

        if (!forceRediscovery)
        {
            var bound = _boundStone;
            if (bound is not null)
            {
                return bound.Endpoint;
            }
        }

        if (!forceRediscovery)
        {
            var selector = ResolvePreferredSelector();
            if (!string.IsNullOrWhiteSpace(selector))
            {
                var selected = await ResolveStoneFromSelectorAsync(selector, ct);
                if (selected is not null)
                {
                    return BindStone(selected).Endpoint;
                }
            }

            var cached = await ResolveFromCacheAsync(ct);
            if (cached is not null)
            {
                return BindStone(cached).Endpoint;
            }
        }

        if (_options.EnableDiscovery)
        {
            var discovered = await DiscoverStonesAsync(
                ResolveDiscoveryTimeout(),
                waitForAll: true,
                ct);

            var reachable = await FindFirstReachableAsync(discovered, ct);
            if (reachable is not null)
            {
                return BindStone(reachable).Endpoint;
            }

            if (discovered.Count > 0)
            {
                return BindStone(discovered[0]).Endpoint;
            }
        }

        throw new InvalidOperationException(
            "Unable to resolve a Moss endpoint. Configure Koan:ZenGarden:Endpoint or GARDEN_STONE, or ensure UDP discovery on port 7184 is available.");
    }

    private string? ResolvePreferredSelector()
    {
        var configured = NormalizeEndpointOrSelector(_options.Endpoint);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var env = NormalizeEndpointOrSelector(System.Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.GardenStone));
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        return null;
    }

    private async Task<CachedMossStone?> ResolveStoneFromSelectorAsync(string selector, CancellationToken ct)
    {
        if (TryNormalizeAbsoluteEndpoint(selector, out var endpoint))
        {
            var candidate = new CachedMossStone
            {
                Endpoint = endpoint,
                StoneName = new Uri(endpoint).Host,
                LastSeenUtc = DateTimeOffset.UtcNow
            };

            if (await IsMossReachableAsync(candidate, ct))
            {
                return CacheStone(candidate);
            }

            return null;
        }

        PurgeExpiredCacheEntries();

        foreach (var cached in _stoneCache.Values)
        {
            if (!MatchesSelector(cached, selector))
            {
                continue;
            }

            if (await IsMossReachableAsync(cached, ct))
            {
                return CacheStone(cached with { LastSeenUtc = DateTimeOffset.UtcNow });
            }
        }

        if (!_options.EnableDiscovery)
        {
            return null;
        }

        var discovered = await DiscoverStonesAsync(
            ResolveDiscoveryTimeout(),
            waitForAll: true,
            ct);

        foreach (var stone in discovered)
        {
            if (!MatchesSelector(stone, selector))
            {
                continue;
            }

            if (await IsMossReachableAsync(stone, ct))
            {
                return CacheStone(stone);
            }
        }

        return null;
    }

    private async Task<CachedMossStone?> ResolveFromCacheAsync(CancellationToken ct)
    {
        PurgeExpiredCacheEntries();
        var cached = _stoneCache.Values
            .DistinctBy(x => x.Endpoint)
            .OrderByDescending(x => x.LastSeenUtc)
            .ToArray();

        foreach (var stone in cached)
        {
            if (await IsMossReachableAsync(stone, ct))
            {
                return CacheStone(stone with { LastSeenUtc = DateTimeOffset.UtcNow });
            }
        }

        return null;
    }

    private async Task<CachedMossStone?> FindFirstReachableAsync(
        IReadOnlyList<CachedMossStone> stones,
        CancellationToken ct)
    {
        foreach (var stone in stones)
        {
            if (await IsMossReachableAsync(stone, ct))
            {
                return CacheStone(stone with { LastSeenUtc = DateTimeOffset.UtcNow });
            }
        }

        return null;
    }

    private void InvalidateBoundStone(string reason, Exception? ex = null)
    {
        CachedMossStone? previous;
        lock (_bindLock)
        {
            previous = _boundStone;
            _boundStone = null;
        }

        if (previous is null)
        {
            return;
        }

        if (ex is null)
        {
            _logger.LogWarning("Unbound Moss endpoint {Endpoint}: {Reason}", previous.Endpoint, reason);
        }
        else
        {
            _logger.LogWarning(ex, "Unbound Moss endpoint {Endpoint}: {Reason}", previous.Endpoint, reason);
        }
    }

    private CachedMossStone BindStone(CachedMossStone stone)
    {
        var cached = CacheStone(stone with { LastSeenUtc = DateTimeOffset.UtcNow });

        lock (_bindLock)
        {
            _boundStone = cached;
        }

        _logger.LogDebug("Bound to Moss endpoint {Endpoint} ({Stone})", cached.Endpoint, cached.StoneName);
        return cached;
    }

    private CachedMossStone CacheStone(CachedMossStone stone)
    {
        var key = string.IsNullOrWhiteSpace(stone.StoneId)
            ? stone.StoneName
            : stone.StoneId!;

        _stoneCache[key] = stone;
        if (!string.Equals(key, stone.StoneName, StringComparison.OrdinalIgnoreCase))
        {
            _stoneCache[stone.StoneName] = stone;
        }

        return stone;
    }

    private bool IsCacheExpired(CachedMossStone stone)
    {
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.DiscoveryCacheTtlSeconds));
        return DateTimeOffset.UtcNow - stone.LastSeenUtc > ttl;
    }

    private void PurgeExpiredCacheEntries()
    {
        foreach (var pair in _stoneCache)
        {
            if (IsCacheExpired(pair.Value))
            {
                _stoneCache.TryRemove(pair.Key, out _);
            }
        }
    }

    private async Task<bool> IsMossReachableAsync(CachedMossStone stone, CancellationToken ct)
    {
        var healthUri = BuildHealthUri(stone.Endpoint);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            using var response = await _httpClient.GetAsync(healthUri, linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<CachedMossStone>> DiscoverStonesAsync(
        TimeSpan timeout,
        bool waitForAll,
        CancellationToken cancellationToken)
    {
        var request = new DiscoveryRequestEnvelope
        {
            Type = Constants.Discovery.RequestType,
            MsgId = Guid.NewGuid().ToString("N"),
            Data = new DiscoveryRequestData
            {
                Discover = Constants.Discovery.DiscoverTargetMoss,
                RequestId = $"koan-{Guid.NewGuid():N}",
                Requester = "koan-framework"
            }
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(request, _jsonOptions);
        var discovered = new Dictionary<string, CachedMossStone>(StringComparer.OrdinalIgnoreCase);

        var multicastGroup = ResolveDiscoveryMulticastGroup();
        var port = ResolveDiscoveryPort();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.ReceiveTimeout = 500;

        var bindAddress = GetLanBindAddress() ?? IPAddress.Any;
        socket.Bind(new IPEndPoint(bindAddress, port));

        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(multicastGroup), port);
        try
        {
            await socket.SendToAsync(payload, SocketFlags.None, multicastEndpoint, cancellationToken);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Multicast discovery send failed.");
        }

        if (ResolveDiscoveryBroadcastFallback())
        {
            try
            {
                await socket.SendToAsync(payload, SocketFlags.None, new IPEndPoint(IPAddress.Broadcast, port), cancellationToken);
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Directed broadcast discovery send failed.");
            }
        }

        if (ResolveDiscoveryLimitedBroadcastFallback())
        {
            try
            {
                await socket.SendToAsync(
                    payload,
                    SocketFlags.None,
                    new IPEndPoint(IPAddress.Parse("255.255.255.255"), port),
                    cancellationToken);
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Limited broadcast discovery send failed.");
            }
        }

        var deadline = DateTime.UtcNow.Add(timeout);
        var buffer = new byte[8192];

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(500, remaining.TotalMilliseconds)));

                var result = await socket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    new IPEndPoint(IPAddress.Any, 0),
                    receiveCts.Token);

                if (!TryParseDiscoveryResponse(buffer.AsSpan(0, result.ReceivedBytes), out var response) ||
                    response?.Data is null)
                {
                    continue;
                }

                var stone = ToCachedStone(response.Data, result.RemoteEndPoint as IPEndPoint);
                if (stone is null)
                {
                    continue;
                }

                discovered[stone.CacheKey] = CacheStone(stone);
                if (!waitForAll)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // per-read timeout
            }
            catch (SocketException) when (!cancellationToken.IsCancellationRequested)
            {
                // transient read timeout
            }
        }

        return discovered.Values.ToArray();
    }

    private static bool ShouldRetryWithRediscovery(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.NotFound;
    }

    private static bool ShouldRetryWithRediscovery(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is HttpRequestException)
        {
            return true;
        }

        if (ex is IOException)
        {
            return true;
        }

        return ex.InnerException is SocketException;
    }

    private static string? NormalizeEndpointOrSelector(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().TrimEnd('/');
    }

    private static bool TryNormalizeAbsoluteEndpoint(string raw, out string endpoint)
    {
        endpoint = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Trim().TrimEnd('/');

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute) &&
            IsSupportedHttpScheme(absolute.Scheme) &&
            !string.IsNullOrWhiteSpace(absolute.Host))
        {
            endpoint = NormalizeEndpoint(absolute);
            return true;
        }

        if (candidate.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        if (candidate.Contains(':', StringComparison.Ordinal) &&
            Uri.TryCreate($"http://{candidate}", UriKind.Absolute, out var hostWithPort) &&
            !string.IsNullOrWhiteSpace(hostWithPort.Host) &&
            hostWithPort.Port > 0)
        {
            endpoint = NormalizeEndpoint(hostWithPort);
            return true;
        }

        if (candidate.Contains('.', StringComparison.Ordinal) &&
            Uri.TryCreate($"http://{candidate}:{Constants.Moss.DefaultPort}", UriKind.Absolute, out var hostOnly))
        {
            endpoint = NormalizeEndpoint(hostOnly);
            return true;
        }

        return false;
    }

    private static bool MatchesSelector(CachedMossStone stone, string selector)
    {
        var normalized = NormalizeEndpointOrSelector(selector);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (TryNormalizeAbsoluteEndpoint(normalized, out var endpoint))
        {
            return string.Equals(stone.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(stone.StoneName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(stone.StoneId) &&
            string.Equals(stone.StoneId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            var withoutLocal = normalized[..^6];
            if (string.Equals(stone.StoneName, withoutLocal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (Uri.TryCreate(stone.Endpoint, UriKind.Absolute, out var stoneUri))
        {
            var hostPort = $"{stoneUri.Host}:{stoneUri.Port}";
            if (string.Equals(stoneUri.Host, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hostPort, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var localHost = $"{stoneUri.Host}.local";
            if (string.Equals(localHost, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private int ResolveDiscoveryPort()
    {
        if (TryReadIntEnvironment(
            Constants.EnvironmentVariables.DiscoveryPort,
            minValue: 1,
            maxValue: 65535,
            out var envPort))
        {
            return envPort;
        }

        if (_options.DiscoveryPort > 0 && _options.DiscoveryPort <= 65535)
        {
            return _options.DiscoveryPort;
        }

        return Constants.Discovery.DefaultPort;
    }

    private TimeSpan ResolveDiscoveryTimeout()
    {
        if (TryReadIntEnvironment(
            Constants.EnvironmentVariables.GardenDiscoveryTimeoutSeconds,
            minValue: 1,
            maxValue: 60,
            out var envTimeoutSeconds))
        {
            return TimeSpan.FromSeconds(envTimeoutSeconds);
        }

        var optionTimeout = _options.DiscoveryTimeoutSeconds > 0
            ? _options.DiscoveryTimeoutSeconds
            : Constants.Discovery.DefaultTimeoutSeconds;

        return TimeSpan.FromSeconds(optionTimeout);
    }

    private string ResolveDiscoveryMulticastGroup()
    {
        var envGroup = System.Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.DiscoveryMulticastGroup);
        if (IsValidDiscoveryMulticastGroup(envGroup, out var parsedEnv))
        {
            return parsedEnv;
        }

        if (IsValidDiscoveryMulticastGroup(_options.DiscoveryMulticastGroup, out var parsedOption))
        {
            return parsedOption;
        }

        return Constants.Discovery.DefaultMulticastGroup;
    }

    private bool ResolveDiscoveryBroadcastFallback()
    {
        return TryReadBooleanEnvironment(
            Constants.EnvironmentVariables.DiscoveryEnableBroadcastFallback,
            out var envValue)
            ? envValue
            : _options.DiscoveryEnableBroadcastFallback;
    }

    private bool ResolveDiscoveryLimitedBroadcastFallback()
    {
        return TryReadBooleanEnvironment(
            Constants.EnvironmentVariables.DiscoveryEnableLimitedBroadcast,
            out var envValue)
            ? envValue
            : _options.DiscoveryEnableLimitedBroadcast;
    }

    private static IPAddress? GetLanBindAddress()
    {
        try
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces()
                .Where(iface => iface.OperationalStatus == OperationalStatus.Up)
                .Where(iface => iface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(iface => iface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(iface => iface.GetIPProperties().UnicastAddresses)
                .Select(addr => addr.Address)
                .FirstOrDefault(addr =>
                    addr.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr) &&
                    !addr.ToString().StartsWith("169.254.", StringComparison.Ordinal));

            return candidate;
        }
        catch
        {
            return null;
        }
    }

    private bool TryParseDiscoveryResponse(
        ReadOnlySpan<byte> payload,
        out DiscoveryResponseEnvelope? response)
    {
        response = null;
        if (payload.IsEmpty)
        {
            return false;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<DiscoveryResponseEnvelope>(payload, _jsonOptions);
            if (envelope?.Data is not null)
            {
                if (!string.IsNullOrWhiteSpace(envelope.Type) &&
                    !string.Equals(
                        envelope.Type,
                        Constants.Discovery.ResponseType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                response = envelope;
                return true;
            }
        }
        catch (JsonException)
        {
            // fall back to raw payload shape
        }

        try
        {
            var raw = JsonSerializer.Deserialize<DiscoveryResponseData>(payload, _jsonOptions);
            if (raw is null)
            {
                return false;
            }

            response = new DiscoveryResponseEnvelope
            {
                Type = Constants.Discovery.ResponseType,
                Data = raw
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private CachedMossStone? ToCachedStone(DiscoveryResponseData response, IPEndPoint? sender)
    {
        var rawEndpoint = NormalizeEndpointOrSelector(response.StoneEndpoint);
        if (string.IsNullOrWhiteSpace(rawEndpoint) ||
            !TryNormalizeAbsoluteEndpoint(rawEndpoint, out var endpoint))
        {
            if (sender is null)
            {
                return null;
            }

            endpoint = $"http://{sender.Address}:{Constants.Moss.DefaultPort}";
        }

        var stoneName = string.IsNullOrWhiteSpace(response.StoneName)
            ? sender?.Address.ToString() ?? "unknown"
            : response.StoneName.Trim();

        return new CachedMossStone
        {
            Endpoint = endpoint,
            StoneId = NormalizeEndpointOrSelector(response.StoneId),
            StoneName = stoneName,
            MossVersion = NormalizeEndpointOrSelector(response.MossVersion),
            LanternEndpoint = NormalizeEndpointOrSelector(response.LanternEndpoint),
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    private static bool TryReadIntEnvironment(
        string key,
        int minValue,
        int maxValue,
        out int value)
    {
        value = default;
        var raw = System.Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < minValue || parsed > maxValue)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryReadBooleanEnvironment(string key, out bool value)
    {
        value = default;
        var raw = System.Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim();
        if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (normalized.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool IsValidDiscoveryMulticastGroup(string? value, out string normalized)
    {
        normalized = Constants.Discovery.DefaultMulticastGroup;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!IPAddress.TryParse(value.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var firstOctet = ip.GetAddressBytes()[0];
        if (firstOctet < 224 || firstOctet > 239)
        {
            return false;
        }

        normalized = ip.ToString();
        return true;
    }

    private static bool IsSupportedHttpScheme(string scheme)
    {
        return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEndpoint(Uri uri)
    {
        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port);
        return builder.Uri.ToString().TrimEnd('/');
    }

    private Uri BuildSnapshotUri(string endpoint, ZenGardenSubscription subscription, long? since)
    {
        var builder = CreateBaseUriBuilder(endpoint, Constants.Moss.ToolsEndpoint);
        var query = new List<string>();

        if (subscription.ToolType is not null)
        {
            query.Add($"tool_type={Uri.EscapeDataString(ToWireToolType(subscription.ToolType.Value))}");
        }

        if (!string.IsNullOrWhiteSpace(subscription.ToolFqid))
        {
            query.Add($"tool_fqid={Uri.EscapeDataString(subscription.ToolFqid)}");
        }

        if (subscription.Requires.Count > 0)
        {
            var selector = string.Join(",", subscription.Requires.Select(x => x.Canonical));
            query.Add($"capability={Uri.EscapeDataString(selector)}");
        }

        if (since is not null)
        {
            query.Add($"since={since.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        builder.Query = string.Join("&", query);
        return builder.Uri;
    }

    private Uri BuildStreamUri(string endpoint, long? since)
    {
        var builder = CreateBaseUriBuilder(endpoint, Constants.Moss.ToolsStreamEndpoint);
        if (since is not null)
        {
            builder.Query = $"since={since.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        return builder.Uri;
    }

    private static Uri BuildHealthUri(string endpoint)
    {
        var normalized = endpoint.TrimEnd('/');
        return new UriBuilder($"{normalized}{Constants.Moss.HealthEndpoint}").Uri;
    }

    private static UriBuilder CreateBaseUriBuilder(string endpoint, string path)
    {
        return new UriBuilder($"{endpoint.TrimEnd('/')}{path}");
    }

    private static string ToWireToolType(ZenGardenToolType toolType)
    {
        return toolType switch
        {
            ZenGardenToolType.Offering => "offering",
            ZenGardenToolType.SeedBank => "seed-bank",
            _ => "unknown"
        };
    }

    private static JsonElement ExtractDataOrSelf(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && TryGetProperty(element, "data", out var data))
        {
            return data;
        }

        return element;
    }

    private static IReadOnlyList<ZenGardenToolSnapshot> ParseToolList(JsonElement payload)
    {
        if (!TryGetProperty(payload, "tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ZenGardenToolSnapshot>();
        }

        var result = new List<ZenGardenToolSnapshot>();
        foreach (var item in tools.EnumerateArray())
        {
            if (TryParseSnapshot(item, out var snapshot))
            {
                result.Add(snapshot);
            }
        }

        return result;
    }

    private static bool TryParseSnapshot(JsonElement element, out ZenGardenToolSnapshot snapshot)
    {
        snapshot = default!;
        var payload = ExtractDataOrSelf(element);

        var toolFqid = ReadString(payload, "tool_fqid");
        if (string.IsNullOrWhiteSpace(toolFqid))
        {
            return false;
        }

        var capabilities = ParseCapabilities(payload);
        var ready = ReadBoolean(payload, "ready") ?? false;
        var state = ParseToolState(ReadString(payload, "state"));

        if (state == ZenGardenToolState.Ready)
        {
            ready = true;
        }

        snapshot = new ZenGardenToolSnapshot
        {
            ToolFqid = toolFqid.ToLowerInvariant(),
            ToolUid = ReadString(payload, "tool_uid"),
            ToolType = ParseToolType(ReadString(payload, "tool_type")) ?? ZenGardenToolType.Unknown,
            State = state,
            Ready = ready,
            Revision = ReadLong(payload, "revision") ?? 0,
            StoneId = ReadString(payload, "stone_id"),
            StoneName = ReadString(payload, "stone_name"),
            Connection = ParseConnection(payload),
            Capabilities = capabilities,
            CapabilityRevision = ReadLong(payload, "capability_revision"),
            UpdatedAt = ParseDateTimeOffset(ReadString(payload, "updated_at"))
        };

        return true;
    }

    private static ZenGardenConnection? ParseConnection(JsonElement payload)
    {
        if (!TryGetProperty(payload, "connection", out var connectionElement) ||
            connectionElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var uris = Array.Empty<string>();
        if (TryGetProperty(connectionElement, "uris", out var urisElement) &&
            urisElement.ValueKind == JsonValueKind.Array)
        {
            uris = urisElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        return new ZenGardenConnection
        {
            Protocol = ReadString(connectionElement, "protocol"),
            Hostname = ReadString(connectionElement, "hostname"),
            Ip = ReadString(connectionElement, "ip"),
            Port = ReadInt(connectionElement, "port"),
            Uris = uris
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseCapabilities(JsonElement payload)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(payload, "capabilities", out var caps) || caps.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in caps.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var items = property.Value.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result[property.Name.ToLowerInvariant()] = items;
        }

        return result;
    }

    private static ZenGardenToolType? ParseToolType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "offering" => ZenGardenToolType.Offering,
            "seed-bank" => ZenGardenToolType.SeedBank,
            _ => ZenGardenToolType.Unknown
        };
    }

    private static ZenGardenToolState ParseToolState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ZenGardenToolState.Unknown;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "ready" => ZenGardenToolState.Ready,
            "degraded" => ZenGardenToolState.Degraded,
            "unavailable" => ZenGardenToolState.Unavailable,
            _ => ZenGardenToolState.Unknown
        };
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
        {
            return value;
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool? ReadBoolean(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        var value = ReadLong(element, name);
        if (value is null)
        {
            return null;
        }

        if (value.Value < int.MinValue || value.Value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZenGardenClient));
        }
    }

    private sealed record CachedMossStone
    {
        public required string Endpoint { get; init; }
        public string? StoneId { get; init; }
        public required string StoneName { get; init; }
        public string? MossVersion { get; init; }
        public string? LanternEndpoint { get; init; }
        public DateTimeOffset LastSeenUtc { get; init; }
        public string CacheKey => string.IsNullOrWhiteSpace(StoneId) ? StoneName : StoneId!;
    }

    private sealed record DiscoveryRequestEnvelope
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = Constants.Discovery.RequestType;

        [JsonPropertyName("msg_id")]
        public string? MsgId { get; init; }

        [JsonPropertyName("data")]
        public required DiscoveryRequestData Data { get; init; }
    }

    private sealed record DiscoveryRequestData
    {
        [JsonPropertyName("discover")]
        public string Discover { get; init; } = Constants.Discovery.DiscoverTargetMoss;

        [JsonPropertyName("request_id")]
        public required string RequestId { get; init; }

        [JsonPropertyName("requester")]
        public string Requester { get; init; } = "koan-framework";
    }

    private sealed record DiscoveryResponseEnvelope
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("msg_id")]
        public string? MsgId { get; init; }

        [JsonPropertyName("data")]
        public DiscoveryResponseData? Data { get; init; }
    }

    private sealed record DiscoveryResponseData
    {
        [JsonPropertyName("stone_id")]
        public string? StoneId { get; init; }

        [JsonPropertyName("stone_name")]
        public required string StoneName { get; init; }

        [JsonPropertyName("stone_endpoint")]
        public required string StoneEndpoint { get; init; }

        [JsonPropertyName("moss_version")]
        public string? MossVersion { get; init; }

        [JsonPropertyName("lantern_endpoint")]
        public string? LanternEndpoint { get; init; }
    }

    private sealed record SubscriptionRegistration(
        Guid Id,
        ZenGardenSubscription Subscription,
        ZenGardenWatchOptions Options,
        Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> Handler);

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly ZenGardenClient _owner;
        private readonly Guid _id;
        private int _disposed;

        public SubscriptionHandle(ZenGardenClient owner, Guid id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner._subscriptions.TryRemove(_id, out _);
        }
    }
}
