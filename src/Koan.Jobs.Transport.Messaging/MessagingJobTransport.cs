using Koan.Messaging;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Transport.Messaging;

/// <summary>
/// Cross-node push-dispatch over Koan.Messaging. Wraps an in-process signal for the local wake and fans a
/// <see cref="JobReadySignal"/> out to peers on every submit; an inbound peer signal wakes this node to claim.
/// </summary>
/// <remarks>
/// The ledger is still the truth — the signal only trims dispatch latency, so a dropped or duplicated message
/// costs at most one poll interval and never affects correctness (claims remain atomic, handlers idempotent).
/// </remarks>
public sealed class MessagingJobTransport : IJobTransport
{
    private static readonly string NodeId = Guid.CreateVersion7().ToString("N");
    private readonly InProcessJobTransport _local = new();
    private readonly IMessageProxy _proxy;
    private readonly ILogger<MessagingJobTransport> _logger;

    public MessagingJobTransport(IMessageProxy proxy, ILogger<MessagingJobTransport> logger)
    {
        _proxy = proxy;
        _logger = logger;
    }

    public void Notify()
    {
        _local.Notify();   // wake this node immediately
        _ = Fanout();      // and tell peers (fire-and-forget; the ledger covers any loss)
    }

    private async Task Fanout()
    {
        try { await _proxy.SendAsync(new JobReadySignal { OriginNode = NodeId }); }
        catch (Exception ex) { _logger.LogDebug(ex, "Koan.Jobs: ready-signal publish failed"); }
    }

    public Task WaitForWork(TimeSpan timeout, CancellationToken ct) => _local.WaitForWork(timeout, ct);

    /// <summary>Invoked by the messaging handler when a peer publishes a ready signal.</summary>
    internal void OnRemote(JobReadySignal signal)
    {
        if (string.Equals(signal.OriginNode, NodeId, StringComparison.Ordinal)) return;  // ignore our own echo
        _local.Notify();   // a peer has work → wake to compete for the claim
    }
}
