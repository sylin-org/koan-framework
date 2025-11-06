using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Adapter.Redis.Options;
using Koan.Cache.Adapter.Redis.Serialization;
using Koan.Cache.Adapter.Redis.Stores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Hosting;

internal sealed class RedisInvalidationListener : IHostedService, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisCacheStore _store;
    private readonly IOptionsMonitor<RedisCacheAdapterOptions> _options;
    private readonly ILogger<RedisInvalidationListener> _logger;
    private ChannelMessageQueue? _subscription;
    private bool _started;

    public RedisInvalidationListener(
        IConnectionMultiplexer multiplexer,
        RedisCacheStore store,
        IOptionsMonitor<RedisCacheAdapterOptions> options,
        ILogger<RedisInvalidationListener> logger)
    {
        _multiplexer = multiplexer;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (!opts.EnablePubSubInvalidation)
        {
            _logger.LogDebug("Redis cache invalidation listener skipped because pub/sub is disabled.");
            return;
        }

        if (_started)
        {
            return;
        }

        var subscriber = _multiplexer.GetSubscriber();
        _subscription = await subscriber.SubscribeAsync(_store.Channel);
        _subscription.OnMessage(async message => await HandleMessageAsync(message.Message));
        _started = true;
        _logger.LogInformation("Redis cache invalidation listener subscribed to channel {Channel}.", _store.Channel);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started || _subscription is null)
        {
            return;
        }

        try
        {
            var subscriber = _multiplexer.GetSubscriber();
            await subscriber.UnsubscribeAsync(_store.Channel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unsubscribe from Redis cache invalidation channel {Channel}.", _store.Channel);
        }
        finally
        {
            await _subscription.UnsubscribeAsync();
            _subscription = null;
            _started = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null)
        {
            try
            {
                await _subscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing Redis cache invalidation subscription.");
            }
        }
    }

    private async Task HandleMessageAsync(RedisValue payload)
    {
        try
        {
            var message = RedisCacheJsonConverter.DeserializeInvalidation(payload);
            await _store.HandleInvalidationMessageAsync(message, CancellationToken.None);
            _logger.LogDebug("Processed Redis cache invalidation for key {Key}.", message.Key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process Redis cache invalidation message.");
        }
    }
}
