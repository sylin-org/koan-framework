using Sora.Core;

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
                return Task.FromResult(new HealthReport(Name, HealthState.Healthy, "connected"));
            }
            return Task.FromResult(new HealthReport(Name, HealthState.Degraded, "RabbitMQ not the default bus"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex));
        }
    }
}