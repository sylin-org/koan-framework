using System.Threading;
using System.Threading.Tasks;

namespace Sora.Messaging.Provisioning;

/// <summary>
/// Provider-agnostic topology provisioner used by the startup planner to declare exchanges, queues and bindings.
/// Implementations MUST be idempotent: declaring an existing object with identical properties MUST NOT throw.
/// </summary>
public interface ITopologyProvisioner
{
    Task DeclareExchangeAsync(string name, ExchangeType type = ExchangeType.Topic, bool durable = true, bool autoDelete = false, CancellationToken ct = default);
    Task DeclareQueueAsync(string name, bool durable = true, bool exclusive = false, bool autoDelete = false, CancellationToken ct = default);
    Task BindQueueAsync(string queue, string exchange, string routingKey, CancellationToken ct = default);
}
