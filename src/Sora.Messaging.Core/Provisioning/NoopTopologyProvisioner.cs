using Sora.Messaging.Provisioning;

namespace Sora.Messaging;

/// <summary>
/// Default provisioner used when a provider does not supply an implementation yet.
/// Performs no operations (safe no-op) but allows planner code paths to execute.
/// </summary>
internal sealed class NoopTopologyProvisioner : IAdvancedTopologyProvisioner
{
    public Task DeclareExchangeAsync(string name, ExchangeType type = ExchangeType.Topic, bool durable = true, bool autoDelete = false, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task DeclareQueueAsync(string name, bool durable = true, bool exclusive = false, bool autoDelete = false, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task BindQueueAsync(string queue, string exchange, string routingKey, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task DeclareQueueAsync(QueueSpec spec, CancellationToken ct = default)
        => Task.CompletedTask;
}
