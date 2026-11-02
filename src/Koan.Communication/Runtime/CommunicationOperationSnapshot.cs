namespace Koan.Communication.Runtime;

internal sealed record CommunicationOperationSnapshot(
    Guid OperationId,
    long Enumerated,
    long Accepted,
    long Rejected,
    bool SourceCompleted,
    int? TargetGroups,
    bool SettlementObservable,
    bool AwaitsCommit,
    string Channel,
    string Adapter,
    string Assurance,
    Task<CommunicationSettlementCounts> Settlement);
