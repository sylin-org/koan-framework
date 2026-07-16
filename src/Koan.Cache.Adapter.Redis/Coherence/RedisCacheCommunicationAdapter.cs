using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Cache.Options;
using Koan.Cache.Topology;
using Koan.Communication;
using Koan.Communication.Adapters;
using Koan.Core;
using Koan.Core.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Coherence;

/// <summary>
/// Layered Redis capability for Communication's every-node framework-broadcast route. It remains dormant unless
/// Redis is the active remote cache tier and Cache coherence is enabled.
/// </summary>
[ProviderPriority(100)]
internal sealed class RedisCacheCommunicationAdapter : ICommunicationAdapter
{
    private const string ProviderId = "redis-cache";
    private const string ReferenceIdentity = "Koan.Cache.Adapter.Redis";

    private readonly CommunicationAdapterDescriptor _descriptor;
    private readonly RedisCacheBroadcastOptions _options;
    private readonly ILogger<RedisCacheCommunicationAdapter> _logger;
    private readonly ISubscriber _subscriber;
    private readonly int _ingressCapacity;
    private readonly CancellationTokenSource _abort = new();
    private readonly List<ChannelMessageQueue> _subscriptions = [];
    private readonly List<Channel<RedisValue>> _inboxes = [];
    private readonly List<Task> _workers = [];
    private string? _meshId;
    private int _state;

    public RedisCacheCommunicationAdapter(
        CacheTopology topology,
        IConnectionMultiplexer multiplexer,
        IOptions<RedisCacheBroadcastOptions> options,
        IOptions<CacheOptions> cacheOptions,
        IOptions<CommunicationOptions> communicationOptions,
        ILogger<RedisCacheCommunicationAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(multiplexer);
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscriber = multiplexer.GetSubscriber();
        _ingressCapacity = communicationOptions.Value.InProcessCapacity;

        var active = topology.Remote is RedisCacheStore
                     && cacheOptions.Value.CoherenceMode != Koan.Cache.Abstractions.Coherence.CoherenceMode.Disabled;
        _descriptor = new CommunicationAdapterDescriptor(
            ProviderId,
            active ? [CommunicationLane.FrameworkBroadcasts] : [],
            CommunicationDeliveryAssurance.BestEffort,
            CommunicationAdapterCapabilities.ContractIdentity
            | CommunicationAdapterCapabilities.SnapshotCopy
            | CommunicationAdapterCapabilities.NodeFanOut
            | CommunicationAdapterCapabilities.MessageIdentity
            | CommunicationAdapterCapabilities.BoundedAcceptance,
            [ReferenceIdentity],
            IsLayered: true);
    }

    public CommunicationAdapterDescriptor Descriptor => _descriptor;
    public bool IsReady => Volatile.Read(ref _state) == 1;

    public async Task Start(CommunicationAdapterHost host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Interlocked.CompareExchange(ref _state, 3, 0) != 0)
            throw new InvalidOperationException("The Redis Cache Communication provider cannot be started more than once.");

        _meshId = host.MeshId;
        try
        {
            var routes = host.Bindings
                .Where(static binding => binding.Lane == CommunicationLane.FrameworkBroadcasts)
                .GroupBy(static binding => binding.ContractId, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .ToArray();

            foreach (var route in routes)
            {
                var queue = await _subscriber.SubscribeAsync(Channel(host.MeshId, route.Key)).ConfigureAwait(false);
                _subscriptions.Add(queue);
                var inbox = System.Threading.Channels.Channel.CreateBounded<RedisValue>(new BoundedChannelOptions(_ingressCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
                _inboxes.Add(inbox);
                queue.OnMessage(message =>
                {
                    if (!inbox.Writer.TryWrite(message.Message))
                        _logger.LogDebug("Koan Cache Redis broadcast ingress is full; L1 TTL remains the staleness bound.");
                });
                _workers.Add(Pump(host, inbox.Reader, route.ToArray(), _abort.Token));
            }

            Interlocked.Exchange(ref _state, 1);
            _logger.LogInformation(
                "Koan Cache Redis broadcast: active, mesh={Mesh}, contracts={Contracts}, delivery=every-active-node.",
                host.MeshId,
                routes.Length);
        }
        catch
        {
            Interlocked.Exchange(ref _state, 2);
            await StopSubscriptions().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct)
    {
        if (publication.Lane != CommunicationLane.FrameworkBroadcasts)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                $"The Redis Cache provider does not claim {publication.Lane}.");
        if (!IsReady || _meshId is null)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "The Redis Cache broadcast provider is not ready.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.PublishTimeout);
        try
        {
            var subscribers = await _subscriber.PublishAsync(
                    Channel(_meshId, publication.ContractId),
                    publication.Payload.ToArray(),
                    CommandFlags.None)
                .WaitAsync(timeout.Token)
                .ConfigureAwait(false);
            var targets = subscribers > int.MaxValue ? int.MaxValue : (int)subscribers;
            return new CommunicationAdapterAcceptance(targets, SettlementObservable: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "Redis did not accept the Cache broadcast before the configured timeout.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CommunicationAdapterException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "Redis could not publish the Cache broadcast.",
                error);
        }
    }

    public async Task Stop(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _state, 2) == 0) return;
        _abort.Cancel();
        await StopSubscriptions().ConfigureAwait(false);
        if (_workers.Count > 0)
        {
            try { await Task.WhenAll(_workers).WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, 2);
        _abort.Cancel();
        await StopSubscriptions().ConfigureAwait(false);
        if (_workers.Count > 0)
        {
            try { await Task.WhenAll(_workers).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _abort.Dispose();
    }

    private async Task Pump(
        CommunicationAdapterHost host,
        ChannelReader<RedisValue> inbox,
        IReadOnlyList<CommunicationAdapterBinding> bindings,
        CancellationToken ct)
    {
        try
        {
            await foreach (var message in inbox.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var payload = (byte[]?)message;
                if (payload is null) continue;
                foreach (var binding in bindings)
                {
                    await host.Dispatch(binding.Id, payload, ContextIngressTrust.Authenticated, ct)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Koan Cache Redis broadcast receiver stopped unexpectedly.");
            Interlocked.Exchange(ref _state, 2);
        }
    }

    private async Task StopSubscriptions()
    {
        foreach (var subscription in _subscriptions)
        {
            try { await subscription.UnsubscribeAsync().ConfigureAwait(false); }
            catch (Exception error) { _logger.LogDebug(error, "Koan Cache Redis broadcast unsubscribe failed."); }
        }
        _subscriptions.Clear();
        foreach (var inbox in _inboxes) inbox.Writer.TryComplete();
        _inboxes.Clear();
    }

    private RedisChannel Channel(string meshId, string contractId)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.ChannelName) ? "koan-cache" : _options.ChannelName.Trim();
        return new RedisChannel(
            $"{prefix}:{Hash(meshId)}:{Hash(contractId)}",
            RedisChannel.PatternMode.Literal);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
}
