namespace Koan.Communication.Runtime;

internal sealed record CommunicationSettlementCounts(
    long Expected,
    long Delivered,
    long Filtered,
    long Failed);
