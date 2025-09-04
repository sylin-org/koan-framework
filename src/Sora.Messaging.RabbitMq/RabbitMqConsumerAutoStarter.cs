using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Messaging;

namespace Sora.Messaging.RabbitMq;

/// <summary>
/// Pure Sora approach: If you have OnMessage handlers, you get consumers automatically.
/// Zero config, zero setup, it just works.
/// </summary>
internal sealed class RabbitMqConsumerAutoStarter : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<RabbitMqConsumerAutoStarter>? _logger;

    public RabbitMqConsumerAutoStarter(IServiceProvider sp, ILogger<RabbitMqConsumerAutoStarter>? logger = null)
    {
        _sp = sp;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Non-blocking: let the app start, then ensure consumers exist
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureConsumersExistAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[RabbitMQ] Consumer auto-start failed, but application will continue");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureConsumersExistAsync(CancellationToken cancellationToken)
    {
        // The Sora way: if message handlers exist, ensure we have a bus
        // The factory handles everything else (retry, connection, consumer creation)
        
        // Check if any IMessageHandler<T> services are registered
        var hasHandlers = _sp.GetServices<object>()
            .Any(service => service.GetType().GetInterfaces()
                .Any(iface => iface.IsGenericType && 
                             iface.GetGenericTypeDefinition() == typeof(IMessageHandler<>)));

        if (!hasHandlers)
        {
            _logger?.LogDebug("[RabbitMQ] No IMessageHandler<T> services found - no consumers needed");
            return;
        }

        _logger?.LogInformation("[RabbitMQ] Message handlers detected - ensuring consumers exist");

        // Simple retry with backoff - let the factory handle the complex stuff
        const int maxAttempts = 10;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var busSelector = _sp.GetService<IMessageBusSelector>();
                if (busSelector != null)
                {
                    // This triggers factory.Create() which handles consumer creation
                    var bus = busSelector.Resolve(_sp, "rabbit");
                    if (bus != null)
                    {
                        _logger?.LogInformation("[RabbitMQ] Consumers ensured via bus resolution");
                        return;
                    }
                }

                _logger?.LogDebug("[RabbitMQ] Bus resolution attempt {Attempt} failed, retrying...", attempt);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger?.LogDebug(ex, "[RabbitMQ] Consumer ensure attempt {Attempt} failed, retrying...", attempt);
            }

            // Simple exponential backoff
            var delay = TimeSpan.FromMilliseconds(1000 * Math.Pow(1.5, attempt - 1));
            var maxDelay = TimeSpan.FromSeconds(30);
            var finalDelay = delay > maxDelay ? maxDelay : delay;
            
            await Task.Delay(finalDelay, cancellationToken);
        }

        _logger?.LogWarning("[RabbitMQ] Failed to ensure consumers after {MaxAttempts} attempts", maxAttempts);
    }
}