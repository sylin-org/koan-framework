using System;

namespace Koan.Data.Core.Transfers;

/// <summary>
/// One audit event of a transfer. <see cref="From"/> and <see cref="To"/> are always the builder's declared
/// <c>From()</c> and <c>To()</c>, on every batch and on the summary; <see cref="Direction"/> says which of them a batch
/// wrote into (NK-252). A Pull, and the reverse writes of a bidirectional Mirror, write into <c>From()</c>.
/// </summary>
public sealed record TransferAuditBatch(
    TransferKind Kind,
    long BatchNumber,
    int BatchCount,
    long TotalProcessed,
    TransferContextSnapshot From,
    TransferContextSnapshot To,
    TimeSpan Elapsed,
    bool IsSummary)
{
    /// <summary>The side this batch wrote into. The summary reports <see cref="TransferDirection.IntoDestination"/>.</summary>
    public TransferDirection Direction { get; init; } = TransferDirection.IntoDestination;

    /// <summary>The place <see cref="From"/> resolved to.</summary>
    public TransferEndpoint? ResolvedFrom { get; init; }

    /// <summary>The place <see cref="To"/> resolved to.</summary>
    public TransferEndpoint? ResolvedTo { get; init; }
}

public sealed record TransferConflict<TKey>(TKey Id, string Reason) where TKey : notnull;
