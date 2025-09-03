using System.Threading;
using System.Threading.Tasks;

namespace Sora.Messaging.Provisioning;

/// <summary>
/// Optional extended provisioner contract allowing the planner to pass the full QueueSpec (arguments, DLQ, retry metadata).
/// Implementations may ignore unsupported features but MUST remain idempotent.
/// </summary>
public interface IAdvancedTopologyProvisioner : ITopologyProvisioner
{
    /// <summary>
    /// Declare a queue using the full queue specification.
    /// </summary>
    Task DeclareQueueAsync(QueueSpec spec, CancellationToken ct = default);
}
