using System.Linq.Expressions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Transactions;
using Microsoft.Extensions.DependencyInjection;

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
        {
            RefuseAReadThatHidesRows(typeof(TEntity));
            return await Data<TEntity, string>.Top(sort => sort.OrderByDescending(column), ct) is { } top ? read(top) : 0;
        });
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

    /// <summary>A read scope that hides rows inside one unique index, as soft delete does, hides the top of the column, and the next number
    /// collides with a row nobody can see. An equality scope such as a tenant is a domain of its own, and leaves this alone.</summary>
    private static void RefuseAReadThatHidesRows(Type entity)
    {
        var hiding = AppHost.Current?.GetServices<IReadFilterContributor>().Where(contributor => contributor is not ManagedEqualityReadContributor).ToArray();
        if (hiding is not null && ReadScopeFold.Fold(hiding, entity) is not null)
            throw new InvalidOperationException(
                $"A read scope hides rows of {entity.Name}, so a sequence over it would take a number a hidden row already holds. " +
                "Number an entity no read scope hides rows of.");
    }
}
