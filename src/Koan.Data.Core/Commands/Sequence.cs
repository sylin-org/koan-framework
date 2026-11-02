using System.Linq.Expressions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;

namespace Koan.Data.Core.Commands;

/// <summary>
/// A numbered column with no gap in what is committed (DATA-0167). Declare one per column, which carries
/// <c>[Index(Unique = true)]</c> so that its top is one row to read:
/// <code>public static readonly Sequence Log = Sequence.Of&lt;Event&gt;(e =&gt; e.Seq);</code>
/// </summary>
public sealed class Sequence
{
    private readonly Func<CancellationToken, Task<long>> _highest;

    private Sequence(Func<CancellationToken, Task<long>> highest) => _highest = highest;

    public static Sequence Of<TEntity>(Expression<Func<TEntity, long>> column) where TEntity : Entity<TEntity>
    {
        if (column.Body is not MemberExpression { Member.Name: var name } ||
            !IndexMetadata.GetIndexes(typeof(TEntity)).Any(index => index.Unique && index.Properties is [var only] && only.Name == name))
            throw new InvalidOperationException(
                $"A sequence over {typeof(TEntity).Name} reads the top of its column as one row: name a long property that carries [Index(Unique = true)].");
        var read = column.Compile();
        return new Sequence(async ct =>
            (await Entity<TEntity>.FirstPage(1, sort => sort.OrderByDescending(column), ct)).FirstOrDefault() is { } top ? read(top) : 0);
    }

    /// <summary>The highest value stored, or zero, in one bounded read: committed state, or inside a hold what it has written too.</summary>
    public Task<long> Highest(CancellationToken ct = default) => _highest(ct);

    /// <summary>The next number. The first use in a hold reads the store, and the hold counts on from there, so a rollback gives
    /// every number back and another process's hold continues from this one's commit. Outside a hold it refuses.</summary>
    public Task<long> Next(CancellationToken ct = default) =>
        EntityContext.CurrentTransaction is TransactionCoordinator { IsHeld: true, IsCompleted: false } hold
            ? hold.Take(this, ct)
            : throw new InvalidOperationException(
                "A sequence numbers inside a hold, so that the read and the write are one transaction. " +
                "Take it in a command's Run, or inside EntityContext.Hold.");
}
