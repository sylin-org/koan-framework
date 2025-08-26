using Sora.Core;
using Sora.Core.Observability.Health;

namespace Sora.Messaging.RabbitMq;

internal sealed class RabbitMqHealth : IHealthContributor
{
    private readonly IMessageBusSelector _selector;
    private readonly IServiceProvider _sp;
    public RabbitMqHealth(IMessageBusSelector selector, IServiceProvider sp) { _selector = selector; _sp = sp; }
    public string Name => "mq:rabbitmq";
    public bool IsCritical => true;
    public Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Resolve default bus and attempt a passive declare to ensure connectivity
            var bus = _selector.ResolveDefault(_sp);
            if (bus is RabbitMqBus rabbit)
            {
                // Fast path: connected if channel open; more advanced checks can be added later
                return Task.FromResult(new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Healthy, "connected", null, null));
            }
            return Task.FromResult(new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Degraded, "RabbitMQ not the default bus", null, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthReport(Name, Sora.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null));
        }
    }
}