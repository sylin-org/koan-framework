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

    /// <summary>The place the <c>From()</c> side resolved to, or <see langword="null"/> when the run was not admitted.</summary>
    public TransferEndpoint? ResolvedFrom { get; init; }

    /// <summary>The place the <c>To()</c> side resolved to, or <see langword="null"/> when the run was not admitted.</summary>
    public TransferEndpoint? ResolvedTo { get; init; }

    /// <summary>How the source rows were read: streamed in provider-bounded pages, or materialized once.</summary>
    public TransferReadStrategy ReadStrategy { get; init; }
}
