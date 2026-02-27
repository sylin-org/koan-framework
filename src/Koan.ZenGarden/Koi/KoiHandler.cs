using System.Runtime.CompilerServices;

namespace Koan.ZenGarden.Koi;

/// <summary>
/// Self-contained background process that maintains a live topology projection from Koi.
/// Consumes Koi's SSE events stream for <c>_moss._tcp</c> (and optionally <c>_lantern._tcp</c>)
/// and publishes topology events that <see cref="ZenGardenClient"/> intercepts.
/// </summary>
internal sealed class KoiHandler : IKoiHandler
{
    private readonly ZenGardenOptions _options;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string _koiEndpoint;

    private readonly ConcurrentDictionary<string, DiscoveredStone> _stones = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DiscoveredLantern> _lanterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, SubscriptionEntry> _subscribers = new();

    private volatile KoiTopologySnapshot _snapshot = KoiTopologySnapshot.Empty;
    private volatile KoiHandlerState _state = KoiHandlerState.Initializing;
    private CancellationTokenSource? _lifetimeCts;
    private DateTimeOffset? _koiDetectedAt;
    private string? _koiVersion;
    private int _started;
    private int _disposed;

    public KoiHandler(ZenGardenOptions options, ILogger logger)
        : this(options, logger, new HttpClient())
    {
    }

    internal KoiHandler(ZenGardenOptions options, ILogger logger, HttpClient httpClient)
    {
        _options = options;
        _logger = logger;
        _httpClient = httpClient;
        _koiEndpoint = ResolveKoiEndpoint(options);
    }

    public KoiHandlerState State => _state;
    public KoiTopologySnapshot CurrentSnapshot => _snapshot;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;

        _lifetimeCts = new CancellationTokenSource();
        _ = RunAsync(_lifetimeCts.Token);
    }

    public IDisposable OnTopologyEvent(Func<KoiTopologyEvent, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var entry = new SubscriptionEntry(Guid.NewGuid(), handler);
        _subscribers[entry.Id] = entry;
        return new SubscriptionHandle(this, entry.Id);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetimeCts?.Cancel();
        _lifetimeCts?.Dispose();
        _httpClient.Dispose();
    }

    // ── Core loop ────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.Zero;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (backoff > TimeSpan.Zero)
                    await Task.Delay(backoff, ct).ConfigureAwait(false);

                // Probe Koi health
                var healthy = await ProbeHealthAsync(ct).ConfigureAwait(false);
                if (!healthy)
                {
                    if (_state != KoiHandlerState.NotDetected)
                    {
                        _logger.LogInformation(
                            "Koi not detected at {Endpoint}; discovery will retry every {Interval}s.",
                            _koiEndpoint, _options.KoiRetryInterval.TotalSeconds);
                    }
                    TransitionTo(KoiHandlerState.NotDetected);
                    backoff = _options.KoiRetryInterval;
                    continue;
                }

                // Connected — browse and stream
                _koiDetectedAt ??= DateTimeOffset.UtcNow;
                TransitionTo(KoiHandlerState.Connected);
                await EmitAsync(KoiTopologyEventKind.KoiAvailable, ct).ConfigureAwait(false);

                // Initial browse: snapshot the current topology
                await BrowseStonesAsync(ct).ConfigureAwait(false);
                if (_options.KoiLanternDiscovery)
                    await BrowseLanternsAsync(ct).ConfigureAwait(false);

                PublishSnapshot();
                _logger.LogInformation(
                    "Koi connected at {Endpoint} (v{Version}): discovered {StoneCount} stone(s), {LanternCount} lantern(s).",
                    _koiEndpoint, _koiVersion ?? "?", _snapshot.Stones.Count, _snapshot.Lanterns.Count);
                await EmitAsync(KoiTopologyEventKind.TopologyReset, ct).ConfigureAwait(false);

                // Long-lived SSE event streams
                if (_options.KoiContinuousDiscovery)
                    await ConsumeEventsAsync(ct).ConfigureAwait(false);
                else
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Koi handler loop iteration failed.");

                if (_state == KoiHandlerState.Connected)
                {
                    _logger.LogWarning("Koi connection lost: {Message}. Reconnecting.", ex.Message);
                    TransitionTo(KoiHandlerState.Reconnecting);
                    await EmitSafeAsync(KoiTopologyEventKind.KoiLost, ct).ConfigureAwait(false);
                }

                backoff = NextBackoff(backoff);
            }
        }
    }

    // ── Health probe ────────────────────────────────────────────────────

    private async Task<bool> ProbeHealthAsync(CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(_options.KoiHealthTimeout);

            using var response = await _httpClient
                .GetAsync($"{_koiEndpoint}{Constants.Koi.HealthEndpoint}", linked.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return false;

            // Health returns {"ok":true}; version is on /v1/status
            await TryFetchVersionAsync(linked.Token).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug("Koi health probe at {Endpoint} failed: {Message}", _koiEndpoint, ex.Message);
            return false;
        }
    }

    private async Task TryFetchVersionAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"{_koiEndpoint}{Constants.Koi.StatusEndpoint}", ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("version", out var ver))
                _koiVersion = ver.GetString();
        }
        catch
        {
            // Version extraction is best-effort; don't fail the health probe path.
        }
    }

    // ── Initial browse ──────────────────────────────────────────────────

    private async Task BrowseStonesAsync(CancellationToken ct)
    {
        await foreach (var stone in BrowseServiceAsync(Constants.Koi.MossServiceType, ct).ConfigureAwait(false))
        {
            if (stone is null) continue;
            _stones[stone.CacheKey] = stone;
        }
    }

    private async Task BrowseLanternsAsync(CancellationToken ct)
    {
        await foreach (var lantern in BrowseLanternServiceAsync(ct).ConfigureAwait(false))
        {
            if (lantern is null) continue;
            _lanterns[lantern.Name] = lantern;
        }
    }

    private async IAsyncEnumerable<DiscoveredStone?> BrowseServiceAsync(
        string serviceType, [EnumeratorCancellation] CancellationToken ct)
    {
        var uri = $"{_koiEndpoint}{Constants.Koi.BrowseEndpoint}?type={serviceType}&idle_for={(int)_options.KoiBrowseIdleTimeout.TotalSeconds}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line.AsSpan(6);
            yield return ParseStoneFromBrowse(json);
        }
    }

    private async IAsyncEnumerable<DiscoveredLantern?> BrowseLanternServiceAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var uri = $"{_koiEndpoint}{Constants.Koi.BrowseEndpoint}?type={Constants.Koi.LanternServiceType}&idle_for={(int)_options.KoiBrowseIdleTimeout.TotalSeconds}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line.AsSpan(6);
            yield return ParseLanternFromBrowse(json);
        }
    }

    // ── Continuous SSE consumption ──────────────────────────────────────

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        // Run Stone and Lantern event streams. Stones are required;
        // Lanterns are secondary. If Lantern stream fails, only Stones continue.
        if (_options.KoiLanternDiscovery)
        {
            var lanternCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var lanternTask = ConsumeServiceEventsAsync(
                Constants.Koi.LanternServiceType,
                ProcessLanternEventAsync,
                lanternCts.Token);

            try
            {
                await ConsumeServiceEventsAsync(
                    Constants.Koi.MossServiceType,
                    ProcessStoneEventAsync,
                    ct).ConfigureAwait(false);
            }
            finally
            {
                await lanternCts.CancelAsync();
                lanternCts.Dispose();
                try { await lanternTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
        else
        {
            await ConsumeServiceEventsAsync(
                Constants.Koi.MossServiceType,
                ProcessStoneEventAsync,
                ct).ConfigureAwait(false);
        }
    }

    private async Task ConsumeServiceEventsAsync(
        string serviceType,
        Func<string, ReadOnlyMemory<char>, CancellationToken, ValueTask> processor,
        CancellationToken ct)
    {
        var uri = $"{_koiEndpoint}{Constants.Koi.EventsEndpoint}?type={serviceType}&idle_for=0";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var json = line.AsMemory(6);
            var eventKind = ExtractEventKind(json.Span);
            if (eventKind is not null)
                await processor(eventKind, json, ct).ConfigureAwait(false);
        }
    }

    // ── Stone event processing ──────────────────────────────────────────

    private async ValueTask ProcessStoneEventAsync(string eventKind, ReadOnlyMemory<char> json, CancellationToken ct)
    {
        switch (eventKind)
        {
            case KoiEventKinds.Resolved:
            {
                var stone = ParseStoneFromEvent(json.Span);
                if (stone is null) return;

                var isNew = !_stones.TryGetValue(stone.CacheKey, out var previous);
                _stones[stone.CacheKey] = stone;
                PublishSnapshot();

                if (isNew)
                    await EmitStoneAsync(KoiTopologyEventKind.StoneOnline, stone, null, ct).ConfigureAwait(false);
                else if (!stone.TopologyEquals(previous))
                    await EmitStoneAsync(KoiTopologyEventKind.StoneChanged, stone, previous, ct).ConfigureAwait(false);
                break;
            }
            case KoiEventKinds.Removed:
            {
                var name = ExtractServiceName(json.Span);
                if (name is null) return;

                // Try to find the stone by name in our projection
                DiscoveredStone? removed = null;
                foreach (var kvp in _stones)
                {
                    if (string.Equals(kvp.Value.StoneName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        removed = kvp.Value;
                        _stones.TryRemove(kvp.Key, out _);
                        break;
                    }
                }

                if (removed is not null)
                {
                    PublishSnapshot();
                    await EmitStoneAsync(KoiTopologyEventKind.StoneOffline, removed, null, ct).ConfigureAwait(false);
                }

                break;
            }
            // "found" events are incomplete (no IP yet) — wait for "resolved"
        }
    }

    // ── Lantern event processing ────────────────────────────────────────

    private async ValueTask ProcessLanternEventAsync(string eventKind, ReadOnlyMemory<char> json, CancellationToken ct)
    {
        switch (eventKind)
        {
            case KoiEventKinds.Resolved:
            {
                var lantern = ParseLanternFromEvent(json.Span);
                if (lantern is null) return;

                var isNew = !_lanterns.ContainsKey(lantern.Name);
                _lanterns[lantern.Name] = lantern;
                PublishSnapshot();

                if (isNew)
                    await EmitLanternAsync(KoiTopologyEventKind.LanternFound, lantern, ct).ConfigureAwait(false);
                break;
            }
            case KoiEventKinds.Removed:
            {
                var name = ExtractServiceName(json.Span);
                if (name is null) return;

                if (_lanterns.TryRemove(name, out var removed))
                {
                    PublishSnapshot();
                    await EmitLanternAsync(KoiTopologyEventKind.LanternLost, removed, ct).ConfigureAwait(false);
                }

                break;
            }
        }
    }

    // ── Snapshot management ─────────────────────────────────────────────

    private void PublishSnapshot()
    {
        _snapshot = new KoiTopologySnapshot
        {
            State = _state,
            Stones = _stones.Values.ToArray(),
            Lanterns = _lanterns.Values.ToArray(),
            LastUpdate = DateTimeOffset.UtcNow,
            KoiDetectedAt = _koiDetectedAt,
            KoiVersion = _koiVersion
        };
    }

    private void TransitionTo(KoiHandlerState newState)
    {
        if (_state == newState) return;
        _logger.LogDebug("Koi handler: {Previous} → {New}", _state, newState);
        _state = newState;
    }

    // ── Event emission ──────────────────────────────────────────────────

    private async ValueTask EmitAsync(KoiTopologyEventKind kind, CancellationToken ct)
    {
        var evt = new KoiTopologyEvent { Kind = kind, Snapshot = _snapshot };
        await DispatchAsync(evt, ct).ConfigureAwait(false);
    }

    private async ValueTask EmitStoneAsync(
        KoiTopologyEventKind kind, DiscoveredStone stone, DiscoveredStone? previous, CancellationToken ct)
    {
        var evt = new KoiTopologyEvent
        {
            Kind = kind,
            Stone = stone,
            Previous = previous,
            Snapshot = _snapshot
        };
        await DispatchAsync(evt, ct).ConfigureAwait(false);
    }

    private async ValueTask EmitLanternAsync(
        KoiTopologyEventKind kind, DiscoveredLantern lantern, CancellationToken ct)
    {
        var evt = new KoiTopologyEvent
        {
            Kind = kind,
            Lantern = lantern,
            Snapshot = _snapshot
        };
        await DispatchAsync(evt, ct).ConfigureAwait(false);
    }

    private ValueTask EmitSafeAsync(KoiTopologyEventKind kind, CancellationToken ct)
    {
        try
        {
            return EmitAsync(kind, ct);
        }
        catch
        {
            return ValueTask.CompletedTask;
        }
    }

    private async ValueTask DispatchAsync(KoiTopologyEvent evt, CancellationToken ct)
    {
        // Snapshot subscribers before iterating to avoid mutation during dispatch
        var subscribers = _subscribers.Values.ToArray();
        foreach (var entry in subscribers)
        {
            try
            {
                await entry.Handler(evt, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Koi topology event handler {Id} failed for {Kind}.", entry.Id, evt.Kind);
            }
        }
    }

    // ── JSON parsing ────────────────────────────────────────────────────

    private static DiscoveredStone? ParseStoneFromBrowse(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            var root = doc.RootElement;

            // Browse format: {"found": {"name":..., "ip":..., "port":..., "txt":...}}
            if (!root.TryGetProperty("found", out var svc))
                return null;

            return MapStone(svc);
        }
        catch
        {
            return null;
        }
    }

    private static DiscoveredStone? ParseStoneFromEvent(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            var root = doc.RootElement;

            // Event format: {"event":"resolved", "service": {"name":..., "ip":..., ...}}
            if (!root.TryGetProperty("service", out var svc))
                return null;

            return MapStone(svc);
        }
        catch
        {
            return null;
        }
    }

    private static DiscoveredStone? MapStone(JsonElement svc)
    {
        var name = svc.TryGetProperty("name", out var n) ? n.GetString() : null;
        var ip = svc.TryGetProperty("ip", out var i) ? i.GetString() : null;
        var port = svc.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : (int?)null;
        var host = svc.TryGetProperty("host", out var h) ? h.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ip) || port is null)
            return null;

        // Extract TXT records
        string? stoneId = null, mossVersion = null, health = null, mac = null, stoneName = null;
        if (svc.TryGetProperty("txt", out var txt) && txt.ValueKind == JsonValueKind.Object)
        {
            stoneId = txt.TryGetProperty("stone_id", out var sid) ? sid.GetString() : null;
            stoneName = txt.TryGetProperty("stone_name", out var sn) ? sn.GetString() : null;
            mossVersion = txt.TryGetProperty("version", out var ver) ? ver.GetString() : null;
            health = txt.TryGetProperty("health", out var hlt) ? hlt.GetString() : null;
            mac = txt.TryGetProperty("mac", out var m) ? m.GetString() : null;
        }

        // Prefer TXT stone_name, fall back to mDNS instance name
        var resolvedName = !string.IsNullOrWhiteSpace(stoneName) ? stoneName : name;

        var endpoint = $"http://{ip}:{port.Value}";
        string? localEndpoint = null;
        if (!string.IsNullOrWhiteSpace(host))
        {
            var cleanHost = host.TrimEnd('.');
            localEndpoint = $"http://{cleanHost}:{port.Value}";
        }

        return new DiscoveredStone
        {
            StoneName = resolvedName,
            StoneId = stoneId,
            Endpoint = endpoint,
            LocalEndpoint = localEndpoint,
            MossVersion = mossVersion,
            Health = health,
            Mac = mac,
            DiscoveredAt = DateTimeOffset.UtcNow
        };
    }

    private static DiscoveredLantern? ParseLanternFromBrowse(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            if (!doc.RootElement.TryGetProperty("found", out var svc))
                return null;
            return MapLantern(svc);
        }
        catch
        {
            return null;
        }
    }

    private static DiscoveredLantern? ParseLanternFromEvent(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            if (!doc.RootElement.TryGetProperty("service", out var svc))
                return null;
            return MapLantern(svc);
        }
        catch
        {
            return null;
        }
    }

    private static DiscoveredLantern? MapLantern(JsonElement svc)
    {
        var name = svc.TryGetProperty("name", out var n) ? n.GetString() : null;
        var ip = svc.TryGetProperty("ip", out var i) ? i.GetString() : null;
        var port = svc.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : (int?)null;
        var host = svc.TryGetProperty("host", out var h) ? h.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ip) || port is null)
            return null;

        string? localEndpoint = null;
        if (!string.IsNullOrWhiteSpace(host))
        {
            var cleanHost = host.TrimEnd('.');
            localEndpoint = $"http://{cleanHost}:{port.Value}";
        }

        return new DiscoveredLantern
        {
            Name = name,
            Endpoint = $"http://{ip}:{port.Value}",
            LocalEndpoint = localEndpoint,
            DiscoveredAt = DateTimeOffset.UtcNow
        };
    }

    private static string? ExtractEventKind(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            return doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractServiceName(ReadOnlySpan<char> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToString());
            if (doc.RootElement.TryGetProperty("service", out var svc) &&
                svc.TryGetProperty("name", out var n))
                return n.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ── Endpoint resolution ─────────────────────────────────────────────

    private static string ResolveKoiEndpoint(ZenGardenOptions options)
    {
        // 1. Explicit config
        if (!string.IsNullOrWhiteSpace(options.KoiEndpoint))
            return options.KoiEndpoint.TrimEnd('/');

        // 2. Environment variable
        var env = Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.KoiEndpoint);
        if (!string.IsNullOrWhiteSpace(env))
            return env.TrimEnd('/');

        // 3. Container auto-detect
        var inContainer = string.Equals(
            Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.DotnetRunningInContainer),
            "true", StringComparison.OrdinalIgnoreCase);

        if (inContainer)
        {
            var host = !string.IsNullOrWhiteSpace(options.ContainerHost)
                ? options.ContainerHost
                : "host.docker.internal";
            return $"http://{host}:{Constants.Koi.DefaultPort}";
        }

        // 4. Localhost
        return $"http://localhost:{Constants.Koi.DefaultPort}";
    }

    // ── Backoff ─────────────────────────────────────────────────────────

    private TimeSpan NextBackoff(TimeSpan current)
    {
        if (current <= TimeSpan.Zero)
            return TimeSpan.FromSeconds(1);

        var next = TimeSpan.FromTicks(current.Ticks * 2);
        var cap = _options.KoiRetryInterval;
        return next > cap ? cap : next;
    }

    // ── Inner types ─────────────────────────────────────────────────────

    private sealed record SubscriptionEntry(Guid Id, Func<KoiTopologyEvent, CancellationToken, ValueTask> Handler);

    private sealed class SubscriptionHandle(KoiHandler owner, Guid id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            owner._subscribers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Well-known event kind strings from Koi SSE.
    /// </summary>
    private static class KoiEventKinds
    {
        public const string Found = "found";
        public const string Resolved = "resolved";
        public const string Removed = "removed";
    }
}
