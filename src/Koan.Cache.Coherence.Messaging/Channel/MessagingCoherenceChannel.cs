using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Coherence;
using Koan.Data.Abstractions;
using Koan.Core;
using Koan.Messaging;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Coherence.Messaging.Channel;

/// <summary>
/// Cross-node cache coherence over <c>Koan.Messaging</c>. Uses the framework's
/// <see cref="IMessageProxy"/> to publish; receives via a handler registered with
/// <c>services.On&lt;MessagingInvalidationEnvelope&gt;</c> in the auto-registrar.
/// </summary>
/// <remarks>
/// <para>
/// <c>[ProviderPriority(150)]</c> outranks <c>RedisCoherenceChannel</c> (100). Rationale:
/// when an app already has a messaging bus configured (RabbitMQ, Azure Service Bus, in-memory),
/// re-using that infrastructure is preferred over standing up Redis pub/sub just for cache
/// coherence. Apps that prefer Redis can pin via <c>CacheOptions.CoherenceTransport = "redis-pubsub"</c>.
/// </para>
/// <para>
/// Subscription model: the auto-registrar registers a handler at DI-time. When messages arrive,
/// the handler resolves this channel from <c>AppHost.Current</c> and calls
/// <see cref="HandleIncoming"/>, which forwards to the coordinator-supplied callback.
/// </para>
/// </remarks>
[ProviderPriority(150)]
public sealed class MessagingCoherenceChannel : ICacheCoherenceChannel
{
    private readonly IMessageProxy _proxy;
    private readonly ILogger<MessagingCoherenceChannel> _logger;
    private Func<CacheInvalidation, CancellationToken, ValueTask>? _onReceived;

    public MessagingCoherenceChannel(IMessageProxy proxy, ILogger<MessagingCoherenceChannel> logger)
    {
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string TransportName => "koan-messaging";

    public CoherenceCapabilities Capabilities { get; } = CoherenceCapabilities.BestEffort;

    public async ValueTask Publish(CacheInvalidation invalidation, CancellationToken ct)
    {
        try
        {
            var envelope = MessagingInvalidationEnvelope.FromMessage(invalidation);
            await _proxy.SendAsync(envelope, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Cache: messaging publish failed for kind={Kind} key={Key}",
                invalidation.Kind, invalidation.Key?.Value);
        }
    }

    public ValueTask Subscribe(Func<CacheInvalidation, CancellationToken, ValueTask> onReceived, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onReceived);
        // The actual transport-side subscription is wired at DI-time via services.On<T>().
        // Here we just remember the coordinator's callback so HandleIncoming can dispatch to it.
        _onReceived = onReceived;
        return ValueTask.CompletedTask;
    }

    /// <summary>No-op — Koan.Messaging is not durability-replay-capable from this surface.</summary>
    public ValueTask<string?> CatchUp(
        string? cursor,
        Func<CacheInvalidation, CancellationToken, ValueTask> onReceived,
        CancellationToken ct)
        => ValueTask.FromResult<string?>(null);

    /// <summary>
    /// Called by the messaging handler registered at DI-time. Forwards the deserialized
    /// invalidation to the coordinator-supplied callback if Subscribe has been called.
    /// </summary>
    internal async Task HandleIncoming(MessagingInvalidationEnvelope envelope)
    {
        var handler = _onReceived;
        if (handler is null)
        {
            _logger.LogDebug("Koan.Cache: received messaging invalidation before Subscribe; dropping.");
            return;
        }

        try
        {
            await handler(envelope.ToMessage(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Koan.Cache: failed to apply messaging invalidation kind={Kind} key={Key}",
                envelope.Kind, envelope.Key);
        }
    }
}
