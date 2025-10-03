using System;
using System.Collections.Generic;

namespace Koan.Data.Core.Transfers;

public sealed class TransferResult<TKey>
    where TKey : notnull
{
    public TransferKind Kind { get; init; }
    public int ReadCount { get; init; }
    public int CopiedCount { get; init; }
    public int DeletedCount { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<TransferConflict<TKey>> Conflicts { get; init; } = Array.Empty<TransferConflict<TKey>>();
    public IReadOnlyList<TransferAuditBatch> Audit { get; init; } = Array.Empty<TransferAuditBatch>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool HasConflicts => Conflicts.Count > 0;
}
