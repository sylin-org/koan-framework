using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Data.Abstractions;
using Koan.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Cache.Coherence;

/// <summary>
/// Owns cross-node cache coherence. Subscribes to every registered <c>ICacheCoherenceChannel</c>,
/// routes received invalidations to <c>LayeredCache.ApplyRemoteInvalidation</c>, and exposes
/// <see cref="BroadcastEvict"/>/<see cref="BroadcastEvictByTag"/>/<see cref="BroadcastEvictAll"/>
/// for the cache client to call after successful writes.
/// </summary>
/// <remarks>
/// <para>
/// Coordinator behaviour follows <see cref="CacheOptions.CoherenceMode"/>:
/// <list type="bullet">
///   <item><c>AutoDetect</c> (default): active iff ≥1 channel registered.</item>
///   <item><c>Required</c>: throws at boot if no channels are registered AND a Remote tier is present.</item>
///   <item><c>Disabled</c>: completely inactive; <see cref="BroadcastEvict"/> is a no-op.</item>
/// </list>
/// </para>
/// <para>
/// Origin filter: every published message carries the local <see cref="NodeId"/>; received
/// messages whose <c>OriginNodeId</c> matches are dropped (writer's own subscription echo).
/// </para>
/// <para>
/// Publish failures are logged but never thrown — coherence is best-effort by design. L1 TTL
/// provides defense-in-depth bounded staleness if broadcasts are lost.
/// </para>
/// </remarks>
internal sealed class CoherenceCoordinator : IHostedService, IAsyncDisposable
{
    private readonly NodeIdProvider _nodeIdProvider;
    private readonly IReadOnlyList<ICacheCoherenceChannel> _channels;
    private readonly LayeredCache _layered;
    private readonly CursorStore _cursors;
    private readonly CoherenceCoalescingBuffer? _coalescer;
    private readonly IOptionsMonitor<CacheOptions> _options;
    private readonly ILogger<CoherenceCoordinator> _logger;
    private bool _active;

    public CoherenceCoordinator(
        NodeIdProvider nodeIdProvider,
        IEnumerable<ICacheCoherenceChannel> channels,
        LayeredCache layered,
        CursorStore cursors,
        IOptionsMonitor<CacheOptions> options,
        ILogger<CoherenceCoordinator> logger,
        ILoggerFactory loggerFactory)
    {
        _nodeIdProvider = nodeIdProvider;
        _channels = SelectAndOrderChannels(channels, options.CurrentValue);
        _layered = layered;
        _cursors = cursors;
        _options = options;
        _logger = logger;

        // Coalescing buffer is optional — only constructed if enabled.
        if (options.CurrentValue.CoherenceCoalescingMs > 0)
        {
            _coalescer = new CoherenceCoalescingBuffer(
                options,
                loggerFactory.CreateLogger<CoherenceCoalescingBuffer>(),
                PublishToAllChannels);
        }
    }

    public Guid NodeId => _nodeIdProvider.NodeId;

    public bool IsActive => _active;

    public IReadOnlyList<ICacheCoherenceChannel> Channels => _channels;

    public async Task StartAsync(CancellationToken ct)
    {
        var opts = _options.CurrentValue;

        // Honour Disabled mode regardless of channel registrations.
        if (opts.CoherenceMode == CoherenceMode.Disabled)
        {
            _logger.LogInformation("Koan.Cache coherence: disabled by configuration.");
            return;
        }

        // Honour Required mode: fail fast if no channels and Remote tier is present.
        if (opts.CoherenceMode == CoherenceMode.Required && _channels.Count == 0 && _layered.Topology.Remote is not null)
        {
            throw new InvalidOperationException(
                "CacheOptions.CoherenceMode = Required but no ICacheCoherenceChannel is registered while a Remote tier is configured. " +
                "Reference a coherence-capable adapter (e.g. Koan.Cache.Adapter.Redis or Koan.Cache.Coherence.Messaging) or set CoherenceMode to AutoDetect/Disabled.");
        }

        if (_channels.Count == 0)
        {
            _logger.LogInformation("Koan.Cache coherence: no channels registered — coordinator inactive.");
            return;
        }

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startupCts.CancelAfter(TimeSpan.FromMilliseconds(opts.CoherenceStartupTimeoutMs));

        var subscribed = 0;
        var subscribeFailures = new List<(ICacheCoherenceChannel Channel, Exception Error)>();

        foreach (var channel in _channels)
        {
            try
            {
                await channel.Subscribe(OnReceived, startupCts.Token).ConfigureAwait(false);
                _logger.LogInformation("Koan.Cache coherence: subscribed to {Transport}.", channel.TransportName);
                subscribed++;
            }
            catch (Exception ex)
            {
                subscribeFailures.Add((channel, ex));
                _logger.LogWarning(ex,
                    "Koan.Cache coherence: failed to subscribe to {Transport}; channel will be inactive until next restart.",
                    channel.TransportName);
                // Skip CatchUp for failed channels — there's nothing to catch up against.
                continue;
            }

            if (channel.Capabilities.SupportsCatchUp)
            {
                var cursor = _cursors.Load(channel.TransportName);
                try
                {
                    var newCursor = await channel.CatchUp(cursor, OnReceived, startupCts.Token).ConfigureAwait(false);
                    _cursors.Save(channel.TransportName, newCursor);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Koan.Cache coherence: catch-up failed on {Transport}; continuing.", channel.TransportName);
                }
            }
        }

        // CoherenceMode.Required is a strict contract: every registered channel must subscribe.
        // Any failure means the application's coherence guarantees can't be honored; fail boot.
        // AutoDetect is permissive: degrade gracefully if some/all channels can't reach their
        // transport. The host stays alive; broadcasts are no-ops until a restart with reachable
        // infra. This is the right default per ARCH-0079 (a downed Redis cache shouldn't kill
        // an app whose other pillars are healthy).
        if (opts.CoherenceMode == CoherenceMode.Required && subscribeFailures.Count > 0)
        {
            var first = subscribeFailures[0];
            throw new InvalidOperationException(
                $"CacheOptions.CoherenceMode = Required, but {subscribeFailures.Count} of {_channels.Count} coherence channel(s) " +
                $"failed to subscribe. First failure: {first.Channel.TransportName} — {first.Error.Message}. " +
                $"Set CoherenceMode to AutoDetect (default) to allow graceful degradation, " +
                $"or fix the transport(s) before restarting.",
                first.Error);
        }

        _active = subscribed > 0;

        if (subscribeFailures.Count > 0)
        {
            _logger.LogWarning(
                "Koan.Cache coherence: started in degraded mode — {Subscribed} of {Total} channel(s) subscribed; {Failed} failed.",
                subscribed, _channels.Count, subscribeFailures.Count);
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        _active = false;
        return Task.CompletedTask;
    }

    public ValueTask BroadcastEvict(CacheKey key, string? region, CancellationToken ct)
    {
        if (!_active || _channels.Count == 0) return ValueTask.CompletedTask;
        var msg = CacheInvalidation.EvictKey(key, NodeId, region);
        return EnqueueOrPublish(msg, ct);
    }

    public ValueTask BroadcastEvictByTag(IReadOnlySet<string> tags, string? region, CancellationToken ct)
    {
        if (!_active || _channels.Count == 0 || tags.Count == 0) return ValueTask.CompletedTask;
        var msg = CacheInvalidation.EvictByTag(tags, NodeId, region);
        return EnqueueOrPublish(msg, ct);
    }

    public ValueTask BroadcastEvictAll(string? region, CancellationToken ct)
    {
        if (!_active || _channels.Count == 0) return ValueTask.CompletedTask;
        var msg = CacheInvalidation.EvictAll(NodeId, region);
        return EnqueueOrPublish(msg, ct);
    }

    private ValueTask EnqueueOrPublish(CacheInvalidation msg, CancellationToken ct)
        => _coalescer is not null ? _coalescer.Enqueue(msg, ct) : PublishToAllChannels(msg, ct);

    private async ValueTask PublishToAllChannels(CacheInvalidation msg, CancellationToken ct)
    {
        var tasks = new List<ValueTask>(_channels.Count);
        foreach (var channel in _channels)
            tasks.Add(PublishSafe(channel, msg, ct));

        foreach (var task in tasks)
            await task.ConfigureAwait(false);
    }

    private async ValueTask PublishSafe(ICacheCoherenceChannel channel, CacheInvalidation msg, CancellationToken ct)
    {
        try
        {
            await channel.Publish(msg, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Cache coherence: publish failed on {Transport}.", channel.TransportName);
        }
    }

    private async ValueTask OnReceived(CacheInvalidation msg, CancellationToken ct)
    {
        // Origin filter — never apply our own published invalidations.
        if (msg.OriginNodeId == NodeId) return;

        try
        {
            await _layered.ApplyRemoteInvalidation(msg, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Cache coherence: ApplyRemoteInvalidation failed for key {Key}.", msg.Key?.Value);
        }
    }

    private static IReadOnlyList<ICacheCoherenceChannel> SelectAndOrderChannels(
        IEnumerable<ICacheCoherenceChannel> channels,
        CacheOptions options)
    {
        var snapshot = channels?.ToList() ?? new List<ICacheCoherenceChannel>();

        // Config pin: take only the named transport.
        if (!string.IsNullOrWhiteSpace(options.CoherenceTransport))
        {
            var pinned = snapshot.Where(c => c.TransportName.Equals(options.CoherenceTransport, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pinned.Count > 0) return pinned;
        }

        // Order by [ProviderPriority] descending; ties broken by registration order.
        return snapshot
            .Select((c, idx) => (Channel: c, Priority: GetPriority(c), Index: idx))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Index)
            .Select(t => t.Channel)
            .ToList();
    }

    private static int GetPriority(ICacheCoherenceChannel channel)
        => channel.GetType().GetCustomAttribute<ProviderPriorityAttribute>()?.Priority ?? 0;

    public ValueTask DisposeAsync()
    {
        _coalescer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
