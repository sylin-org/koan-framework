using System.Diagnostics;
using System.Linq.Expressions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Naming;
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
    private const string HeadOperation = "sequence head";

    private readonly Type _entity;
    private readonly Func<CancellationToken, Task<long>> _highest;

    private Sequence(Type entity, Func<CancellationToken, Task<long>> highest)
    {
        _entity = entity;
        _highest = highest;
    }

    public static Sequence Of<TEntity>(Expression<Func<TEntity, long>> column) where TEntity : Entity<TEntity>
    {
        if (column.Body is not MemberExpression { Member.Name: var name } ||
            !IndexMetadata.GetIndexes(typeof(TEntity)).Any(index => index.Unique && index.Properties is [var only] && only.Name == name))
            throw new InvalidOperationException(
                $"A sequence over {typeof(TEntity).Name} reads the top of its column as one row: name a long property that carries [Index(Unique = true)].");
        var read = column.Compile();
        return new Sequence(typeof(TEntity), async ct =>
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

    /// <summary>The highest number that has committed, with every number below it committed too (DATA-0169). The first ask reads the store
    /// once, as <see cref="Highest"/> does outside a hold; after it this answers from memory. A hold of this host raises it before its sender is
    /// answered; another process's commit reaches it only through <see cref="WaitPast"/>. It refuses inside a hold, and wherever the column would
    /// be read from anything but the entity's default store.</summary>
    public async Task<long> Head(CancellationToken ct = default)
    {
        var (_, head) = HeadHere();
        if (!head.Read) head.Learn(await Highest(ct).ConfigureAwait(false));
        return head.Value;
    }

    /// <summary>Waits until the head passes <paramref name="after"/>, for at most <paramref name="wait"/>, and answers the head as it stands then
    /// (DATA-0169). A commit of this host wakes it at once. A wait that ends at its bound reads the store once and raises the head to what it finds, so
    /// another process's commit reaches it within one bound. It holds nothing a writer waits on, and refuses where <see cref="Head"/> does.</summary>
    /// <param name="after">The number the reader has read through.</param>
    /// <param name="wait">The bound, which is positive and finite: the interval at which a stream would say it is still there, for example.</param>
    /// <param name="ct">Ends the wait, which then throws <see cref="OperationCanceledException"/>.</param>
    public async Task<long> WaitPast(long after, TimeSpan wait, CancellationToken ct = default)
    {
        if (wait <= TimeSpan.Zero || wait == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(wait), wait,
                "A wait on a sequence's head is bounded: give a positive, finite bound. To read the head without waiting, use Head.");
        var (heads, head) = HeadHere();
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            // The wake is taken before the head is read, so a commit that lands between the two still ends this wait.
            var wake = head.Wake;
            if (!head.Read) head.Learn(await Highest(ct).ConfigureAwait(false));
            var now = head.Value;
            if (heads.BetweenReadAndWait is { } between) await between().ConfigureAwait(false);
            if (now > after) return now;
            var left = wait - Stopwatch.GetElapsedTime(started);
            if (left <= TimeSpan.Zero) break;
            await wake.WaitAsync(left, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            ct.ThrowIfCancellationRequested();
            if (!wake.IsCompleted) break;
        }

        // No commit of this host moved the head past `after` within the bound; another process's may have.
        head.Learn(await Highest(ct).ConfigureAwait(false));
        return head.Value;
    }

    /// <summary>Whether a number taken in this flow is one the head counts: the entity's default store, with nothing else in scope. A hold raises
    /// the head only for a sequence every number of which it took there.</summary>
    internal bool NumbersTheHead() => Elsewhere() is null;

    private (SequenceHeads Heads, SequenceHeads.Head Head) HeadHere()
    {
        if (EntityContext.CurrentTransaction is TransactionCoordinator { IsHeld: true, IsCompleted: false })
            throw new InvalidOperationException(
                $"Inside a hold, Highest is the read of a sequence over {_entity.Name}: it sees the hold's own numbers, while the head is only what " +
                "has committed, and a wait here would keep every other writer of the store waiting. Read Highest here, or ask the head before the " +
                "hold or after it.");
        if (Elsewhere() is { } refusal) throw new InvalidOperationException(refusal);
        var heads = AppHost.GetRequiredService<SequenceHeads>(HeadOperation);
        return (heads, heads.Of(this));
    }

    /// <summary>Why this flow would read the column from somewhere other than the entity's default store, whose head is the one a host keeps,
    /// or null when it reads the default store.</summary>
    private string? Elsewhere()
    {
        var where = EntityContext.Current switch
        {
            { Source: { } source } => $"source '{source}'",
            { Adapter: { } adapter } => $"adapter '{adapter}'",
            { Partition: { } partition } => $"partition '{partition}'",
            _ => StorageNameParticleRegistry.Gather(_entity) is [var particle, ..] ? $"the container its '{particle.Axis}' axis names" : null,
        };
        if (where is not null)
            return $"The head of a sequence over {_entity.Name} is its default store's, and this flow reads {_entity.Name} from {where}, which " +
                   "numbers on its own. Ask the head with no source, adapter, partition or container in scope, or read Highest inside a hold there.";
        return ManagedFieldRegistry.EqualityFields(_entity) is [var field, ..]
            ? $"A sequence over {_entity.Name} numbers within each value of its field '{field.StorageName}', such as a tenant, so it has no one " +
              "head, whatever value this flow carries. Read Highest inside a hold, which numbers within the value in scope."
            : null;
    }

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
