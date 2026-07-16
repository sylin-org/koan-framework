using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;

namespace Koan.Communication;

/// <summary>A fixed-size summary of one Entity Transport publication operation.</summary>
public sealed class TransportAcceptance
{
    private readonly Task<CommunicationSettlementCounts> _settlement;

    internal TransportAcceptance(CommunicationOperationSnapshot snapshot)
    {
        OperationId = snapshot.OperationId;
        Enumerated = snapshot.Enumerated;
        Accepted = snapshot.Accepted;
        Rejected = snapshot.Rejected;
        SourceCompleted = snapshot.SourceCompleted;
        ReceiverGroups = snapshot.TargetGroups;
        Channel = Constants.Transport.DefaultChannel;
        Adapter = Constants.Transport.InProcessAdapter;
        Assurance = Constants.Transport.ProcessMemoryAssurance;
        _settlement = snapshot.Settlement;
    }

    public Guid OperationId { get; }
    public long Enumerated { get; }
    public long Accepted { get; }
    public long Rejected { get; }
    public bool SourceCompleted { get; }
    public int ReceiverGroups { get; }
    public string Channel { get; }
    public string Adapter { get; }
    public string Assurance { get; }

    /// <summary>Waits only for this operation's accepted local receiver targets to settle.</summary>
    public async Task<TransportSettlement> WaitForSettlement(CancellationToken ct = default)
    {
        var counts = ct.CanBeCanceled
            ? await _settlement.WaitAsync(ct).ConfigureAwait(false)
            : await _settlement.ConfigureAwait(false);
        return new TransportSettlement(
            OperationId,
            counts.Expected,
            counts.Delivered,
            counts.Filtered,
            counts.Failed);
    }
}
