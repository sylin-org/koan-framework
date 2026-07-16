namespace Koan.Communication.Runtime;

internal sealed record CommunicationOperationSnapshot(
    Guid OperationId,
    long Enumerated,
    long Accepted,
    long Rejected,
    bool SourceCompleted,
    int TargetGroups,
    Task<CommunicationSettlementCounts> Settlement);
