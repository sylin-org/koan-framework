using System;

namespace Koan.Data.Core.Transfers;

public sealed record TransferAuditBatch(
    TransferKind Kind,
    long BatchNumber,
    int BatchCount,
    long TotalProcessed,
    TransferContextSnapshot From,
    TransferContextSnapshot To,
    TimeSpan Elapsed,
    bool IsSummary);

public sealed record TransferConflict<TKey>(TKey Id, string Reason) where TKey : notnull;
