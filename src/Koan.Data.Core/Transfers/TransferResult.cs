using System;
using System.Collections.Generic;

namespace Koan.Data.Core.Transfers;

public sealed class TransferResult<TKey>
    where TKey : notnull
{
    public TransferKind Kind { get; init; }
    public long ReadCount { get; init; }
    public long CopiedCount { get; init; }
    public long DeletedCount { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<TransferConflict<TKey>> Conflicts { get; init; } = Array.Empty<TransferConflict<TKey>>();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool HasConflicts => Conflicts.Count > 0;
}
