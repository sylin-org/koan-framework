using Koan.ZenGarden.Koi;
using Koan.ZenGarden.Models;
using Koan.ZenGarden.Persistence;

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

    private static readonly JsonSerializerOptions TopologySerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, ZenGardenToolSnapshot> _tools =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, SubscriptionRegistration> _subscriptions = new();
    private readonly ConcurrentDictionary<Guid, CapabilitySubscriptionRegistration> _capabilitySubscriptions = new();
    private readonly ConcurrentDictionary<string, CapabilityWishRegistration> _capabilityWishes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedMossStone> _stoneCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IStoneRosterStore? _rosterStore;
    private readonly IKoiHandler? _koiHandler;
    private readonly IDisposable? _koiSubscription;
    private readonly string? _mossTopologyPath;

    private readonly HashSet<string> _seenEventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _seenEventOrder = new();
    private readonly object _seenEventLock = new();
    private readonly object _disposeLock = new();
    private readonly object _bindLock = new();

    private CancellationTokenSource _lifetimeCts = new();
    private Task? _streamLoopTask;
    private int _streamStarted;
    private int _persistedRosterSeeded;
    private DateTimeOffset _lastTopologyHydration;
    private bool _disposed;

    private long? _cursor;
    private string? _lastEventId;
    private CachedMossStone? _boundStone;

    public ZenGardenClient(
        HttpClient httpClient,
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options = null)
        : this(httpClient, logger, options, ownsHttpClient: false, rosterStore: null, koiHandler: null)
    {
    }

    public ZenGardenClient(
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options = null)
        : this(new HttpClient(), logger, options, ownsHttpClient: true, rosterStore: null, koiHandler: null)
    {
    }

    internal ZenGardenClient(
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options,
        IStoneRosterStore? rosterStore,
        IKoiHandler? koiHandler = null)
        : this(new HttpClient(), logger, options, ownsHttpClient: true, rosterStore, koiHandler)
    {
    }

    private ZenGardenClient(
        HttpClient httpClient,
        ILogger<ZenGardenClient> logger,
        ZenGardenOptions? options,
        bool ownsHttpClient,
        IStoneRosterStore? rosterStore,
        IKoiHandler? koiHandler)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? new ZenGardenOptions();
        _ownsHttpClient = ownsHttpClient;
        _rosterStore = rosterStore;
        _koiHandler = koiHandler;
        _mossTopologyPath = _options.PersistDiscoveryCache
            ? StoneRosterPathResolver.ResolveMossTopologyPath(_options)
            : null;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _httpClient.Timeout = TimeSpan.FromSeconds(_options.HttpTimeoutSeconds);

        if (_koiHandler is not null)
        {
            _koiSubscription = _koiHandler.OnTopologyEvent(OnKoiTopologyEvent);
            _koiHandler.Start();
        }
    }

    /// <inheritdoc />
    public string? BoundEndpoint => _boundStone?.Endpoint;

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
            _ = EmitInitialState(registration, _lifetimeCts.Token);
        }

        return new SubscriptionHandle(this, registration.Id);
    }

    public IDisposable SubscribeCapability(
        string requestId,
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("Request id is required.", nameof(requestId));
        }

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var registration = new CapabilitySubscriptionRegistration(
            Guid.NewGuid(),
            requestId.Trim(),
            options ?? new ZenGardenCapabilityWatchOptions(),
            handler);

        _capabilitySubscriptions[registration.Id] = registration;
        EnsureStreamLoopStarted();

        if (registration.Options.EmitInitialState)
        {
            _ = EmitCapabilityInitialState(registration, _lifetimeCts.Token);
        }

        return new CapabilitySubscriptionHandle(this, registration.Id);
    }

    public IDisposable SubscribeCapability(
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> handler,
        ZenGardenCapabilityWatchOptions? options = null)
    {
        ThrowIfDisposed();

        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var registration = new CapabilitySubscriptionRegistration(
            Guid.NewGuid(),
            null,
            options ?? new ZenGardenCapabilityWatchOptions(),
            handler);

        _capabilitySubscriptions[registration.Id] = registration;
        EnsureStreamLoopStarted();

        if (registration.Options.EmitInitialState)
        {
            _ = EmitCapabilityInitialState(registration, _lifetimeCts.Token);
        }

        return new CapabilitySubscriptionHandle(this, registration.Id);
    }

    public async Task<IReadOnlyList<ZenGardenToolSnapshot>> Catalog(
        ZenGardenSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (subscription is null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        using var response = await GetSnapshotWithRecovery(subscription, cancellationToken);
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

        // Server returns results pre-sorted (exact fqid → category → alphabetical).
        var filtered = snapshots
            .Where(subscription.Matches)
            .Where(subscription.RequirementsSatisfiedBy)
            .ToArray();

        return filtered;
    }

    public async ValueTask<ZenGardenCapabilityWish> Wish(
        string offering,
        IReadOnlyList<string> capabilities,
        ZenGardenCapabilityWishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(offering))
        {
            throw new ArgumentException("Offering is required.", nameof(offering));
        }

        var requestedRequirements = CollectRequestedRequirements(offering, capabilities, options);
        if (requestedRequirements.Count == 0)
        {
            throw new ArgumentException("At least one capability is required.", nameof(capabilities));
        }

        var offeringSubscription = ZenGardenSubscription.ForOffering(offering);
        var toolFqid = offeringSubscription.ToolFqid
            ?? throw new InvalidOperationException("Failed to normalize offering selector.");
        var offeringSelector = Core.ToolFqid.Parse(toolFqid).ToString();

        var snapshot = await ResolveCurrentToolSnapshot(toolFqid, cancellationToken).ConfigureAwait(false);
        var requested = requestedRequirements.Select(static x => x.Canonical).ToArray();
        var satisfied = EvaluateSatisfiedCapabilities(requestedRequirements, snapshot).ToArray();
        var missing = requested
            .Except(satisfied, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        var wish = new ZenGardenCapabilityWish
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolFqid = toolFqid,
            OfferingSelector = offeringSelector,
            Requested = requested,
            Satisfied = satisfied,
            Missing = missing,
            IsFulfilled = missing.Length == 0,
            Status = missing.Length == 0 ? "fulfilled" : satisfied.Length > 0 ? "partial" : "requested",
            CreatedAt = now,
            UpdatedAt = now
        };

        var registration = new CapabilityWishRegistration
        {
            RequestId = wish.RequestId,
            ToolFqid = wish.ToolFqid,
            OfferingSelector = wish.OfferingSelector,
            Requirements = requestedRequirements,
            Current = wish
        };

        _capabilityWishes[wish.RequestId] = registration;

        await PublishCapabilityProgress(
            kind: wish.IsFulfilled
                ? ZenGardenCapabilityProgressEventKind.Fulfilled
                : wish.Satisfied.Count > 0
                    ? ZenGardenCapabilityProgressEventKind.PartiallyFulfilled
                    : ZenGardenCapabilityProgressEventKind.Requested,
            registration: registration,
            previous: null,
            currentTool: snapshot,
            eventId: null,
            cursor: _cursor,
            ct: cancellationToken).ConfigureAwait(false);

        if (missing.Length == 0)
        {
            return wish;
        }

        var wishOptions = options ?? new ZenGardenCapabilityWishOptions();
        var failures = new List<string>();

        foreach (var requirement in requestedRequirements.Where(r => missing.Contains(r.Canonical, StringComparer.OrdinalIgnoreCase)))
        {
            var ensureResponse = await EnsureCapabilityWishfully(
                offeringSelector,
                requirement,
                wishOptions,
                cancellationToken).ConfigureAwait(false);

            if (!ensureResponse.Success)
            {
                failures.Add($"{requirement.Canonical}: {ensureResponse.Message}");
            }
        }

        if (failures.Count > 0)
        {
            var previous = UpdateCapabilityWish(registration, current =>
            {
                return current with
                {
                    Status = "failed",
                    Message = string.Join("; ", failures),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            });

            await PublishCapabilityProgress(
                ZenGardenCapabilityProgressEventKind.Failed,
                registration,
                previous,
                currentTool: snapshot,
                eventId: null,
                cursor: _cursor,
                ct: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var previous = UpdateCapabilityWish(registration, current =>
            {
                return current with
                {
                    Status = "in_progress",
                    Message = "Capability ensure requests accepted.",
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            });

            await PublishCapabilityProgress(
                ZenGardenCapabilityProgressEventKind.InProgress,
                registration,
                previous,
                currentTool: snapshot,
                eventId: null,
                cursor: _cursor,
                ct: cancellationToken).ConfigureAwait(false);
        }

        return SnapshotCapabilityWish(registration);
    }

    public bool TryGetCapabilityWish(string requestId, out ZenGardenCapabilityWish wish)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            wish = default!;
            return false;
        }

        if (_capabilityWishes.TryGetValue(requestId.Trim(), out var registration))
        {
            wish = SnapshotCapabilityWish(registration);
            return true;
        }

        wish = default!;
        return false;
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

        _koiSubscription?.Dispose();

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

    private async Task EmitInitialState(SubscriptionRegistration registration, CancellationToken ct)
    {
        try
        {
            // When the subscription has capability requirements, CatalogAsync filters
            // out tools that don't satisfy them — leaving nothing to emit when
            // capabilities are unsatisfied. Strip requirements so we get the full
            // tool list, then classify each tool locally below.
            var catalogSubscription = registration.Subscription.Requires.Count > 0
                ? registration.Subscription with { Requires = [] }
                : registration.Subscription;

            var tools = await Catalog(catalogSubscription, ct);
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

    private async Task EmitCapabilityInitialState(CapabilitySubscriptionRegistration registration, CancellationToken ct)
    {
        try
        {
            var wishes = _capabilityWishes.Values.ToArray();
            foreach (var wishRegistration in wishes)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (!registration.Matches(wishRegistration.RequestId))
                {
                    continue;
                }

                var wish = SnapshotCapabilityWish(wishRegistration);
                await registration.Handler(
                    new ZenGardenCapabilityProgressEvent
                    {
                        Kind = MapStatusToProgressKind(wish),
                        Wish = wish,
                        Previous = null,
                        CurrentTool = null,
                        EventId = wish.EventId,
                        Cursor = wish.Cursor,
                        Timestamp = wish.UpdatedAt
                    },
                    ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to emit initial capability state for subscription {SubscriptionId}", registration.Id);
        }
    }

    private void EnsureStreamLoopStarted()
    {
        if (Interlocked.CompareExchange(ref _streamStarted, 1, 0) != 0)
        {
            return;
        }

        _streamLoopTask = Task.Run(() => RunStreamLoop(_lifetimeCts.Token), _lifetimeCts.Token);
    }

    private async Task RunStreamLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConsumeStream(ct);
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

    private async Task ConsumeStream(CancellationToken ct)
    {
        using var response = await OpenStreamWithRecovery(ct);
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
                    await ProcessStreamEvent(eventName, eventId, payload, ct);
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

    private async Task ProcessStreamEvent(string eventName, string? eventId, string payloadJson, CancellationToken ct)
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
                await ApplySnapshot(eventPayload, eventId, ct);
                break;
            case "tool.upsert":
                if (TryParseSnapshot(eventPayload, out var upsertSnapshot))
                {
                    await ApplyUpsert(upsertSnapshot, eventId, cursor, ct);
                }
                break;
            case "tool.remove":
                await ApplyRemove(eventPayload, eventId, cursor, ct);
                break;
            case "tools.heartbeat":
                await TryHydrateTopologyThrottled(ct);
                break;
            default:
                await TryProcessGenericEvent(eventPayload, eventId, cursor, ct);
                break;
        }
    }

    private async Task ApplySnapshot(JsonElement payload, string? eventId, CancellationToken ct)
    {
        foreach (var snapshot in ParseToolList(payload))
        {
            await ApplyUpsert(snapshot, eventId, _cursor, ct);
        }

        if (TryGetProperty(payload, "replay", out var replay) && replay.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in replay.EnumerateArray())
            {
                await TryProcessGenericEvent(item, eventId, _cursor, ct);
            }
        }
    }

    private async Task TryProcessGenericEvent(JsonElement payload, string? eventId, long? cursor, CancellationToken ct)
    {
        var inner = ExtractDataOrSelf(payload);
        var candidateEvent = ReadString(payload, "event") ?? ReadString(payload, "type");

        if (string.Equals(candidateEvent, "tool.remove", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyRemove(inner, eventId, cursor, ct);
            return;
        }

        if (TryParseSnapshot(inner, out var snapshot))
        {
            await ApplyUpsert(snapshot, eventId, cursor, ct);
        }
    }

    private async Task ApplyUpsert(
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
        TryEnrichTopologyFromSnapshot(current);
        await PublishDerivedEvents(current, previous, eventId, cursor, ct);
        await PublishCapabilityUpdates(current, eventId, cursor, ct);
    }

    private async Task ApplyRemove(JsonElement payload, string? eventId, long? cursor, CancellationToken ct)
    {
        var toolFqid = ReadString(payload, "fqid") ?? ReadString(payload, "tool_fqid");
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
            ToolUid = previous?.ToolUid,
            OfferingType = previous?.OfferingType,
            Category = previous?.Category,
            ToolType = previous?.ToolType ?? ZenGardenToolType.Unknown,
            State = ZenGardenToolState.Unavailable,
            Ready = false,
            Revision = revision,
            StoneId = previous?.StoneId,
            StoneName = previous?.StoneName,
            StoneEndpoint = previous?.StoneEndpoint,
            Connection = previous?.Connection,
            Capabilities = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            CapabilityRevision = previous?.CapabilityRevision,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await PublishDerivedEvents(current, previous, eventId, cursor, ct);
        await PublishCapabilityUpdates(current, eventId, cursor, ct);
    }

    private async Task PublishDerivedEvents(
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

    private async Task PublishCapabilityUpdates(
        ZenGardenToolSnapshot current,
        string? eventId,
        long? cursor,
        CancellationToken ct)
    {
        var registrations = _capabilityWishes.Values.ToArray();
        foreach (var registration in registrations)
        {
            if (!IsToolMatch(registration.ToolFqid, current))
            {
                continue;
            }

            var satisfied = EvaluateSatisfiedCapabilities(registration.Requirements, current).ToArray();
            var requested = registration.Requirements.Select(static x => x.Canonical).ToArray();
            var missing = requested
                .Except(satisfied, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ZenGardenCapabilityWish? previousWish = null;
            ZenGardenCapabilityProgressEventKind? kind = null;

            lock (registration.Gate)
            {
                var currentWish = registration.Current;
                var currentSatisfied = currentWish.Satisfied.ToArray();
                var currentMissing = currentWish.Missing.ToArray();

                var satisfiedChanged = !currentSatisfied.SequenceEqual(satisfied, StringComparer.OrdinalIgnoreCase);
                var missingChanged = !currentMissing.SequenceEqual(missing, StringComparer.OrdinalIgnoreCase);
                if (!satisfiedChanged && !missingChanged)
                {
                    continue;
                }

                previousWish = currentWish;
                var nextStatus = missing.Length == 0 ? "fulfilled" : satisfied.Length > 0 ? "partial" : "in_progress";
                registration.Current = currentWish with
                {
                    Satisfied = satisfied,
                    Missing = missing,
                    IsFulfilled = missing.Length == 0,
                    Status = nextStatus,
                    Message = missing.Length == 0
                        ? "Capability wish fulfilled."
                        : $"Capabilities still pending: {string.Join(", ", missing)}",
                    UpdatedAt = DateTimeOffset.UtcNow,
                    EventId = eventId,
                    Cursor = cursor
                };

                kind = missing.Length == 0
                    ? ZenGardenCapabilityProgressEventKind.Fulfilled
                    : satisfied.Length > 0
                        ? ZenGardenCapabilityProgressEventKind.PartiallyFulfilled
                        : ZenGardenCapabilityProgressEventKind.InProgress;
            }

            if (kind is not null && previousWish is not null)
            {
                await PublishCapabilityProgress(
                    kind.Value,
                    registration,
                    previousWish,
                    current,
                    eventId,
                    cursor,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private async Task PublishCapabilityProgress(
        ZenGardenCapabilityProgressEventKind kind,
        CapabilityWishRegistration registration,
        ZenGardenCapabilityWish? previous,
        ZenGardenToolSnapshot? currentTool,
        string? eventId,
        long? cursor,
        CancellationToken ct)
    {
        var current = SnapshotCapabilityWish(registration);
        var progressEvent = new ZenGardenCapabilityProgressEvent
        {
            Kind = kind,
            Wish = current,
            Previous = previous,
            CurrentTool = currentTool,
            EventId = eventId ?? current.EventId,
            Cursor = cursor ?? current.Cursor,
            Timestamp = DateTimeOffset.UtcNow
        };

        var subscriptions = _capabilitySubscriptions.Values.ToArray();
        foreach (var subscription in subscriptions)
        {
            if (!subscription.Matches(current.RequestId))
            {
                continue;
            }

            try
            {
                await subscription.Handler(progressEvent, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "ZenGarden capability subscription handler failed for {SubscriptionId}", subscription.Id);
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

    private async Task<ZenGardenToolSnapshot?> ResolveCurrentToolSnapshot(string toolFqid, CancellationToken ct)
    {
        if (_tools.TryGetValue(toolFqid, out var current))
        {
            return current;
        }

        var scoped = await Catalog(new ZenGardenSubscription
        {
            ToolType = Models.ZenGardenToolType.Offering,
            ToolFqid = toolFqid
        }, ct).ConfigureAwait(false);

        if (scoped.Count > 0)
        {
            return scoped[0];
        }

        var broad = await Catalog(new ZenGardenSubscription
        {
            ToolType = Models.ZenGardenToolType.Offering
        }, ct).ConfigureAwait(false);

        var query = Core.ToolFqid.Parse(toolFqid);
        return broad.FirstOrDefault(tool =>
            query.MatchesSnapshot(tool.ToolFqid, tool.OfferingType, tool.Aliases));
    }

    private static IReadOnlyList<ZenGardenCapabilityRequirement> CollectRequestedRequirements(
        string offering,
        IReadOnlyList<string> capabilities,
        ZenGardenCapabilityWishOptions? options)
    {
        var parsed = ZenGardenCapabilityRequirement.ParseMany(capabilities ?? []);
        var selector = ZenGardenSubscription.ForOffering(offering);
        if (selector.Requires.Count > 0)
        {
            parsed = parsed.Concat(selector.Requires).Distinct().ToArray();
        }

        if (!string.IsNullOrWhiteSpace(options?.TypeHint))
        {
            var type = options.TypeHint.Trim().ToLowerInvariant();
            parsed = parsed
                .Select(r =>
                {
                    if (string.IsNullOrWhiteSpace(r.Type))
                    {
                        return r with { Type = type };
                    }

                    return r;
                })
                .ToArray();
        }

        return parsed;
    }


    private static IReadOnlyCollection<string> EvaluateSatisfiedCapabilities(
        IReadOnlyList<ZenGardenCapabilityRequirement> requirements,
        ZenGardenToolSnapshot? tool)
    {
        if (requirements.Count == 0 || tool is null)
        {
            return [];
        }

        var satisfied = new List<string>();
        foreach (var requirement in requirements)
        {
            if (requirement.Matches(tool.Capabilities))
            {
                satisfied.Add(requirement.Canonical);
            }
        }

        return satisfied
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsToolMatch(string requestedToolFqid, ZenGardenToolSnapshot current)
    {
        return Core.ToolFqid.Parse(requestedToolFqid)
            .MatchesSnapshot(current.ToolFqid, current.OfferingType, current.Aliases);
    }

    private ZenGardenCapabilityWish SnapshotCapabilityWish(CapabilityWishRegistration registration)
    {
        lock (registration.Gate)
        {
            return registration.Current;
        }
    }

    private ZenGardenCapabilityWish UpdateCapabilityWish(
        CapabilityWishRegistration registration,
        Func<ZenGardenCapabilityWish, ZenGardenCapabilityWish> mutator)
    {
        lock (registration.Gate)
        {
            var previous = registration.Current;
            registration.Current = mutator(previous);
            return previous;
        }
    }

    private static ZenGardenCapabilityProgressEventKind MapStatusToProgressKind(ZenGardenCapabilityWish wish)
    {
        if (string.Equals(wish.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return ZenGardenCapabilityProgressEventKind.Failed;
        }

        if (wish.IsFulfilled)
        {
            return ZenGardenCapabilityProgressEventKind.Fulfilled;
        }

        if (wish.Satisfied.Count > 0)
        {
            return ZenGardenCapabilityProgressEventKind.PartiallyFulfilled;
        }

        if (string.Equals(wish.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            return ZenGardenCapabilityProgressEventKind.InProgress;
        }

        return ZenGardenCapabilityProgressEventKind.Requested;
    }

    private async Task<HttpResponseMessage> GetSnapshotWithRecovery(
        ZenGardenSubscription subscription,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = await EnsureBoundEndpoint(ct, forceRediscovery: attempt > 0);
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

        var fallbackEndpoint = await EnsureBoundEndpoint(ct, forceRediscovery: true);
        return await _httpClient.GetAsync(BuildSnapshotUri(fallbackEndpoint, subscription, since: null), ct);
    }

    private async Task<HttpResponseMessage> OpenStreamWithRecovery(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = await EnsureBoundEndpoint(ct, forceRediscovery: attempt > 0);
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

        var finalEndpoint = await EnsureBoundEndpoint(ct, forceRediscovery: true);
        using var finalRequest = new HttpRequestMessage(HttpMethod.Get, BuildStreamUri(finalEndpoint, _cursor));
        finalRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(_lastEventId))
        {
            finalRequest.Headers.TryAddWithoutValidation("Last-Event-ID", _lastEventId);
        }

        return await _httpClient.SendAsync(finalRequest, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private async Task<CapabilityEnsureResult> EnsureCapabilityWishfully(
        string offeringSelector,
        ZenGardenCapabilityRequirement requirement,
        ZenGardenCapabilityWishOptions options,
        CancellationToken ct)
    {
        var request = new CapabilityEnsureRequest
        {
            Name = requirement.Name,
            Type = requirement.Type,
            DryRun = options.DryRun
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = await EnsureBoundEndpoint(ct, forceRediscovery: attempt > 0).ConfigureAwait(false);
            var uri = BuildCapabilityEnsureUri(endpoint, offeringSelector);

            try
            {
                var payload = JsonSerializer.Serialize(request, _jsonOptions);
                using var response = await _httpClient.PostAsync(
                    uri,
                    new StringContent(payload, Encoding.UTF8, "application/json"),
                    ct).ConfigureAwait(false);

                if (attempt == 0 && ShouldRetryWithRediscovery(response.StatusCode))
                {
                    InvalidateBoundStone($"Capability ensure failed with {(int)response.StatusCode}.");
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var message = ParseCapabilityEnsureStatus(body) ?? "accepted";
                    return new CapabilityEnsureResult(true, message);
                }

                return new CapabilityEnsureResult(false, ParseCapabilityEnsureStatus(body) ?? response.ReasonPhrase ?? "request failed");
            }
            catch (Exception ex) when (attempt == 0 && ShouldRetryWithRediscovery(ex))
            {
                InvalidateBoundStone("Capability ensure failed with connection exception.", ex);
            }
            catch (Exception ex)
            {
                return new CapabilityEnsureResult(false, ex.Message);
            }
        }

        return new CapabilityEnsureResult(false, "unable to reach moss endpoint");
    }

    private static string? ParseCapabilityEnsureStatus(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var payload = ExtractDataOrSelf(doc.RootElement);
            var status = ReadString(payload, "status");
            var message = ReadString(payload, "message");

            if (!string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(message))
            {
                return $"{status}: {message}";
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                return status;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var success = ReadBoolean(payload, "success");
            if (success is not null)
            {
                return success.Value ? "success" : "failed";
            }
        }
        catch (JsonException)
        {
            return body.Trim();
        }

        return null;
    }

    private async Task<string> EnsureBoundEndpoint(CancellationToken ct, bool forceRediscovery = false)
    {
        ThrowIfDisposed();
        await SeedFromPersistedRoster(ct).ConfigureAwait(false);
        var containerized = IsContainerizedRuntime();

        // 1. Currently bound Stone (skip when force-rediscovering after a failure)
        if (!forceRediscovery)
        {
            var bound = _boundStone;
            if (bound is not null)
            {
                return bound.Endpoint;
            }
        }

        // 2. Explicit endpoint / GARDEN_STONE selector
        var selector = ResolvePreferredSelector();
        if (!string.IsNullOrWhiteSpace(selector))
        {
            var selected = await ResolveStoneFromSelector(selector, ct);
            if (selected is not null)
            {
                return BindStone(selected).Endpoint;
            }
        }

        // 3. Koi topology snapshot (authoritative when connected)
        var koiStone = ResolveFromKoiSnapshot();
        if (koiStone is not null)
        {
            return BindStone(koiStone).Endpoint;
        }

        // 4. Preferred Stone name (soft affinity)
        var preferred = await ResolvePreferredStoneName(ct);
        if (preferred is not null)
        {
            return BindStone(preferred).Endpoint;
        }

        // 5. In-memory cache (includes seeded persisted entries)
        var cached = await ResolveFromCache(ct);
        if (cached is not null)
        {
            return BindStone(cached).Endpoint;
        }

        // 6. Container host binding
        if (containerized)
        {
            TryResolveContainerHostEndpoint(out var configuredContainerEndpoint);
            var hostStone = await ResolveContainerHostStone(ct).ConfigureAwait(false);
            if (hostStone is not null)
            {
                return BindStone(hostStone).Endpoint;
            }

            // 7. Persisted roster re-read (catches sibling container writes since seeding)
            var fallbackPersisted = await ResolveFromPersistedRoster(ct);
            if (fallbackPersisted is not null)
            {
                return BindStone(fallbackPersisted).Endpoint;
            }

            // If we have prior topology knowledge (persisted roster was loaded or
            // stones were learned via SSE enrichment), return optimistically without
            // health check. The caller's HTTP request will fail naturally and the
            // reconnect loop retries until a Moss comes back. UDP discovery can't
            // cross container network boundaries, so cached topology is the only
            // viable failover path.
            //
            // Only throw the configuration error on a true cold start with an empty
            // cache (no prior successful connection), where misconfiguration is likely.
            if (!_stoneCache.IsEmpty)
            {
                var failoverStone = ResolveBestCachedStoneForFailover(configuredContainerEndpoint);
                if (failoverStone is not null)
                {
                    _logger.LogDebug(
                        "Container failover: binding to cached Stone {StoneName} at {Endpoint} (not health-checked).",
                        failoverStone.StoneName, failoverStone.Endpoint);
                    return BindStone(failoverStone).Endpoint;
                }

                // All cached stones were filtered (shouldn't happen, but fall back to
                // container host optimistically rather than throw).
                if (!string.IsNullOrWhiteSpace(configuredContainerEndpoint))
                {
                    _logger.LogDebug(
                        "Container failover: no cached alternatives, returning host endpoint {Endpoint} optimistically.",
                        configuredContainerEndpoint);
                    var hostFallback = new CachedMossStone
                    {
                        Endpoint = configuredContainerEndpoint,
                        StoneName = new Uri(configuredContainerEndpoint).Host,
                        LastSeenUtc = DateTimeOffset.UtcNow
                    };
                    return BindStone(hostFallback).Endpoint;
                }
            }

            if (RequireHostMossWhenContainerized())
            {
                throw new InvalidOperationException(
                    "Containerized runtime requires host Moss endpoint but none was reachable. " +
                    $"Configured host endpoint candidate: {(string.IsNullOrWhiteSpace(configuredContainerEndpoint) ? "(none)" : configuredContainerEndpoint)}. " +
                    "Configure Koan:ZenGarden:ContainerHost (or KOAN_ZENGARDEN_CONTAINER_HOST) and optional ContainerHostPort.");
            }
        }

        if (_options.EnableDiscovery)
        {
            var discovered = await DiscoverStones(
                ResolveDiscoveryTimeout(),
                waitForAll: true,
                ct);

            var reachable = await FindFirstReachable(discovered, ct);
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

    private async Task<CachedMossStone?> ResolveContainerHostStone(CancellationToken ct)
    {
        if (!TryResolveContainerHostEndpoint(out var endpoint))
        {
            return null;
        }

        var candidate = new CachedMossStone
        {
            Endpoint = endpoint,
            StoneName = new Uri(endpoint).Host,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        if (!await IsMossReachable(candidate, ct).ConfigureAwait(false))
        {
            return null;
        }

        return CacheStone(candidate);
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

    private async Task<CachedMossStone?> ResolveStoneFromSelector(string selector, CancellationToken ct)
    {
        if (TryNormalizeAbsoluteEndpoint(selector, out var endpoint))
        {
            var candidate = new CachedMossStone
            {
                Endpoint = endpoint,
                StoneName = new Uri(endpoint).Host,
                LastSeenUtc = DateTimeOffset.UtcNow
            };

            if (await IsMossReachable(candidate, ct))
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

            if (await IsMossReachable(cached, ct))
            {
                return CacheStone(cached with { LastSeenUtc = DateTimeOffset.UtcNow });
            }
        }

        if (!_options.EnableDiscovery)
        {
            return null;
        }

        var discovered = await DiscoverStones(
            ResolveDiscoveryTimeout(),
            waitForAll: true,
            ct);

        foreach (var stone in discovered)
        {
            if (!MatchesSelector(stone, selector))
            {
                continue;
            }

            if (await IsMossReachable(stone, ct))
            {
                return CacheStone(stone);
            }
        }

        return null;
    }

    private async Task<CachedMossStone?> ResolveFromCache(CancellationToken ct)
    {
        PurgeExpiredCacheEntries();
        var cached = _stoneCache.Values
            .DistinctBy(x => x.Endpoint)
            .OrderByDescending(x => x.LastSeenUtc)
            .ToArray();

        foreach (var stone in cached)
        {
            if (await IsMossReachable(stone, ct))
            {
                return CacheStone(stone with { LastSeenUtc = DateTimeOffset.UtcNow });
            }
        }

        return null;
    }

    private async Task SeedFromPersistedRoster(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _persistedRosterSeeded, 1, 0) != 0)
        {
            return;
        }

        // 1. Own roster (garden-stones.json) — client's operational knowledge
        if (_rosterStore is not null)
        {
            try
            {
                var persisted = await _rosterStore.Load(ct).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;
                foreach (var stone in persisted)
                {
                    // Refresh LastSeenUtc so seeded entries survive the in-memory TTL purge
                    // (persisted roster already filtered by its own 7-day TTL)
                    var refreshed = stone with { LastSeenUtc = now };
                    _stoneCache.TryAdd(refreshed.CacheKey, refreshed);
                    if (!string.Equals(refreshed.CacheKey, refreshed.StoneName, StringComparison.OrdinalIgnoreCase))
                    {
                        _stoneCache.TryAdd(refreshed.StoneName, refreshed);
                    }
                }

                if (persisted.Count > 0)
                {
                    _logger.LogInformation(
                        "Seeded {Count} stones from persisted roster into in-memory cache.",
                        persisted.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Failed to seed stone cache from persisted roster.");
            }
        }

        // 2. Moss topology (garden-topology.json) — fills gaps from Moss's mesh view
        await SeedFromMossTopology(ct).ConfigureAwait(false);
    }

    private async Task SeedFromMossTopology(CancellationToken ct)
    {
        try
        {
            if (_mossTopologyPath is null || !File.Exists(_mossTopologyPath))
                return;

            var json = await File.ReadAllTextAsync(_mossTopologyPath, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return;

            // File is a bare JSON array — NOT the HTTP API envelope.
            // The HTTP path unwraps {"data": [...]}, but the file is just [...].
            var entries = JsonSerializer.Deserialize<List<MossTopologyEntry>>(json, TopologySerializerOptions);
            if (entries is null || entries.Count == 0)
                return;

            var seeded = 0;
            var now = DateTimeOffset.UtcNow;

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Endpoint))
                    continue;

                var cacheKey = !string.IsNullOrWhiteSpace(entry.StoneId)
                    ? entry.StoneId
                    : entry.StoneName;

                if (string.IsNullOrWhiteSpace(cacheKey))
                    continue;

                // Only add if not already present from own roster (own roster wins)
                if (_stoneCache.ContainsKey(cacheKey))
                    continue;

                // Refresh LastSeenUtc to now — same policy as own-roster seeding.
                // The file may be hours old if Moss is down, but the data is the best
                // available truth. Let active hydration update to real timestamps later.
                var stone = new CachedMossStone
                {
                    Endpoint = entry.Endpoint,
                    StoneId = entry.StoneId,
                    StoneName = entry.StoneName ?? cacheKey,
                    MossVersion = entry.MossVersion,
                    LastSeenUtc = now
                };

                if (_stoneCache.TryAdd(cacheKey, stone))
                {
                    seeded++;

                    // Index by name if StoneId was the primary key
                    if (!string.IsNullOrWhiteSpace(entry.StoneId)
                        && !string.IsNullOrWhiteSpace(entry.StoneName))
                    {
                        _stoneCache.TryAdd(entry.StoneName, stone);
                    }

                    // Also cache .local variant for mDNS resolution parity.
                    // Active hydration already does this — file-based seed should too,
                    // so host-network lookups find file-seeded stones by .local name.
                    if (!string.IsNullOrWhiteSpace(entry.StoneName))
                    {
                        var localKey = $"{entry.StoneName}.local";
                        var localEndpoint = $"http://{entry.StoneName}.local:{Constants.Moss.DefaultPort}";
                        var localStone = stone with { Endpoint = localEndpoint };
                        _stoneCache.TryAdd(localKey, localStone);
                    }
                }
            }

            if (seeded > 0)
            {
                _logger.LogDebug(
                    "Seeded {Count} stones from Moss topology file into in-memory cache.",
                    seeded);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not seed from Moss topology file (non-fatal).");
        }
    }

    private async Task<CachedMossStone?> ResolvePreferredStoneName(CancellationToken ct)
    {
        var preferred = ResolvePreferredStoneNameValue();
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return null;
        }

        foreach (var stone in _stoneCache.Values.DistinctBy(x => x.Endpoint))
        {
            if (!MatchesSelector(stone, preferred))
            {
                continue;
            }

            if (await IsMossReachable(stone, ct))
            {
                return CacheStone(stone with { LastSeenUtc = DateTimeOffset.UtcNow });
            }
        }

        if (!_options.EnableDiscovery)
        {
            return null;
        }

        var discovered = await DiscoverStones(
            ResolveDiscoveryTimeout(),
            waitForAll: true,
            ct);

        foreach (var stone in discovered)
        {
            if (!MatchesSelector(stone, preferred))
            {
                continue;
            }

            if (await IsMossReachable(stone, ct))
            {
                return CacheStone(stone);
            }
        }

        return null;
    }

    private string? ResolvePreferredStoneNameValue()
    {
        var envValue = System.Environment.GetEnvironmentVariable(
            Constants.EnvironmentVariables.PreferredStoneName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_options.PreferredStoneName))
        {
            return _options.PreferredStoneName.Trim();
        }

        return null;
    }

    private async Task<CachedMossStone?> ResolveFromPersistedRoster(CancellationToken ct)
    {
        if (_rosterStore is null)
        {
            return null;
        }

        IReadOnlyList<CachedMossStone> persisted;
        try
        {
            persisted = await _rosterStore.Load(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to load persisted stone roster for failover.");
            return null;
        }

        if (persisted.Count == 0)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var stone in persisted)
        {
            var refreshed = stone with { LastSeenUtc = now };
            _stoneCache.TryAdd(refreshed.CacheKey, refreshed);
            if (!string.Equals(refreshed.CacheKey, refreshed.StoneName, StringComparison.OrdinalIgnoreCase))
            {
                _stoneCache.TryAdd(refreshed.StoneName, refreshed);
            }
        }

        var candidates = persisted
            .DistinctBy(s => s.Endpoint)
            .OrderByDescending(s => s.LastSeenUtc)
            .ToArray();

        foreach (var stone in candidates)
        {
            if (await IsMossReachable(stone, ct).ConfigureAwait(false))
            {
                return CacheStone(stone with { LastSeenUtc = now });
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the best cached Stone for optimistic failover in a container.
    /// Prefers alternative stones over the (known-down) container host, ordered
    /// by most recently seen. Returns null only when the cache is empty.
    /// </summary>
    private CachedMossStone? ResolveBestCachedStoneForFailover(string? containerHostEndpoint)
    {
        if (_stoneCache.IsEmpty)
        {
            return null;
        }

        var candidates = _stoneCache.Values
            .DistinctBy(s => s.Endpoint, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(s => s.LastSeenUtc)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        // Prefer alternatives over the container host (which we already know is unreachable)
        if (!string.IsNullOrWhiteSpace(containerHostEndpoint))
        {
            var alternative = candidates.FirstOrDefault(
                s => !string.Equals(s.Endpoint, containerHostEndpoint, StringComparison.OrdinalIgnoreCase));
            if (alternative is not null)
            {
                return alternative;
            }
        }

        // All cached stones are the container host — return it as last resort
        return candidates[0];
    }

    private void PersistRosterFireAndForget()
    {
        if (_rosterStore is null)
        {
            return;
        }

        var snapshot = _stoneCache.Values
            .DistinctBy(s => s.CacheKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                await _rosterStore.Persist(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background stone roster persist failed.");
            }
        });
    }

    // ── Koi topology event interception ────────────────────────────────

    private ValueTask OnKoiTopologyEvent(KoiTopologyEvent evt, CancellationToken ct)
    {
        switch (evt.Kind)
        {
            case KoiTopologyEventKind.StoneOnline:
                if (evt.Stone is not null)
                {
                    var cached = CacheStone(evt.Stone.ToCachedMossStone());
                    if (!string.IsNullOrWhiteSpace(evt.Stone.LocalEndpoint))
                        CacheStone(cached with { Endpoint = evt.Stone.LocalEndpoint });

                    PersistRosterFireAndForget();
                    _logger.LogInformation(
                        "Koi: stone {StoneName} online at {Endpoint}.",
                        evt.Stone.StoneName, evt.Stone.Endpoint);
                }
                break;

            case KoiTopologyEventKind.StoneChanged:
                if (evt.Stone is not null)
                {
                    var updated = CacheStone(evt.Stone.ToCachedMossStone());
                    if (!string.IsNullOrWhiteSpace(evt.Stone.LocalEndpoint))
                        CacheStone(updated with { Endpoint = evt.Stone.LocalEndpoint });

                    PersistRosterFireAndForget();
                }
                break;

            case KoiTopologyEventKind.StoneOffline:
                if (evt.Stone is not null)
                {
                    EvictStone(evt.Stone);
                    PersistRosterFireAndForget();
                    _logger.LogInformation(
                        "Koi: stone {StoneName} went offline — evicted from cache.",
                        evt.Stone.StoneName);
                }
                break;

            case KoiTopologyEventKind.TopologyReset:
                ReconcileCacheWithKoiSnapshot(evt.Snapshot);
                PersistRosterFireAndForget();
                _logger.LogInformation(
                    "Koi: topology reconciled — {StoneCount} stone(s) in view.",
                    evt.Snapshot.Stones.Count);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private void EvictStone(DiscoveredStone stone)
    {
        var key = stone.CacheKey;
        _stoneCache.TryRemove(key, out _);
        _stoneCache.TryRemove(stone.StoneName, out _);

        // If the evicted stone is the currently bound endpoint, invalidate it
        var bound = _boundStone;
        if (bound is not null &&
            (string.Equals(bound.CacheKey, key, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(bound.Endpoint, stone.Endpoint, StringComparison.OrdinalIgnoreCase)))
        {
            InvalidateBoundStone("Koi reported stone offline");
        }
    }

    private void ReconcileCacheWithKoiSnapshot(KoiTopologySnapshot snapshot)
    {
        if (snapshot.State != KoiHandlerState.Connected)
            return;

        // Build set of stone keys from Koi's current view
        var koiKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stone in snapshot.Stones)
        {
            var cached = CacheStone(stone.ToCachedMossStone());
            koiKeys.Add(cached.CacheKey);
            koiKeys.Add(cached.StoneName);

            if (!string.IsNullOrWhiteSpace(stone.LocalEndpoint))
            {
                var local = CacheStone(cached with { Endpoint = stone.LocalEndpoint });
                koiKeys.Add(local.StoneName);
            }
        }

        // Evict stones no longer visible to Koi (authoritative)
        foreach (var key in _stoneCache.Keys)
        {
            if (!koiKeys.Contains(key))
            {
                if (_stoneCache.TryRemove(key, out var removed))
                {
                    _logger.LogDebug(
                        "Koi reconciliation: evicted stale stone {StoneName} at {Endpoint}",
                        removed.StoneName, removed.Endpoint);
                }
            }
        }

        _logger.LogInformation(
            "Koi topology reset: reconciled cache to {Count} stones.",
            snapshot.Stones.Count);
    }

    private CachedMossStone? ResolveFromKoiSnapshot()
    {
        var snapshot = _koiHandler?.CurrentSnapshot;
        if (snapshot is null || snapshot.State != KoiHandlerState.Connected || snapshot.Stones.Count == 0)
            return null;

        // Prefer the PreferredStoneName if configured
        var preferred = _options.PreferredStoneName;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = snapshot.Stones.FirstOrDefault(s =>
                string.Equals(s.StoneName, preferred, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.StoneId, preferred, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
                return match.ToCachedMossStone();
        }

        // Return the most recently seen stone
        var best = snapshot.Stones
            .OrderByDescending(s => s.DiscoveredAt)
            .First();

        return best.ToCachedMossStone();
    }

    private void TryEnrichTopologyFromSnapshot(ZenGardenToolSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.StoneName))
        {
            return;
        }

        var cacheKey = string.IsNullOrWhiteSpace(snapshot.StoneId)
            ? snapshot.StoneName
            : snapshot.StoneId!;

        if (_stoneCache.TryGetValue(cacheKey, out var existing) &&
            DateTimeOffset.UtcNow - existing.LastSeenUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        // Prefer stone.endpoint (the Moss base URL) directly
        string? mossEndpoint = null;
        if (!string.IsNullOrWhiteSpace(snapshot.StoneEndpoint))
        {
            mossEndpoint = snapshot.StoneEndpoint.TrimEnd('/');
        }
        else
        {
            var host = !string.IsNullOrWhiteSpace(snapshot.Connection?.Hostname)
                ? snapshot.Connection.Hostname
                : !string.IsNullOrWhiteSpace(snapshot.Connection?.Ip)
                    ? snapshot.Connection.Ip
                    : null;

            if (!string.IsNullOrWhiteSpace(host))
            {
                mossEndpoint = $"http://{host}:{Constants.Moss.DefaultPort}";
            }
        }

        if (string.IsNullOrWhiteSpace(mossEndpoint))
        {
            return;
        }

        var learned = new CachedMossStone
        {
            Endpoint = mossEndpoint,
            StoneId = snapshot.StoneId,
            StoneName = snapshot.StoneName,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        CacheStone(learned);
        _logger.LogDebug(
            "Topology enrichment: learned stone {StoneName} at {Endpoint} from tool {ToolFqid}",
            learned.StoneName, learned.Endpoint, snapshot.ToolFqid);
    }

    /// <summary>
    /// Fetches the full topology from the bound Moss and caches all known stones.
    /// Called after bind and periodically on heartbeat to keep the roster warm.
    /// </summary>
    private async Task HydrateTopologyFromMoss(CancellationToken ct)
    {
        var bound = _boundStone;
        if (bound is null)
        {
            return;
        }

        try
        {
            var uri = $"{bound.Endpoint.TrimEnd('/')}{Constants.Moss.TopologyEndpoint}";

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _httpClient.GetAsync(uri, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var json = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<TopologyApiResponse>(json, _jsonOptions);
            var entries = envelope?.Data;
            if (entries is null || entries.Count == 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var learned = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Endpoint) || string.IsNullOrWhiteSpace(entry.StoneName))
                {
                    continue;
                }

                var lastSeen = entry.LastSeen ?? now;

                // Primary: cache with the endpoint from the topology API (typically IP-based).
                var stone = new CachedMossStone
                {
                    Endpoint = entry.Endpoint,
                    StoneId = entry.StoneId,
                    StoneName = entry.StoneName,
                    MossVersion = entry.MossVersion,
                    LastSeenUtc = lastSeen
                };

                CacheStone(stone);
                learned++;

                // Secondary: also cache a .local variant for mDNS resolution on the host network.
                // This gives the resolution chain two reachable paths per stone.
                var localEndpoint = $"http://{entry.StoneName}.local:{Constants.Moss.DefaultPort}";
                if (!string.Equals(entry.Endpoint, localEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    var localStone = stone with { Endpoint = localEndpoint };
                    _stoneCache.TryAdd($"{entry.StoneName}.local", localStone);
                }
            }

            _lastTopologyHydration = now;

            if (learned > 0)
            {
                _logger.LogDebug(
                    "Topology hydration: cached {Count} stones from bound Moss {StoneName}.",
                    learned, bound.StoneName);
                PersistRosterFireAndForget();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Topology hydration from bound Moss failed.");
        }
    }

    private void HydrateTopologyFireAndForget(CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await HydrateTopologyFromMoss(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Background topology hydration failed.");
            }
        }, CancellationToken.None);
    }

    private async Task TryHydrateTopologyThrottled(CancellationToken ct)
    {
        var elapsed = DateTimeOffset.UtcNow - _lastTopologyHydration;
        if (elapsed < TimeSpan.FromMinutes(Constants.Moss.TopologyHydrationIntervalMinutes))
        {
            return;
        }

        await HydrateTopologyFromMoss(ct).ConfigureAwait(false);
    }

    private async Task<CachedMossStone?> FindFirstReachable(
        IReadOnlyList<CachedMossStone> stones,
        CancellationToken ct)
    {
        foreach (var stone in stones)
        {
            if (await IsMossReachable(stone, ct))
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
        PersistRosterFireAndForget();
        HydrateTopologyFireAndForget(_lifetimeCts.Token);
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

    private async Task<bool> IsMossReachable(CachedMossStone stone, CancellationToken ct)
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

    private async Task<IReadOnlyList<CachedMossStone>> DiscoverStones(
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
        endpoint = "";
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

    private bool IsContainerizedRuntime()
    {
        if (TryReadBooleanEnvironment(Constants.EnvironmentVariables.DotnetRunningInContainer, out var explicitContainer))
        {
            return explicitContainer;
        }

        return false;
    }

    private bool RequireHostMossWhenContainerized()
    {
        if (TryReadBooleanEnvironment(Constants.EnvironmentVariables.RequireHostMossWhenContainerized, out var envOverride))
        {
            return envOverride;
        }

        return _options.RequireHostMossWhenContainerized;
    }

    private bool TryResolveContainerHostEndpoint(out string endpoint)
    {
        endpoint = "";

        var hostSelector = NormalizeEndpointOrSelector(
            System.Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.ContainerHost));
        if (string.IsNullOrWhiteSpace(hostSelector))
        {
            hostSelector = NormalizeEndpointOrSelector(_options.ContainerHost);
        }

        if (string.IsNullOrWhiteSpace(hostSelector))
        {
            return false;
        }

        if (TryNormalizeAbsoluteEndpoint(hostSelector, out endpoint))
        {
            return true;
        }

        var port = _options.ContainerHostPort;
        if (TryReadIntEnvironment(Constants.EnvironmentVariables.ContainerHostPort, 1, 65535, out var envPort))
        {
            port = envPort;
        }

        var hostOnly = $"http://{hostSelector.Trim()}:{port}";
        return TryNormalizeAbsoluteEndpoint(hostOnly, out endpoint);
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

        if (subscription.ToolType == ZenGardenToolType.SeedBank)
        {
            query.Add($"category={Uri.EscapeDataString(ToWireCategory(subscription.ToolType.Value))}");
        }

        if (!string.IsNullOrWhiteSpace(subscription.ToolFqid))
        {
            query.Add($"fqid={Uri.EscapeDataString(subscription.ToolFqid)}");
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

    private Uri BuildCapabilityEnsureUri(string endpoint, string offeringSelector)
    {
        var encodedOffering = Uri.EscapeDataString(offeringSelector.Trim().ToLowerInvariant());
        var path = string.Format(
            CultureInfo.InvariantCulture,
            Constants.Moss.CapabilityEnsureEndpointFormat,
            encodedOffering);
        return CreateBaseUriBuilder(endpoint, path).Uri;
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

    private static string ToWireCategory(ZenGardenToolType toolType)
    {
        return toolType switch
        {
            ZenGardenToolType.Offering => "offering",
            ZenGardenToolType.SeedBank => "storage",
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
            return [];
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

        // New GardenTool shape: { fqid, tool: {}, stone: {}, service: {}, capabilities: [] }
        var toolFqid = ReadString(payload, "fqid") ?? ReadString(payload, "tool_fqid");
        if (string.IsNullOrWhiteSpace(toolFqid))
        {
            return false;
        }

        // Nested objects
        TryGetProperty(payload, "tool", out var toolElement);
        TryGetProperty(payload, "stone", out var stoneElement);
        TryGetProperty(payload, "service", out var serviceElement);

        var offeringType = ReadString(toolElement, "type");
        var category = ReadString(toolElement, "category");
        var toolUid = ReadString(toolElement, "id") ?? ReadString(payload, "tool_uid");

        var stoneId = ReadString(stoneElement, "id") ?? ReadString(payload, "stone_id");
        var stoneName = ReadString(stoneElement, "name") ?? ReadString(payload, "stone_name");
        var stoneEndpoint = ReadString(stoneElement, "endpoint");

        // Service status/ready — fall back to old flat fields
        var statusString = ReadString(serviceElement, "status") ?? ReadString(payload, "state");
        var state = ParseToolState(statusString);
        var ready = ReadBoolean(serviceElement, "ready") ?? ReadBoolean(payload, "ready") ?? false;
        if (state == ZenGardenToolState.Ready)
        {
            ready = true;
        }

        var capabilities = ParseCapabilities(payload);

        snapshot = new ZenGardenToolSnapshot
        {
            ToolFqid = toolFqid.ToLowerInvariant(),
            ToolUid = toolUid,
            OfferingType = offeringType,
            Category = category,
            ToolType = ParseToolTypeFromCategory(category)
                ?? ParseToolType(ReadString(payload, "tool_type"))
                ?? ZenGardenToolType.Unknown,
            State = state,
            Ready = ready,
            Revision = ReadLong(payload, "revision") ?? 0,
            StoneId = stoneId,
            StoneName = stoneName,
            StoneEndpoint = stoneEndpoint,
            Aliases = ParseAliases(payload),
            Connection = ParseServiceConnection(serviceElement, stoneElement),
            Capabilities = capabilities,
            CapabilityRevision = ReadLong(payload, "capability_revision"),
            UpdatedAt = ParseDateTimeOffset(ReadString(payload, "updated_at"))
        };

        return true;
    }

    private static ZenGardenConnection? ParseServiceConnection(JsonElement serviceElement, JsonElement stoneElement)
    {
        if (serviceElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string[] uris = [];
        if (TryGetProperty(serviceElement, "uris", out var urisElement) &&
            urisElement.ValueKind == JsonValueKind.Array)
        {
            uris = urisElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        var protocol = ReadString(serviceElement, "protocol");

        // Derive hostname / IP from stone metadata
        string? hostname = null;
        string? ip = null;
        int? port = null;

        var stoneName = ReadString(stoneElement, "name");
        if (!string.IsNullOrWhiteSpace(stoneName))
        {
            hostname = stoneName.Contains('.') ? stoneName : $"{stoneName}.local";
        }

        var stoneEndpoint = ReadString(stoneElement, "endpoint");
        if (!string.IsNullOrWhiteSpace(stoneEndpoint))
        {
            ip = ExtractHost(stoneEndpoint);
        }

        // Extract port from first URI
        if (uris.Length > 0 && Uri.TryCreate(uris[0], UriKind.Absolute, out var parsed) && parsed.Port > 0)
        {
            port = parsed.Port;
        }

        return new ZenGardenConnection
        {
            Protocol = protocol,
            Hostname = hostname,
            Ip = ip,
            Port = port,
            Uris = uris
        };
    }

    private static string? ExtractHost(string endpoint)
    {
        var without = endpoint
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase);
        var hostPort = without.Split('/')[0];
        var colonIdx = hostPort.LastIndexOf(':');
        return colonIdx > 0 ? hostPort[..colonIdx] : hostPort;
    }

    private static IReadOnlyList<string> ParseAliases(JsonElement payload)
    {
        if (!TryGetProperty(payload, "aliases", out var aliasesElement) ||
            aliasesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return aliasesElement.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseCapabilities(JsonElement payload)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(payload, "capabilities", out var caps))
        {
            return result;
        }

        // New format: array of { type, items }
        if (caps.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in caps.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var capType = ReadString(entry, "type") ?? ReadString(entry, "cap_type");
                if (string.IsNullOrWhiteSpace(capType))
                {
                    continue;
                }

                if (!TryGetProperty(entry, "items", out var itemsElement) ||
                    itemsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var items = itemsElement.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (items.Length > 0)
                {
                    result[capType.Trim().ToLowerInvariant()] = items;
                }
            }

            return result;
        }

        // Legacy format: object map { "model": ["llama3"] }
        if (caps.ValueKind == JsonValueKind.Object)
        {
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
        }

        return result;
    }

    private static ZenGardenToolType? ParseToolTypeFromCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        return category.Trim().ToLowerInvariant() switch
        {
            "storage" => ZenGardenToolType.SeedBank,
            _ => ZenGardenToolType.Offering
        };
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
            "running" or "ready" => ZenGardenToolState.Ready,
            "degraded" => ZenGardenToolState.Degraded,
            "stopped" or "unavailable" => ZenGardenToolState.Unavailable,
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

    private sealed record CapabilitySubscriptionRegistration(
        Guid Id,
        string? RequestId,
        ZenGardenCapabilityWatchOptions Options,
        Func<ZenGardenCapabilityProgressEvent, CancellationToken, ValueTask> Handler)
    {
        public bool Matches(string requestId)
        {
            return string.IsNullOrWhiteSpace(RequestId)
                || string.Equals(RequestId, requestId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class CapabilityWishRegistration
    {
        public required string RequestId { get; init; }
        public required string ToolFqid { get; init; }
        public required string OfferingSelector { get; init; }
        public required IReadOnlyList<ZenGardenCapabilityRequirement> Requirements { get; init; }
        public required ZenGardenCapabilityWish Current { get; set; }
        public object Gate { get; } = new();
    }

    private sealed record CapabilityEnsureRequest
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; init; }

        [JsonPropertyName("dry_run")]
        public bool DryRun { get; init; }
    }

    private readonly record struct CapabilityEnsureResult(bool Success, string Message);

    private sealed record TopologyApiResponse
    {
        [JsonPropertyName("data")]
        public List<MossTopologyEntry>? Data { get; init; }
    }

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

    private sealed class CapabilitySubscriptionHandle : IDisposable
    {
        private readonly ZenGardenClient _owner;
        private readonly Guid _id;
        private int _disposed;

        public CapabilitySubscriptionHandle(ZenGardenClient owner, Guid id)
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

            _owner._capabilitySubscriptions.TryRemove(_id, out _);
        }
    }
}
