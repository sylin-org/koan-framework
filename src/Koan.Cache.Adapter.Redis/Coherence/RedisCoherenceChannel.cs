using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Serialization;
using Koan.Data.Abstractions;
using Koan.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Coherence;

/// <summary>
/// Redis pub/sub <see cref="ICacheCoherenceChannel"/>. Best-effort, at-most-once delivery —
/// missed messages during transient disconnects are NOT replayed. L1 TTL provides
/// defense-in-depth bounded staleness.
/// </summary>
/// <remarks>
/// <para>
/// Shares the <see cref="IConnectionMultiplexer"/> with <c>RedisCacheStore</c> — single
/// connection, dual purpose. Channel name + publish timeout configured via
/// <see cref="RedisCoherenceChannelOptions"/>.
/// </para>
/// <para>
/// Outranks <c>InMemoryCoherenceChannel</c> at priority 100; outranked by
/// <c>MessagingCoherenceChannel</c> at priority 150 when both are registered (so users
/// who have <c>Koan.Messaging</c> wired up don't need separate Redis pub/sub for cache).
/// </para>
/// </remarks>
[ProviderPriority(100)]
public sealed class RedisCoherenceChannel : ICacheCoherenceChannel, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisCoherenceChannelOptions _options;
    private readonly ILogger<RedisCoherenceChannel> _logger;
    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;
    private ChannelMessageQueue? _subscription;

    public RedisCoherenceChannel(
        IConnectionMultiplexer multiplexer,
        IOptions<RedisCoherenceChannelOptions> options,
        ILogger<RedisCoherenceChannel> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _subscriber = _multiplexer.GetSubscriber();
        _channel = new RedisChannel(
            string.IsNullOrWhiteSpace(_options.ChannelName) ? "koan-cache" : _options.ChannelName,
            RedisChannel.PatternMode.Literal);
    }

    public string TransportName => "redis-pubsub";

    public CoherenceCapabilities Capabilities { get; } = CoherenceCapabilities.BestEffort;

    public async ValueTask Publish(CacheInvalidation invalidation, CancellationToken ct)
    {
        try
        {
            var envelope = RedisInvalidationEnvelope.FromMessage(invalidation);
            var payload = RedisCacheJsonConverter.SerializeInvalidation(envelope);
            await _subscriber.PublishAsync(_channel, payload, CommandFlags.FireAndForget).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Cache: Redis pub/sub publish failed for kind={Kind} key={Key}",
                invalidation.Kind, invalidation.Key?.Value);
        }
    }

    public async ValueTask Subscribe(
        Func<CacheInvalidation, CancellationToken, ValueTask> onReceived,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onReceived);

        _subscription = await _subscriber.SubscribeAsync(_channel).ConfigureAwait(false);
        _subscription.OnMessage(async message =>
        {
            try
            {
                var envelope = RedisCacheJsonConverter.DeserializeInvalidation(message.Message);
                await onReceived(envelope.ToMessage(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Koan.Cache: failed to process Redis pub/sub invalidation message.");
            }
        });

        _logger.LogInformation("Koan.Cache: Redis pub/sub channel subscribed: {Channel}", _channel);
    }

    /// <summary>No-op — Redis pub/sub has no replay. <c>RedisStreamsCoherenceChannel</c> (deferred) will.</summary>
    public ValueTask<string?> CatchUp(
        string? cursor,
        Func<CacheInvalidation, CancellationToken, ValueTask> onReceived,
        CancellationToken ct)
        => ValueTask.FromResult<string?>(null);

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            try
            {
                await _subscription.UnsubscribeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Koan.Cache: failed to unsubscribe from Redis pub/sub channel.");
            }
        }
    }
}
