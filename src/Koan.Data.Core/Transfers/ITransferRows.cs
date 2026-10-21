using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

/// <summary>
/// What a transfer needs from the facade beyond ordinary Entity work to carry a delete override's state (DATA-0155). A
/// soft-deleted row is the stored row plus the override's field (<c>__deleted</c>), which the Entity type cannot hold,
/// so the transfer asks which rows it read carry it, writes those rows with it, and removes moved rows physically.
/// Every call is an ordinary guarded facade operation under the ambient context: source policy, segmentation, guards and
/// the read scope apply.
/// </summary>
internal interface ITransferRows<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>Whether a delete override (soft delete) governs this Entity.</summary>
    bool HasDeleteOverride { get; }

    /// <summary>The identities among <paramref name="ids"/> whose stored row the delete override has marked, within the read scope.</summary>
    Task<IReadOnlySet<TKey>> Marked(IReadOnlyList<TKey> ids, CancellationToken ct);

    /// <summary>Writes <paramref name="rows"/> with the delete override's mark, as a delete would leave them.</summary>
    Task<int> UpsertMarked(IReadOnlyList<TEntity> rows, CancellationToken ct);

    /// <summary>Removes the visible rows among <paramref name="ids"/> physically, bypassing the delete override.</summary>
    Task<int> Purge(IReadOnlyList<TKey> ids, CancellationToken ct);
}
